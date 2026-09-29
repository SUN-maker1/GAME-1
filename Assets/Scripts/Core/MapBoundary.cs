using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>地图四条边。Auto 表示「看这个物体摆在哪边就自动认那边」。</summary>
public enum MapEdge
{
    Auto = 0,
    Left = 1,
    Right = 2,
    Bottom = 3,
    Top = 4
}

/// <summary>
/// 地图边界总管 —— 让「人物永远走不出地图图片」这件事不再依赖手工坐标。
///
/// 【为什么以前总出问题】
///   手摆的墙、传送条、出生点都是写死坐标（±33 / x=30），而地图图片的世界尺寸由
///   贴图的 Pixels Per Unit 决定。图片一换，尺寸就变，坐标全部失效 ——
///   于是人物能走到图外面很远才碰到传送点，甚至直接走出画面。
///
/// 【现在怎么做】
///   进场景后先找到地图背景，算出它【真实渲染出来的矩形】，然后：
///     1. 可选把地图宽度归一到 targetMapWidth（默认 64，和小镇保持一致）
///     2. 沿四条边自动生成碰撞墙，角色物理上出不去
///     3. 有传送点的那条边，自动在传送条的位置留一个缺口 —— 只有那个缺口能走出去
///     4. 把同一个矩形同步给 PlayerMovement（位置兜底）和 CameraFollow（镜头不出图）
///   换任何一张新图，只要还是 Map_Background（或场上最大的那张精灵），全部自动跟上。
///
/// 【怎么用】
///   什么都不用做，进游戏自动生效（会自动建一个 MapBoundary (Auto) 物体）。
///   想自己调参数：手动把这个组件挂到场景里任意物体上，它就不会再自动创建了。
/// </summary>
[DefaultExecutionOrder(-100)]
public class MapBoundary : MonoBehaviour
{
    [Header("地图")]
    [Tooltip("留空 = 自动找：优先名字叫 Map_Background 的，其次场上面积最大的那张精灵")]
    public SpriteRenderer mapRenderer;

    [Tooltip("勾选 = 不管你拖多大的图进来，都等比拉伸到目标宽度。\n" +
             "注意：拉伸会让画面变糊。想让小图变清晰，应该换像素更大的图，别靠这个硬撑。")]
    public bool normalizeMapWidth = false;

    [Tooltip("归一的目标宽度（世界单位）。小镇现在是 64，保持一致最省心")]
    public float targetMapWidth = 64f;

    [Header("围墙")]
    public bool buildWalls = true;
    [Tooltip("墙的厚度（世界单位）。太薄会被高速移动的角色穿过去，2 比较稳")]
    public float wallThickness = 2f;
    [Tooltip("正值把墙往地图内侧收一点；0 = 墙的内表面正好压在图片边缘上")]
    public float wallInset = 0f;
    [Tooltip("某条边上有传送点时，在那儿留一个缺口（就是能走出去的那个口子）")]
    public bool leaveGapsForPortals = true;

    [Tooltip("删掉以前手摆的那些 Wall_ 物体，避免和自动生成的新墙打架")]
    public bool removeLegacyWalls = true;

    [Header("同步给其它组件")]
    [Tooltip("打开玩家身上的 useBoundary，并把边界写进去（防止高速穿墙的兜底）")]
    public bool syncPlayerClamp = true;
    [Tooltip("允许的越界余量：要够角色把身子探进贴边的传送条里")]
    public float clampMargin = 2.5f;

    [Tooltip("把相机Follow 的边界也设成地图矩形，镜头永远不会露出图外的黑边")]
    public bool syncCameraBounds = true;

    [Header("相机视野自动贴合地图")]
    [Tooltip("勾选 = 自动设置 Orthographic Size，让画面刚好装进地图（不会露出图外的黑边）。\n" +
             "小图不会被拉大，所以永远是清晰的；多出来的方向就留给走动和卷镜头。")]
    public bool syncCameraZoom = true;
    public float minOrthoSize = 2.5f;
    public float maxOrthoSize = 24f;

    [Header("调试")]
    public bool verboseLog = true;

    private Bounds _mapBounds;
    private readonly List<GameObject> _generated = new List<GameObject>();
    private bool _built;

