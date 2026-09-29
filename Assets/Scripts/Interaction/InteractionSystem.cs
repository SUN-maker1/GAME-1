using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局互动系统 —— 追踪当前是否有可互动物体在玩家附近。
/// PlayerMovement 在攻击前会检查这个标志，避免和互动冲突。
/// </summary>
public static class InteractionSystem
{
    private static readonly HashSet<MonoBehaviour> _inRange = new HashSet<MonoBehaviour>();

    /// <summary>当前是否有可互动物体在玩家附近</summary>
    public static bool AnyInteractableInRange => _inRange.Count > 0;

    public static void RegisterInRange(MonoBehaviour interactable)
    {
        _inRange.Add(interactable);
    }

    public static void UnregisterInRange(MonoBehaviour interactable)
    {
        _inRange.Remove(interactable);
    }
}
