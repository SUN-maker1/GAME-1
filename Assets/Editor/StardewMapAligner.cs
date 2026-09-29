#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 【为什么要有这个工具】
///
/// 地图在世界里有多大，不是场景里说了算，而是「贴图像素 ÷ PPU」决定的：
///     原占位图 512px ÷ PPU 8   = 64 × 64 单位   ← 墙和传送点当初就是按这个摆的
///     换成 1280px ÷ PPU 100    = 12.8 × 10.7 单位 ← 图片一下子缩水到原来的 1/5
///
/// 图片换了、PPU 没改 → 地图本体变小，可墙 / 传送点 / 入口点还留在 ±32 的老位置，
/// 于是角色走出图片边界二十几个单位，才碰到那条传送边条 ——
/// 这就是「超出地图图片很远才传送」的全部原因。角色在黑地里空走，相机也跟着跑出去。
///
/// 这个工具按【当前实际渲染的地图范围】重排一切：
///   墙        → 刚好包住图片外侧
///   传送边条  → 贴着图片边缘内侧，做成一整条（星露谷式：沿着整条边缘走过去就换图）
///   入口点    → 从同一条边往里缩进，保证落地时身体不会压在传送点上
///   角色碰撞体 → 压到不会堵通道、不会误触传送点的尺寸
///   相机边界 / 移动边界 → 跟图片完全一致，再也走不到黑边上去
///
/// 换任何一张图，点一下就全对齐。
/// </summary>
public static class StardewMapAligner
{
    private const string MenuRoot = "Tools/农场RPG/";

    // 以下数值都可以按需改：想让传送边条更厚 / 入口点更靠里，改这里即可
    private const float PortalDepth = 0.8f;    // 传送边条厚度（世界单位），约半格多一点
    private const float PortalInset = 0f;      // 边条再往里缩多少，0 = 正好贴着图片边沿
    private const float PortalTrim = 0.15f;    // 边条两端的余量，避免和角落的墙打架
    private const float EntryInset = 2.2f;     // 入口点离地图边缘多远
    private const float WallThickness = 1f;    // 四周墙的厚度（兜底用，平时由移动边界拦住）
    private const float MaxPlayerCollider = 1.5f;