    /// <summary>当前场景的边界总管</summary>
    public static MapBoundary Current { get; private set; }

    /// <summary>地图真实占用的世界矩形</summary>
    public Bounds MapBounds => _mapBounds;

    // ------------------------------------------------------------ 自动安装

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    /// <summary>场景里没有手挂的 MapBoundary 时才自动建一个</summary>
    public static MapBoundary Ensure()
    {
        if (Current != null) return Current;

        MapBoundary found = FindObjectOfType<MapBoundary>();
        if (found == null)
        {
            GameObject go = new GameObject("MapBoundary (Auto)");
            found = go.AddComponent<MapBoundary>();
        }

        Current = found;
        found.Build();
        return found;
    }

    // ------------------------------------------------------------ 生命周期

    private void Awake()
    {
        if (Current == null) Current = this;
    }

    private void OnEnable()
    {
        if (Current == null) Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    private void Start()
    {
        // Bootstrap / Ensure 在场景加载完就跑过一次了，这里只是给手动挂载的情况兜底
        if (!_built) Build();
    }

    [ContextMenu("立即重建边界")]
    private void ContextRebuild()
    {
        Build();
        if (verboseLog)
            Debug.Log($"[MapBoundary] 重建完成：X[{_mapBounds.min.x:F2}, {_mapBounds.max.x:F2}] " +
                      $"Y[{_mapBounds.min.y:F2}, {_mapBounds.max.y:F2}]", this);
    }

    // ------------------------------------------------------------ 主流程

    /// <summary>重新测量地图 → 生成墙 → 同步给玩家和相机</summary>
    public void Build()
    {
        SpriteRenderer sr = ResolveMap();
        if (sr == null || sr.sprite == null)
        {
            Debug.LogWarning("[MapBoundary] 场上找不到带精灵的地图背景，边界没生成。", this);
            return;
        }

        _mapCache = sr;

        if (normalizeMapWidth && targetMapWidth > 0.01f)
            NormalizeWidth(sr);

        _mapBounds = WorldBounds(sr);

        // 顺序很重要：必须先把传送条吸到边上，再去建墙，
        // 否则墙会按旧坐标留缺口，口子和门对不上。
        SnapSceneComponents();
        Physics2D.SyncTransforms();

        if (removeLegacyWalls) RemoveLegacyWalls(sr);
        ClearWalls();
        if (buildWalls)
        {
            BuildSide(MapEdge.Left);
            BuildSide(MapEdge.Right);
            BuildSide(MapEdge.Bottom);
            BuildSide(MapEdge.Top);
        }

        SyncConsumers();

        _built = true;

        if (verboseLog)
        {
            Debug.Log($"[MapBoundary] {gameObject.scene.name} 地图 {_mapBounds.size.x:F2} × {_mapBounds.size.y:F2} 单位\n" +
                      $"  X[{_mapBounds.min.x:F2}, {_mapBounds.max.x:F2}]  Y[{_mapBounds.min.y:F2}, {_mapBounds.max.y:F2}]\n" +
                      $"  生成 {_generated.Count} 段墙，玩家只能走到图片边缘为止", this);
        }
    }

    // ------------------------------------------------------------ 地图

    /// <summary>
    /// 找到地图背景：优先认名字里带 Map / Background / Ground 的，
    /// 找不到就认场上渲染面积最大的那张精灵。
    /// </summary>
    private SpriteRenderer ResolveMap()
    {
        if (mapRenderer != null && mapRenderer.sprite != null) return mapRenderer;

        // 缓存只是给 Scene 视图的实时 Gizmo 用的，物体被删掉时它会自动失效
        if (_mapCache != null && _mapCache.sprite != null) return _mapCache;

        SpriteRenderer named = null, biggest = null;
        float biggestArea = 0f;

        foreach (SpriteRenderer sr in FindObjectsOfType<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null) continue;

            Bounds b = WorldBounds(sr);
            float area = b.size.x * b.size.y;

            if (IsNamed(sr) && named == null) named = sr;

            if (area > biggestArea)
            {
                biggestArea = area;
                biggest = sr;
            }
        }

        return named != null ? named : biggest;
    }

