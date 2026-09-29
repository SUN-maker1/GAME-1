using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 开始界面（主菜单）。
///
/// 【整个界面都是代码搭出来的】
/// 场景里只要挂这一个脚本就够了，不用手工摆 Canvas / 按钮 / 文字。
/// 好处是：改配色、改标题、加按钮都在 Inspector 上改参数，不会出现
/// 「改了脚本但场景里的预制体没同步」这类对不上的问题。
///
/// 菜单结构：
///   背景渐变 → 大标题 → 开始游戏 / 继续游戏 / 读取存档 / 设置 / 退出游戏
///   读取存档：3 个槽位卡片，可读取、可删除（删除要再点一次确认）
///   设置    ：主音量 / 音乐 / 音效 / 全屏，全存 PlayerPrefs
///
/// 依赖 SceneLoader 做过场淡入淡出和落位，所以菜单场景里也要放一个 SceneLoader。
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    /// <summary>运行时搭出来的菜单 Canvas 的名字（独立根物体，随菜单场景卸载）</summary>
    private const string CanvasName = "MainMenuCanvas";

    // ============================================================ Inspector

    [Header("标题")]
    [Tooltip("游戏大标题，随便改")]
    public string gameTitle = "晨曦农场";
    public string gameSubtitle = "D A W N L I G H T   F A R M";
    public string versionText = "v0.1.0";

    [Header("起始场景")]
    [Tooltip("点「开始游戏」后进哪个场景、站在哪个入口点上")]
    public string startSceneName = "Area_Farm";
    public string startEntryID = "Start";

    [Tooltip("勾选 = 开始游戏和读档都进上面这个场景（站在 startEntryID 入口点上），" +
             "不管存档时人在小镇还是屋里。\n不勾 = 读档回到存档时所在的场景和坐标。")]
    public bool alwaysLoadStartScene = true;

    [Header("自毁保护")]
    [Tooltip("这套菜单属于哪个场景。留空 = 自动取进入时的活动场景名（一般就是 MainMenu）。\n" +
             "作用：如果有人把它和 SceneLoader 挂在同一个物体上，SceneLoader 的 DontDestroyOnLoad " +
             "会把标题界面一起带进游戏场景，靠这个场景名判定「已经离开菜单」并自动拆掉自己。")]
    public string menuSceneName = "";

    [Header("配色")]
    public Color titleColor = new Color(1f, 0.95f, 0.78f);
    public Color subtitleColor = new Color(0.88f, 0.82f, 0.95f);
    public Color textColor = new Color(0.96f, 0.93f, 0.86f);
    public Color dimTextColor = new Color(0.65f, 0.62f, 0.57f);
    public Color buttonColor = new Color(0.16f, 0.13f, 0.20f, 0.94f);
    public Color accentColor = new Color(0.98f, 0.76f, 0.32f);
    public Color panelColor = new Color(0.09f, 0.08f, 0.12f, 0.97f);
    public Color rowColor = new Color(0.15f, 0.13f, 0.18f, 0.92f);

    [Header("天空气氛（背景渐变）")]
    public Color skyTop = new Color(0.10f, 0.07f, 0.19f);
    public Color skyMid = new Color(0.85f, 0.42f, 0.28f);
    public Color skyHorizon = new Color(0.98f, 0.72f, 0.35f);
    public Color groundColor = new Color(0.10f, 0.14f, 0.09f);

    [Header("标题呼吸动画")]
    public bool animateTitle = true;
    public float titleFloatAmplitude = 10f;
    public float titleFloatSpeed = 1.1f;

    // ============================================================ 运行时状态

    private Canvas canvas;
    private RectTransform titleRt;
    private float titleBaseY;

    private Button continueButton;
    private Text continueLabel;

    private Button quitButton;
    private Text quitLabel;
    private bool quitConfirming;
    private float quitConfirmTimer;

    /// <summary>本套 UI 属于哪个场景。跨场景了就说明被人误带走了</summary>
    private string homeSceneName;

    private GameObject savePanel;
    private RectTransform saveListContent;

    private GameObject settingsPanel;
    private RectTransform settingsContent;

    private Text toastText;
    private float toastTimer;

    private int pendingDeleteSlot = -1;
    private float pendingDeleteTimer;

    private bool built;

    // ============================================================ 生命周期

    private void Awake()
    {
        Build();

        // 出生场景名：用「进入时的活动场景」而不是 gameObject.scene.name。
        //
        // 坑：如果有人把 MainMenuUI 和 SceneLoader 挂在同一个物体上（旧版一键生成就是这个结构），
        // SceneLoader.Awake 里的 DontDestroyOnLoad(gameObject) 会把整个物体挪进
        // "DontDestroyOnLoad" 场景 —— 这时 gameObject.scene.name 会变成 "DontDestroyOnLoad"。
        // 于是之后任何一次场景事件都会让这套 UI 误判成「已经离开菜单场景」而当场自毁，
        // 表现就是：进 Play 直接黑屏 / 进了游戏又弹回标题 / 标题界面一闪就没。
        // 而 DontDestroyOnLoad 永远不是 activeScene，所以取 activeScene 一定是准的。
        string sceneName = string.IsNullOrEmpty(menuSceneName)
            ? SceneManager.GetActiveScene().name
            : menuSceneName;

        if (sceneName == "DontDestroyOnLoad")
        {
            // 兜底：真拿不到名字时宁可关掉保护，也不能让它误杀自己
            Debug.LogWarning("[主菜单] 拿不到有效的场景名，自毁保护已停用（宁可不拆，也不能把标题界面拆没了）。");
            sceneName = "";
        }

        homeSceneName = sceneName;
        SceneManager.sceneLoaded += OnSceneLoadedGuard;

        if (GetComponent<SceneLoader>() != null)
        {
            Debug.LogWarning("[主菜单] MainMenuUI 和 SceneLoader 挂在同一个物体上：SceneLoader 会把这个物体" +
                             "一起 DontDestroyOnLoad，标题界面会跟着进游戏场景。\n" +
                             "建议用菜单 Tools ▸ 农场RPG ▸ 生成开始界面（MainMenu 场景）重建一次，" +
                             "新版本会把两者拆成两个独立物体。");
        }

        Debug.Log("[主菜单] 标题界面已生成 | 属于场景=" +
                  (string.IsNullOrEmpty(homeSceneName) ? "(未设，自毁保护关闭)" : homeSceneName) +
                  " | 当前活动场景=" + SceneManager.GetActiveScene().name +
                  " | 同物体有 SceneLoader=" + (GetComponent<SceneLoader>() != null));
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedGuard;
    }

    /// <summary>
    /// 自保：一旦被人（最常见是同物体上的 SceneLoader）用 DontDestroyOnLoad 带着跨了场景，
    /// 这套菜单 UI 会一直盖在游戏画面上，看起来就像"跳回了主界面"。
    /// 发现自己已经不在出生场景了就直接销毁，省得污染游戏画面。
    /// </summary>
    private void OnSceneLoadedGuard(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying) return;
        if (string.IsNullOrEmpty(homeSceneName)) return;

        // 新场景或当前活动场景有一个还是菜单场景，就说明没离开，什么都不做
        if (string.Equals(scene.name, homeSceneName)) return;
        if (string.Equals(SceneManager.GetActiveScene().name, homeSceneName)) return;

        Debug.Log("[主菜单] 已经离开菜单场景，标题界面自行销毁（它不该跟着进游戏）。");

        // 只拆掉自己这套 UI，绝不 Destroy(gameObject)：
        // 万一有人把它和 SceneLoader 挂在同一个物体上（SceneLoader 靠 DontDestroyOnLoad 活着），
        // 删整个物体会把过场淡入淡出一起干掉，屏幕就黑在那儿了。
        if (canvas != null) Destroy(canvas.gameObject);
        else DestroyOrphanCanvas();
        Destroy(this);
    }

    /// <summary>
    /// 兜底：Canvas 是独立根物体，正常会随菜单场景一起卸载；
    /// 万一它因为什么原因活过了场景切换，按名字把它清掉，绝不让它盖在游戏画面上。
    /// </summary>
    private void DestroyOrphanCanvas()
    {
        Canvas[] all = FindObjectsOfType<Canvas>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gameObject.name == CanvasName)
                Destroy(all[i].gameObject);
        }
    }

    private void Start()
    {
        // 把「开始游戏 / 读档都去哪个场景」这条规则写进 SaveSystem，
        // 游戏里按 F9 读档也照同一套规则走，不会出现菜单和游戏内不一致
        SaveSystem.ForceSceneName = alwaysLoadStartScene ? startSceneName : "";
        SaveSystem.ForceEntryID = startEntryID;

        RefreshContinueState();
    }

    private void Update()
    {
        if (animateTitle && titleRt != null)
        {
            float y = titleBaseY + Mathf.Sin(Time.unscaledTime * titleFloatSpeed) * titleFloatAmplitude;
            titleRt.anchoredPosition = new Vector2(titleRt.anchoredPosition.x, y);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (savePanel != null && savePanel.activeSelf) { ClosePanels(); return; }
            if (settingsPanel != null && settingsPanel.activeSelf) { ClosePanels(); return; }
        }

        if (quitConfirming)
        {
            quitConfirmTimer -= Time.unscaledDeltaTime;
            if (quitConfirmTimer <= 0f)
            {
                quitConfirming = false;
                if (quitLabel != null) quitLabel.text = "退出游戏";
            }
        }

        if (pendingDeleteSlot > 0)
        {
            pendingDeleteTimer -= Time.unscaledDeltaTime;
            if (pendingDeleteTimer <= 0f)
            {
                pendingDeleteSlot = -1;
                if (savePanel != null && savePanel.activeSelf) RebuildSaveList();
            }
        }

        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            if (toastText != null)
            {
                Color c = toastText.color;
                c.a = Mathf.Clamp01(toastTimer / 0.6f);
                toastText.color = c;
            }
        }
    }

    // ============================================================ 搭建

    private void Build()
    {
        if (built) return;
        built = true;

        EnsureEventSystem();

        canvas = MakeCanvas();
        Transform root = canvas.transform;

        MakeBackground(root);
        MakeTitle(root);
        MakeButtons(root);
        MakeToast(root);

        savePanel = MakePanel(root, "SavePanel", "读取存档", new Vector2(1040f, 660f), out saveListContent);
        settingsPanel = MakePanel(root, "SettingsPanel", "设置", new Vector2(900f, 620f), out settingsContent);

        savePanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private Canvas MakeCanvas()
    {
        GameObject go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        // 【故意不 SetParent(transform)】
        // 一旦挂在 MainMenuUI 所在物体下，同物体上的 SceneLoader 执行 DontDestroyOnLoad 时
        // 会把 Canvas 一起拖进游戏场景，标题界面就盖在农场画面上（sortingOrder 1000，压过一切）。
        // 做成独立根物体后，它随 MainMenu 场景正常卸载，怎么都不会跟去游戏里。
        go.transform.position = Vector3.zero;

        Canvas c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 1000;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return c;
    }

    /// <summary>黄昏渐变背景：上紫 → 中橙 → 地平线亮黄 → 下方深色地面</summary>
    private void MakeBackground(Transform parent)
    {
        int w = 16, h = 256;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            // 纹理坐标 y=0 在底部
            float t = y / (float)(h - 1);
            Color col = EvalSky(t);
            for (int x = 0; x < w; x++)
                px[y * w + x] = col;
        }

        tex.SetPixels(px);
        tex.Apply(false, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;   // 像素风：不要平滑插值
        tex.hideFlags = HideFlags.HideAndDontSave;

        Sprite sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        sp.hideFlags = HideFlags.HideAndDontSave;

        Image img = UiKit.MakeImage("Background", parent, Color.white);
        img.sprite = sp;
        img.type = Image.Type.Simple;
        UiKit.Stretch(img.rectTransform, 0f, 0f, 0f, 0f);

        // 压一层暗色，让标题和按钮更清楚
        Image veil = UiKit.MakeImage("Veil", parent, new Color(0.05f, 0.04f, 0.10f, 0.35f));
        UiKit.Stretch(veil.rectTransform, 0f, 0f, 0f, 0f);
    }

    private Color EvalSky(float t)
    {
        if (t < 0.20f) return groundColor;
        if (t < 0.26f) return Color.Lerp(groundColor, skyHorizon, (t - 0.20f) / 0.06f);
        if (t < 0.52f) return Color.Lerp(skyHorizon, skyMid, (t - 0.26f) / 0.26f);
        return Color.Lerp(skyMid, skyTop, (t - 0.52f) / 0.48f);
    }

    private void MakeTitle(Transform parent)
    {
        RectTransform holder = NewRect("TitleHolder", parent);
        holder.anchorMin = new Vector2(0.5f, 0.5f);
        holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.anchoredPosition = new Vector2(0f, 210f);
        holder.sizeDelta = new Vector2(1200f, 220f);

        titleRt = holder;
        titleBaseY = holder.anchoredPosition.y;

        Text title = UiKit.MakeText("GameTitle", holder, 96, titleColor);
        title.text = gameTitle;
        title.alignment = TextAnchor.MiddleCenter;
        title.resizeTextForBestFit = false;

        Outline ol = title.GetComponent<Outline>();
        if (ol != null)
        {
            ol.effectColor = new Color(0.12f, 0.06f, 0.02f, 0.95f);
            ol.effectDistance = new Vector2(3f, -3f);
        }

        RectTransform titleRect = title.rectTransform;
        UiKit.Stretch(titleRect, 0f, 0f, 0f, 60f);

        Text sub = UiKit.MakeText("Subtitle", holder, 30, subtitleColor);
        sub.text = gameSubtitle;
        sub.alignment = TextAnchor.UpperCenter;

        Outline subOl = sub.GetComponent<Outline>();
        if (subOl != null) subOl.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform subRect = sub.rectTransform;
        subRect.anchorMin = Vector2.zero;
        subRect.anchorMax = Vector2.one;
        subRect.offsetMin = new Vector2(0f, 10f);
        subRect.offsetMax = new Vector2(0f, -140f);

        // 副标题下面一条细装饰线
        Image line = UiKit.MakeImage("TitleLine", holder, accentColor);
        RectTransform lineRect = line.rectTransform;
        lineRect.anchorMin = new Vector2(0.5f, 0f);
        lineRect.anchorMax = new Vector2(0.5f, 0f);
        lineRect.pivot = new Vector2(0.5f, 0.5f);
        lineRect.anchoredPosition = new Vector2(0f, -88f);
        lineRect.sizeDelta = new Vector2(520f, 4f);

        if (!string.IsNullOrEmpty(versionText))
        {
            Text ver = UiKit.MakeText("Version", parent, 22, dimTextColor);
            ver.text = versionText;
            ver.alignment = TextAnchor.LowerLeft;
            RectTransform vr = ver.rectTransform;
            vr.anchorMin = Vector2.zero;
            vr.anchorMax = Vector2.zero;
            vr.pivot = Vector2.zero;
            vr.anchoredPosition = new Vector2(28f, 22f);
            vr.sizeDelta = new Vector2(400f, 40f);
        }
    }

    private void MakeButtons(Transform parent)
    {
        RectTransform holder = NewRect("MenuButtons", parent);
        holder.anchorMin = new Vector2(0.5f, 0.5f);
        holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.anchoredPosition = new Vector2(0f, -110f);
        holder.sizeDelta = new Vector2(460f, 480f);

        float step = 94f;
        int count = 5;
        float top = (count - 1) * 0.5f * step;

        MakeMenuButton(holder, "开始游戏", new Vector2(0f, top), OnStartClicked);
        continueButton = MakeMenuButton(holder, "继续游戏", new Vector2(0f, top - step), OnContinueClicked);
        continueLabel = continueButton.GetComponentInChildren<Text>();
        MakeMenuButton(holder, "读取存档", new Vector2(0f, top - step * 2f), OnOpenSavesClicked);
        MakeMenuButton(holder, "设置", new Vector2(0f, top - step * 3f), OnOpenSettingsClicked);
        quitButton = MakeMenuButton(holder, "退出游戏", new Vector2(0f, top - step * 4f), OnQuitClicked);
        quitLabel = quitButton.GetComponentInChildren<Text>();
    }

    private Button MakeMenuButton(Transform parent, string label, Vector2 pos, Action onClick)
    {
        Button btn = MakeButton(parent, label, new Vector2(420f, 78f), pos, 34, onClick);

        // 主菜单按钮左侧加一条高亮竖条，鼠标移上去会亮起来
        Image bar = UiKit.MakeImage("AccentBar", btn.transform, accentColor);
        RectTransform br = bar.rectTransform;
        br.anchorMin = new Vector2(0f, 0.5f);
        br.anchorMax = new Vector2(0f, 0.5f);
        br.pivot = new Vector2(0f, 0.5f);
        br.anchoredPosition = new Vector2(10f, 0f);
        br.sizeDelta = new Vector2(6f, 44f);
        bar.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.55f);

        return btn;
    }

    private Button MakeButton(Transform parent, string label, Vector2 size, Vector2 pos, int fontSize, Action onClick)
    {
        GameObject go = new GameObject("Button_" + label);
        RectTransform rt = go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.sprite = UiKit.WhiteSprite();
        img.color = buttonColor;

        Outline ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(0f, 0f, 0f, 0.7f);
        ol.effectDistance = new Vector2(2f, -2f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.45f, 1.30f, 1.05f, 1f);
        cb.pressedColor = new Color(0.80f, 0.78f, 0.72f, 1f);
        cb.selectedColor = new Color(1.45f, 1.30f, 1.05f, 1f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.75f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        Text t = UiKit.MakeText("Label", go.transform, fontSize, textColor);
        t.text = label;
        t.alignment = TextAnchor.MiddleCenter;
        UiKit.Stretch(t.rectTransform, 0f, 0f, 0f, 0f);

        if (onClick != null)
        {
            // 注意：委托不能 new UnityAction(另一个委托)，只能包一层
            Action handler = onClick;
            btn.onClick.AddListener(() => handler());
        }

        return btn;
    }

    private void MakeToast(Transform parent)
    {
        Text t = UiKit.MakeText("Toast", parent, 26, accentColor);
        t.text = "";
        t.alignment = TextAnchor.MiddleCenter;

        RectTransform rt = t.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 90f);
        rt.sizeDelta = new Vector2(1000f, 50f);

        Color c = t.color;
        c.a = 0f;
        t.color = c;

        toastText = t;
    }

    private void ShowToast(string msg)
    {
        if (toastText == null) return;
        toastText.text = msg;
        Color c = toastText.color;
        c.a = 1f;
        toastText.color = c;
        toastTimer = 1.8f;
    }

    // ============================================================ 面板骨架

    /// <summary>建一个「遮罩 + 卡片 + 标题 + 内容区 + 返回按钮」的通用面板</summary>
    private GameObject MakePanel(Transform parent, string name, string title, Vector2 size, out RectTransform content)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform));
        RectTransform prt = panel.GetComponent<RectTransform>();
        panel.transform.SetParent(parent, false);
        UiKit.Stretch(prt, 0f, 0f, 0f, 0f);

        // 遮罩：挡住后面的按钮点击
        Image shade = UiKit.MakeImage("Shade", panel.transform, new Color(0f, 0f, 0f, 0.55f));
        UiKit.Stretch(shade.rectTransform, 0f, 0f, 0f, 0f);
        shade.raycastTarget = true;

        // 卡片
        Image card = UiKit.MakeImage("Card", panel.transform, panelColor);
        RectTransform crt = card.rectTransform;
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = size;

        Outline cardOutline = card.gameObject.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        cardOutline.effectDistance = new Vector2(3f, -3f);

        // 标题
        Text head = UiKit.MakeText("Title", card.transform, 42, accentColor);
        head.text = title;
        head.alignment = TextAnchor.MiddleCenter;
        RectTransform hrt = head.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 1f);
        hrt.anchorMax = new Vector2(0.5f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = new Vector2(0f, -28f);
        hrt.sizeDelta = new Vector2(size.x - 60f, 70f);

        // 内容区
        RectTransform body = NewRect("Content", card.transform);
        body.anchorMin = new Vector2(0.5f, 0.5f);
        body.anchorMax = new Vector2(0.5f, 0.5f);
        body.pivot = new Vector2(0.5f, 0.5f);
        body.anchoredPosition = new Vector2(0f, -10f);
        body.sizeDelta = new Vector2(size.x - 80f, size.y - 190f);
        content = body;

        // 返回按钮
        MakeButton(card.transform, "返回", new Vector2(240f, 68f), new Vector2(0f, -size.y * 0.5f + 52f), 30, ClosePanels);

        return panel;
    }

    private void ClosePanels()
    {
        if (savePanel != null) savePanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        pendingDeleteSlot = -1;
        RefreshContinueState();
    }

    // ============================================================ 存档面板

    private void OpenSavePanel()
    {
        if (savePanel == null) return;
        pendingDeleteSlot = -1;
        RebuildSaveList();
        savePanel.SetActive(true);
    }

    private void RebuildSaveList()
    {
        if (saveListContent == null) return;

        for (int i = saveListContent.childCount - 1; i >= 0; i--)
        {
            Transform child = saveListContent.GetChild(i);
            if (child != null) Destroy(child.gameObject);
        }

        float rowHeight = 124f;
        float gap = 14f;
        float top = (SaveSystem.SlotCount - 1) * 0.5f * (rowHeight + gap);
        float width = saveListContent.sizeDelta.x;

        for (int i = 0; i < SaveSystem.SlotCount; i++)
        {
            int slot = i + 1;
            SaveData data = SaveSystem.ReadSlot(slot);
            BuildSaveRow(saveListContent, slot, data, new Vector2(0f, top - i * (rowHeight + gap)), width, rowHeight);
        }
    }

    private void BuildSaveRow(Transform parent, int slot, SaveData data, Vector2 pos, float width, float height)
    {
        bool empty = data == null;

        Image row = UiKit.MakeImage("Slot_" + slot, parent, rowColor);
        RectTransform rt = row.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, height);

        Outline ol = row.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0f, 0f, 0f, 0.6f);
        ol.effectDistance = new Vector2(2f, -2f);

        Text name = UiKit.MakeText("Name", row.transform, 30, textColor);
        name.text = empty ? "空存档位 " + slot : data.title;
        name.alignment = TextAnchor.MiddleLeft;
        RectTransform nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0f, 0.5f);
        nrt.anchorMax = new Vector2(0f, 0.5f);
        nrt.pivot = new Vector2(0f, 0.5f);
        nrt.anchoredPosition = new Vector2(26f, 22f);
        nrt.sizeDelta = new Vector2(300f, 44f);

        // 存档时间：右端顶到 x=100，避开右侧按钮区
        Text timeText = UiKit.MakeText("Time", row.transform, 22, dimTextColor);
        timeText.text = empty ? "" : SaveSystem.TimeText(data);
        timeText.alignment = TextAnchor.MiddleRight;
        RectTransform ttrt = timeText.rectTransform;
        ttrt.anchorMin = new Vector2(0f, 0.5f);
        ttrt.anchorMax = new Vector2(0f, 0.5f);
        ttrt.pivot = new Vector2(1f, 0.5f);
        ttrt.anchoredPosition = new Vector2(580f, 22f);
        ttrt.sizeDelta = new Vector2(200f, 40f);

        Text info = UiKit.MakeText("Info", row.transform, 22, dimTextColor);
        info.text = empty ? "还没有进度，点右侧按钮开一局" : data.Summary();
        info.alignment = TextAnchor.MiddleLeft;
        RectTransform irt = info.rectTransform;
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(26f, -26f);
        irt.sizeDelta = new Vector2(560f, 40f);

        // 主按钮：有档=读取，空槽=新游戏
        string mainLabel = empty ? "新游戏" : "读取";
        int capturedSlot = slot;
        MakeButton(row.transform, mainLabel, new Vector2(170f, 64f),
            new Vector2(width * 0.5f - 280f, 0f), 28, delegate { OnSlotMainClicked(capturedSlot); });

        if (empty) return;

        bool confirming = pendingDeleteSlot == slot;
        MakeButton(row.transform, confirming ? "确认删除" : "删除", new Vector2(170f, 64f),
            new Vector2(width * 0.5f - 95f, 0f), 28, delegate { OnSlotDeleteClicked(capturedSlot); });
    }

    private void OnSlotMainClicked(int slot)
    {
        ClosePanels();

        SaveData data = SaveSystem.ReadSlot(slot);
        if (data == null)
            SaveSystem.StartNewGame(slot, startSceneName, startEntryID);
        else
            SaveSystem.LoadSave(data);
    }

    private void OnSlotDeleteClicked(int slot)
    {
        if (pendingDeleteSlot != slot)
        {
            pendingDeleteSlot = slot;
            pendingDeleteTimer = 3.5f;
            RebuildSaveList();
            ShowToast("再点一次「确认删除」就真的删掉");
            return;
        }

        SaveSystem.DeleteSlot(slot);
        pendingDeleteSlot = -1;
        RebuildSaveList();
        ShowToast("已删除存档 " + slot);
    }

    // ============================================================ 设置面板

    private void OpenSettingsPanel()
    {
        if (settingsPanel == null) return;
        RebuildSettings();
        settingsPanel.SetActive(true);
    }

    private void RebuildSettings()
    {
        if (settingsContent == null) return;

        for (int i = settingsContent.childCount - 1; i >= 0; i--)
        {
            Transform child = settingsContent.GetChild(i);
            if (child != null) Destroy(child.gameObject);
        }

        float width = settingsContent.sizeDelta.x;
        float step = 92f;
        float top = 130f;

        MakeStepper(settingsContent, "主音量", new Vector2(0f, top), width, GetMaster(), SetMaster);
        MakeStepper(settingsContent, "音乐音量", new Vector2(0f, top - step), width, GetMusic(), SetMusic);
        MakeStepper(settingsContent, "音效音量", new Vector2(0f, top - step * 2f), width, GetSfx(), SetSfx);

        MakeToggleRow(settingsContent, "全屏", new Vector2(0f, top - step * 3f), width, Screen.fullScreen,
            delegate(bool on)
            {
                Screen.fullScreen = on;
                SetInt(PrefFullScreen, on ? 1 : 0);
                ShowToast(on ? "已切换到全屏" : "已切换到窗口");
            });
    }

    private void MakeStepper(Transform parent, string label, Vector2 pos, float width, float value, Action<float> onChanged)
    {
        float left = -width * 0.5f + 20f;

        Text t = UiKit.MakeText("Label_" + label, parent, 30, textColor);
        t.text = label;
        t.alignment = TextAnchor.MiddleLeft;
        RectTransform trt = t.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.5f);
        trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0f, 0.5f);
        trt.anchoredPosition = new Vector2(left, pos.y);
        trt.sizeDelta = new Vector2(230f, 60f);

        float barX = left + 260f;
        float barW = 300f;

        Image barBg = UiKit.MakeImage("BarBg", parent, new Color(0.05f, 0.04f, 0.07f, 0.9f));
        RectTransform brt = barBg.rectTransform;
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0f, 0.5f);
        brt.anchoredPosition = new Vector2(barX, pos.y);
        brt.sizeDelta = new Vector2(barW, 22f);

        Image fill = UiKit.MakeImage("Fill", barBg.transform, accentColor);
        RectTransform frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(0f, 1f);
        frt.pivot = new Vector2(0f, 0.5f);
        frt.anchoredPosition = new Vector2(2f, 0f);
        frt.sizeDelta = new Vector2(Mathf.Max(0f, (barW - 4f) * Mathf.Clamp01(value)), -4f);

        Text valueText = UiKit.MakeText("Value", parent, 26, dimTextColor);
        valueText.text = Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        valueText.alignment = TextAnchor.MiddleLeft;
        RectTransform vrt = valueText.rectTransform;
        vrt.anchorMin = new Vector2(0.5f, 0.5f);
        vrt.anchorMax = new Vector2(0.5f, 0.5f);
        vrt.pivot = new Vector2(0f, 0.5f);
        vrt.anchoredPosition = new Vector2(barX + barW + 16f, pos.y);
        vrt.sizeDelta = new Vector2(110f, 60f);

        Action<float> apply = delegate(float v)
        {
            v = Mathf.Clamp01(v);
            frt.sizeDelta = new Vector2(Mathf.Max(0f, (barW - 4f) * v), -4f);
            valueText.text = Mathf.RoundToInt(v * 100f) + "%";
            if (onChanged != null) onChanged(v);
        };

        float btnX = barX + barW + 120f;
        MakeButton(parent, "-", new Vector2(56f, 52f), new Vector2(btnX, pos.y), 30,
            delegate { apply(Mathf.Clamp01(value) - 0.05f); RebuildSettings(); });
        MakeButton(parent, "+", new Vector2(56f, 52f), new Vector2(btnX + 70f, pos.y), 30,
            delegate { apply(Mathf.Clamp01(value) + 0.05f); RebuildSettings(); });
    }

    private void MakeToggleRow(Transform parent, string label, Vector2 pos, float width, bool value, Action<bool> onChanged)
    {
        float left = -width * 0.5f + 20f;

        Text t = UiKit.MakeText("Label_" + label, parent, 30, textColor);
        t.text = label;
        t.alignment = TextAnchor.MiddleLeft;
        RectTransform trt = t.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.5f);
        trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0f, 0.5f);
        trt.anchoredPosition = new Vector2(left, pos.y);
        trt.sizeDelta = new Vector2(230f, 60f);

        MakeButton(parent, value ? "开" : "关", new Vector2(120f, 56f),
            new Vector2(left + 300f, pos.y), 30, delegate { if (onChanged != null) onChanged(!value); });
    }

    // ---- 设置项读写（PlayerPrefs）

    private const string PrefMaster = "农场RPG.Settings.Master";
    private const string PrefMusic = "农场RPG.Settings.Music";
    private const string PrefSfx = "农场RPG.Settings.Sfx";
    private const string PrefFullScreen = "农场RPG.Settings.FullScreen";

    private static float GetFloat(string key, float fallback)
    {
        return PlayerPrefs.GetFloat(key, fallback);
    }

    private static void SetFloat(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
    }

    private static void SetInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
    }

    private float GetMaster() { return GetFloat(PrefMaster, 1f); }
    private void SetMaster(float v) { SetFloat(PrefMaster, v); AudioListener.volume = v; }

    private float GetMusic() { return GetFloat(PrefMusic, 0.8f); }
    private void SetMusic(float v) { SetFloat(PrefMusic, v); }

    private float GetSfx() { return GetFloat(PrefSfx, 0.8f); }
    private void SetSfx(float v) { SetFloat(PrefSfx, v); }

    // ============================================================ 按钮行为

    private void RefreshContinueState()
    {
        if (continueButton == null) return;

        int latest = SaveSystem.LatestSlot();
        bool has = latest > 0;

        continueButton.interactable = has;
        if (continueLabel != null)
            continueLabel.color = has ? textColor : dimTextColor;

        if (continueLabel != null)
            continueLabel.text = has ? "继续游戏" : "继续游戏（无存档）";
    }

    private void OnStartClicked()
    {
        ClosePanels();

        int slot = SaveSystem.FirstEmptySlot();
        if (slot < 0)
        {
            // 三个槽都满了：让玩家去存档面板挑一个覆盖，避免手滑顶掉进度
            ShowToast("存档位已满，请在「读取存档」里选一个覆盖");
            OpenSavePanel();
            return;
        }

        SaveSystem.StartNewGame(slot, startSceneName, startEntryID);
    }

    private void OnContinueClicked()
    {
        ClosePanels();

        int slot = SaveSystem.LatestSlot();
        if (slot < 0)
        {
            ShowToast("还没有存档，先点「开始游戏」");
            return;
        }

        SaveData data = SaveSystem.ReadSlot(slot);
        SaveSystem.LoadSave(data);
    }

    private void OnOpenSavesClicked()
    {
        OpenSavePanel();
    }

    private void OnOpenSettingsClicked()
    {
        OpenSettingsPanel();
    }

    private void OnQuitClicked()
    {
        // 编辑器里点退出会直接把 Play 停掉，看着就像"莫名其妙回到标题界面"，所以要确认一次
        if (!quitConfirming)
        {
            quitConfirming = true;
            quitConfirmTimer = 3f;
            if (quitLabel != null) quitLabel.text = "确认退出";
            ShowToast("再点一次「确认退出」才真的退出");
            return;
        }

        AppQuit.Request();
    }

    // ============================================================ 小工具

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }
}