    [MenuItem(MenuRoot + "星露谷式：对齐【当前场景】地图边界与传送点", false, 40)]
    public static void AlignCurrentScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            Debug.LogWarning("[星露谷式对齐] 请先打开并保存一个场景（比如 Area_Farm）。");
            return;
        }

        AlignScene(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem(MenuRoot + "星露谷式：对齐【全部】区域场景（推荐）", false, 41)]
    public static void AlignAllAreaScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[星露谷式对齐] 已取消：先把没保存的场景处理好。");
            return;
        }

        string currentPath = SceneManager.GetActiveScene().path;
        List<string> paths = EditorBuildSettings.scenes
            .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToList();

        if (paths.Count == 0)
        {
            Debug.LogWarning("[星露谷式对齐] Build Settings 里没有已勾选的场景。" +
                             "File / Build Settings 里把它们加进去再来。");
            return;
        }

        foreach (string path in paths)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            AlignScene(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (!string.IsNullOrEmpty(currentPath))
            EditorSceneManager.OpenScene(currentPath, OpenSceneMode.Single);

        Debug.Log($"[星露谷式对齐] 已处理 {paths.Count} 个场景：{string.Join("、", paths.Select(System.IO.Path.GetFileNameWithoutExtension))}");
    }

    // ------------------------------------------------------------ 核心

    private static void AlignScene(Scene scene)
    {
        List<GameObject> roots = scene.GetRootGameObjects().ToList();

        Bounds map;
        if (!TryFindMapBounds(roots, out map))
        {
            Debug.LogWarning($"[星露谷式对齐] {scene.name} 里找不到带 SpriteRenderer 的地图背景，" +
                             "把地图物体叫 Map_Background 并确保它有 Sprite Renderer。");
            return;
        }

        AlignWalls(roots, map);
        AlignPortals(roots, map);
        AlignEntries(roots, map);
        FixPlayerCollider(roots, map);
        ApplyBounds(roots, map);

        EditorSceneManager.MarkSceneDirty(scene);

        string report =
            $"[星露谷式对齐] {scene.name} 完成\n" +
            $"  地图范围 X[{map.min.x:F2}, {map.max.x:F2}]  Y[{map.min.y:F2}, {map.max.y:F2}]（{map.size.x:F2} × {map.size.y:F2} 单位）\n" +
            $"  传送边条厚度 {PortalDepth} 单位，入口点内缩 {EntryInset} 单位";
        Debug.Log(report);

        WarnIfMapSmallerThanView(roots, map);
    }

    /// <summary>
    /// 地图比相机视野还小时，玩家会看到图片四周一圈黑边、相机也不跟着走。
    /// 这种情况给个明确的提示和一句话解法，别让人猜。
    /// </summary>
    private static void WarnIfMapSmallerThanView(List<GameObject> roots, Bounds map)
    {
        Camera cam = FindMainCamera(roots);
        if (cam == null || !cam.orthographic) return;

        float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
        float viewW = cam.orthographicSize * 2f * aspect;
        float viewH = cam.orthographicSize * 2f;

        if (map.size.x >= viewW && map.size.y >= viewH) return;

        float fit = Mathf.Max(2f, Mathf.Min(map.extents.y, map.extents.x / aspect));

        Debug.LogWarning(
            $"[星露谷式对齐] {cam.name} 的视野是 {viewW:F1}×{viewH:F1} 单位，地图只有 {map.size.x:F1}×{map.size.y:F1} 单位 —— " +
            "地图比屏幕小，四周会是黑边。\n" +
            $"  二选一：① 菜单「Tools / 农场RPG / 星露谷式：把地图放大到指定宽度…」把地图放大；" +
            $"② 菜单「星露谷式：相机视野贴合地图」把 Orthographic Size 从 {cam.orthographicSize:F2} 调到 {fit:F2} 左右。");
    }

    private static Camera FindMainCamera(List<GameObject> roots)
    {
        foreach (GameObject go in AllObjects(roots))
        {
            Camera cam = go.GetComponent<Camera>();
            if (cam != null) return cam;
        }
        return null;
    }

    /// <summary>
    /// 把正交相机缩到「视野不超过地图」为止 —— 图片不再小于屏幕，黑边消失，
    /// 相机也能真正地跟着角色横向/纵向移动，这就是星露谷那种一屏多一点的感觉。
    /// </summary>
    [MenuItem(MenuRoot + "星露谷式：相机视野贴合地图（调 Orthographic Size）", false, 44)]
    public static void FitCameraToMap()
    {
        Scene scene = SceneManager.GetActiveScene();
        List<GameObject> roots = scene.GetRootGameObjects().ToList();

        Bounds map;
        if (!TryFindMapBounds(roots, out map))
        {
            Debug.LogWarning("[星露谷式对齐] 找不到地图背景。");
            return;
        }

        Camera cam = FindMainCamera(roots);
        if (cam == null)
        {
            Debug.LogWarning("[星露谷式对齐] 场景里没有相机。");
            return;
        }

        cam.orthographic = true;

        float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
        float fit = Mathf.Max(2f, Mathf.Min(map.extents.y, map.extents.x / aspect));
        float before = cam.orthographicSize;

        // 只有视野大于地图时才收紧，别反过来把已经合适的视野拉大
        if (map.size.y >= cam.orthographicSize * 2f && map.size.x >= cam.orthographicSize * 2f * aspect)
        {
            Debug.Log($"[星露谷式对齐] 地图已经比视野大（地图 {map.size.x:F1}×{map.size.y:F1}，" +
                      $"视野 {cam.orthographicSize * 2f * aspect:F1}×{cam.orthographicSize * 2f:F1}），不用调。");
            return;
        }

        Undo.RecordObject(cam, "Fit Camera To Map");
        cam.orthographicSize = fit;
        EditorUtility.SetDirty(cam.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[星露谷式对齐] Orthographic Size {before:F2} → {fit:F2}，" +
                  $"视野现在是 {fit * 2f * aspect:F1}×{fit * 2f:F1} 单位，刚好装进地图 {map.size.x:F1}×{map.size.y:F1}。");
    }

    /// <summary>按实际渲染结果取地图世界范围（会算上 transform 缩放）</summary>
    private static bool TryFindMapBounds(List<GameObject> roots, out Bounds bounds)
    {
        bounds = default;

        SpriteRenderer chosen = FindMapRenderer(roots);
        if (chosen == null) return false;

        bounds = chosen.bounds;
        return bounds.size.x > 0.01f && bounds.size.y > 0.01f;
    }

    /// <summary>找出场景里的地图背景：优先叫 Map_Background 的，其次面积最大的那张</summary>
    public static SpriteRenderer FindMapRenderer(List<GameObject> roots)
    {
        SpriteRenderer chosen = null;

        foreach (GameObject go in roots)
        {
            if (go == null) continue;
            SpriteRenderer sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr == null || sr.sprite == null) continue;

            if (go.name == "Map_Background")
                return sr;

            if (chosen == null || sr.bounds.size.sqrMagnitude > chosen.bounds.size.sqrMagnitude)
                chosen = sr;
        }

        return chosen;
    }

    // ------------------------------------------------------------ 墙

    private static void AlignWalls(List<GameObject> roots, Bounds map)
    {
        float halfW = map.extents.x + WallThickness * 0.5f;
        float halfH = map.extents.y + WallThickness * 0.5f;
        float longW = map.size.x + WallThickness * 2f;
        float longH = map.size.y + WallThickness * 2f;

        SetWall(FindByName(roots, "Wall_Top"), new Vector2(map.center.x, map.center.y + halfH), new Vector2(longW, WallThickness));
        SetWall(FindByName(roots, "Wall_Bottom"), new Vector2(map.center.x, map.center.y - halfH), new Vector2(longW, WallThickness));
        SetWall(FindByName(roots, "Wall_Left"), new Vector2(map.center.x - halfW, map.center.y), new Vector2(WallThickness, longH));
        SetWall(FindByName(roots, "Wall_Right"), new Vector2(map.center.x + halfW, map.center.y), new Vector2(WallThickness, longH));
    }

    private static void SetWall(GameObject go, Vector2 pos, Vector2 size)
    {
        if (go == null) return;

        Undo.RecordObject(go.transform, "Align Map Wall");
        go.transform.position = new Vector3(pos.x, pos.y, go.transform.position.z);

        BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
        if (bc == null) bc = go.AddComponent<BoxCollider2D>();

        Undo.RecordObject(bc, "Align Map Wall");
        bc.isTrigger = false;
        bc.size = size;
        EditorUtility.SetDirty(go);
    }

    // ------------------------------------------------------------ 传送边条

    private static void AlignPortals(List<GameObject> roots, Bounds map)
    {
        foreach (GameObject go in AllObjects(roots))
        {
            ScenePortal portal = go.GetComponent<ScenePortal>();
            if (portal == null) continue;

            Side side = GuessSide(go.transform.position, map);

            Undo.RecordObject(go.transform, "Align Map Portal");

            Vector3 pos = go.transform.position;
            Vector2 size;

            switch (side)
            {
                case Side.Left:
                case Side.Right:
                    bool right = side == Side.Right;
                    float x = right ? map.max.x - PortalInset - PortalDepth * 0.5f
                                    : map.min.x + PortalInset + PortalDepth * 0.5f;
                    pos = new Vector3(x, map.center.y, pos.z);
                    size = new Vector2(PortalDepth, Mathf.Max(1f, map.size.y - PortalTrim * 2f));
                    break;

                default:
                    bool top = side == Side.Top;
                    float y = top ? map.max.y - PortalInset - PortalDepth * 0.5f
                                  : map.min.y + PortalInset + PortalDepth * 0.5f;
                    pos = new Vector3(map.center.x, y, pos.z);
                    size = new Vector2(Mathf.Max(1f, map.size.x - PortalTrim * 2f), PortalDepth);
                    break;
            }

            go.transform.position = pos;

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc == null) bc = go.AddComponent<BoxCollider2D>();

            Undo.RecordObject(bc, "Align Map Portal");
            bc.isTrigger = true;
            bc.offset = Vector2.zero;
            bc.size = size;

            // 顺手把手感参数校准一下：被改过头的数值这里统一拉回来
            Undo.RecordObject(portal, "Align Map Portal");
            if (portal.rearmDelay < 0.2f) portal.rearmDelay = 1.2f;
            if (portal.holdBeforeTrigger < 0.05f || portal.holdBeforeTrigger > 0.5f)
                portal.holdBeforeTrigger = 0.12f;
            portal.waitForInputReleaseOnArrival = true;

            EditorUtility.SetDirty(go);
        }
    }

    // ------------------------------------------------------------ 入口点

    private static void AlignEntries(List<GameObject> roots, Bounds map)
    {
        foreach (GameObject go in AllObjects(roots))
        {
            SceneEntryPoint entry = go.GetComponent<SceneEntryPoint>();
            if (entry == null) continue;

            Undo.RecordObject(go.transform, "Align Map Entry");

            // 起始点留在地图正中，别让它掉到地图外面
            if (entry.entryID == "Start")
            {
                Vector3 c = ClampInside(go.transform.position, map, 1f);
                go.transform.position = new Vector3(c.x, c.y, go.transform.position.z);
                EditorUtility.SetDirty(go);
                continue;
            }

            Side side = GuessSide(go.transform.position, map);
            float depth = Mathf.Min(EntryInset, Mathf.Max(1.2f, (side == Side.Left || side == Side.Right ? map.size.x : map.size.y) * 0.35f));

            Vector3 pos = go.transform.position;
            switch (side)
            {
                case Side.Right:  pos = new Vector3(map.max.x - depth, ClampY(go.transform.position.y, map), pos.z); break;
                case Side.Left:   pos = new Vector3(map.min.x + depth, ClampY(go.transform.position.y, map), pos.z); break;
                case Side.Top:    pos = new Vector3(ClampX(go.transform.position.x, map), map.max.y - depth, pos.z); break;
                case Side.Bottom: pos = new Vector3(ClampX(go.transform.position.x, map), map.min.y + depth, pos.z); break;
            }

            go.transform.position = pos;
            EditorUtility.SetDirty(go);
        }
    }

    private static float ClampY(float y, Bounds map)
        => Mathf.Clamp(y, map.min.y + 1f, map.max.y - 1f);

    private static float ClampX(float x, Bounds map)
        => Mathf.Clamp(x, map.min.x + 1f, map.max.x - 1f);

    private static Vector3 ClampInside(Vector3 p, Bounds map, float margin)
        => new Vector3(
            Mathf.Clamp(p.x, map.min.x + margin, map.max.x - margin),
            Mathf.Clamp(p.y, map.min.y + margin, map.max.y - margin),
            p.z);

    // ------------------------------------------------------------ 角色碰撞体

    /// <summary>
    /// 角色碰撞体必须是「脚下一小块」，不能等于贴图尺寸。
    /// 贴图一换（比如曾经挂过 20.48 单位的大图），这里残留一个 14×14 的方盒子，
    /// 落地时身体直接压在传送边条上 —— 于是「传过去立刻又传回来」。
    /// </summary>
    private static void FixPlayerCollider(List<GameObject> roots, Bounds map)
    {
        foreach (GameObject go in AllObjects(roots))
        {
            if (go.GetComponent<PersistentPlayer>() == null && go.name != "Player") continue;

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc == null || bc.isTrigger) continue;

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            Vector2 spriteSize = sr != null && sr.sprite != null
                ? sr.sprite.bounds.size
                : Vector2.one;

            Vector2 want = new Vector2(
                Mathf.Clamp(spriteSize.x * 0.6f, 0.3f, MaxPlayerCollider),
                Mathf.Clamp(spriteSize.y * 0.45f, 0.3f, MaxPlayerCollider));

            if (bc.size.x <= MaxPlayerCollider && bc.size.y <= MaxPlayerCollider) continue;

            Vector2 old = bc.size;

            Undo.RecordObject(bc, "Fix Player Collider");
            bc.size = want;
            Debug.LogWarning($"[星露谷式对齐] {go.name} 的碰撞体原本 {old.x:F2}×{old.y:F2}，" +
                             $"已经压到 {want.x:F2}×{want.y:F2}。碰撞体过大会堵通道、误触传送边条，别再手调回去。");
            EditorUtility.SetDirty(go);
        }
    }

    // ------------------------------------------------------------ 边界

    private static void ApplyBounds(List<GameObject> roots, Bounds map)
    {
        Rect rect = new Rect(map.min.x, map.min.y, map.size.x, map.size.y);

        foreach (GameObject go in AllObjects(roots))
        {
            CameraFollow follow = go.GetComponent<CameraFollow>();
            if (follow != null)
            {
                Undo.RecordObject(follow, "Align Map Bounds");
                follow.useManualBounds = true;
                follow.manualBounds = rect;
                EditorUtility.SetDirty(go);
            }

            PlayerMovement move = go.GetComponent<PlayerMovement>();
            if (move != null)
            {
                Undo.RecordObject(move, "Align Map Bounds");
                move.useBoundary = true;
                move.boundary = rect;
                EditorUtility.SetDirty(go);
            }
        }
    }

    // ------------------------------------------------------------ 工具

    private enum Side { Left, Right, Top, Bottom }

    /// <summary>看这个点离哪条边最近，判断它属于哪一侧</summary>
    private static Side GuessSide(Vector3 p, Bounds b)
    {
        float nx = (p.x - b.center.x) / Mathf.Max(0.0001f, b.extents.x);
        float ny = (p.y - b.center.y) / Mathf.Max(0.0001f, b.extents.y);

        if (Mathf.Abs(nx) >= Mathf.Abs(ny))
            return nx >= 0f ? Side.Right : Side.Left;

        return ny >= 0f ? Side.Top : Side.Bottom;
    }

    private static GameObject FindByName(List<GameObject> roots, string name)
    {
        foreach (GameObject go in AllObjects(roots))
        {
            if (go != null && go.name == name) return go;
        }
        return null;
    }

    private static IEnumerable<GameObject> AllObjects(List<GameObject> roots)
    {
        foreach (GameObject root in roots)
        {
            if (root == null) continue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                yield return t.gameObject;
        }
    }

    // ------------------------------------------------------------ 体检

    [MenuItem(MenuRoot + "星露谷式：体检（打印当前场景的对不对）", false, 42)]
    public static void Diagnose()
    {
        Scene scene = SceneManager.GetActiveScene();
        List<GameObject> roots = scene.GetRootGameObjects().ToList();

        Bounds map;
        if (!TryFindMapBounds(roots, out map))
        {
            Debug.LogWarning("[体检] 找不到地图背景 SpriteRenderer。");
            return;
        }

        List<string> lines = new List<string>
        {
            $"场景：{scene.name}",
            $"地图范围 X[{map.min.x:F2}, {map.max.x:F2}]  Y[{map.min.y:F2}, {map.max.y:F2}]  尺寸 {map.size.x:F2}×{map.size.y:F2}"
        };

        foreach (GameObject go in AllObjects(roots))
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                Texture tex = sr.sprite.texture;
                lines.Add($"  · {go.name}：贴图 {tex.width}×{tex.height}px，世界尺寸 {sr.bounds.size.x:F2}×{sr.bounds.size.y:F2}");
            }
        }

        foreach (GameObject go in AllObjects(roots))
        {
            ScenePortal portal = go.GetComponent<ScenePortal>();
            if (portal == null) continue;

            Bounds pb = GetPortalBox(portal);
            bool insideMap = map.Intersects(pb);
            float gapToEdge = DistanceOutside(map, pb);

            lines.Add($"  · 传送点 {go.name} → {portal.targetSceneName}/{portal.entryID}：" +
                      $"盒子 {pb.size.x:F2}×{pb.size.y:F2} @({pb.center.x:F2}, {pb.center.y:F2})，" +
                      (insideMap ? "在地图内 OK" : $"完全在地图外 ✗ 距离地图边 {gapToEdge:F2} 单位"));

            if (!insideMap)
                lines.Add($"      ↑ 这就是「要走出图片很远才传送」：玩家得空走 {gapToEdge:F2} 单位才够得着它");
        }

        foreach (GameObject go in AllObjects(roots))
        {
            SceneEntryPoint entry = go.GetComponent<SceneEntryPoint>();
            if (entry == null) continue;

            foreach (GameObject other in AllObjects(roots))
            {
                ScenePortal portal = other.GetComponent<ScenePortal>();
                if (portal == null) continue;

                Bounds pb = GetPortalBox(portal);
                if (pb.Contains(go.transform.position))
                    lines.Add($"  · 入口点 {go.name}（{entry.entryID}）正压在传送点 {other.name} 里 ✗ 落地就会被传回去");
            }
        }

        foreach (GameObject go in AllObjects(roots))
        {
            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc == null || bc.isTrigger) continue;
            if (go.GetComponent<Rigidbody2D>() == null) continue;

            if (bc.size.x > 2f || bc.size.y > 2f)
                lines.Add($"  · {go.name} 的角色碰撞体 {bc.size.x:F2}×{bc.size.y:F2} 过大 ✗ 会误触传送边条");
        }

        Debug.Log("───── 地图布局体检 ─────\n" + string.Join("\n", lines));
    }

    private static Bounds GetPortalBox(ScenePortal portal)
    {
        BoxCollider2D bc = portal.GetComponent<BoxCollider2D>();
        return bc != null ? bc.bounds : new Bounds(portal.transform.position, Vector3.one);
    }

    /// <summary>传送点盒子到地图矩形的最短距离；在地图内时返回 0</summary>
    private static float DistanceOutside(Bounds map, Bounds portal)
    {
        float dx = Mathf.Max(map.min.x - portal.max.x, portal.min.x - map.max.x, 0f);
        float dy = Mathf.Max(map.min.y - portal.max.y, portal.min.y - map.max.y, 0f);
        return Mathf.Max(dx, dy);
    }
}

