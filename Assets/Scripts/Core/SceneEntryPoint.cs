using UnityEngine;

/// <summary>
/// 场景入口点。玩家从别的区域传送过来时，会按 entryID 找到对应的点并站上去。
/// 用法：在场景里放一个空物体挂这个脚本，填 entryID，摆到你想让玩家出现的位置。
/// 每个场景建议至少放一个 Start 作为默认出生点。
/// </summary>
public class SceneEntryPoint : MonoBehaviour
{
    [Tooltip("入口编号，要和别的场景里 ScenePortal 的 Entry ID 对上")]
    public string entryID = "Start";

    [Header("可选：落位后的朝向")]
    [Tooltip("勾选后落位时会把玩家的左右朝向设成这里指定的值")]
    public bool overrideFacing = false;
    public bool faceLeft = false;

    [Header("自动贴地图边缘")]
    [Tooltip("勾选 = 进入场景后自动挪到指定边的内侧，玩家从这个方向进图正好站在门口。\n" +
             "换图后位置会自己跟着地图尺寸更新，不会掉到图外面去。")]
    public bool autoSnapToEdge = false;

    [Tooltip("Auto = 看你现在把它摆在哪边；也可以直接指定上下左右")]
    public MapEdge edge = MapEdge.Auto;

    [Tooltip("离地图边缘往里缩多少（世界单位）。太小会一落地就踩到传送条上被弹回")]
    public float insetFromEdge = 3f;

    [Tooltip("勾选 = 保持你摆的沿边位置；不勾 = 落在那条边的正中间")]
    public bool keepPositionAlongEdge = false;

    private MapEdge _resolvedEdge = MapEdge.Auto;

    /// <summary>这个入口点所属的边（Auto 会自动算一次并缓存）</summary>
    public MapEdge Edge
    {
        get
        {
            if (edge != MapEdge.Auto) return edge;
            if (_resolvedEdge == MapEdge.Auto && MapBoundary.Current != null)
                _resolvedEdge = MapBoundary.Current.NearestEdge(transform.position);
            return _resolvedEdge == MapEdge.Auto ? MapEdge.Left : _resolvedEdge;
        }
    }

    private void Start()
    {
        if (autoSnapToEdge && MapBoundary.Current != null)
            MapBoundary.Current.SnapEntry(this);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.6f, 0.9f);
        Vector3 p = transform.position;
        Gizmos.DrawWireCube(p, new Vector3(1f, 1f, 0f));
        Gizmos.DrawLine(p + Vector3.left * 0.6f, p + Vector3.right * 0.6f);
        Gizmos.DrawLine(p + Vector3.down * 0.6f, p + Vector3.up * 0.6f);
    }

    private void OnDrawGizmosSelected()
    {
        // 选中时画一个向上的小箭头，方便在 Scene 视图里辨认方向
        Gizmos.color = new Color(0.2f, 0.8f, 0.6f, 1f);
        Vector3 p = transform.position;
        Gizmos.DrawLine(p + Vector3.up * 0.7f, p + Vector3.up * 1.4f);
        Gizmos.DrawLine(p + Vector3.up * 1.4f, p + new Vector3(-0.25f, 1.1f, 0f));
        Gizmos.DrawLine(p + Vector3.up * 1.4f, p + new Vector3(0.25f, 1.1f, 0f));
    }
}
