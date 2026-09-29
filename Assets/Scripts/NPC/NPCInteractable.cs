using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 挂在任何想跟他说话的角色身上（NPC、告示牌、猫……都行）。
///
/// 【最快上手】
///   Unity 菜单 Tools ▸ 农场RPG ▸ NPC ▸ 1. 在当前场景创建示例 NPC
///   会自动建好一个带对话的 NPC。想自己搭就：
///     1. 场景里放一个带 SpriteRenderer 的物体
///     2. 挂上这个脚本
///     3. 在 Inspector 的 First Dialogue 里填几句话
///     4. 运行，走近他按 E
///
/// 【灵感到星露谷】
///   走近 → 头顶冒出「按 E 说话」；对话时人物被锁住不能动、不会挥刀；
///   同一段话可以设「第一次说 / 以后再说」，说完还能触发一个 UnityEvent
///   （以后接任务、送礼、开店都从这个事件往外长）。
/// </summary>
[DefaultExecutionOrder(-10)]
public class NPCInteractable : MonoBehaviour
{
    [Header("身份")]
    [Tooltip("对话里显示的名字")]
    public string npcName = "村民";

    [Header("对话内容")]
    [Tooltip("可选：引用一段对话资源（多个 NPC 共用一段话时用）。填了它就优先用它")]
    public DialogueAsset dialogueAsset;

    [Tooltip("第一次说的话（逐句播放）")]
    public List<DialogueLine> firstDialogue = new List<DialogueLine>();

    [Tooltip("第二次及以后说的话。留空 = 每次都说同一段")]
    public List<DialogueLine> repeatDialogue = new List<DialogueLine>();

    [Tooltip("勾选 = 这个 NPC 只能对话一次（说完就不再出现提示）")]
    public bool talkOnlyOnce = false;

    [Header("交互")]
    [Tooltip("走近多少距离内可以对话（世界单位）")]
    public float interactRange = 2.4f;

    [Tooltip("对话按键")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("勾选 = 鼠标左键也能对话（靠近时左键优先触发对话而非攻击）")]
    public bool enableMouseClick = true;

    [Header("头顶提示")]
    public bool showPrompt = true;
    [Tooltip("提示文字")]
    public string promptText = "按 E 说话";
    [Tooltip("提示挂在脚底往上多高的位置（世界单位）")]
    public float promptHeight = 1.5f;

    [Header("朝向")]
    [Tooltip("勾选 = 玩家走近时身体转向玩家那一侧（靠翻转精灵实现）")]
    public bool facePlayer = true;
    [Tooltip("转过来之后发现是背对着你，就把这个勾上")]
    public bool invertFlip = false;

    [Header("事件")]
    [Tooltip("对话结束后触发。可以在这里挂「给任务」「开商店」之类的响应")]
    public UnityEngine.Events.UnityEvent onDialogueEnd;

    private SpriteRenderer _sr;
    private PlayerMovement _player;
    private Canvas _promptCanvas;
    private Text _promptLabel;
    private GameObject _promptRoot;

    private bool _promptVisible;
    private bool _talked;
    private bool _inRange;

    /// <summary>找不到玩家时不要每帧全场搜索，隔一会儿再找一次</summary>
    private float _searchCooldown;

