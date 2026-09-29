using UnityEngine;

/// <summary>
/// 玩家对话锁 —— 对话期间锁定玩家移动/攻击。
///
/// 【修正说明】玩家的 PlayerMovement 只认 SceneTransition.InputBlocked（切场景/背包也用它），
/// 所以这里加锁时直接写到 SceneTransition.InputLocked，玩家脚本不用改任何代码。
///
/// 【为什么用计数而不是布尔】
///   万一以后有多个系统同时要求锁玩家（对话 + 剧情演出），
///   要全部解锁后才真正恢复移动，计数制不会提前解锁。
///
/// 外部使用：只读 IsLocked 即可，加解锁由 DialogueTrigger 自动调用。
/// </summary>
public static class PlayerDialogueLock
{
    private static int _lockCount;

    /// <summary>玩家是否处于对话锁定中</summary>
    public static bool IsLocked => _lockCount > 0;

    /// <summary>锁定玩家（对话开始时调用）</summary>
    public static void Lock()
    {
        _lockCount++;
        Apply();
    }

    /// <summary>解锁玩家（对话结束时调用）</summary>
    public static void Unlock()
    {
        _lockCount--;
        if (_lockCount < 0) _lockCount = 0;
        Apply();
    }

    /// <summary>把锁定状态同步到全局输入锁（PlayerMovement 读的就是它）</summary>
    private static void Apply()
    {
        SceneTransition.InputLocked = _lockCount > 0;
    }
}
