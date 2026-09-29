using UnityEngine;

/// <summary>
/// 单句对话数据
/// 每一句话都可以单独设置：角色名字、对话内容、立绘、对话框样式
/// </summary>
[System.Serializable]
public class DialogueEntry
{
    [Header("角色信息")]
    [Tooltip("角色名字（留空则不显示名字）")]
    public string characterName = "";

    [Header("对话内容")]
    [TextArea(3, 10)]
    [Tooltip("对话文本内容，支持多行")]
    public string dialogueText = "";

    [Header("角色立绘")]
    [Tooltip("角色立绘图片（留空则不显示立绘）")]
    public Sprite portrait;

    [Header("对话框样式")]
    [Tooltip("对话框背景样式（留空则使用默认样式）")]
    public Sprite boxStyle;
}
