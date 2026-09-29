using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 极简对话脚本 —— 一个脚本全搞定，交互方式模仿星露谷：
///   1. 玩家走到本物体附近（距离 < range）按 E，弹出一个底部对话框
///   2. 对话框显示：说话人名字 + 对话内容（打字机逐字显示）+ 左侧立绘（可选）
///   3. 按 E：打字中 = 立即显示完整句子；显示完了 = 切下一句
///   4. 全部说完自动关闭面板，解除玩家移动锁定
///
/// 【为什么简单】
///   - 不用建 ScriptableObject 配置文件：对话直接填在 Inspector 里
///   - 不用手动搭 UI 面板：对话框和提示文字运行时自动生成
///   - 不用配 Collider/Trigger：用距离检测（和星露谷一样）
///   - 文字用 uGUI + 系统字体（UiKit.CjkFont），中文直接显示，不需要 TMP 字体资源
///   - 玩家移动锁定走全局 SceneTransition.InputLocked，自动恢复
///
/// 【使用步骤】
///   1. 把本脚本挂到 NPC/物品 上
///   2. 在 Inspector 的「对话内容」列表里点 + 添加对话行（名字 / 文本 / 立绘）
///   3. 确认玩家物体上的 Tag 是 Player（一般已经是）
///   4. 运行，走进 range 范围按 E 即可对话
/// </summary>
public class SimpleDialogue : MonoBehaviour
{
    /// <summary>一句对话 = 名字 + 内容 + 立绘（可空）</summary>
    [System.Serializable]
    public class Line
    {
        [Tooltip("说话人名字，可空")]
        public string speaker = "";

        [TextArea(2, 4)]
        [Tooltip("对话内容")]
        public string text = "";

        [Tooltip("这句话的立绘，可空（留空则不显示立绘）")]
        public Sprite portrait;
    }

    [Header("对话内容（直接在下面填）")]
    public List<Line> lines = new List<Line>();

    [Header("触发设置")]
    [Tooltip("玩家离这个物体多近时可以对话（世界单位）")]
    public float range = 1.5f;

    [Tooltip("对话用的按键")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("靠近时屏幕下方显示的提示文字；留空 = 不显示提示")]
    public string promptText = "按 E 对话";

    [Header("打字机")]
    [Tooltip("每个字符出现的间隔秒数，越小越快")]
    public float typeSpeed = 0.03f;

    // ---- 运行时状态 ----
    private Transform player;
    private bool talking;
    private int lineIndex;
    private bool lineFullyShown;
    private string currentFullText;

    // 全局锁计数：场景里多个对话脚本不会互相抢着解锁
    private static int dialogueLocks;

