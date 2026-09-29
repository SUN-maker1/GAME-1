#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 传送点可视化编辑器 —— 用鼠标直接决定「传送点在哪、多宽」。
///
/// 菜单：Tools ▸ 农场RPG ▸ 星露谷式：传送点编辑器（鼠标拖）
///
/// 两种鼠标操作：
///   1. 拖着画：窗口里打开「拖着画一个」，在 Scene 视图按住左键拖一条线，
///      松手就在最近的地图边上生成传送点 —— 线的长度就是通道宽度，位置就是门的位置。
///   2. 拖手柄：选中任意传送点后，Scene 视图会出现手柄
///      圆点（两端）= 拖宽/拖窄通道宽度
///      方块（中心）= 沿边挪动门的位置
///      菱形（外侧）= 调边条厚度（伸出地图多深）
///
/// 所有改动都会写回 ScenePortal 的 lengthAlongEdge / thickness / edge，
/// 并把 keepPositionAlongEdge 打开 —— 不然运行时自动贴边会把它挪回边的正中间。
/// </summary>
public class ScenePortalDesigner : EditorWindow
{
    private string[] sceneNames = new string[0];
    private int sceneIndex = 0;
    private string targetScene = "";
    private string entryID = "FromFarm";

    private float thickness = 1.5f;
    private float defaultLength = 6f;
    private bool snapToEdge = true;

    private bool drawMode;
    private bool dragging;
    private Vector3 dragStart;
    private Vector3 dragEnd;

    private Vector2 scroll;
    private GUIStyle wrap;

    [MenuItem("Tools/农场RPG/星露谷式：传送点编辑器（鼠标拖）", false, 210)]
    public static void Open()
    {
        ScenePortalDesigner w = GetWindow<ScenePortalDesigner>("传送点编辑器");
        w.Refresh();
        w.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Refresh();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // ---------------------------------------------------------------- 共享几何工具

    /// <summary>取当前场景地图背景的世界矩形（找不到返回 false）</summary>
    public static bool TryGetMapBounds(out Bounds bounds)
    {
        bounds = default;

        SpriteRenderer named = null, biggest = null;
        float biggestArea = 0f;

        foreach (SpriteRenderer sr in FindObjectsOfType<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null) continue;

            Bounds b = MapBoundary.WorldBounds(sr);
            float area = b.size.x * b.size.y;

            if (sr.name.IndexOf("Map", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                sr.name.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                sr.name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (named == null) named = sr;
            }

            if (area > biggestArea)
            {
                biggestArea = area;
                biggest = sr;
            }
        }

        SpriteRenderer map = named != null ? named : biggest;
        if (map == null || map.sprite == null) return false;

        bounds = MapBoundary.WorldBounds(map);
        return bounds.size.x > 0.01f && bounds.size.y > 0.01f;
    }

    /// <summary>左右边上的传送条是竖着延伸的（沿 Y），上下边是横着的（沿 X）</summary>
    public static bool IsVerticalEdge(MapEdge edge)
    {
        return edge == MapEdge.Left || edge == MapEdge.Right;
    }

    /// <summary>传送条延伸的方向</summary>
    public static Vector3 AxisOf(MapEdge edge)
    {
        return IsVerticalEdge(edge) ? Vector3.up : Vector3.right;
    }

    /// <summary>「走出地图」的方向</summary>
    public static Vector3 OutwardOf(MapEdge edge)
    {
        switch (edge)
        {
            case MapEdge.Left:   return Vector3.left;
            case MapEdge.Right:  return Vector3.right;
            case MapEdge.Bottom: return Vector3.down;
            default:             return Vector3.up;
        }
    }

    /// <summary>取某个世界坐标在「沿边方向」上的分量</summary>
    public static float AlongOf(Vector3 pos, MapEdge edge)
    {
        return IsVerticalEdge(edge) ? pos.y : pos.x;
    }

    /// <summary>
    /// 编辑器里没有 MapBoundary.Current，所以自己按地图矩形算最近的边。
    /// （运行时 ScenePortal.Edge 走的是 MapBoundary 那份逻辑，规则一致。）
    /// </summary>
    public static MapEdge ResolveEdge(ScenePortal portal, Bounds map, bool hasMap)
    {
        if (portal != null && portal.edge != MapEdge.Auto) return portal.edge;
        if (!hasMap) return MapEdge.Right;

        Vector3 p = portal != null ? portal.transform.position : map.center;

        float dl = Mathf.Abs(p.x - map.min.x);
        float dr = Mathf.Abs(map.max.x - p.x);
        float db = Mathf.Abs(p.y - map.min.y);
        float dt = Mathf.Abs(map.max.y - p.y);

        float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
        if (m == dl) return MapEdge.Left;
        if (m == dr) return MapEdge.Right;
        if (m == db) return MapEdge.Bottom;
        return MapEdge.Top;
    }

    /// <summary>
    /// 把传送点的几何写回组件 + 碰撞体 + Transform。
    /// alongCenter：沿边方向的中心坐标（左右边传 y，上下边传 x）。
    /// </summary>
    public static void ApplyGeometry(ScenePortal portal, MapEdge edge,
                                     float alongCenter, float length, float thick)
    {
        if (portal == null) return;

        Bounds map;
        bool hasMap = TryGetMapBounds(out map);

        // Auto 必须先落成一条具体的边，否则改宽度会按「上下边」处理，门会跑错边
        if (edge == MapEdge.Auto)
            edge = ResolveEdge(portal, map, hasMap);

        length = Mathf.Max(0.3f, length);
        thick = Mathf.Max(0.2f, thick);

        // 沿边方向别超出地图（门比地图还长时退回中心）
        if (hasMap)
        {
            float minA = IsVerticalEdge(edge) ? map.min.y : map.min.x;
            float maxA = IsVerticalEdge(edge) ? map.max.y : map.max.x;

            if (maxA - minA > length)
                alongCenter = Mathf.Clamp(alongCenter, minA + length * 0.5f, maxA - length * 0.5f);
            else
                alongCenter = (minA + maxA) * 0.5f;
        }

        Vector3 pos = portal.transform.position;
        Vector2 size;

        switch (edge)
        {
            case MapEdge.Left:
            case MapEdge.Right:
                pos.x = hasMap ? (edge == MapEdge.Left ? map.min.x : map.max.x) : pos.x;
                pos.y = alongCenter;
                size = new Vector2(thick, length);
                break;

            default:
                pos.y = hasMap ? (edge == MapEdge.Bottom ? map.min.y : map.max.y) : pos.y;
                pos.x = alongCenter;
                size = new Vector2(length, thick);
                break;
        }

        Undo.RecordObject(portal.transform, "编辑传送点");
        Undo.RecordObject(portal, "编辑传送点");

        BoxCollider2D box = portal.GetComponent<BoxCollider2D>();
        if (box != null) Undo.RecordObject(box, "编辑传送点");

        portal.edge = edge;
        portal.lengthAlongEdge = length;
        portal.thickness = thick;

        // 关键：不打开的话，运行时 autoSnapToEdge 会把门挪回边的正中间
        portal.keepPositionAlongEdge = true;

        portal.transform.position = pos;

        if (box != null)
        {
            box.isTrigger = true;
            box.size = size;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.SetDirty(portal);
    }

    /// <summary>鼠标屏幕坐标 → 地图所在的 z 平面上的世界坐标</summary>
    public static Vector3 MouseToWorld(Vector2 guiPoint, float planeZ)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, planeZ));

