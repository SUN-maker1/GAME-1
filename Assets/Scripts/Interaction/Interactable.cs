using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 通用互动组件 —— 挂在任何想让玩家互动的物体上（告示牌、宝箱、门、机器……都行）。
///
/// 【最快上手】
///   1. 场景里放一个物体（带 SpriteRenderer 或任意 GameObject）
///   2. 挂上这个脚本
///   3. 在 Inspector 里填互动文本（或拖一个 DialogueAsset 进来）
///   4. 运行，走近按 E 或鼠标左键
///
/// 【和 NPCInteractable 的区别】
///   NPCInteractable 是专门给 NPC 的（有立绘、表情、朝向玩家等功能）。
///   Interactable 是通用的 —— 任何物体都能挂，互动后弹出文本框显示自定义内容。
///
/// 【鼠标左键说明】
///   勾选 Enable Mouse Click 后，玩家靠近物体时按左键会触发互动而非攻击。
///   远离物体时左键仍然正常攻击。
/// </summary>
[DefaultExecutionOrder(-10)]
public class Interactable : MonoBehaviour
{
    [Header("互动文本")]
    [Tooltip("可选：引用一段对话资源。填了它就优先用它")]
    public DialogueAsset dialogueAsset;

    [Tooltip("互动后显示的文本（逐句播放）")]
    public List<DialogueLine> dialogueLines = new List<DialogueLine>();

    [Tooltip("说话人名字（对话框顶部显示）")]
    public string speakerName = "";

    [Header("交互设置")]
    [Tooltip("走近多少距离内可以互动（世界单位）")]
    public float interactRange = 2.4f;

    [Tooltip("键盘互动按键")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("勾选 = 鼠标左键也能互动（靠近时左键优先触发互动而非攻击）")]
    public bool enableMouseClick = true;

    [Header("头顶提示")]
    public bool showPrompt = true;
    [Tooltip("提示文字")]
    public string promptText = "按 E 互动";
    [Tooltip("提示挂在脚底往上多高的位置（世界单位）")]
    public float promptHeight = 1.5f;

    [Header("人物立绘")]
    [Tooltip("立绘图片（透明底 PNG，512x512 起）。设置后自动应用到所有对话行")]
    public Sprite portrait;

    [Tooltip("立绘显示在哪一侧")]
    public DialoguePortraitSide portraitSide = DialoguePortraitSide.Inherit;

    [Header("事件")]
    [Tooltip("互动文本结束后触发")]
    public UnityEngine.Events.UnityEvent onInteractEnd;

    private PlayerMovement _player;
    private Canvas _promptCanvas;
    private Text _promptLabel;
    private GameObject _promptRoot;
    private bool _promptVisible;
    private float _searchCooldown;
    private bool _inRange;

    private void OnEnable()
    {
        _player = null;
        _inRange = false;
    }

    private void Update()
    {
        if (_player == null)
        {
            _searchCooldown -= Time.deltaTime;
            if (_searchCooldown > 0f) return;
            _searchCooldown = 0.4f;
            _player = FindObjectOfType<PlayerMovement>();
            if (_player == null) return;
        }

        Vector2 self = transform.position;
        Vector2 other = _player.transform.position;
        float dist = Vector2.Distance(self, other);
        bool wasInRange = _inRange;
        _inRange = dist <= interactRange;

        if (_inRange && !wasInRange)
            InteractionSystem.RegisterInRange(this);
        else if (!_inRange && wasInRange)
            InteractionSystem.UnregisterInRange(this);

        if (showPrompt) SetPromptVisible(_inRange && !DialogueManager.IsOpen);

        if (!_inRange) return;
        if (SceneTransition.InputBlocked) return;
        if (DialogueManager.IsOpen) return;

        bool keyPressed = Input.GetKeyDown(interactKey);
        bool mousePressed = enableMouseClick && Input.GetMouseButtonDown(0);

        if (keyPressed || mousePressed)
            Interact();
    }

    private void OnDisable()
    {
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

    /// <summary>开始互动（脚本也能主动调）</summary>
    public void Interact()
    {
        IList<DialogueLine> lines = GetLines();
        if (lines == null || lines.Count == 0)
        {
            Debug.LogWarning($"[Interactable] {name} 身上没填互动文本：Inspector 里填 Dialogue Lines，或者拖一个 Dialogue Asset 进来。", this);
            return;
        }

        SetPromptVisible(false);
        DialogueManager.Show(lines, speakerName, () =>
        {
            if (onInteractEnd != null)
                onInteractEnd.Invoke();
        });
    }

    private IList<DialogueLine> GetLines()
    {
        if (dialogueAsset != null && dialogueAsset.HasLines)
            return dialogueAsset.lines;
        return dialogueLines;
    }

    private void SetPromptVisible(bool visible)
    {
        if (!showPrompt) return;

        if (_promptRoot == null)
        {
            if (!visible) return;
            BuildPrompt();
        }

        if (_promptCanvas != null && _promptCanvas.worldCamera == null)
            _promptCanvas.worldCamera = Camera.main;

        if (_promptVisible == visible && _promptRoot.activeSelf == visible) return;

        _promptVisible = visible;
        _promptRoot.SetActive(visible);
    }

    private void BuildPrompt()
    {
        GameObject go = new GameObject("InteractionPrompt");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, promptHeight, 0f);
        go.transform.localScale = Vector3.one * 0.005f;

        _promptCanvas = go.AddComponent<Canvas>();
        _promptCanvas.renderMode = RenderMode.WorldSpace;
        _promptCanvas.worldCamera = Camera.main;
        _promptCanvas.sortingOrder = 5000;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(320f, 90f);

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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 0.6f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }

    /// <summary>把立绘应用到所有对话行（编辑器调用）</summary>
    public void ApplyPortraitToAllLines()
    {
        if (portrait == null) return;

        foreach (DialogueLine line in dialogueLines)
        {
            line.portrait = portrait;
            line.portraitSide = portraitSide;
        }
    }

    /// <summary>把立绘应用到 DialogueAsset 的所有对话行（编辑器调用）</summary>
    public void ApplyPortraitToAsset()
    {
        if (portrait == null || dialogueAsset == null) return;

        foreach (DialogueLine line in dialogueAsset.lines)
        {
            line.portrait = portrait;
            line.portraitSide = portraitSide;
        }
    }

    // ------------------------------------------------------------------ 右键快捷菜单

#if UNITY_EDITOR
    /// <summary>右键组件标题 ▸ 添加一行对话</summary>
    [ContextMenu("添加一行对话")]
    public void AddDialogueLine()
    {
        dialogueLines.Add(new DialogueLine());
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>右键组件标题 ▸ 清空所有对话</summary>
    [ContextMenu("清空所有对话")]
    public void ClearDialogueLines()
    {
        dialogueLines.Clear();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>右键组件标题 ▸ 创建对话资源（保存为DialogueAsset）</summary>
    [ContextMenu("创建对话资源（保存为Asset）")]
    public void CreateDialogueAssetFromEditor()
    {
        UnityEditor.Selection.activeObject = this;
    }

    /// <summary>右键组件标题 ▸ 应用立绘到所有对话行</summary>
    [ContextMenu("应用立绘到所有对话行")]
    public void ApplyPortraitContextMenu()
    {
        ApplyPortraitToAllLines();
        if (dialogueAsset != null)
            ApplyPortraitToAsset();
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.EditorUtility.SetDirty(dialogueAsset);
    }
#endif
}