    /// <summary>最后一次解析出来的地图背景（Gizmo 用，不参与序列化）</summary>
    private SpriteRenderer _mapCache;

    private static bool IsNamed(SpriteRenderer sr)
    {
        string n = sr.name;
        return n.IndexOf("Map", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>把地图等比缩放到目标宽度</summary>
    private void NormalizeWidth(SpriteRenderer sr)
    {
        Bounds b = WorldBounds(sr);
        if (b.size.x <= 0.01f) return;

        float k = targetMapWidth / b.size.x;
        if (Mathf.Abs(k - 1f) < 0.001f) return;

        if (k > 12f)
        {
            Debug.LogWarning($"[MapBoundary] {sr.name} 这张图只有 {b.size.x:F2} 单位宽，" +
                             $"放大到 {targetMapWidth} 需要 {k:F1} 倍，画面大概率会糊。" +
                             $"建议换成像素更大的图，或者把 targetMapWidth 调小。", sr);
        }

        sr.transform.localScale *= k;
    }

    /// <summary>精灵在世界空间中的真实矩形（含缩放和旋转）</summary>
    public static Bounds WorldBounds(SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null) return default;

        Bounds local = sr.sprite.bounds;
        Transform t = sr.transform;

        Vector3 c = local.center;
        Vector3 e = local.extents;
        Bounds world = new Bounds(t.TransformPoint(c), Vector3.zero);

        for (int i = 0; i < 8; i++)
        {
            Vector3 p = new Vector3(
                c.x + ((i & 1) == 0 ? -e.x : e.x),
                c.y + ((i & 2) == 0 ? -e.y : e.y),
                c.z + ((i & 4) == 0 ? -e.z : e.z));
            world.Encapsulate(t.TransformPoint(p));
        }
        return world;
    }

    // ------------------------------------------------------------ 围墙

    private void RemoveLegacyWalls(SpriteRenderer map)
    {
        BoxCollider2D[] boxes = FindObjectsOfType<BoxCollider2D>();
        foreach (BoxCollider2D bc in boxes)
        {
            if (bc == null || bc.isTrigger) continue;
            if (bc.gameObject == map.gameObject) continue;
            if (bc.GetComponent<ScenePortal>() != null) continue;
            // 跳过「自定义碰撞区域」（ObstacleShape2D）—— 那些是家具/水池等该保留的，
            // 不能再当成旧手摆墙清掉。名字也不再强制以 "Wall_" 开头（见 ObstacleShapeBrush）。
            if (bc.GetComponent<ObstacleShape2D>() != null) continue;
            if (!bc.name.StartsWith("Wall_")) continue;

            if (verboseLog)
                Debug.Log($"[MapBoundary] 删掉旧的手摆墙 {bc.name}（位置 {bc.transform.position}），" +
                          $"现在由地图形状自动生成。", bc);

            Destroy(bc.gameObject);
        }
    }

    private void ClearWalls()
    {
        for (int i = _generated.Count - 1; i >= 0; i--)
        {
            if (_generated[i] != null)
                Destroy(_generated[i]);
        }
        _generated.Clear();
    }

    /// <summary>一条边一个人口 iterate</summary>
    private void BuildSide(MapEdge edge)
    {
        Bounds b = _mapBounds;
        bool vertical = edge == MapEdge.Left || edge == MapEdge.Right;

        float alongMin = vertical ? b.min.y : b.min.x;
        float alongMax = vertical ? b.max.y : b.max.x;

        float cross = vertical
            ? (edge == MapEdge.Left ? b.min.x - wallInset : b.max.x + wallInset)
            : (edge == MapEdge.Bottom ? b.min.y - wallInset : b.max.y + wallInset);

        float t = Mathf.Max(0.05f, wallThickness);

        List<Vector2> gaps = leaveGapsForPortals ? CollectGaps(edge) : new List<Vector2>();

        foreach (Vector2 seg in Complement(alongMin, alongMax, gaps))
        {
            float len = seg.y - seg.x;
            if (len <= 0.02f) continue;

            string wallName = "Wall_" + edge + "_" + Mathf.RoundToInt(seg.x * 10f);
            GameObject go = new GameObject(wallName);
            go.transform.SetParent(transform, false);

            if (vertical)
            {
                float x = cross + (edge == MapEdge.Left ? -t * 0.5f : t * 0.5f);
                go.transform.position = new Vector3(x, (seg.x + seg.y) * 0.5f, 0f);
                AddWall(go, new Vector2(t, len));
            }
            else
            {
                float y = cross + (edge == MapEdge.Bottom ? -t * 0.5f : t * 0.5f);
                go.transform.position = new Vector3((seg.x + seg.y) * 0.5f, y, 0f);
                AddWall(go, new Vector2(len, t));
            }

            _generated.Add(go);
        }
    }

    private void AddWall(GameObject go, Vector2 size)
    {
        BoxCollider2D bc = go.AddComponent<BoxCollider2D>();
        bc.isTrigger = false;
        bc.size = size;
        bc.offset = Vector2.zero;
    }

    /// <summary>这条边上所有传送点在「沿边方向」占据的区间</summary>
    private List<Vector2> CollectGaps(MapEdge edge)
    {
        List<Vector2> gaps = new List<Vector2>();

        ScenePortal[] portals = FindObjectsOfType<ScenePortal>();
        foreach (ScenePortal p in portals)
        {
            Collider2D col = p.GetComponent<Collider2D>();
            if (col == null) continue;
            if (p.Edge != edge) continue;

            Bounds pb = col.bounds;
            bool vertical = edge == MapEdge.Left || edge == MapEdge.Right;

            if (vertical)
                gaps.Add(new Vector2(pb.min.y, pb.max.y));
            else
                gaps.Add(new Vector2(pb.min.x, pb.max.x));
        }
        return gaps;
    }

    /// <summary>把 [min,max] 挖掉若干缺口后剩下哪些段</summary>
    private static List<Vector2> Complement(float min, float max, List<Vector2> holes)
    {
        List<Vector2> result = new List<Vector2>();
        List<Vector2> sorted = new List<Vector2>(holes);
        sorted.Sort((a, b) => a.x.CompareTo(b.x));

        float cursor = min;
        foreach (Vector2 h in sorted)
        {
            float a = h.x, bnd = h.y;
            if (bnd <= cursor) continue;
            if (a >= max) break;

            if (a > cursor) result.Add(new Vector2(cursor, Mathf.Min(a, max)));
            cursor = Mathf.Max(cursor, bnd);
            if (cursor >= max) break;
        }

        if (cursor < max) result.Add(new Vector2(cursor, max));
        return result;
    }

    // ------------------------------------------------------------ 对接其它组件

    private void SyncConsumers()
    {
        Rect r = new Rect(_mapBounds.min.x - clampMargin, _mapBounds.min.y - clampMargin,
                          _mapBounds.size.x + clampMargin * 2f, _mapBounds.size.y + clampMargin * 2f);

        if (syncPlayerClamp)
        {
            PlayerMovement[] movers = FindObjectsOfType<PlayerMovement>();
            foreach (PlayerMovement m in movers)
            {
                m.useBoundary = true;
                m.boundary = r;
            }
        }

        if (syncCameraBounds)
        {
            CameraFollow[] cams = FindObjectsOfType<CameraFollow>();
            foreach (CameraFollow c in cams)
            {
                c.useManualBounds = true;
                c.manualBounds = new Rect(_mapBounds.min.x, _mapBounds.min.y,
                                          _mapBounds.size.x, _mapBounds.size.y);

                if (syncCameraZoom) ApplyZoom(c);
            }
        }
    }

    /// <summary>
    /// 让画面刚好装进地图：取「高度装得下」和「宽度装得下」里更严的那个。
    /// 这样四周永远不会露出图外的黑边，而另一个方向就留给卷镜头。
    /// </summary>
    private void ApplyZoom(CameraFollow follow)
    {
        Camera cam = follow.GetComponent<Camera>();
        if (cam == null || !cam.orthographic) return;

        float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
        float byHeight = _mapBounds.size.y * 0.5f;
        float byWidth = _mapBounds.size.x / (2f * aspect);

        float want = Mathf.Clamp(Mathf.Min(byHeight, byWidth), minOrthoSize, maxOrthoSize);
        cam.orthographicSize = want;
    }

    // ------------------------------------------------------------ 给传送点 / 入口点用

    /// <summary>让场上所有传送条 / 出生点先归位</summary>
    private void SnapSceneComponents()
    {
        foreach (ScenePortal portal in FindObjectsOfType<ScenePortal>())
        {
            if (portal != null && portal.autoSnapToEdge)
                SnapPortal(portal, true);
        }

        foreach (SceneEntryPoint entry in FindObjectsOfType<SceneEntryPoint>())
        {
            if (entry != null && entry.autoSnapToEdge)
                SnapEntry(entry);
        }
    }

    /// <summary>把传送条贴到指定边上</summary>
    public void SnapPortal(ScenePortal portal, bool resize)
    {
        Bounds b = _mapBounds;
        MapEdge edge = portal.Edge;
        Collider2D col = portal.GetComponent<Collider2D>();
        if (col == null) return;

        BoxCollider2D box = col as BoxCollider2D;
        Vector3 pos = portal.transform.position;

        switch (edge)
        {
            case MapEdge.Left:
            case MapEdge.Right:
                if (box != null && resize)
                    box.size = new Vector2(portal.thickness, portal.lengthAlongEdge);
                pos.x = edge == MapEdge.Left ? b.min.x : b.max.x;
                if (!portal.keepPositionAlongEdge) pos.y = b.center.y;
                break;

            case MapEdge.Bottom:
            case MapEdge.Top:
                if (box != null && resize)
                    box.size = new Vector2(portal.lengthAlongEdge, portal.thickness);
                pos.y = edge == MapEdge.Bottom ? b.min.y : b.max.y;
                if (!portal.keepPositionAlongEdge) pos.x = b.center.x;
                break;
        }

        portal.transform.position = pos;
        Physics2D.SyncTransforms();
    }

    /// <summary>把出生点贴到指定边内侧</summary>
    public void SnapEntry(SceneEntryPoint entry)
    {
        Bounds b = _mapBounds;
        MapEdge edge = entry.Edge;
        Vector3 pos = entry.transform.position;
        float inset = Mathf.Max(0f, entry.insetFromEdge);

        switch (edge)
        {
            case MapEdge.Left:
                pos.x = b.min.x + inset;
                if (!entry.keepPositionAlongEdge) pos.y = b.center.y;
                break;
            case MapEdge.Right:
                pos.x = b.max.x - inset;
                if (!entry.keepPositionAlongEdge) pos.y = b.center.y;
                break;
            case MapEdge.Bottom:
                pos.y = b.min.y + inset;
                if (!entry.keepPositionAlongEdge) pos.x = b.center.x;
                break;
            case MapEdge.Top:
                pos.y = b.max.y - inset;
                if (!entry.keepPositionAlongEdge) pos.x = b.center.x;
                break;
        }

        entry.transform.position = pos;
    }

    /// <summary>根据一个位置判断它贴在哪条边上</summary>
    public MapEdge NearestEdge(Vector3 pos)
    {
        Bounds b = _mapBounds;
        float dl = Mathf.Abs(pos.x - b.min.x);
        float dr = Mathf.Abs(b.max.x - pos.x);
        float db = Mathf.Abs(pos.y - b.min.y);
        float dt = Mathf.Abs(b.max.y - pos.y);

        float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
        if (m == dl) return MapEdge.Left;
        if (m == dr) return MapEdge.Right;
        if (m == db) return MapEdge.Bottom;
        return MapEdge.Top;
    }

    // ------------------------------------------------------------ 可视化

    private void OnDrawGizmos()
    {
        SpriteRenderer sr = ResolveMap();
        if (sr == null || sr.sprite == null) return;

        Bounds b = WorldBounds(sr);

        // 归一后的预览范围
        if (normalizeMapWidth && targetMapWidth > 0.01f && b.size.x > 0.01f)
        {
            float k = targetMapWidth / b.size.x;
            Vector3 c = b.center;
            Vector3 s = b.size * k;
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.18f);
            Gizmos.DrawCube(c, s);
        }

        Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.65f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
