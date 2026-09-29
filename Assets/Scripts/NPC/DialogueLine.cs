using System.Collections.Generic;
using UnityEngine;

/// <summary>立绘画在对话框的哪一侧。Inherit = 沿用对话布局资源里的默认方向。</summary>
public enum DialoguePortraitSide
{
    Inherit = 0,
    Left = 1,
    Right = 2
}

/// <summary>
/// 一句对话。
/// 一个 NPC 的对话就是一串 DialogueLine，按顺序一句句播。
/// </summary>
[System.Serializable]
public class DialogueLine
{
    [Tooltip("说话人名字。留空 = 沿用上一句的名字（对话里同一个人连着说几句时很省事）")]
    public string speakerName = "";

    [TextArea(2, 6)]
    [Tooltip("这句话的内容，可以用换行")]
    public string text = "";

    [Tooltip("可选：这句话显示的人物立绘（旧版单张写法，留着兼容早期填过的数据）。\n" +
             "如果下面 portraits 里也放了图，以 portraits 为准。")]
    public Sprite portrait;

    [Tooltip("这一句的单张立绘画在哪一侧。Inherit = 沿用布局里的默认方向。")]
    public DialoguePortraitSide portraitSide = DialoguePortraitSide.Inherit;

    [Tooltip("这句话要显示的立绘，可以放好几个：\n" +
             "· 每个槽位都能单独定位置、大小、左右翻转、染色\n" +
             "· 留一样的图、一个偏左一个偏右就是「面对面说话」\n" +
             "· 最简单的办法：靠近 NPC 按 E 打开对话，再按 F7 用鼠标拖着调，调完自动存\n" +
             "· 什么都没放 = 这句话不显示立绘")]
    public List<DialoguePortraitSlot> portraits = new List<DialoguePortraitSlot>();

    [Tooltip("这句的立绘带了表情列表时：\n" +
             "勾上 = 按推进键（E）先一个一个换表情，换完再翻到下一句；\n" +
             "不勾 = 按 E 直接翻句（想换表情按 Q 手动切）。")]
    public bool expressionOnAdvance = true;

    [Tooltip("可选：这一句用的对话框样式名（Assets/Resources/UI/dialogue_box_<名字>）。\n" +
             "留空 = 用当前全局选的样式。写上就能一句一个框（例如回忆用黑框、平时用卷轴）。")]
    public string boxStyle = "";

    [Tooltip("可选：这一句整套换外观（字体 / 字号 / 颜色 / 描边 / 框体大小 / 框体样式）。\n" +
             "拖一个「对话外观预设」资源进来就行 —— 比如回忆片段用另一种字和颜色，说完这句自动还原。\n" +
             "预设可以在 Play 模式 F7 面板的「模板」页里一键存出来。")]
    public DialoguePreset preset;

    private static readonly List<DialoguePortraitSlot> NoSlots = new List<DialoguePortraitSlot>();
    private List<DialoguePortraitSlot> _compat;

    /// <summary>这一句要画的立绘列表（永远不会返回 null）</summary>
    public List<DialoguePortraitSlot> GetPortraitSlots()
    {
        if (portraits != null && portraits.Count > 0) return portraits;

        if (portrait == null) return NoSlots;

        // 老的写法（单张 portrait 字段）：临时包成一个槽位，行为跟以前一样
        if (_compat == null)
        {
            _compat = new List<DialoguePortraitSlot>();
            _compat.Add(new DialoguePortraitSlot());
        }

        DialoguePortraitSlot s = _compat[0];
        s.sprite = portrait;
        s.side = portraitSide;
        s.offsetX = 0f;
        s.offsetY = 0f;
        s.width = -1f;
        s.height = -1f;
        s.flipX = false;
        s.tint = Color.white;
        s.order = 0;
        if (s.expressions == null) s.expressions = new List<Sprite>();
        s.expressions.Clear();
        return _compat;
    }
}
