using UnityEngine;

/// <summary>
/// 玩家标记组件，解决「哪个才是当前玩家」的问题。
///
/// 【重要设计改动】
/// 早期版本在这里做了「重复实例自我销毁」，那会引发切场景时的时序灾难：
/// 新场景的 Player 先 Awake（把自己销毁掉），旧场景的 Player 再 OnDestroy
/// （把 Instance 清成 null），最后落位时.Instance == null，玩家没被搬走，
/// 于是卡在传送点上被反复传送。现在改成下面这样：
///
///   - Awake 只负责「登记」自己，绝不销毁别人
///   - OnDestroy 不碰 Instance，避免把有效引用清成 null
///   - SceneLoader 落位时用 gameObject.scene 过滤，只认「当前激活场景里的玩家」
///
/// 【关于 DontDestroyOnLoad】
/// 现在玩家【不做】DontDestroyOnLoad。因为用的是 LoadSceneMode.Single，
/// 切场景时旧场景会被整体销毁，玩家跟着销毁是干净的、不会残留重复实例。
/// 需要跨场景保留的东西（血量 / 背包 / 任务）应该写成独立的存档系统，
/// 而不是靠「玩家活着」来带。
/// </summary>
public class PersistentPlayer : MonoBehaviour
{
    /// <summary>最近一次 Awake 的玩家。落位时不要直接信它，用 SceneLoader 里的场景过滤。</summary>
    public static PersistentPlayer Instance { get; private set; }

    public static bool Exists => Instance != null && Instance.gameObject != null;

    private void Awake()
    {
        // 只登记，不销毁任何人。后醒的覆盖先醒的，顺序无关紧要。
        Instance = this;

        // 顺手把标签补上，方便其它脚本用 GameObject.FindWithTag("Player") 找人
        if (!string.IsNullOrEmpty(tag) && tag != "Player")
        {
            try { tag = "Player"; }
            catch (UnityException) { /* 标签不存在，忽略 */ }
        }
    }

    /// <summary>
    /// 注意：这里故意【不】清空 Instance。
    /// 场景卸载时旧玩家会走 OnDestroy，如果此时把 Instance 清成 null，
    /// 而新场景的 Player 还没 Awake，就会出现「找不到玩家」的空窗期。
    /// 真正的过滤交给 SceneLoader.FindPlayerInActiveScene()。
    /// </summary>
    private void OnDestroy()
    {
        //  intentionally left empty
    }
}