    // ------------------------------------------------------------------ 生命周期

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        _player = null;   // 换场景了，缓存的玩家不能再用
        _inRange = false;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_inRange)
            InteractionSystem.UnregisterInRange(this);
        _inRange = false;
    }

    private void OnDestroy()
    {
        if (_inRange)
            InteractionSystem.UnregisterInRange(this);
        _inRange = false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _player = null;
    }

    private void Update()
    {
        if (_player == null)
        {
            // 玩家可能还没生成（刚进场景 / 刚切场景），别每帧全场搜索
            _searchCooldown -= Time.deltaTime;
            if (_searchCooldown > 0f) return;

            _searchCooldown = 0.4f;
            _player = FindObjectOfType<PlayerMovement>();
            if (_player == null) return;
        }

        Vector2 self = transform.position;
        Vector2 other = _player.transform.position;
        float dist = Vector2.Distance(self, other);

        bool inRange = dist <= interactRange;
        bool usable = !(talkOnlyOnce && _talked);

        bool wasInRange = _inRange;
        _inRange = inRange && usable;

        if (_inRange && !wasInRange)
            InteractionSystem.RegisterInRange(this);
        else if (!_inRange && wasInRange)
            InteractionSystem.UnregisterInRange(this);

        if (showPrompt) SetPromptVisible(inRange && usable && !DialogueManager.IsOpen);

        if (facePlayer && inRange && _sr != null)
        {
            bool playerOnLeft = other.x < self.x;
            _sr.flipX = invertFlip ? !playerOnLeft : playerOnLeft;
        }

        if (!inRange || !usable) return;
        if (SceneTransition.InputBlocked) return;
        if (DialogueManager.IsOpen) return;

        bool keyPressed = Input.GetKeyDown(interactKey);
        bool mousePressed = enableMouseClick && Input.GetMouseButtonDown(0);

        if (keyPressed || mousePressed)
            Talk();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 0.6f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }

    // ------------------------------------------------------------------ 对话

    /// <summary>开始对话（脚本也能主动调，比如剧情触发）</summary>
    public void Talk()
    {
        IList<DialogueLine> lines = PickLines();

        if (lines == null || lines.Count == 0)
        {
            Debug.LogWarning($"[NPC] {name} 身上没填对话内容：Inspector 里填 First Dialogue，或者拖一个 Dialogue Asset 进来。", this);
            return;
        }

        _talked = true;
        SetPromptVisible(false);

        DialogueManager.Show(lines, npcName, () =>
        {
            if (onDialogueEnd != null)
                onDialogueEnd.Invoke();
        });
    }

    /// <summary>决定这次该播哪一段</summary>
    private IList<DialogueLine> PickLines()
    {
        if (dialogueAsset != null && dialogueAsset.HasLines)
            return dialogueAsset.lines;

        if (_talked && repeatDialogue != null && repeatDialogue.Count > 0)
            return repeatDialogue;

        return firstDialogue;
    }

    // ------------------------------------------------------------------ 头顶提示

    private void SetPromptVisible(bool visible)
    {
        if (!showPrompt) return;

        if (_promptRoot == null)
        {
            if (!visible) return;   // 还没建过，而且也不需要显示 —— 省一次创建
            BuildPrompt();
        }

        if (_promptCanvas != null && _promptCanvas.worldCamera == null)
            _promptCanvas.worldCamera = Camera.main;

        if (_promptVisible == visible && _promptRoot.activeSelf == visible) return;

        _promptVisible = visible;
        _promptRoot.SetActive(visible);
    }

    /// <summary>建一个挂在头顶的世界空间小 Canvas</summary>
    private void BuildPrompt()
    {
        GameObject go = new GameObject("InteractionPrompt");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, promptHeight, 0f);
        go.transform.localScale = Vector3.one * 0.005f;   // 参考像素 → 世界单位

        _promptCanvas = go.AddComponent<Canvas>();
        _promptCanvas.renderMode = RenderMode.WorldSpace;
        _promptCanvas.worldCamera = Camera.main;
        _promptCanvas.sortingOrder = 5000;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(320f, 90f);

        // 半透明底衬，白字在任何地图上都能看清
        Image bg = UiKit.MakeImage("Bg", go.transform, new Color(0f, 0f, 0f, 0.55f));
        UiKit.Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

        _promptLabel = UiKit.MakeText("Label", go.transform, 42, Color.white);
        _promptLabel.text = promptText;
        _promptLabel.alignment = TextAnchor.MiddleCenter;
        _promptLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Stretch(_promptLabel.rectTransform, 10f, 10f, 10f, 10f);

        _promptRoot = go;
        go.SetActive(false);
    }

    /// <summary>改提示文字（比如任务完成后变成「按 E 交任务」）</summary>
    public void SetPromptText(string text)
    {
        promptText = text;
        if (_promptLabel != null) _promptLabel.text = text;
    }
}