/// <summary>
/// 地图在世界里多大 = 贴图像素 ÷ PPU。想让地图变大，改 PPU 就行：
///     1280px ÷ PPU 100 = 12.8 单位（现在的农场，比角色大不了多少）
///     1280px ÷ PPU 20  = 64 单位（和原本的场地一样大）
///
/// 这一个窗口把「改 PPU → 重排布局」两步合成一步。
/// </summary>
public class StardewMapResizer : EditorWindow
{
    private const string MenuRoot = "Tools/农场RPG/";

    private float targetWidth = 64f;
    private bool alignAfterwards = true;

    [MenuItem(MenuRoot + "星露谷式：把地图放大到指定宽度…", false, 43)]
    public static void Open()
    {
        StardewMapResizer win = GetWindow<StardewMapResizer>("地图放大");
        win.minSize = new Vector2(340f, 150f);
        win.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "地图世界尺寸 = 贴图像素 ÷ PPU。填一个目标宽度，PPU 会自动重算。\n" +
            "1280px 的图填 64，就是 PPU 20，和原本的场地一样大。",
            MessageType.Info);

        targetWidth = EditorGUILayout.FloatField("目标宽度（世界单位）", targetWidth);
        alignAfterwards = EditorGUILayout.Toggle("放大后重新对齐传送点 / 相机边界", alignAfterwards);

