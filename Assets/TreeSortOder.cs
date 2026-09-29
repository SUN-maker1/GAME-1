using UnityEngine;

/// <summary>
/// 按世界坐标 Y 自动设置 SortingOrder：Y 越小（越靠屏幕下方＝越"前面"），Order 越大，画得越靠上，
/// 于是前面的树会遮住后面的树。
///
/// 用法：挂到树 prefab 的根节点上就行
///   · 编辑器里摆放 / 拖动 → OnValidate 触发，松手即生效
///   · 运行时种树（Instantiate）→ OnEnable 触发，种下瞬间自动排好
///   · 手动改完参数 → Inspector 右键 → Refresh Sorting
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Rendering/Y 轴自动排序")]
public class TreeSortOrder : MonoBehaviour
{
    [Header("排序基准")]
    [Tooltip("用「脚下位置」（渲染包围盒底部）而不是物体原点来排序。\n不同高度的树混放时必须勾，否则大树的中心点会把它算到前面去。")]
    public bool useFootPosition = true;

    [Header("映射参数")]
    [Tooltip("基准 Order。最终 Order = 基准 - Y × 每单位差值 + 手调偏移")]
    public int orderBase = 1000;

    [Tooltip("1 个世界单位 Y 换算成多少 Order 差。数值越大排序越精细，但别让结果超出 ±32767")]
    public float orderPerUnit = 100f;

    [Tooltip("同高度时的手调微调，数值越大越靠前。也可用来把某棵树强制压到最前面。")]
    public int orderOffset = 0;

    [Header("作用范围")]
    [Tooltip("把子物体（树干 / 树冠 / 阴影）一起改，并保留它们原来的前后关系")]
    public bool includeChildren = true;

    [Tooltip("顺带强制指定 Sorting Layer，留空则不动。建议给树单独建一层，避免被地面盖住。")]
    public string sortingLayerName = "";

    [Header("性能")]
    [Tooltip("树会移动时才勾。不勾的话只在创建 / 编辑器改动时算一次。")]
    public bool updateEveryFrame = false;

    private SpriteRenderer[] _renderers = new SpriteRenderer[0];
    private Vector3 _lastPosition;
    private bool _hasLastPosition;

    private void OnEnable()
    {
        CacheRenderers();
        Refresh();
    }

    private void OnValidate()
    {
        // 编辑模式下拖动 / 改参数时立刻看到结果
        CacheRenderers();
        Refresh();
    }

    private void LateUpdate()
    {
        if (!updateEveryFrame) return;

        // 没动过就不重算，省掉每帧读包围盒的开销
        if (_hasLastPosition && transform.position == _lastPosition) return;
        _lastPosition = transform.position;
        _hasLastPosition = true;

        Refresh();
    }

    /// <summary>重新收集要改的 SpriteRenderer。改完 includeChildren 后要调一次。</summary>
    public void CacheRenderers()
    {
        _renderers = includeChildren
            ? GetComponentsInChildren<SpriteRenderer>(true)
            : GetComponents<SpriteRenderer>();

        if (_renderers == null)
            _renderers = new SpriteRenderer[0];
    }

    /// <summary>重新按当前位置算一次排序。运行时种树 / 移动后可手动调用。</summary>
    [ContextMenu("Refresh Sorting")]
    public void Refresh()
    {
        if (_renderers == null || _renderers.Length == 0)
            CacheRenderers();
        if (_renderers.Length == 0)
            return;

        // Y 越大 → Order 越小 → 越靠后被遮住
        int target = orderBase + orderOffset - Mathf.RoundToInt(GetSortY() * orderPerUnit);
        target = Mathf.Clamp(target, short.MinValue, short.MaxValue);

        // 以当前 Order 为参照算增量：所有 renderer 加同一个 delta，
        // 既保持子物体之间的相对层次，又保证重复调用结果一致（幂等）
        SpriteRenderer anchor = null;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null) { anchor = _renderers[i]; break; }
        }
        if (anchor == null) return;

        int delta = target - anchor.sortingOrder;

        for (int i = 0; i < _renderers.Length; i++)
        {
            SpriteRenderer r = _renderers[i];
            if (r == null) continue;

            int value = Mathf.Clamp(r.sortingOrder + delta, short.MinValue, short.MaxValue);
            if (r.sortingOrder != value)
                r.sortingOrder = value;

            if (!string.IsNullOrEmpty(sortingLayerName) && r.sortingLayerName != sortingLayerName)
                r.sortingLayerName = sortingLayerName;
        }
    }

    private float GetSortY()
    {
        SpriteRenderer reference = null;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null) { reference = _renderers[i]; break; }
        }

        if (useFootPosition && reference != null)
        {
            Bounds bounds = reference.bounds;
            // 脚底（树根）所在的 Y，比物体原点更符合"谁在前面"的直觉
            return bounds.min.y;
        }

        return transform.position.y;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/刷新全部 Y 轴排序")]
    private static void RefreshAllInScene()
    {
        TreeSortOrder[] all = FindObjectsOfType<TreeSortOrder>();
        foreach (TreeSortOrder item in all)
        {
            item.CacheRenderers();
            item.Refresh();
            UnityEditor.EditorUtility.SetDirty(item);
        }
        Debug.Log("[TreeSortOrder] 已刷新 " + all.Length + " 个物体的 Y 轴排序。");
    }
#endif
}
