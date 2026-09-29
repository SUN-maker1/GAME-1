using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 区域场景切换的总管。整个流程：
///   淡出 → 锁输入 → 异步加载新场景 → 【场景激活事件里】按 entryID 落位玩家 → 淡入 → 解锁
///
/// 【这次的关键改动】
/// 落位不再放在协程里「yield return op 之后」立刻做，而是挂在
/// SceneManager.sceneLoaded 事件上等场景真正激活。这样新场景的物体一定已经 Awake 完，
/// 一定能找到玩家和入口点，不会出现「找不到玩家 → 玩家卡在传送点上 → 被反复传送」。
///
/// 挂在场景里一个空物体上即可，它自己会 DontDestroyOnLoad，跨场景存活。
/// 每个区域场景都放一份也没关系，多余的会自己销毁。
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [Header("过场")]
    public Color fadeColor = Color.black;
    public float fadeOutTime = 0.35f;
    public float fadeInTime = 0.45f;

    [Header("兜底 · 卡死看门狗")]
    [Tooltip("切换过程超过这个秒数还没结束，就强制解锁。\n" +
             "没有它，任何一个环节出错（比如 sceneLoaded 没回调），IsBusy 会永久卡住，" +
             "导致传送点再也来不及、玩家像被卡在虚空里。")]
    public float transitionTimeout = 6f;

    [Header("兜底")]
    [Tooltip("找不到指定入口点时使用这个入口")]
    public string fallbackEntryID = "Start";

    [Header("防「传过去又传回来」")]
    [Tooltip("落地后先等玩家松开方向键，最多等这么久（秒）。\n" +
             "玩家通常是按住右键走进传送点的，不给他松手的机会就会被立刻送回原图。\n" +
             "0 = 关掉这个功能。")]
    public float arrivalInputGrace = 2.5f;

    [Header("落地位置（星露谷式）")]
    [Tooltip("勾选 = 优先落在目标场景里「指回出发场景的那个传送点」内侧附近 ——\n" +
             "从哪条边的门出去，就从哪条边的门进来。找不到回程传送点时才用 entryID 入口点。")]
    public bool preferPortalLanding = true;

    [Tooltip("落在回程传送点内侧多远（世界单位）。太近会一落地就踩回传送条上被弹回去")]
    public float landingInset = 1.2f;

    [Header("调试")]
    public bool verboseLog = true;

    private Canvas fadeCanvas;
    private Image fadeImage;
    private bool busy;
    private float busyTimer;

    // 本次切换的落点，-sceneLoaded 触发时消费掉
    private string pendingEntryID;

    // 读档专用：一次性的落位坐标。设置后下一次切场景会强制站到这个点上，
    // 用完自动清空。没它的话读档只能落在入口点，玩家会发现自己不在存档时的位置。
    private static bool hasArrivalOverride;
    private static Vector3 arrivalOverride;

    /// <summary>下一次切场景时，把玩家强制放到这个世界坐标（读档用）</summary>
    public static void SetArrivalOverride(Vector3 worldPos)
    {
        arrivalOverride = worldPos;
        hasArrivalOverride = true;
    }

    public static void ClearArrivalOverride()
    {
        hasArrivalOverride = false;
    }

    /// <summary>出发传送点的快照（场景卸载后原物体就没了，所以只留数据）</summary>
    private struct ExitInfo
    {
        public bool valid;
        public string fromScene;
        public MapEdge edge;
        public Vector3 center;
        public Vector3 extents;
    }

    private ExitInfo lastExit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // 【只销毁这个组件，绝不 Destroy(gameObject)】
            // 老代码删的是整个物体：万一有人把 SceneLoader 和别的脚本挂在同一个物体上
            // （比如主菜单的 MainMenuUI），新场景一加载就会把整套菜单界面连锅端掉 → 黑屏。
            // 自毁只是为了"同一个场景里别有两个 Loader"，删掉组件本身就够了。
            Debug.LogWarning("[SceneLoader] 场景里已经有一个 SceneLoader 在跨场景存活了，把自己这个多余的实例撤掉。");
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 同物体上还挂着别的东西时给个提醒：DontDestroyOnLoad 带的是整个 GameObject，
        // 那些组件会被一起拖到下一个场景去。
        Component[] comps = GetComponents<Component>();
        if (comps.Length > 2)
        {
            Debug.LogWarning("[SceneLoader] 这个物体上还挂着别的组件，它们会被 DontDestroyOnLoad 一起带去下一个场景。" +
                             "建议把 SceneLoader 单独放到一个空物体上。");
        }

        // 关键：用官方事件监听场景激活，保证落位时机正确
        SceneManager.sceneLoaded += OnSceneLoaded;

        BuildFadeUI();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this)
        {
            Instance = null;
            SceneTransition.IsBusy = false;
        }
    }

    /// <summary>切到指定场景，并站在指定入口点上</summary>
    public void LoadScene(string sceneName, string entryID)
    {
        LoadScene(sceneName, entryID, null);
    }

    /// <summary>
    /// 带出发传送点的重载：fromPortal 不为空时，落地位置优先选
    /// 目标场景里「指回出发场景的传送点」内侧（星露谷式对应门）。
    /// </summary>
    public void LoadScene(string sceneName, string entryID, ScenePortal fromPortal)
    {
        if (busy)
        {
            Debug.LogWarning($"[SceneLoader] 正在切换中，忽略本次请求：{sceneName}");
            return;
        }

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[SceneLoader] 目标场景名为空");
            return;
        }

        // 场景没进 Build Settings，编辑器里能打开但打包/运行时会失败，这里提前拦住
        if (!IsSceneInBuild(sceneName))
            Debug.LogWarning($"[SceneLoader] 场景 '{sceneName}' 不在 Build Settings 里，" +
                             "Play 时可能加载失败。去 File / Build Settings / Scenes In Build 加上它。");

        // 趁出发场景还活着，把传送点数据抄下来
        lastExit = CaptureExit(fromPortal);

        pendingEntryID = entryID;
        busy = true;
        busyTimer = transitionTimeout;
        StartCoroutine(TransitionRoutine(sceneName));
    }

    private static ExitInfo CaptureExit(ScenePortal portal)
    {
        ExitInfo info = default;

        if (portal == null) return info;

        info.valid = true;
        info.fromScene = portal.gameObject.scene.name;
        info.edge = portal.Edge;

        Collider2D col = portal.GetComponent<Collider2D>();
        if (col != null)
        {
            Bounds b = col.bounds;
            info.center = b.center;
            info.extents = b.extents;
        }
        else
        {
            info.center = portal.transform.position;
            info.extents = Vector3.one * 0.5f;
        }
        return info;
    }

    private void Update()
    {
        if (!busy) return;

        busyTimer -= Time.unscaledDeltaTime;
        if (busyTimer <= 0f)
        {
            Debug.LogWarning("[SceneLoader] 切换超时（" + transitionTimeout + " 秒），" +
                             "强制解锁。可能是目标场景没进 Build Settings，或者落位流程中断了。");
            ForceUnlock();
        }
    }

    /// <summary>无论进行到哪一步，直接恢复到可操作状态</summary>
    private void ForceUnlock()
    {
        busy = false;
        pendingEntryID = null;
        SceneTransition.IsBusy = false;
        StartCoroutine(Fade(1f, 0f, fadeInTime));
    }

    private IEnumerator TransitionRoutine(string sceneName)
    {
        busy = true;
        SceneTransition.IsBusy = true;

        if (verboseLog) Debug.Log($"[SceneLoader] 开始切换 → {sceneName}");

        // 1. 淡出
        yield return Fade(0f, 1f, fadeOutTime);

        // 2. 异步加载：先不激活，等资源就绪
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (op == null)
        {
            Debug.LogError($"[SceneLoader] 场景加载失败：{sceneName}。确认它已经加进 Build Settings。");
            pendingEntryID = null;
            yield return Fade(1f, 0f, fadeInTime);
            busy = false;
            SceneTransition.IsBusy = false;
            yield break;
        }

        op.allowSceneActivation = false;
        while (!op.isDone && op.progress < 0.9f)
            yield return null;

        // 3. 放行激活。Activated 那一帧 Unity 会回调 sceneLoaded，
        //    落位代码在 OnSceneLoaded 里等着，这里不去抢。
        op.allowSceneActivation = true;
        yield return null;
    }

    // ------------------------------------------------------------ 落位

    /// <summary>场景激活后 Unity 回调。真正搬玩家的地方。</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (pendingEntryID == null)
            return;   // 不是我们发起的切换（比如编辑器手动打开场景），不管

        string entry = pendingEntryID;
        pendingEntryID = null;

        PlacePlayer(entry);

        // 淡入要在这里开始，否则会黑屏不动
        StartCoroutine(Fade(1f, 0f, fadeInTime));

        busy = false;
        SceneTransition.IsBusy = false;
        if (verboseLog) Debug.Log($"[SceneLoader] 切换完成 → {scene.name}（{entry}）");
    }

    /// <summary>把玩家放到入口点，并让相机瞬移贴过去（不做平滑，否则会看到相机飞过来）</summary>
    private void PlacePlayer(string entryID)
    {
        Transform player = FindPlayerInActiveScene();
        if (player == null)
        {
            Debug.LogWarning("[SceneLoader] 当前场景里找不到玩家。确认 Player 物体上挂了 PersistentPlayer " +
                             "并且它属于刚加载的这个场景。");
            return;
        }

        // 先让地图边界把传送条和入口点吸附到图片边缘上，
        // 再去找入口点——否则拿到的是换图后已经失效的旧坐标（玩家会掉在图外面）。
        MapBoundary.Ensure();

        Vector3 landPos = player.position;   // 先给个安全默认值（找不到任何落点时=原位），编译器也要求明确赋值
        PlayerMovement.Facing? landFacing = null;
        SceneEntryPoint point = null;
        bool landed = false;

        // ① 首选：目标场景里「指回出发场景的那个传送点」，落在它内侧
        if (preferPortalLanding && lastExit.valid)
        {
            ScenePortal back = FindReturnPortal(lastExit.fromScene);
            if (back != null)
            {
                landPos = ComputeLandingSpot(back);
                landFacing = InwardFacing(back.Edge);
                landed = true;

                if (verboseLog)
                    Debug.Log($"[SceneLoader] 从 {lastExit.fromScene} 过来 → 落在回程传送点「{back.name}」" +
                              $"（{back.Edge} 边）内侧 {landingInset:F2} 单位，面朝 {landFacing.Value}", back);
            }
            else if (verboseLog)
            {
                Debug.LogWarning($"[SceneLoader] {SceneManager.GetActiveScene().name} 里没找到指回 " +
                                 $"{lastExit.fromScene} 的传送点，改用入口点 entryID='{entryID}'。");
            }
        }

        // ② 兜底：按 entryID 找入口点
        if (!landed)
        {
            point = FindEntryPoint(entryID);
            if (point == null && !string.IsNullOrEmpty(fallbackEntryID))
            {
                Debug.LogWarning($"[SceneLoader] 找不到入口点 '{entryID}'，改用兜底入口 '{fallbackEntryID}'");
                point = FindEntryPoint(fallbackEntryID);
            }

            if (point != null)
            {
                landPos = point.transform.position;
                landed = true;
            }
            else
            {
                Debug.LogWarning("[SceneLoader] 场景里找不到任何入口点，玩家保持原位（" + player.position + "）。");
                // landPos 保持默认值 player.position
            }
        }

        // 读档落位：override 优先级最高（压过入口点和传送点判定），且只生效一次
        if (hasArrivalOverride)
        {
            landPos = arrivalOverride;
            hasArrivalOverride = false;
            if (verboseLog) Debug.Log($"[SceneLoader] 读档落位 → {landPos}");
        }

        player.position = landPos;

        // 落地朝向：从哪条边的门进来，就面朝地图内侧（进门的行走方向）
        PlayerMovement mover = player.GetComponent<PlayerMovement>();
        if (landFacing.HasValue && mover != null)
        {
            mover.SetFacing(landFacing.Value);
        }
        else if (point != null && point.overrideFacing)
        {
            SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
            if (sr != null) sr.flipX = point.faceLeft;
        }

        // 清掉上一场景残留的速度，别让角色带着惯性冲进传送点
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.position = player.position;   // 物理坐标同步，别让触发器用旧位置判定
        }

        // 物理位置一改，立刻同步一遍，别让触发器还拿着上一帧的旧包围盒判定
        Physics2D.SyncTransforms();

        // 让场上所有传送点进入「已用」状态：
        // 玩家身体压在哪个传送点上，那个点就进入抑制态，必须走出去才会重新武装。
        Collider2D playerBody = player.GetComponent<Collider2D>();
        foreach (ScenePortal portal in FindObjectsOfType<ScenePortal>())
            portal.MarkUsed(playerBody);

        // 落地后先等方向键松开（或超时），这段时间所有传送点都不响应
        if (arrivalInputGrace > 0f)
            ScenePortal.NotifyPlayerArrived(arrivalInputGrace);

        // 相机瞬移
        CameraFollow follow = FindObjectOfType<CameraFollow>();
        if (follow != null)
        {
            follow.target = player;
            follow.SnapToTarget();
        }
    }

    // ------------------------------------------------------------ 星露谷式落地

    /// <summary>
    /// 在当前场景里找「指回 fromScene 的传送点」。
    /// 指向自己所在场景的自环传送点不算（那基本是误建的）。
    /// </summary>
    private ScenePortal FindReturnPortal(string fromScene)
    {
        if (string.IsNullOrEmpty(fromScene)) return null;

        Scene active = SceneManager.GetActiveScene();
        ScenePortal best = null;

        foreach (ScenePortal p in FindObjectsOfType<ScenePortal>())
        {
            if (p == null || p.gameObject.scene != active) continue;

            string target = p.targetSceneName;
            if (string.IsNullOrEmpty(target)) continue;

            // 自环：指向自己所在场景 → 跳过
            if (string.Equals(target, active.name, System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(target, fromScene, System.StringComparison.OrdinalIgnoreCase))
            {
                if (best == null)
                    best = p;
                else if (verboseLog)
                    Debug.LogWarning($"[SceneLoader] 有多个指回 {fromScene} 的传送点，用第一个（{best.name}）。" +
                                     $"如果这不是你要的那扇门，检查 {p.name}。", p);
            }
        }
        return best;
    }

    /// <summary>回程传送点内侧的落点：门中心往地图里挪「半张门厚 + 落地余量」</summary>
    private Vector3 ComputeLandingSpot(ScenePortal back)
    {
        Collider2D col = back.GetComponent<Collider2D>();
        Bounds pb = col != null ? col.bounds : new Bounds(back.transform.position, Vector3.one);

        MapEdge edge = back.Edge;
        Vector2 inward = InwardDir(edge);
        float half = (edge == MapEdge.Left || edge == MapEdge.Right) ? pb.extents.x : pb.extents.y;

        Vector3 pos = pb.center + (Vector3)(inward * (half + Mathf.Max(0.2f, landingInset)));

        // 保险：落点一定夹在地图矩形内
        if (MapBoundary.Current != null)
        {
            Bounds m = MapBoundary.Current.MapBounds;
            float margin = Mathf.Clamp(landingInset * 0.5f, 0.2f, 2f);
            pos.x = Mathf.Clamp(pos.x, m.min.x + margin, m.max.x - margin);
            pos.y = Mathf.Clamp(pos.y, m.min.y + margin, m.max.y - margin);
        }
        return pos;
    }

    private static Vector2 InwardDir(MapEdge edge)
    {
        switch (edge)
        {
            case MapEdge.Left:   return Vector2.right;
            case MapEdge.Right:  return Vector2.left;
            case MapEdge.Bottom: return Vector2.up;
            case MapEdge.Top:    return Vector2.down;
            default:             return Vector2.zero;
        }
    }

    private static PlayerMovement.Facing InwardFacing(MapEdge edge)
    {
        switch (edge)
        {
            case MapEdge.Left:   return PlayerMovement.Facing.Right;
            case MapEdge.Right:  return PlayerMovement.Facing.Left;
            case MapEdge.Bottom: return PlayerMovement.Facing.Back;
            case MapEdge.Top:    return PlayerMovement.Facing.Front;
            default:             return PlayerMovement.Facing.Front;
        }
    }

    /// <summary>
    /// 只认【当前激活场景】里的玩家。
    /// 这是修死循环的关键：加载 Single 场景后场上可能短暂存在旧场景的残留 Player，
    /// 直接信 PersistentPlayer.Instance 有可能拿到那个残留的。
    /// </summary>
    private Transform FindPlayerInActiveScene()
    {
        Scene active = SceneManager.GetActiveScene();

        PersistentPlayer[] all = FindObjectsOfType<PersistentPlayer>();
        PersistentPlayer best = null;
        foreach (PersistentPlayer p in all)
        {
            if (p != null && p.gameObject != null && p.gameObject.scene == active)
                best = p;
        }
        if (best != null) return best.transform;

        // 兜底：还按标签找一次（标签不存在会抛异常，这里兜住）
        try
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null && go.scene == active) return go.transform;
        }
        catch (UnityException) { }

        return null;
    }

    private SceneEntryPoint FindEntryPoint(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (SceneEntryPoint point in FindObjectsOfType<SceneEntryPoint>())
        {
            if (point != null && point.entryID == id) return point;
        }
        return null;
    }

    private static bool IsSceneInBuild(string sceneName)
    {
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.EndsWith($"{sceneName}.unity")) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ 淡入淡出

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (fadeImage == null)
        {
            BuildFadeUI();
            if (fadeImage == null) yield break;
        }

        float t = 0f;
        SetFadeAlpha(from);

        if (duration <= 0f)
        {
            SetFadeAlpha(to);
            yield break;
        }

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetFadeAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
            yield return null;
        }

        SetFadeAlpha(to);
    }

    private void SetFadeAlpha(float a)
    {
        if (fadeImage == null) return;
        Color c = fadeImage.color;
        c.a = Mathf.Clamp01(a);
        fadeImage.color = c;
        fadeImage.enabled = c.a > 0.001f;
    }

    /// <summary>运行时自己搭一个全屏黑幕，不需要手工建 UI prefab</summary>
    private void BuildFadeUI()
    {
        if (fadeCanvas != null) return;

        GameObject canvasGo = new GameObject("SceneFader", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject imgGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        imgGo.transform.SetParent(canvasGo.transform, false);

        Image img = imgGo.GetComponent<Image>();
        img.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 0f);
        img.raycastTarget = false;
        img.enabled = false;

        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        fadeCanvas = canvas;
        fadeImage = img;
    }
}
