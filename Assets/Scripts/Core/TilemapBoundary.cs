using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 给「瓦片地图」用的地图边界。
///
/// 为什么不用 MapBoundary：那只认 SpriteRenderer，而 Tilemap 用的是 TilemapRenderer，
/// 它会退化去挑场上面积最大的精灵，边界就会算错。
/// 这个脚本直接读 Tilemap 里实际有砖的格子范围，正好能用你标好的那个边界图层。
///
/// 用法：菜单 Tools ▸ 农场RPG ▸ 用瓦片地图层一键定边界…，选中你的边界图层点一下即可。
/// </summary>
[ExecuteAlways]
public class TilemapBoundary : MonoBehaviour
{
    [Header("边界来源")]
    [Tooltip("标边界用的那个 Tilemap 图层。留空会自动找名字里带 边界/Bound/Wall/Ground 的图层")]
    public Tilemap boundaryTilemap;

    [Tooltip("把场景里所有瓦片图层的范围合并起来当边界（推荐）。\n" +
             "单个图层往往只画了地图的一部分，用它算出来的边界会和整张地图对不上")]
    public bool useAllLayers = true;

    [Tooltip("忽略离主体很远的零星瓦片（强烈建议开）。\n" +
             "只要有几块画到远处的散砖，包围盒就会被撑大好几倍——这就是边界\"超出这么多\"的原因。\n" +
             "开着它只按连成一片的主体部分算边界，被忽略的格子会打在 Console 里提醒你")]
    public bool ignoreStrayTiles = true;

    [Tooltip("按格子算范围，而不是按瓦片精灵的外框。\n" +
             "某个瓦片图比格子大时，用精灵外框会让边界比眼睛看到的地图大一圈")]
    public bool useCellCorners = true;

    [Tooltip("实在搞不定就自己填一个矩形：勾上之后完全按下面这四个数来，不看瓦片")]
    public bool useManualRect = false;

    [Tooltip("手动矩形的左下角 X / Y 和 宽 / 高（世界单位）")]
    public Rect manualRect = new Rect(-10f, -10f, 20f, 20f);

    [Header("墙")]
    public bool buildWalls = true;

    /// <summary>墙贴在边界的哪一侧</summary>
    public enum WallPlacement
    {
        Outside,   // 整块在地图外面：人能走到最边上一格（功能最正确，但画出来的框比地图大一圈）
        OnEdge,    // 骑在边线上：一半在外一半在内（画出来最贴合，代价是最外一格站不上去）
        Inside     // 整块在地图里面
    }

    [Tooltip("墙贴在边界的哪一侧。\n" +
             "Outside = 整块在地图外（人能走到最边上一格，但画出来的碰撞框会比地图大一圈）\n" +
             "OnEdge  = 骑在边线上，一半在外一半在内（画出来最贴合）\n" +
             "Inside  = 整块在地图里")]
    public WallPlacement wallPlacement = WallPlacement.Outside;

    [Tooltip("墙的厚度（世界单位）。太薄高速移动会穿过去")]
    public float wallThickness = 1f;

    [Tooltip("正值把墙往地图内侧收；0 = 正好压在瓦片边缘")]
    public float wallInset = 0f;

    [Tooltip("清掉别处自动生成的 Wall_ 物体（包括 MapBoundary 算错的那批）")]
    public bool clearForeignWalls = true;

    [Tooltip("某条边上有传送点时，在那儿留一个缺口，人才走得出去")]
    public bool leaveGapsForPortals = true;

    [Header("其他")]
    [Tooltip("把相机限位也设成这个范围，镜头不会露出图外的黑边")]
    public bool syncCamera = true;

    [Tooltip("把玩家身上的移动范围钳制也同步成这里算出的地图范围。\n" +
             "之前有个小房间的 17.8×15.71 钳制被存进了玩家 prefab，就是这个东西在地图中间造出空气墙")]
    public bool syncPlayerClamp = true;

    [Tooltip("玩家允许越出地图边缘的余量，要够他探进贴边的传送条")]
    public float clampMargin = 2.5f;

