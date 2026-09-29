using UnityEngine;

/// <summary>
/// 区域出口 / 传送点。挂在地图边缘的一条「边条」触发器上（星露谷 / 太阳港式）。
///
/// 【想要的体验】
///   角色走到地图图片边缘 → 立刻加载相邻区域 → 落在新地图靠内的位置。
///   落地那一刻不会再被传回去，也不会因为一直按着方向键而被立刻送回原图。
///
/// 【三条防弹回规则，缺一条就会「传过去又传回来」】
///   1. 挤开（Cooldown）      ：传送成功后进入冷却，落地后 rearmDelay 秒内不响应。
///   2. 必须先离开（Suppress）：落地点如果和某个传送点【坐标有重叠】，把它压成
///                              「抑制态」，必须等玩家真正走出去（Trigger Exit）
///                              才重新武装。这是星露谷的门：出生在门里不算进门。
///                              判定用的是【玩家碰撞体的包围盒】而不是中心点 ——
///                              用中心点会有漏网：碰撞体比通道还宽时，中心点还在
///                              外面，身体已经压在线上了，照样会被踢回去。
///   3. 等松手（WaitRelease）：落地瞬间玩家多半还按着方向键，
///                              这时若立刻武装传送点，角色会一路走进去再换回来。
///                              所以落地后先等方向键松开（最多 arrivalGrace 秒），
///                              才允许任何传送点响应。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class ScenePortal : MonoBehaviour
{
    [Header("去哪")]
    [Tooltip("目标场景文件名，不含路径后缀，例如 Area_Town。必须在 Build Settings 里")]
    public string targetSceneName = "";

    [Tooltip("目标场景里 SceneEntryPoint 的 entryID")]
    public string entryID = "Start";

    [Header("触发方式")]
    [Tooltip("勾上＝踩上去就得按交互键；不勾＝走到边条上就传送（星露谷默认）")]
    public bool requireKeyPress = false;
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("踩上去后停留多久才传送，避免擦边误触。0.1~0.2 最顺手")]
    public float holdBeforeTrigger = 0.12f;

    [Header("防重复触发 / 防弹回")]
    [Tooltip("传送成功后的硬冷却，单位秒")]
    public float rearmDelay = 1.2f;

    [Tooltip("勾上＝落地时若玩家还按着方向键，先等他松开（或超时）才武装传送点。\n" +
             "关掉这条，玩家一路按着右键就会在两张图之间来回弹。")]
    public bool waitForInputReleaseOnArrival = true;

    [Header("自动贴地图边缘")]
    [Tooltip("勾选 = 进入场景后自动吸附到地图图片的边缘，位置和尺寸都不用手填。\n" +
             "换图后坐标会自己跟着更新，不会出现『走很远才传送』的问题。")]
    public bool autoSnapToEdge = true;

    [Tooltip("Auto = 看你现在把它摆在哪边；也可以直接指定上下左右")]
    public MapEdge edge = MapEdge.Auto;

    [Tooltip("边条沿着地图边有多长（世界单位）。这是通道的宽度")]
    public float lengthAlongEdge = 6f;

    [Tooltip("边条的厚度（往地图外伸出去的深度），1~2 就够")]
    public float thickness = 1.5f;

    [Tooltip("勾选 = 保持你在 Scene 里摆的沿边位置（比如门偏上一点）；不勾 = 贴在这条边的正中间")]
    public bool keepPositionAlongEdge = false;

    [Header("过滤")]
    public string playerTag = "Player";

    private MapEdge _resolvedEdge = MapEdge.Auto;

    /// <summary>这条传送点所属的边（Auto 会自动算一次并缓存）</summary>
    public MapEdge Edge
    {
        get
        {
            if (edge != MapEdge.Auto) return edge;
            if (_resolvedEdge == MapEdge.Auto && MapBoundary.Current != null)
                _resolvedEdge = MapBoundary.Current.NearestEdge(transform.position);
            return _resolvedEdge == MapEdge.Auto ? MapEdge.Right : _resolvedEdge;
        }
    }

    private BoxCollider2D trigger;
    private bool playerInside;
    private float holdTimer;
    private float cooldownTimer;
    private bool triggered;

    /// <summary>抑制中：玩家没真正走出去之前，永远不会触发</summary>
    private bool suppressUntilExit;

    // 全局「等松手」窗口，由 SceneLoader 落地成功后打开
    private static bool s_waitInputRelease;
    private static float s_waitUntil;

    private void Awake()
    {
        trigger = GetComponent<BoxCollider2D>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void Start()
    {
        // 地图边界就绪之后（MapBoundary 在场景加载完就建好了）把自己贴到图片边缘
        if (autoSnapToEdge && MapBoundary.Current != null)
            MapBoundary.Current.SnapPortal(this, true);
    }

    private void Reset()
    {
        trigger = GetComponent<BoxCollider2D>();
        if (trigger == null) return;
        trigger.isTrigger = true;
        if (trigger.size.x <= 0f || trigger.size.y <= 0f)
            trigger.size = new Vector2(1f, 8f);
    }

    private void Update()
    {
        TickArrivalWindow();

        if (triggered) return;
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        // 抑制期：玩家还没走出这个传送点，什么都不做
        if (suppressUntilExit) return;

        if (!playerInside) return;
        if (cooldownTimer > 0f) return;
        if (SceneTransition.IsBusy) return;

        // 落地后还按着方向键：先等松手，别急着武装
        if (waitForInputReleaseOnArrival && s_waitInputRelease) return;

        if (requireKeyPress)
        {
            if (Input.GetKeyDown(interactKey))
                Fire();
            return;
        }

        holdTimer += Time.deltaTime;
        if (holdTimer >= holdBeforeTrigger)
            Fire();
    }

    /// <summary>
    /// 落地后的「等松手」窗口推进：移动轴全部归零（松手）或超时后关闭。
    /// 这里读的是 Unity 旧 Input 系统的 Horizontal / Vertical，
    /// 如果你换成了新 Input System，把这个判断换成自己的输入接口即可。
    /// </summary>
    private static void TickArrivalWindow()
    {
        if (!s_waitInputRelease) return;

        float h = 0f, v = 0f;
        try { h = Input.GetAxisRaw("Horizontal"); v = Input.GetAxisRaw("Vertical"); }
        catch (System.Exception) { s_waitInputRelease = false; return; }

        bool idle = Mathf.Abs(h) < 0.01f && Mathf.Abs(v) < 0.01f;

        if (idle || Time.unscaledTime >= s_waitUntil)
            s_waitInputRelease = false;
    }

    private void Fire()
    {
        if (triggered) return;

        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogWarning($"[ScenePortal] {name} 没填目标场景名，无法传送。", this);
            return;
        }

        triggered = true;
        playerInside = false;
        holdTimer = 0f;

        // 把自己传给 SceneLoader：落地时它会在新场景里找「指回来的那个传送点」，
        // 让玩家落在对应传送门附近，而不是入口点/地图中心。
        SceneLoader.Instance?.LoadScene(targetSceneName, entryID, this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        if (SceneTransition.IsBusy) return;

        // 抑制期里的「进入」通常就是落地那一帧 Unity 补发的重叠事件，忽略
        if (suppressUntilExit) return;

        playerInside = true;
        holdTimer = 0f;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;

        // 真正走出去一次，抑制解除
        suppressUntilExit = false;
        playerInside = false;
        holdTimer = 0f;
    }

    /// <summary>
    /// 由 SceneLoader 落位成功后调用。
    /// 传【玩家碰撞体】进来：只要身体任何一部分压在本传送点上，就进入抑制态。
    /// </summary>
    public void MarkUsed(Collider2D playerCollider)
    {
        triggered = false;
        playerInside = false;
        holdTimer = 0f;
        cooldownTimer = rearmDelay;

        if (trigger == null) return;

        // 关键：拿玩家【碰撞体包围盒】判定，而不是中心点。
        // 碰撞体偏大的时候，中心点还在外面、身体已经压线了，用点判定会漏掉这种情况。
        if (playerCollider != null)
        {
            if (trigger.bounds.Intersects(playerCollider.bounds))
                suppressUntilExit = true;
        }
    }

    /// <summary>同上，给没有碰撞体的玩家兜底（按落地点判定，不推荐长期依赖）</summary>
    public void MarkUsed(Vector3 playerPos)
    {
        triggered = false;
        playerInside = false;
        holdTimer = 0f;
        cooldownTimer = rearmDelay;

        if (trigger != null && trigger.OverlapPoint(playerPos))
            suppressUntilExit = true;
    }

    /// <summary>
    /// 玩家刚落地：所有传送点进入冷却，并打开全局「等松手」窗口。
    /// </summary>
    public static void NotifyPlayerArrived(float graceSeconds)
    {
        s_waitInputRelease = true;
        s_waitUntil = Time.unscaledTime + Mathf.Max(0f, graceSeconds);
    }

    public void SetCooldown(float seconds)
    {
        cooldownTimer = Mathf.Max(cooldownTimer, seconds);
    }

    /// <summary>这个传送点是否正压在玩家身上（诊断用）</summary>
    public bool IsOverlapping(Collider2D playerCollider)
    {
        if (trigger == null || playerCollider == null) return false;
        return trigger.bounds.Intersects(playerCollider.bounds);
    }

    private bool IsPlayer(Collider2D other)
    {
        if (other == null) return false;
        if (other.GetComponent<PersistentPlayer>() != null) return true;

        if (!string.IsNullOrEmpty(playerTag))
        {
            try
            {
                if (other.CompareTag(playerTag)) return true;
            }
            catch (UnityException)
            {
            }
        }
        return false;
    }

    // ------------------------------------------------------------ 可视化

    private void OnDrawGizmos()
    {
        BoxCollider2D bc = GetComponent<BoxCollider2D>();
        if (bc == null) return;

        Color c = string.IsNullOrEmpty(targetSceneName)
            ? new Color(1f, 0.3f, 0.3f, 0.35f)
            : new Color(0.3f, 0.8f, 1f, 0.35f);

        Gizmos.color = c;
        Gizmos.DrawCube(transform.position + (Vector3)bc.offset, bc.size);
        Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
        Gizmos.DrawWireCube(transform.position + (Vector3)bc.offset, bc.size);
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider2D bc = GetComponent<BoxCollider2D>();
        if (bc == null) return;

        Vector3 center = transform.position + (Vector3)bc.offset;

        // 箭头指向「离开地图」的方向：就是玩家走出去会被传走的方向
        Vector3 dir = Vector3.zero;
        if (Mathf.Abs(bc.size.x) < Mathf.Abs(bc.size.y)) dir = Vector3.right;
        else dir = Vector3.up;

        if (Vector3.Dot(center.normalized, dir) < 0f) dir = -dir;

        Vector3 perp = new Vector3(-dir.y, dir.x, 0f) * 0.3f;

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.95f);
        Gizmos.DrawLine(center, center + dir * 1.6f);
        Gizmos.DrawLine(center + dir * 1.6f, center + dir * 1.1f + perp);
        Gizmos.DrawLine(center + dir * 1.6f, center + dir * 1.1f - perp);
    }
}