        EditorGUILayout.Space();

        if (GUILayout.Button("应用到当前场景的地图"))
            Apply();
    }

    private void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            Debug.LogWarning("[地图放大] 请先打开并保存一个场景。");
            return;
        }

        if (targetWidth <= 0.5f)
        {
            Debug.LogWarning("[地图放大] 目标宽度得大于 0.5。");
            return;
        }

        List<GameObject> roots = scene.GetRootGameObjects().ToList();
        SpriteRenderer map = StardewMapAligner.FindMapRenderer(roots);

        if (map == null || map.sprite == null)
        {
            Debug.LogWarning("[地图放大] 找不到地图背景 SpriteRenderer。");
            return;
        }

        Texture2D tex = map.sprite.texture;
        string path = AssetDatabase.GetAssetPath(tex);

        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null)
        {
            Debug.LogWarning($"[地图放大] 拿不到导入设置：{path}");
            return;
        }

        float oldPPu = ti.spritePixelsPerUnit;
        float newPPu = tex.width / targetWidth;

        // 同一张贴图如果还被别的物体用着，改 PPU 会连带把那些物体一起放大，先提醒
        int otherUsers = 0;
        foreach (SpriteRenderer sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null || sr.sprite.texture != tex) continue;
            if (sr == map) continue;
            otherUsers++;
        }

        ti.spritePixelsPerUnit = newPPu;
        ti.filterMode = FilterMode.Point;
        ti.SaveAndReimport();

        float newHeight = tex.height / newPPu;

        Debug.Log($"[地图放大] {tex.name}：PPU {oldPPu:F1} → {newPPu:F1}，" +
                  $"地图从 {tex.width / oldPPu:F1}×{tex.height / oldPPu:F1} 变成 {targetWidth:F1}×{newHeight:F1} 单位。" +
                  (otherUsers > 0 ? $"\n注意：还有 {otherUsers} 个物体也在用这张贴图，它们会一起变大。" : ""));

        if (alignAfterwards)
        {
            StardewMapAligner.AlignCurrentScene();
            Debug.Log("[地图放大] 布局已重新对齐：墙 / 传送边条 / 入口点 / 相机边界都跟上了新尺寸。");
        }

        Close();
    }
}
#endif
