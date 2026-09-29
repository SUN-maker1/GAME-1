using UnityEngine;

/// <summary>
/// 场景启动时，把玩家放到入口点上。
///
/// 为什么需要它：入口点（SceneEntryPoint）默认只在「从别的场景传送过来」时才生效。
/// 如果你直接打开某个场景按 Play，玩家就待在场景里它被摆放的那个位置，
/// 完全不会走到入口点上去——这也是为什么人会出现在地图外面。
///
/// 挂上这个之后，直接按 Play 也会从入口点开始。
/// 从别的场景传送进来时，落位逻辑会在之后接管并覆盖，两边不会打架。
/// </summary>
public class SceneStartSpawn : MonoBehaviour
{
    [Tooltip("用哪一个 entryID 的入口点。留空 = 用场景里的第一个入口点")]
    public string entryID = "Start";

    [Tooltip("同时把玩家朝向左/右也按入口点设置")]
    public bool applyFacing = true;

    private void Awake()
    {
        PersistentPlayer player = FindObjectOfType<PersistentPlayer>();
        if (player == null)
        {
            Debug.LogWarning("[出生点] 场景里找不到玩家（缺 PersistentPlayer 标记），跳过出生。", this);
            return;
        }

        SceneEntryPoint point = FindEntryPoint();
        if (point == null)
        {
            Debug.LogWarning("[出生点] 场景里没有入口点（SceneEntryPoint），玩家保持原来的位置。", this);
            return;
        }

        player.transform.position = point.transform.position;

        if (applyFacing && point.overrideFacing)
        {
            PlayerMovement move = player.GetComponent<PlayerMovement>();
            if (move != null)
                move.initialFacing = point.faceLeft ? PlayerMovement.Facing.Left : PlayerMovement.Facing.Right;
        }

        Debug.Log("[出生点] 玩家已从入口点「" + point.name + "」开始。", this);
    }

    private SceneEntryPoint FindEntryPoint()
    {
        SceneEntryPoint[] points = FindObjectsOfType<SceneEntryPoint>();
        if (points.Length == 0) return null;

        if (!string.IsNullOrEmpty(entryID))
        {
            foreach (SceneEntryPoint p in points)
            {
                if (p != null && p.entryID == entryID) return p;
            }
        }

        foreach (SceneEntryPoint p in points)
        {
            if (p != null) return p;
        }
        return null;
    }
}