    [Tooltip("造一个和瓦片地图等大的隐形代理交给地图边界脚本。\n" +
             "它只认 SpriteRenderer、看不懂瓦片地图，不配这个翻译器，\n" +
             "传送落位和传送条吸附就会被算到错误的边上（人掉在图外面）")]
    public bool syncToMapBoundary = true;

    public bool verboseLog = true;

    /// <summary>算出来的地图世界范围</summary>
    public Bounds MapBounds { get; private set; }

    /// <summary>这次算边界实际用的是哪些图层（日志/工具窗口显示用）</summary>
    public string BoundsSourceName { get; private set; } = "";

    /// <summary>最近一次计算中被当成散砖忽略掉的格子</summary>
    public static readonly List<Vector3Int> IgnoredCells = new List<Vector3Int>();

    private SpriteRenderer _proxy;
    private static Sprite _unitSprite;

    private readonly List<GameObject> _walls = new List<GameObject>();

    private void Awake() => Rebuild();

    // 双保险：自动生成的 MapBoundary 可能比我们晚一步才建墙，Start 时再清一次并按瓦片范围重建
    private void Start() => Rebuild();

    [ContextMenu("重建边界")]
    public void Rebuild()
    {
        if (useManualRect)
        {
            MapBounds = new Bounds(
                new Vector3(manualRect.x + manualRect.width * 0.5f,
                            manualRect.y + manualRect.height * 0.5f, 0f),
                new Vector3(manualRect.width, manualRect.height, 0f));
            BoundsSourceName = "手动矩形";
        }
        else
        {
            Tilemap[] all = FindObjectsOfType<Tilemap>();
            MapBounds = useAllLayers ? CombinedBounds(all) : SingleBounds(all);
        }

        if (MapBounds.size.x <= 0.01f || MapBounds.size.y <= 0.01f)
        {
            Debug.LogWarning("[瓦片边界] 没算出有效的地图范围（图层为空或没指定）。", this);
            return;
        }

        if (clearForeignWalls) ClearForeignWalls();
        ClearOwnWalls();

        if (buildWalls)
        {
            BuildSide(MapSide.Left);
            BuildSide(MapSide.Right);
            BuildSide(MapSide.Bottom);
            BuildSide(MapSide.Top);
        }

        if (syncToMapBoundary) SyncMapBoundary();
        if (syncCamera) SyncCamera();
        if (syncPlayerClamp) SyncPlayerClamp();

        if (verboseLog)
        {
            string stray = IgnoredCells.Count > 0
                ? "\n  注意：忽略了 " + IgnoredCells.Count + " 块离主体很远的散砖（" +
                  StrayText(IgnoredCells, 8) + "），最好进 Tilemap 里擦掉"
                : "";
            Debug.Log("[瓦片边界] 依据" + BoundsSourceName + " 算出地图 " +
                      MapBounds.size.x.ToString("F2") + " × " + MapBounds.size.y.ToString("F2") + " 单位\n" +
                      "  X[" + MapBounds.min.x.ToString("F2") + ", " + MapBounds.max.x.ToString("F2") + "]  " +
                      "Y[" + MapBounds.min.y.ToString("F2") + ", " + MapBounds.max.y.ToString("F2") + "]\n" +
                      "  生成 " + _walls.Count + " 段墙，人物只能走到边界为止" + stray, this);
        }
    }

    // ------------------------------------------------------------------ 算范围

    /// <summary>按当前设置取某个图层的世界范围</summary>
    private Bounds RectOf(Tilemap m)
    {
        return useCellCorners ? CoreWorldRect(m, ignoreStrayTiles) : RawWorldRect(m);
    }

    /// <summary>所有非空图层取并集</summary>
    private Bounds CombinedBounds(Tilemap[] all)
    {
        Bounds result = new Bounds();
        bool has = false;
        System.Text.StringBuilder names = new System.Text.StringBuilder();

        foreach (Tilemap m in all)
        {
            if (m == null || IsEmpty(m)) continue;
            Bounds b = RectOf(m);
            if (b.size.x <= 0.001f || b.size.y <= 0.001f) continue;

            if (!has) { result = b; has = true; }
            else result.Encapsulate(b);

            if (names.Length > 0) names.Append(" + ");
            names.Append(m.name);
        }

        BoundsSourceName = has ? ("所有瓦片图层（" + names + "）") : "（没有非空的瓦片图层）";
        return result;
    }

