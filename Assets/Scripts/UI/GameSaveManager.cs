using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 游戏内的存档菜单。自动安装（[RuntimeInitializeOnLoadMethod]），场景里不用摆任何东西。
///
///   F5  : 快速保存到当前槽位
///   F9  : 快速读取当前槽位
///   Esc : 打开 / 关闭暂停菜单（继续 / 保存 / 读取 / 返回主菜单 / 退出）
///
/// 主菜单场景里会自动让位：检测到场景中有 MainMenuUI 就不响应热键，
/// 免得在标题界面按 Esc 弹出两个面板。
/// </summary>
public class GameSaveManager : MonoBehaviour
{
    [Header("热键")]
    public KeyCode quickSaveKey = KeyCode.F5;
    public KeyCode quickLoadKey = KeyCode.F9;
    public KeyCode pauseKey = KeyCode.Escape;

    [Header("返回主菜单")]
    public string mainMenuSceneName = "MainMenu";

    [Header("进入场景后自动存一次")]
    [Tooltip("新开局落位后坐标才确定，进场景一小会儿自动补存一次，读档才不会站在原点")]
    public bool autoSaveOnSceneLoaded = true;
    public float autoSaveDelay = 0.8f;

    [Header("配色")]
    public Color textColor = new Color(0.96f, 0.93f, 0.86f);
    public Color dimTextColor = new Color(0.68f, 0.65f, 0.60f);
    public Color buttonColor = new Color(0.16f, 0.13f, 0.20f, 0.94f);
    public Color accentColor = new Color(0.98f, 0.76f, 0.32f);
    public Color panelColor = new Color(0.09f, 0.08f, 0.12f, 0.97f);

    public static GameSaveManager Instance { get; private set; }

    private Canvas canvas;
    private GameObject pausePanel;
    private RectTransform pauseContent;
    private Text toastText;
    private float toastTimer;

    private bool paused;
    private bool inMenuScene;
    private bool built;
    private float pendingAutoSave = -1f;

    private bool quitConfirming;
    private float quitConfirmTimer;

