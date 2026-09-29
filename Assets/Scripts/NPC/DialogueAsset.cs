using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一段可复用的对话资源。
/// 在 Project 窗口右键 ▸ Create ▸ 农场RPG ▸ 对话 就能新建，
/// 然后拖到 NPC 的 Dialogue Asset 上 —— 多个 NPC 共用同一段话时用这个最方便。
/// </summary>
[CreateAssetMenu(menuName = "农场RPG/对话", fileName = "Dialogue_New")]
public class DialogueAsset : ScriptableObject
{
    [Tooltip("默认说话人：某一句没填名字时就用它")]
    public string defaultSpeaker = "";

    [Tooltip("按从上到下的顺序播放")]
    public List<DialogueLine> lines = new List<DialogueLine>();

    /// <summary>有没有填内容</summary>
    public bool HasLines => lines != null && lines.Count > 0;
}