        float dist;
        if (plane.Raycast(ray, out dist)) return ray.GetPoint(dist);

        return new Vector3(ray.origin.x, ray.origin.y, planeZ);
    }

    // ---------------------------------------------------------------- Scene 视图

    private void OnSceneGUI(SceneView view)
    {
        Bounds map;
        bool hasMap = TryGetMapBounds(out map);
        float planeZ = hasMap ? map.center.z : 0f;

        if (hasMap)
        {
            Handles.color = new Color(0.35f, 0.75f, 1f, 0.5f);
            Vector3[] corners =
            {
                new Vector3(map.min.x, map.min.y, planeZ),
                new Vector3(map.max.x, map.min.y, planeZ),
                new Vector3(map.max.x, map.max.y, planeZ),
                new Vector3(map.min.x, map.max.y, planeZ),
            };
            Handles.DrawSolidRectangleWithOutline(corners,
                new Color(0.35f, 0.75f, 1f, 0.04f),
                new Color(0.35f, 0.75f, 1f, 0.45f));
        }

        if (drawMode)
            HandleDrawMode(view, map, hasMap, planeZ);

        // 拖拽中用大号提示，免得忘了自己在干什么
        if (drawMode)
        {
            Handles.BeginGUI();
            GUIStyle box = new GUIStyle(GUI.skin.box);
            box.normal.textColor = Color.white;
            box.fontSize = 12;
            box.alignment = TextAnchor.MiddleLeft;
            GUI.backgroundColor = new Color(0.1f, 0.45f, 0.75f, 0.9f);
            GUI.Box(new Rect(10, 10, 300, 26),
                    dragging ? "松开左键就生成传送点" : "在地图上按住左键拖一条线 = 传送门的位置和宽度", box);
            Handles.EndGUI();
        }
    }

    private void HandleDrawMode(SceneView view, Bounds map, bool hasMap, float planeZ)
    {
        if (!hasMap) return;

        Event e = Event.current;

        // 抢下左键，不然会被默认的框选工具吃掉
        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlID);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            dragStart = MouseToWorld(e.mousePosition, planeZ);
            dragEnd = dragStart;
            dragging = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && dragging)
        {
            dragEnd = MouseToWorld(e.mousePosition, planeZ);
            e.Use();
            view.Repaint();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && dragging && e.button == 0)
        {
            dragging = false;
            CreateFromDrag(map);
            e.Use();
            view.Repaint();
            Repaint();
        }

        if (dragging)
            DrawDragPreview(map, planeZ);
    }

    /// <summary>把当前拖拽换算成「一条边 + 中心 + 长度」</summary>
    private void ResolveDrag(Bounds map, out MapEdge edge, out float along, out float length)
    {
        Vector3 d = dragEnd - dragStart;
        Vector3 mid = (dragStart + dragEnd) * 0.5f;

        // 横向拖 → 门开在上下边；纵向拖 → 门开在左右边
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
        {
            bool bottom = Mathf.Abs(mid.y - map.min.y) <= Mathf.Abs(map.max.y - mid.y);
            edge = bottom ? MapEdge.Bottom : MapEdge.Top;
            length = Mathf.Abs(d.x);
        }
        else
        {
            bool left = Mathf.Abs(mid.x - map.min.x) <= Mathf.Abs(map.max.x - mid.x);
            edge = left ? MapEdge.Left : MapEdge.Right;
            length = Mathf.Abs(d.y);
        }

        if (length < 0.5f) length = defaultLength;   // 手抖点了一下，给个默认宽度
        along = AlongOf(mid, edge);
    }

    private void DrawDragPreview(Bounds map, float planeZ)
    {
        MapEdge edge;
        float along, length;
        ResolveDrag(map, out edge, out along, out length);

        Bounds b = PreviewBounds(map, edge, along, length, thickness);

        Vector3[] corners =
        {
            new Vector3(b.min.x, b.min.y, planeZ),
            new Vector3(b.max.x, b.min.y, planeZ),
            new Vector3(b.max.x, b.max.y, planeZ),
            new Vector3(b.min.x, b.max.y, planeZ),
        };

        Handles.DrawSolidRectangleWithOutline(corners,
            new Color(0.3f, 1f, 0.5f, 0.25f),
            new Color(0.2f, 0.9f, 0.4f, 0.95f));

        Handles.Label(new Vector3(b.center.x, b.max.y, planeZ) + Vector3.up * 0.3f,
                      $"{edge} 边 · 宽 {length:F2}");
    }

    private static Bounds PreviewBounds(Bounds map, MapEdge edge, float along,
                                        float length, float thick)
    {
        float cx, cy, sx, sy;

        if (IsVerticalEdge(edge))
        {
            cx = edge == MapEdge.Left ? map.min.x : map.max.x;
            cy = along;
            sx = thick;
            sy = length;
        }
        else
        {
            cx = along;
            cy = edge == MapEdge.Bottom ? map.min.y : map.max.y;
            sx = length;
            sy = thick;
        }

        return new Bounds(new Vector3(cx, cy, map.center.z), new Vector3(sx, sy, 0f));
    }

    private void CreateFromDrag(Bounds map)
    {
        MapEdge edge;
        float along, length;
        ResolveDrag(map, out edge, out along, out length);

        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogWarning("[传送点] 先在编辑器窗口里选好「目标场景」，再拖。");
            return;
        }

        GameObject go = new GameObject("Portal_To_" + targetScene);
        Undo.RegisterCreatedObjectUndo(go, "新建传送点");

        Transform parent = EnsureContainer();
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(
            IsVerticalEdge(edge) ? (edge == MapEdge.Left ? map.min.x : map.max.x) : along,
            IsVerticalEdge(edge) ? along : (edge == MapEdge.Bottom ? map.min.y : map.max.y),
            map.center.z);

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;

        ScenePortal portal = go.AddComponent<ScenePortal>();
        portal.targetSceneName = targetScene;
        portal.entryID = entryID;
        portal.autoSnapToEdge = snapToEdge;

        ApplyGeometry(portal, edge, along, length, thickness);

        Selection.activeGameObject = go;

        Debug.Log($"[传送点] 已在 {edge} 边生成 {go.name} → {targetScene}（入口 {entryID}）\n" +
                  $"  沿边位置 {along:F2}，通道宽 {length:F2}，厚度 {thickness:F2}\n" +
                  $"  记得 Ctrl+S 保存场景。");
    }

    private static Transform EnsureContainer()
    {
        GameObject existing = GameObject.Find("Portals");
        if (existing != null) return existing.transform;

        GameObject go = new GameObject("Portals");
        Undo.RegisterCreatedObjectUndo(go, "新建传送点容器");
        return go.transform;
    }

    // ---------------------------------------------------------------- 窗口界面

    private void Refresh()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;

        sceneNames = new string[EditorBuildSettings.scenes.Length];
        for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            sceneNames[i] = System.IO.Path.GetFileNameWithoutExtension(EditorBuildSettings.scenes[i].path);
    }

    private void OnGUI()
    {
        if (wrap == null) wrap = new GUIStyle(EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("传送点编辑器", EditorStyles.boldLabel);

        Bounds map;
        bool hasMap = TryGetMapBounds(out map);
        EditorGUILayout.HelpBox(hasMap
            ? $"场景 {SceneManager.GetActiveScene().name}　地图 {map.size.x:F2} × {map.size.y:F2} 单位\n" +
              $"X[{map.min.x:F2}, {map.max.x:F2}]　Y[{map.min.y:F2}, {map.max.y:F2}]"
            : "这个场景里没找到地图背景（Map_Background）。先打开一个 Area_ 场景。",
            hasMap ? MessageType.None : MessageType.Warning);

        EditorGUILayout.Space();

        // ---- 拖着画 ----
        GUI.backgroundColor = drawMode ? new Color(0.4f, 0.9f, 0.5f) : Color.white;
        string btn = drawMode ? "● 拖着画：已开启（点这里关闭）" : "○ 拖着画一个：开启";
        if (GUILayout.Button(btn, GUILayout.Height(30)))
        {
            drawMode = !drawMode;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.LabelField(
            "开启后：在 Scene 视图里按住鼠标左键拖一条线 —— 横着拖门就开在上下边，" +
            "竖着拖就开在左右边；线的长度就是通道宽度，位置就是门的位置。", wrap);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("新建传送点的参数", EditorStyles.boldLabel);

        if (sceneNames.Length > 0)
        {
            sceneIndex = EditorGUILayout.Popup("目标场景", sceneIndex, sceneNames);
            if (sceneIndex >= 0 && sceneIndex < sceneNames.Length)
                targetScene = sceneNames[sceneIndex];
        }
        else
        {
            targetScene = EditorGUILayout.TextField("目标场景名", targetScene);
            EditorGUILayout.LabelField("Build Settings 里还没有场景，去 File ▸ Build Settings 加。", wrap);
        }

        entryID = EditorGUILayout.TextField("目标场景入口编号", entryID);
        thickness = EditorGUILayout.Slider("边条厚度", thickness, 0.3f, 6f);
        defaultLength = EditorGUILayout.FloatField("没拖动时的默认宽度", defaultLength);
        snapToEdge = EditorGUILayout.Toggle("自动贴地图边缘", snapToEdge);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("已有传送点（点一下就选中，可用鼠标拖手柄）", EditorStyles.boldLabel);

        ScenePortal[] portals = FindObjectsOfType<ScenePortal>();
        if (portals.Length == 0)
        {
            EditorGUILayout.LabelField("还没有传送点。开启「拖着画」拖一条，或用菜单里的向导生成。", wrap);
        }
        else
        {
            ScenePortal pendingDelete = null;

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220));
            foreach (ScenePortal p in portals)
            {
                if (p == null) continue;

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button(p.name, GUILayout.Width(150)))
                {
                    Selection.activeGameObject = p.gameObject;
                    SceneView.RepaintAll();
                }

                EditorGUI.BeginChangeCheck();
                float newLen = EditorGUILayout.FloatField(p.lengthAlongEdge, GUILayout.Width(50));
                EditorGUILayout.LabelField("宽", GUILayout.Width(20));
                float newThick = EditorGUILayout.FloatField(p.thickness, GUILayout.Width(50));
                EditorGUILayout.LabelField("厚", GUILayout.Width(20));
                MapEdge newEdge = (MapEdge)EditorGUILayout.EnumPopup(p.edge, GUILayout.Width(70));
                bool changed = EditorGUI.EndChangeCheck();

                if (changed)
                {
                    MapEdge real = newEdge == MapEdge.Auto ? ResolveEdge(p, map, hasMap) : newEdge;
                    ApplyGeometry(p, real, AlongOf(p.transform.position, real), newLen, newThick);
                }

                GUI.color = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button("删", GUILayout.Width(30)))
                    pendingDelete = p;
                GUI.color = Color.white;

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("    → " + p.targetSceneName + " [" + p.entryID + "]", wrap);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // 删除必须等布局画完再做，否则 GUI 栈会不平衡
            if (pendingDelete != null)
            {
                Undo.DestroyObjectImmediate(pendingDelete.gameObject);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Debug.Log("[传送点] 已删除 " + pendingDelete.name);
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("把改动保存下来（Ctrl+S）", GUILayout.Height(24)))
        {
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[传送点] 场景已保存。");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "注意：拖手柄调位置时会自动打开 keepPositionAlongEdge，\n" +
            "否则运行时自动贴边会把门挪回边的正中间。", wrap);
    }
}
#endif