    // ============================================================ 生命周期

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindObjectOfType<GameSaveManager>() != null) return;

        GameObject go = new GameObject("~GameSaveManager");
        go.hideFlags = HideFlags.DontSaveInEditor;
        DontDestroyOnLoad(go);
        go.AddComponent<GameSaveManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        Build();
        RefreshSceneContext();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this)
        {
            Instance = null;
            SceneTransition.InputLocked = false;
            Time.timeScale = 1f;
        }
    }

    private void Update()
    {
        if (inMenuScene) return;

        SaveSystem.Tick(Time.unscaledDeltaTime);

        if (Input.GetKeyDown(quickSaveKey)) QuickSave();
        if (Input.GetKeyDown(quickLoadKey)) QuickLoad();

        if (Input.GetKeyDown(pauseKey))
        {
            // 对话进行中让对话系统先用 Esc，别抢
            if (DialogueManager.IsOpen) return;
            if (paused) Resume();
            else Pause();
        }

        if (pendingAutoSave > 0f)
        {
            pendingAutoSave -= Time.unscaledDeltaTime;
            if (pendingAutoSave <= 0f)
            {
                pendingAutoSave = -1f;
                QuickSave();
            }
        }

        if (quitConfirming)
        {
            quitConfirmTimer -= Time.unscaledDeltaTime;
            if (quitConfirmTimer <= 0f)
            {
                quitConfirming = false;
                if (paused) RebuildPausePanel();
            }
        }

        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            if (toastText != null)
            {
                Color c = toastText.color;
                c.a = Mathf.Clamp01(toastTimer / 0.5f);
                toastText.color = c;
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshSceneContext();

        if (inMenuScene || !autoSaveOnSceneLoaded) return;
        if (SaveSystem.CurrentSlot <= 0) return;

        pendingAutoSave = autoSaveDelay;
    }

    private void RefreshSceneContext()
    {
        Scene active = SceneManager.GetActiveScene();

        // 不能只写 FindObjectOfType<MainMenuUI>() != null：
        // 被 DontDestroyOnLoad 带进游戏场景的菜单 UI 也会被找到（它此刻还没销毁完），
        // 那样游戏里按 Esc / F5 就全哑了。所以要确认它确实属于当前场景。
        MainMenuUI ui = FindObjectOfType<MainMenuUI>();
        bool uiBelongsHere = ui != null && ui.gameObject != null && ui.gameObject.scene == active;

        inMenuScene = uiBelongsHere || active.name == mainMenuSceneName;

        if (inMenuScene && paused) Resume();
    }

    // ============================================================ 存 / 读

    public void QuickSave()
    {
        int slot = SaveSystem.CurrentSlot;

        if (slot < 1 || slot > SaveSystem.SlotCount)
        {
            slot = SaveSystem.FirstEmptySlot();
            if (slot < 0) slot = SaveSystem.LatestSlot();
            if (slot < 0) slot = 1;
        }

        SaveData data = SaveSystem.CaptureCurrent(slot);
        SaveSystem.WriteSlot(data);
        SaveSystem.CurrentSlot = slot;

        ShowToast("已保存到槽位 " + slot);
    }

    public void QuickLoad()
    {
        int slot = SaveSystem.CurrentSlot;
        if (slot < 1) slot = SaveSystem.LatestSlot();

        SaveData data = slot > 0 ? SaveSystem.ReadSlot(slot) : null;
        if (data == null)
        {
            ShowToast("没有可读取的存档");
            return;
        }

        Resume();

        // 目标场景就是当前场景、且没被强制改场景（坐标才有意义）：
        // 直接把人搬过去，省一次场景重载，读档更顺滑
        string target = SaveSystem.ResolveLoadScene(data);
        if (!string.IsNullOrEmpty(target)
            && target == SceneManager.GetActiveScene().name
            && target == data.sceneName)
        {
            ApplyPosition(data.Position);
            ShowToast("已读取存档 " + slot);
            return;
        }

        SaveSystem.LoadSave(data);
    }

    private void ApplyPosition(Vector3 pos)
    {
        Transform player = FindPlayer();
        if (player == null) return;

        player.position = pos;

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.position = pos;
        }

        Physics2D.SyncTransforms();

        CameraFollow follow = FindObjectOfType<CameraFollow>();
        if (follow != null)
        {
            follow.target = player;
            follow.SnapToTarget();
        }
    }

    private static Transform FindPlayer()
    {
        if (PersistentPlayer.Instance != null && PersistentPlayer.Instance.gameObject != null)
            return PersistentPlayer.Instance.transform;

        try
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) return go.transform;
        }
        catch (UnityException) { }

        return null;
    }

    // ============================================================ 暂停菜单

    public void Pause()
    {
        if (paused) return;

        paused = true;
        Time.timeScale = 0f;
        SceneTransition.InputLocked = true;

        if (pausePanel != null)
        {
            RebuildPausePanel();
            pausePanel.SetActive(true);
        }
    }

    public void Resume()
    {
        if (!paused) return;

        paused = false;
        Time.timeScale = 1f;
        SceneTransition.InputLocked = false;

        if (pausePanel != null) pausePanel.SetActive(false);
    }

    private void RebuildPausePanel()
    {
        if (pauseContent == null) return;

        for (int i = pauseContent.childCount - 1; i >= 0; i--)
        {
            Transform child = pauseContent.GetChild(i);
            if (child != null) Destroy(child.gameObject);
        }

        float step = 84f;
        float top = 120f;

        MakePauseButton("继续游戏", new Vector2(0f, top), Resume);
        MakePauseButton("保存游戏", new Vector2(0f, top - step), delegate { QuickSave(); Resume(); });
        MakePauseButton("读取存档", new Vector2(0f, top - step * 2f), delegate { QuickLoad(); });
        MakePauseButton("返回主菜单", new Vector2(0f, top - step * 3f), OnBackToMenu);

        // 退出要点两次：编辑器里点退出会直接把 Play 停掉，手滑一下就得重开
        MakePauseButton(quitConfirming ? "确认退出" : "退出游戏",
            new Vector2(0f, top - step * 4f), OnQuitClicked);
    }

    private void OnQuitClicked()
    {
        if (!quitConfirming)
        {
            quitConfirming = true;
            quitConfirmTimer = 3f;
            RebuildPausePanel();
            ShowToast("再点一次「确认退出」才真的退出");
            return;
        }

        Resume();
        AppQuit.Request();
    }

    private void MakePauseButton(string label, Vector2 pos, Action onClick)
    {
        MakeButton(pauseContent, label, new Vector2(360f, 68f), pos, 30, onClick);
    }

    private void OnBackToMenu()
    {
        // 回主菜单前先存一次，免得白玩
        QuickSave();
        Resume();

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(mainMenuSceneName, "Start");
        else
            SceneManager.LoadScene(mainMenuSceneName);
    }

    // ============================================================ 搭建

    private void Build()
    {
        if (built) return;
        built = true;

        EnsureEventSystem();

        canvas = MakeCanvas();
        Transform root = canvas.transform;

        pausePanel = BuildPausePanel(root);
        pausePanel.SetActive(false);

        BuildToast(root);
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
        GameObject go = new GameObject("GameMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);

        Canvas c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 900;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return c;
    }

    private GameObject BuildPausePanel(Transform parent)
    {
        GameObject panel = new GameObject("PausePanel", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        UiKit.Stretch(panel.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

        Image shade = UiKit.MakeImage("Shade", panel.transform, new Color(0f, 0f, 0f, 0.5f));
        UiKit.Stretch(shade.rectTransform, 0f, 0f, 0f, 0f);
        shade.raycastTarget = true;

        Image card = UiKit.MakeImage("Card", panel.transform, panelColor);
        RectTransform crt = card.rectTransform;
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(560f, 620f);

        Outline ol = card.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0f, 0f, 0f, 0.8f);
        ol.effectDistance = new Vector2(3f, -3f);

        Text head = UiKit.MakeText("Title", card.transform, 40, accentColor);
        head.text = "暂停";
        head.alignment = TextAnchor.MiddleCenter;
        RectTransform hrt = head.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 1f);
        hrt.anchorMax = new Vector2(0.5f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = new Vector2(0f, -24f);
        hrt.sizeDelta = new Vector2(480f, 64f);

        Text hint = UiKit.MakeText("Hint", card.transform, 22, dimTextColor);
        hint.text = "F5 保存 · F9 读档";
        hint.alignment = TextAnchor.LowerCenter;
        RectTransform hintRt = hint.rectTransform;
        hintRt.anchorMin = new Vector2(0.5f, 0f);
        hintRt.anchorMax = new Vector2(0.5f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 18f);
        hintRt.sizeDelta = new Vector2(480f, 36f);

        GameObject content = new GameObject("Content", typeof(RectTransform));
        RectTransform bodyRt = content.GetComponent<RectTransform>();
        content.transform.SetParent(card.transform, false);
        bodyRt.anchorMin = new Vector2(0.5f, 0.5f);
        bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
        bodyRt.pivot = new Vector2(0.5f, 0.5f);
        bodyRt.anchoredPosition = new Vector2(0f, -20f);
        bodyRt.sizeDelta = new Vector2(480f, 440f);
        pauseContent = bodyRt;

        return panel;
    }

    private void BuildToast(Transform parent)
    {
        Text t = UiKit.MakeText("Toast", parent, 28, accentColor);
        t.text = "";
        t.alignment = TextAnchor.UpperCenter;

        Outline ol = t.GetComponent<Outline>();
        if (ol != null) ol.effectDistance = new Vector2(2f, -2f);

        RectTransform rt = t.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -40f);
        rt.sizeDelta = new Vector2(900f, 60f);

        Color c = t.color;
        c.a = 0f;
        t.color = c;

        toastText = t;
    }

    public void ShowToast(string msg)
    {
        if (toastText == null) return;
        toastText.text = msg;
        Color c = toastText.color;
        c.a = 1f;
        toastText.color = c;
        toastTimer = 1.6f;
    }

    // ============================================================ 小工具

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
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        Text t = UiKit.MakeText("Label", go.transform, fontSize, textColor);
        t.text = label;
        t.alignment = TextAnchor.MiddleCenter;
        UiKit.Stretch(t.rectTransform, 0f, 0f, 0f, 0f);

        if (onClick != null)
        {
            Action handler = onClick;
            btn.onClick.AddListener(() => handler());
        }

        return btn;
    }
}