    /// <summary>只用指定的那一个图层</summary>
    private Bounds SingleBounds(Tilemap[] all)
    {
        Tilemap m = boundaryTilemap != null ? boundaryTilemap : AutoFindTilemap(all);
        if (m == null)
        {
            BoundsSourceName = "（没找到瓦片图层）";
            return new Bounds();
        }
        BoundsSourceName = "图层「" + m.name + "」";
        return RectOf(m);
    }

    /// <summary>按「实际画了砖的格子」算世界范围；dropStrays 时先剔掉孤立的散砖</summary>
    public static Bounds CoreWorldRect(Tilemap map, bool dropStrays)
    {
        if (map == null) return new Bounds();

        List<Vector3Int> cells = OccupiedCells(map);
        if (cells.Count == 0) return new Bounds();

        List<Vector3Int> used = dropStrays ? MainCluster(cells) : cells;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in used)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        return WorldRectOfCells(map, minX, minY, maxX, maxY);
    }

    /// <summary>格子范围（含首尾格）→ 世界范围</summary>
    private static Bounds WorldRectOfCells(Tilemap map, int minX, int minY, int maxX, int maxY)
    {
        Vector3 lo = map.CellToWorld(new Vector3Int(minX, minY, 0));
        Vector3 hi = map.CellToWorld(new Vector3Int(maxX + 1, maxY + 1, 0));
        Vector3 min = new Vector3(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), 0f);
        Vector3 max = new Vector3(Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y), 0f);
        return new Bounds((min + max) * 0.5f, new Vector3(max.x - min.x, max.y - min.y, 0f));
    }

    /// <summary>瓦片精灵外框的世界范围（仅供参考，通常比格子范围大）</summary>
    public static Bounds RawWorldRect(Tilemap map)
    {
        Bounds local = map.localBounds;
        Transform t = map.transform;
        Vector3 center = t.TransformPoint(local.center);
        Vector3 size = Vector3.Scale(local.size, t.lossyScale);
        return new Bounds(center, new Vector3(size.x, size.y, 0f));
    }

    public static bool IsEmpty(Tilemap map)
    {
        return map != null && OccupiedCells(map).Count == 0;
    }

    public static List<Vector3Int> OccupiedCells(Tilemap map)
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        if (map == null) return cells;

        BoundsInt cb = map.cellBounds;
        long area = (long)cb.size.x * (long)cb.size.y;
        if (area <= 0 || area > 2000000L) return cells;   // 范围异常大就不扫了，免得卡死

        for (int x = cb.xMin; x < cb.xMax; x++)
        {
            for (int y = cb.yMin; y < cb.yMax; y++)
            {
                Vector3Int c = new Vector3Int(x, y, cb.zMin);
                if (map.HasTile(c)) cells.Add(c);
            }
        }
        return cells;
    }

    /// <summary>
    /// 把格子按 8 邻域分成一簇簇，只留下连成一片的主体（以及贴着主体的小簇）。
    /// 剩下的就是散砖，会被记进 IgnoredCells。
    /// </summary>
    public static List<Vector3Int> MainCluster(List<Vector3Int> cells)
    {
        IgnoredCells.Clear();

        List<Vector3Int> src = new List<Vector3Int>(cells);
        if (src.Count <= 1) return src;

        HashSet<Vector3Int> pool = new HashSet<Vector3Int>(src);
        List<List<Vector3Int>> groups = new List<List<Vector3Int>>();

        while (pool.Count > 0)
        {
            Vector3Int start = default(Vector3Int);
            foreach (Vector3Int c in pool) { start = c; break; }

            List<Vector3Int> group = new List<Vector3Int>();
            Queue<Vector3Int> q = new Queue<Vector3Int>();
            q.Enqueue(start);
            pool.Remove(start);

            while (q.Count > 0)
            {
                Vector3Int c = q.Dequeue();
                group.Add(c);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        Vector3Int n = new Vector3Int(c.x + dx, c.y + dy, c.z);
                        if (pool.Contains(n)) { pool.Remove(n); q.Enqueue(n); }
                    }
                }
            }
            groups.Add(group);
        }

        if (groups.Count == 1) return groups[0];

        groups.Sort((a, b) => b.Count.CompareTo(a.Count));
        List<Vector3Int> main = groups[0];

        int mnx = int.MaxValue, mny = int.MaxValue, mxx = int.MinValue, mxy = int.MinValue;
        foreach (Vector3Int c in main)
        {
            if (c.x < mnx) mnx = c.x;
            if (c.y < mny) mny = c.y;
            if (c.x > mxx) mxx = c.x;
            if (c.y > mxy) mxy = c.y;
        }

        List<Vector3Int> keep = new List<Vector3Int>(main);
        for (int i = 1; i < groups.Count; i++)
        {
            bool near = false;
            foreach (Vector3Int c in groups[i])
            {
                if (c.x >= mnx - 1 && c.x <= mxx + 1 && c.y >= mny - 1 && c.y <= mxy + 1) { near = true; break; }
            }
            if (near) keep.AddRange(groups[i]);
            else IgnoredCells.AddRange(groups[i]);
        }
        return keep;
    }

    public static string StrayText(List<Vector3Int> cells, int maxShow)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < cells.Count && i < maxShow; i++)
        {
            if (i > 0) sb.Append(" ");
            sb.Append("(").Append(cells[i].x).Append(",").Append(cells[i].y).Append(")");
        }
        if (cells.Count > maxShow) sb.Append(" …等 ").Append(cells.Count).Append(" 块");
        return sb.ToString();
    }

    [ContextMenu("列出被忽略的散砖")]
    private void LogStrays()
    {
        Tilemap[] all = FindObjectsOfType<Tilemap>();
        foreach (Tilemap m in all)
        {
            if (m == null || IsEmpty(m)) continue;
            List<Vector3Int> all_ = OccupiedCells(m);
            List<Vector3Int> keep = MainCluster(all_);
            HashSet<Vector3Int> ks = new HashSet<Vector3Int>(keep);
            List<Vector3Int> stray = new List<Vector3Int>();
            foreach (Vector3Int c in all_) if (!ks.Contains(c)) stray.Add(c);

            if (stray.Count > 0)
                Debug.Log("[瓦片边界] 图层「" + m.name + "」有 " + stray.Count + " 块散砖：\n" +
                          StrayText(stray, 200) + "\n建议进 Tilemap 调色板用擦除工具刷掉。", m);
        }
    }

    private Tilemap AutoFindTilemap(Tilemap[] maps)
    {
        if (maps == null || maps.Length == 0) return null;

        string[] keys = { "边界", "bound", "wall", "ground", "ground1", "floor" };
        foreach (Tilemap m in maps)
        {
            string n = m.name.ToLower();
            foreach (string k in keys)
            {
                if (n.Contains(k)) return m;
            }
        }

        // 没有关键字就取「砖最多的那个图层」，而不是外框最大的那个
        Tilemap best = null;
        int bestCount = 0;
        foreach (Tilemap m in maps)
        {
            if (m == null) continue;
            int n = OccupiedCells(m).Count;
            if (n > bestCount) { bestCount = n; best = m; }
        }
        return best != null ? best : maps[0];
    }

    private void ClearForeignWalls()
    {
        // 让自动的那个别再按错误范围建墙
        MapBoundary mb = FindObjectOfType<MapBoundary>();
        if (mb != null) mb.buildWalls = false;

        List<GameObject> stale = new List<GameObject>();
        foreach (Transform tr in transform)
        {
            if (tr != null && tr.name.StartsWith("Wall_")) stale.Add(tr.gameObject);
        }
        GameObject root = mb != null ? mb.gameObject : null;
        if (root != null)
        {
            foreach (Transform tr in root.transform)
            {
                if (tr != null && tr.name.StartsWith("Wall_")) stale.Add(tr.gameObject);
            }
        }
        // 场景里其他遗留下来的
        foreach (MonoBehaviour any in FindObjectsOfType<MonoBehaviour>())
        {
            if (any != null && any.name.StartsWith("Wall_")) stale.Add(any.gameObject);
        }

        foreach (GameObject go in stale)
        {
            if (go == null || go.transform.IsChildOf(transform)) continue;
            SmartDestroy(go);
        }
    }

    private void ClearOwnWalls()
    {
        List<GameObject> old = new List<GameObject>(_walls);
        foreach (GameObject go in old) SmartDestroy(go);
        _walls.Clear();

        List<Transform> leftovers = new List<Transform>();
        foreach (Transform tr in transform)
        {
            if (tr != null && tr.name.StartsWith("Wall_")) leftovers.Add(tr);
        }
        foreach (Transform tr in leftovers) SmartDestroy(tr.gameObject);
    }

    private enum MapSide { Left, Right, Bottom, Top }

    private void BuildSide(MapSide side)
    {
        Bounds b = MapBounds;
        float t = wallThickness;
        float inset = wallInset;

        bool vertical = side == MapSide.Left || side == MapSide.Right;

        // 墙相对边界线的偏移方向：左/下为 -1，右/上为 +1
        float dir = (side == MapSide.Left || side == MapSide.Bottom) ? -1f : 1f;
        float place;
        switch (wallPlacement)
        {
            case WallPlacement.OnEdge: place = 0f; break;
            case WallPlacement.Inside: place = -dir * t * 0.5f; break;
            default: place = dir * t * 0.5f; break;
        }

        // 沿边方向的范围（两侧各缩进 inset）
        float alongMin = vertical ? b.min.y + inset : b.min.x + inset;
        float alongMax = vertical ? b.max.y - inset : b.max.x - inset;
        float cross = vertical
            ? (side == MapSide.Left ? b.min.x - inset : b.max.x + inset)
            : (side == MapSide.Bottom ? b.min.y - inset : b.max.y + inset);

        if (alongMax - alongMin <= 0.05f) return;

        foreach (KeyValuePair<float, float> seg in ComplementWithPortalGaps(alongMin, alongMax, side))
        {
            float len = seg.Value - seg.Key;
            if (len <= 0.05f) continue;

            GameObject go = new GameObject("Wall_" + side + "_" + Mathf.RoundToInt(seg.Key * 10f));
            go.transform.SetParent(transform, false);

            if (vertical)
            {
                float x = cross + place;
                go.transform.position = new Vector3(x, (seg.Key + seg.Value) * 0.5f, 0f);
                SetBox(go, new Vector2(t, len));
            }
            else
            {
                float y = cross + place;
                go.transform.position = new Vector3((seg.Key + seg.Value) * 0.5f, y, 0f);
                SetBox(go, new Vector2(len, t));
            }

            _walls.Add(go);
        }
    }

    /// <summary>把整条边按传送门的位置切成若干段（留缺口）</summary>
    private IEnumerable<KeyValuePair<float, float>> ComplementWithPortalGaps(float min, float max, MapSide side)
    {
        List<KeyValuePair<float, float>> result = new List<KeyValuePair<float, float>>();

        if (!leaveGapsForPortals)
        {
            result.Add(new KeyValuePair<float, float>(min, max));
            return result;
        }

        List<KeyValuePair<float, float>> gaps = new List<KeyValuePair<float, float>>();
        ScenePortal[] portals = FindObjectsOfType<ScenePortal>();
        Bounds b = MapBounds;
        float tol = wallThickness + 1.5f;

        foreach (ScenePortal p in portals)
        {
            if (p == null) continue;
            Collider2D col = p.GetComponent<Collider2D>();
            Bounds pb = col != null ? col.bounds : new Bounds(p.transform.position, Vector3.one);

            switch (side)
            {
                case MapSide.Left:
                    if (Mathf.Abs(pb.min.x - b.min.x) < tol) gaps.Add(new KeyValuePair<float, float>(pb.min.y, pb.max.y));
                    break;
                case MapSide.Right:
                    if (Mathf.Abs(pb.max.x - b.max.x) < tol) gaps.Add(new KeyValuePair<float, float>(pb.min.y, pb.max.y));
                    break;
                case MapSide.Bottom:
                    if (Mathf.Abs(pb.min.y - b.min.y) < tol) gaps.Add(new KeyValuePair<float, float>(pb.min.x, pb.max.x));
                    break;
                case MapSide.Top:
                    if (Mathf.Abs(pb.max.y - b.max.y) < tol) gaps.Add(new KeyValuePair<float, float>(pb.min.x, pb.max.x));
                    break;
            }
        }

        if (gaps.Count == 0)
        {
            result.Add(new KeyValuePair<float, float>(min, max));
            return result;
        }

        gaps.Sort((a, c) => a.Key.CompareTo(c.Key));
        float cursor = min;
        foreach (KeyValuePair<float, float> g in gaps)
        {
            float gapStart = Mathf.Clamp(g.Key, min, max);
            float gapEnd = Mathf.Clamp(g.Value, min, max);
            if (gapStart > cursor) result.Add(new KeyValuePair<float, float>(cursor, gapStart));
            cursor = Mathf.Max(cursor, gapEnd);
        }
        if (cursor < max) result.Add(new KeyValuePair<float, float>(cursor, max));

        return result;
    }

    private static void SetBox(GameObject go, Vector2 size)
    {
        BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
        if (bc == null) bc = go.AddComponent<BoxCollider2D>();
        bc.size = size;
        bc.offset = Vector2.zero;
        bc.isTrigger = false;
    }

    private void SyncMapBoundary()
    {
        MapBoundary mb = MapBoundary.Ensure();
        if (mb == null) return;

        mb.mapRenderer = GetOrCreateProxy();
        mb.buildWalls = false;   // 墙由我来建，避免两套墙打架
        mb.Build();              // 用正确范围重算：入口点 / 传送条才会吸附到真正的地图边上
    }

    /// <summary>一个和瓦片地图等大、不显示的 SpriteRenderer，专门给只认 SpriteRenderer 的脚本看</summary>
    private SpriteRenderer GetOrCreateProxy()
    {
        if (_proxy != null) return _proxy;

        Transform exist = transform.Find("MapBoundsProxy");
        GameObject go = exist != null ? exist.gameObject : new GameObject("MapBoundsProxy");
        if (exist == null) go.transform.SetParent(transform, false);

        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = UnitSprite();
        sr.enabled = false;

        // 用世界坐标摆位，父级缩放不会带偏
        go.transform.position = new Vector3(MapBounds.center.x, MapBounds.center.y, 0f);
        go.transform.localScale = new Vector3(
            MapBounds.size.x / Mathf.Max(0.0001f, transform.lossyScale.x),
            MapBounds.size.y / Mathf.Max(0.0001f, transform.lossyScale.y),
            1f);

        _proxy = sr;
        return sr;
    }

    private static Sprite UnitSprite()
    {
        if (_unitSprite != null) return _unitSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();

        // PPU = 1，所以这张 1×1 的图正好是 1 个世界单位，缩放几倍就是几单位大
        _unitSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        _unitSprite.name = "UnitProxy";
        return _unitSprite;
    }

    private void SyncCamera()
    {
        CameraFollow cf = FindObjectOfType<CameraFollow>();
        if (cf == null) return;

        cf.useManualBounds = true;
        cf.manualBounds = new Rect(MapBounds.min.x, MapBounds.min.y, MapBounds.size.x, MapBounds.size.y);
    }

    private void SyncPlayerClamp()
    {
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
        Rect r = new Rect(
            MapBounds.min.x - clampMargin,
            MapBounds.min.y - clampMargin,
            MapBounds.size.x + clampMargin * 2f,
            MapBounds.size.y + clampMargin * 2f);

        foreach (PlayerMovement p in players)
        {
            if (p == null) continue;
            p.useBoundary = true;
            p.boundary = r;
        }
    }

    private static void SmartDestroy(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }
}