    // ---- 运行时自动创建的 UI ----
    private bool ownCanvas;
    private Canvas canvas;
    private GameObject panel;
    private RectTransform textAreaRect;
    private Text nameText;
    private Text contentText;
    private Image portraitImage;
    private GameObject prompt;
    private Text promptLabel;

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player == null)
            Debug.LogWarning($"[SimpleDialogue] {name} 找不到带 Player 标签的物体，请确认玩家身上设置了 Tag = Player。", this);

        BuildUi();
    }

    private void Update()
    {
        // 对话进行中：只管 E 键翻页
        if (talking)
        {
            if (Input.GetKeyDown(interactKey))
            {
                if (!lineFullyShown) ShowFullLine(); // 打字中：一键显示完整
                else NextLine();                     // 已完整：切下一句
            }
            return;
        }

        if (player == null) return;

        // 距离检测（星露谷同款：走近就能说话，不需要碰撞体）
        bool inRange = Vector2.Distance(player.position, transform.position) <= range;

        // 靠近 + 按 E + 不在切场景过场 + 有台词 → 开始对话
        if (inRange && lines.Count > 0 && !SceneTransition.IsBusy && Input.GetKeyDown(interactKey))
            StartTalking();

        // 靠近提示（只在范围内且有台词时显示）
        if (prompt != null && !string.IsNullOrEmpty(promptText))
            prompt.SetActive(inRange && lines.Count > 0);
    }

    // ------------------------------------------------ 对话流程

    private void StartTalking()
    {
        if (panel == null)
        {
            Debug.LogError($"[SimpleDialogue] {name} 的对话 UI 没有创建成功，无法对话。", this);
            return;
        }

        talking = true;
        lineIndex = 0;
        panel.SetActive(true);
        dialogueLocks++;
        SceneTransition.InputLocked = dialogueLocks > 0; // 锁玩家移动
        ShowLine(0);
    }

    private void ShowLine(int index)
    {
        // 防御：UI 没建好或台词列表异常时直接结束，避免连环报错
        if (nameText == null || contentText == null || index < 0 || index >= lines.Count)
        {
            EndTalking();
            return;
        }

        Line line = lines[index];
        if (line == null)
        {
            EndTalking();
            return;
        }

        nameText.text = line.speaker;

        bool hasPortrait = line.portrait != null;
        if (portraitImage != null)
        {
            portraitImage.gameObject.SetActive(hasPortrait);
            if (hasPortrait) portraitImage.sprite = line.portrait;
            // 有立绘时文字往右让出位置，没有就占满整行
            textAreaRect.offsetMin = new Vector2(hasPortrait ? 250f : 16f, textAreaRect.offsetMin.y);
        }

        currentFullText = line.text;
        StartCoroutine(TypeText());
    }

    private IEnumerator TypeText()
    {
        lineFullyShown = false;
        contentText.text = "";
        WaitForSeconds wait = new WaitForSeconds(typeSpeed);
        foreach (char c in currentFullText)
        {
            contentText.text += c;
            yield return wait;
        }
        lineFullyShown = true;
    }

    private void ShowFullLine()
    {
        StopAllCoroutines();
        if (contentText == null) return;
        contentText.text = currentFullText;
        lineFullyShown = true;
    }

    private void NextLine()
    {
        StopAllCoroutines();
        lineIndex++;
        if (lineIndex >= lines.Count)
        {
            EndTalking();
            return;
        }
        ShowLine(lineIndex);
    }

    private void EndTalking()
    {
        StopAllCoroutines();
        talking = false;
        if (panel != null) panel.SetActive(false);
        dialogueLocks--;
        if (dialogueLocks < 0) dialogueLocks = 0;
        SceneTransition.InputLocked = dialogueLocks > 0; // 解除玩家移动锁定
    }

    // ------------------------------------------------ 运行时自动搭 UI

    private void BuildUi()
    {
        // 1) 找场景里已有的 Canvas，没有就自动建一个
        canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGo = new GameObject("DialogueCanvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            ownCanvas = true;
        }

        Sprite white = UiKit.WhiteSprite();

        // 2) 底部对话框
        panel = new GameObject("DialoguePanel");
        panel.transform.SetParent(canvas.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.sprite = white;
        panelImg.color = new Color(0.13f, 0.12f, 0.17f, 0.97f);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.04f, 0.05f);
        panelRect.anchorMax = new Vector2(0.96f, 0.30f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // 3) 左侧立绘
        GameObject portraitGo = new GameObject("Portrait");
        portraitGo.transform.SetParent(panel.transform, false);
        portraitImage = portraitGo.AddComponent<Image>();
        portraitImage.sprite = white;
        portraitImage.preserveAspect = true;
        RectTransform pr = portraitGo.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(0f, 1f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.anchoredPosition = new Vector2(120f, 0f);
        pr.sizeDelta = new Vector2(210f, 210f);

        // 4) 文字区：上方名字 + 下方正文（uGUI + 系统字体，中文直接显示）
        GameObject textArea = new GameObject("TextArea");
        textArea.transform.SetParent(panel.transform, false);
        textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = new Vector2(0f, 0.08f);
        textAreaRect.anchorMax = new Vector2(1f, 0.92f);
        textAreaRect.offsetMin = new Vector2(250f, 0f);
        textAreaRect.offsetMax = new Vector2(-24f, 0f);

        nameText = UiKit.MakeText("NameText", textArea.transform, 30, new Color(1f, 0.92f, 0.6f));
        nameText.alignment = TextAnchor.MiddleLeft;
        RectTransform nr = nameText.GetComponent<RectTransform>();
        nr.anchorMin = new Vector2(0f, 1f);
        nr.anchorMax = new Vector2(1f, 1f);
        nr.pivot = new Vector2(0f, 1f);
        nr.anchoredPosition = new Vector2(0f, 2f);
        nr.sizeDelta = new Vector2(0f, 36f);

        contentText = UiKit.MakeText("ContentText", textArea.transform, 26, Color.white);
        contentText.alignment = TextAnchor.UpperLeft;
        RectTransform cr = contentText.GetComponent<RectTransform>();
        cr.anchorMin = new Vector2(0f, 0f);
        cr.anchorMax = new Vector2(1f, 1f);
        cr.offsetMin = new Vector2(0f, 8f);
        cr.offsetMax = new Vector2(0f, -44f);

        // 5) 靠近提示（显示在对话框上沿）
        prompt = new GameObject("Prompt");
        prompt.transform.SetParent(canvas.transform, false);
        promptLabel = UiKit.MakeText("PromptText", prompt.transform, 28, new Color(1f, 1f, 0.8f));
        promptLabel.alignment = TextAnchor.MiddleCenter;
        promptLabel.text = promptText;
        RectTransform promptRect = prompt.GetComponent<RectTransform>();
        promptRect.anchorMin = new Vector2(0.5f, 0.30f);
        promptRect.anchorMax = new Vector2(0.5f, 0.30f);
        promptRect.pivot = new Vector2(0.5f, 0.5f);
        promptRect.anchoredPosition = new Vector2(0f, 18f);
        promptRect.sizeDelta = new Vector2(400f, 40f);

        panel.SetActive(false);
        prompt.SetActive(false);
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        if (talking)
        {
            talking = false;
            dialogueLocks--;
            if (dialogueLocks < 0) dialogueLocks = 0;
            SceneTransition.InputLocked = dialogueLocks > 0;
        }
        if (ownCanvas && canvas != null)
            Destroy(canvas.gameObject);
        else
        {
            if (panel != null) Destroy(panel);
            if (prompt != null) Destroy(prompt);
        }
    }
}
