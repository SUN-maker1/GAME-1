using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一句对话里的「一个人物立绘」。
///
/// 一句话可以同时摆好几个立绘（三个人站成一排、主角和 NPC 面对面……），
/// 每一个都能单独指定：
///   · 画在对话框左边还是右边
///   · 左右 / 上下再挪几像素（两个立绘想错开一点时用）
///   · 单独定大小（不想用统一尺寸时）
///   · 左右翻转（让人物转身对着说话的那一边）
///   · 染色（比如暂时不说话的人调灰一点，退到后面）
///   · 层序（重叠时谁盖住谁）
///
/// 【默认摆法】底边坐在对话框上沿，向上露出上半身。
/// 【offsetX】正数 = 往屏幕中间靠；【offsetY】正数 = 往上抬。（像素，参考分辨率 1920x1080）
/// 【多大】width / height 留 -1 = 用「对话布局」资源里的统一尺寸。
/// </summary>
[System.Serializable]
public class DialoguePortraitSlot
{
    [Tooltip("这个立绘用的图。留空 = 这一格不画东西")]
    public Sprite sprite;

    [Tooltip("表情列表（可选）：这张脸之外的其它表情，按顺序切换。\n" +
             "第一个 sprite 是默认表情；对话里按推进键（E）会依次换成列表里的表情，\n" +
             "换完后下一次按 E 才翻到下一句；按 Q 随时手动切。\n" +
             "同一句放多个立绘时，每个立绘可以有自己的表情列表，一起换。")]
    public List<Sprite> expressions = new List<Sprite>();

    [Tooltip("画在对话框的哪一侧。Inherit = 沿用「对话布局」资源里的默认方向")]
    public DialoguePortraitSide side = DialoguePortraitSide.Inherit;

    [Tooltip("左右再挪一点（像素）。正数 = 往屏幕中间靠")]
    public float offsetX = 0f;

    [Tooltip("上下再挪一点（像素）。正数 = 往上抬")]
    public float offsetY = 0f;

    [Tooltip("显示高度（像素）。-1 = 用布局里的默认高度")]
    public float height = -1f;

    [Tooltip("显示宽度（像素）。-1 = 用布局里的默认宽度")]
    public float width = -1f;

    [Tooltip("左右翻转（让这个人物转过身面对另一边）")]
    public bool flipX = false;

    [Tooltip("染色。白色 = 原色；调成灰色可以让暂时不说话的人退到后面")]
    public Color tint = Color.white;

    [Tooltip("层序，数字大的画在上面（几个立绘叠在一起时用）")]
    public int order = 0;

    /// <summary>把位置 / 大小回到「默认」</summary>
    public void ResetTransform()
    {
        offsetX = 0f;
        offsetY = 0f;
        width = -1f;
        height = -1f;
        flipX = false;
        tint = Color.white;
    }

    // ---------------------------------------------------------------- 原始值备份

    // 下面这几个不存到资源里（NonSerialized），只是运行时的临时账本：
    // 「模板 / 逐句记录」都是在「台词里原本写的值」之上改的，不然每次显示这句都会叠一层，越摆越歪。
    [System.NonSerialized] public bool baseCaptured;
    [System.NonSerialized] public float baseOffsetX;
    [System.NonSerialized] public float baseOffsetY;
    [System.NonSerialized] public float baseWidth = -1f;
    [System.NonSerialized] public float baseHeight = -1f;

    /// <summary>第一次显示这句时，把台词里写死的值记下来当底数</summary>
    public void CaptureBaseOnce()
    {
        if (baseCaptured) return;

        baseCaptured = true;
        baseOffsetX = offsetX;
        baseOffsetY = offsetY;
        baseWidth = width;
        baseHeight = height;
    }
}
