using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话数据配置文件（ScriptableObject）
///
/// 创建方式：
///   Project 窗口右键 ▸ Create ▸ Dialogue ▸ Dialogue Data
///
/// 使用方式：
///   在 Inspector 中直接编辑对话列表，不需要改代码
/// </summary>
[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue/Dialogue Data")]
public class DialogueData : ScriptableObject
{
    [Tooltip("对话列表，按顺序播放")]
    public List<DialogueEntry> lines = new List<DialogueEntry>();

    /// <summary>是否有对话内容</summary>
    public bool HasLines => lines != null && lines.Count > 0;
}
