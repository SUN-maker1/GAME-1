using UnityEngine;

/// <summary>
/// 全局过渡状态。所有需要「切场景时别乱动」的脚本都读这里，
/// 这样 PlayerMovement 之类的脚本不用反向依赖 SceneLoader，耦合更低。
/// </summary>
public static class SceneTransition
{
    /// <summary>是否正在切场景（此时应忽略玩家输入、忽略传送点触发）</summary>
    public static bool IsBusy { get; set; }

    /// <summary>额外叠加的输入锁，比如打开背包、对话时也可以用</summary>
    public static bool InputLocked { get; set; }

    /// <summary>玩家输入是否被屏蔽</summary>
    public static bool InputBlocked => IsBusy || InputLocked;
}
