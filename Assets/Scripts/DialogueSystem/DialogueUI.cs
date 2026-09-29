using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 对话 UI 管理器 —— 负责对话面板的显示、打字机效果、逐句推进。
///
/// 挂载位置：Canvas 下的「对话面板」物体上（可用菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 自动搭建对话UI 一键生成）。
///
/// 需要拖入的引用：
///   dialoguePanel     面板根物体（默认隐藏）
///   nameText          角色名字（TextMeshProUGUI）
///   contentText       对话内容（TextMeshProUGUI）
///   portraitImage     立绘（Image）
///   boxBackgroundImage 对话框背景（Image）
///
/// 每句对话可单独切换：名字 / 内容 / 立绘 / 对话框背景样式（boxStyle 为空时用 defaultBoxStyle）。
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("UI 引用")]
    [Tooltip("对话面板根物体（默认隐藏，触发对话后显示）")]
    public GameObject dialoguePanel;

    [Tooltip("角色名字文本 (TextMeshProUGUI)")]
    public TextMeshProUGUI nameText;

    [Tooltip("对话内容文本 (TextMeshProUGUI)")]
    public TextMeshProUGUI contentText;

    [Tooltip("角色立绘图片 (Image)")]
    public Image portraitImage;

    [Tooltip("对话框背景图片 (Image)")]
    public Image boxBackgroundImage;

    [Header("设置")]
    [Tooltip("默认对话框背景样式（对话行没指定 boxStyle 时使用）")]
    public Sprite defaultBoxStyle;

    [Tooltip("统一使用的 TMP 字体资源（中文对话必须指定含中文字形的 TMP 字体，否则显示方块）。\n" +
             "生成方法：Window ▸ TextMeshPro ▸ Font Asset Creator，源字体选系统中文字体（如微软雅黑）")]
    public TMP_FontAsset fontAsset;

    [Tooltip("推进对话的按键")]
    public KeyCode advanceKey = KeyCode.E;

    [Tooltip("打字机速度（字符/秒），0 = 立即显示整句")]
    public float typewriterSpeed = 30f;

    // ------------------------------------------------------------------ 内部状态

    private List<DialogueEntry> _currentLines;
    private int _currentIndex;
    private bool _isActive;
    private bool _isTyping;
    private Action _onDialogueEnd;
    private Coroutine _typewriterCoroutine;

    /// <summary>对话是否正在进行（DialogueTrigger 用它防止重复触发）</summary>
    public bool IsDialogueActive => _isActive;

    // ------------------------------------------------------------------ 生命周期

    private void Awake()
    {
        // 应用统一字体（没指定则用 TMP 默认字体 —— 注意默认 LiberationSans 不含中文！）
        if (fontAsset != null)
        {
            if (nameText != null) nameText.font = fontAsset;
            if (contentText != null) contentText.font = fontAsset;
        }
        else
        {
            Debug.LogWarning("[DialogueUI] 没有指定 fontAsset。中文对话会显示成方块！\n" +
                             "请用 Window ▸ TextMeshPro ▸ Font Asset Creator 生成中文字体资源后拖到这里。", this);
        }

        // 默认隐藏对话面板
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
    }

    private void Update()
    {
        if (!_isActive) return;

        // 按推进键：打字中 = 显示整句；已显示完 = 下一句
        if (Input.GetKeyDown(advanceKey))
        {
            if (_isTyping) StopTypewriter();
            else NextLine();
        }
    }

    // ------------------------------------------------------------------ 公共方法

    /// <summary>
    /// 开始一段对话。
    /// </summary>
    /// <param name="lines">对话行列表（来自 DialogueData.lines）</param>
    /// <param name="onDialogueEnd">对话结束回调（用于解锁玩家等）</param>
    public void StartDialogue(List<DialogueEntry> lines, Action onDialogueEnd = null)
    {
        // 基础规则：同一时间不能重复触发对话
        if (_isActive)
        {
            Debug.LogWarning("[DialogueUI] 对话正在进行中，忽略本次触发。", this);
            return;
        }

        if (lines == null || lines.Count == 0)
        {
            Debug.LogWarning("[DialogueUI] 对话数据为空，无法开始对话。", this);
            return;
        }

        if (dialoguePanel == null || contentText == null)
        {
            Debug.LogError("[DialogueUI] dialoguePanel 或 contentText 没有拖入引用，无法显示对话！", this);
            return;
        }

        _currentLines = lines;
        _currentIndex = 0;
        _onDialogueEnd = onDialogueEnd;
        _isActive = true;

        dialoguePanel.SetActive(true);
        ShowLine(_currentIndex);
    }

    /// <summary>结束对话：隐藏面板、触发结束回调（解锁玩家）</summary>
    public void EndDialogue()
    {
        if (!_isActive) return;

        _isActive = false;
        _isTyping = false;
        _currentLines = null;
        _currentIndex = 0;

        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (_onDialogueEnd != null)
        {
            _onDialogueEnd.Invoke();
            _onDialogueEnd = null;
        }
    }

    // ------------------------------------------------------------------ 私有方法

    /// <summary>显示指定索引的对话行（名字/内容/立绘/框样式逐句切换）</summary>
    private void ShowLine(int index)
    {
        DialogueEntry line = _currentLines[index];

        // 角色名字（留空时隐藏名字栏）
        if (nameText != null)
        {
            nameText.text = line.characterName;
            nameText.gameObject.SetActive(!string.IsNullOrEmpty(line.characterName));
        }

        // 对话内容（打字机或立即显示）
        if (typewriterSpeed > 0f) StartTypewriter(line.dialogueText);
        else contentText.text = line.dialogueText;

        // 立绘（每句可换；留空则隐藏立绘位）
        if (portraitImage != null)
        {
            bool hasPortrait = line.portrait != null;
            portraitImage.gameObject.SetActive(hasPortrait);
            if (hasPortrait) portraitImage.sprite = line.portrait;
        }

        // 对话框样式（每句可换；留空回落到默认样式）
        if (boxBackgroundImage != null)
        {
            Sprite style = line.boxStyle != null ? line.boxStyle : defaultBoxStyle;
            boxBackgroundImage.gameObject.SetActive(style != null);
            if (style != null) boxBackgroundImage.sprite = style;
        }
    }

    /// <summary>推进到下一句；没有下一句则结束对话</summary>
    private void NextLine()
    {
        _currentIndex++;
        if (_currentIndex >= _currentLines.Count) EndDialogue();
        else ShowLine(_currentIndex);
    }

    // ------------------------------------------------ 打字机

    private void StartTypewriter(string text)
    {
        if (_typewriterCoroutine != null)
            StopCoroutine(_typewriterCoroutine);

        _isTyping = true;
        _typewriterCoroutine = StartCoroutine(TypewriterCoroutine(text));
    }

    private IEnumerator TypewriterCoroutine(string text)
    {
        contentText.text = "";
        float interval = 1f / typewriterSpeed;
        WaitForSeconds wait = new WaitForSeconds(interval);

        for (int i = 0; i < text.Length; i++)
        {
            contentText.text = text.Substring(0, i + 1);
            yield return wait;
        }

        _isTyping = false;
        _typewriterCoroutine = null;
    }

    /// <summary>停止打字机，立即显示整句</summary>
    private void StopTypewriter()
    {
        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }

        if (_currentLines != null && _currentIndex < _currentLines.Count)
            contentText.text = _currentLines[_currentIndex].dialogueText;

        _isTyping = false;
    }
}
