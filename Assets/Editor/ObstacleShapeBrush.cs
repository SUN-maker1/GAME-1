#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 碰撞区域绘制工具 —— 像框选一样在 Scene 视图里拖一块区域出来，自动生成碰撞，人物走不过去。
///
/// 【用法】
///   1. 菜单 Tools ▸ 农场RPG ▸ 碰撞 ▸ 绘制碰撞区域… 打开这个窗口
///   2. 选形状（方形 / 椭圆）、要不要只做触发器
///   3. 点「开始绘制」，回到 Scene 视图，按住 Alt + 鼠标左键拖一块范围，松手即生成
///   4. 画完点「停止绘制」，或按 Esc
///
/// 【怎么调】
///   生成后直接选中它，Scene 视图里有手柄可以拖大小、挪位置（见 ObstacleShape2DEditor）。
/// </summary>
public class ObstacleShapeBrush : EditorWindow
{
    private ObstacleShape shape = ObstacleShape.Box;
    private bool isTrigger = false;
    private bool requireAlt = true;
    private int ellipseSegments = 24;
    private string containerName = "_Obstacles";

    private bool drawing;
    private bool hasStart;
    private Vector3 startPoint;
    private Vector3 endPoint;

    [MenuItem("Tools/农场RPG/碰撞/绘制碰撞区域…", false, 200)]
    public static void Open()
    {
        ObstacleShapeBrush w = GetWindow<ObstacleShapeBrush>("碰撞区域绘制");
        w.minSize = new Vector2(300, 260);
        w.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        drawing = false;
        hasStart = false;
    }

    /// <summary>把当前 Scene 视图切回 2D 正视角，避免斜视角绘制导致尺寸被放大/偏移。</summary>
    private static void Force2DView()
    {
        try
        {
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            // Unity 2022.3 起属性名为 in2DMode（旧版本叫 is2DMode）。设为正即正对 z 轴。
            var prop = typeof(SceneView).GetProperty("in2DMode");
            if (prop != null && prop.CanWrite) { prop.SetValue(sv, true); }
            else
            {
                var prop2 = typeof(SceneView).GetProperty("is2DMode");
                if (prop2 != null && prop2.CanWrite) prop2.SetValue(sv, true);
            }
            sv.Repaint();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[碰撞区域] 无法自动切换到 2D 视角（不影响绘制）：" + ex.Message);
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("碰撞区域绘制", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "在 Scene 视图里拖一块范围，自动生成碰撞体挡住人物。\n" +
            "生成后可以直接选中它，用视图里的手柄拖大小和位置。",
            MessageType.Info);

        EditorGUILayout.Space(4);
        shape = (ObstacleShape)EditorGUILayout.EnumPopup("形状", shape);
        isTrigger = EditorGUILayout.Toggle(
            new GUIContent("只做触发器（不挡人）", "勾上是感应区（水面/触发事件）；不勾是实心墙"),
            isTrigger);
        requireAlt = EditorGUILayout.Toggle(
            new GUIContent("绘制需按住 Alt", "默认开启：避免和 Scene 视图的框选冲突。取消则直接左键拖即可绘制"),
            requireAlt);
        if (shape == ObstacleShape.Ellipse)
            ellipseSegments = EditorGUILayout.IntSlider("椭圆精度", ellipseSegments, 6, 64);

        containerName = EditorGUILayout.TextField(
            new GUIContent("存放父物体", "生成的东西会挂到这个物体下面，没有就自动建一个"),
            containerName);

        EditorGUILayout.Space(10);

        GUI.backgroundColor = drawing ? new Color(1f, 0.65f, 0.3f) : new Color(0.55f, 0.9f, 0.6f);
        string btn = drawing ? "停止绘制（绘制中…）" : "开始绘制";
        if (GUILayout.Button(btn, GUILayout.Height(34)))
        {
            drawing = !drawing;
            hasStart = false;
            if (drawing)
            {
                // 斜视角下用 z=0 平面接鼠标射线会把拖出来的区域算大（且偏移），
                // 所以一开始绘制就强制 Scene 视图切回 2D 正视角，保证画出来的尺寸 = 你看到的尺寸。
                Force2DView();
                Debug.Log($"[碰撞区域] 开始绘制：{(requireAlt ? "按住 Alt + " : "")}左键拖动 = 画一块{(shape == ObstacleShape.Box ? "方形" : "椭圆")}区域。已自动切到 2D 正视角。");
            }
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            drawing
                ? $"绘制中：{(requireAlt ? "按住 Alt + " : "")}在 Scene 视图左键拖动，松手生成，按 Esc 退出。"
                : "点上面按钮开始，或选中已有物体用菜单「把选中物体变成碰撞区域」。",
            drawing ? MessageType.Warning : MessageType.None);
    }

    private void OnSceneGUI(SceneView view)
    {
        if (!drawing) return;

        Event e = Event.current;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            drawing = false;
            hasStart = false;
            Repaint();
            SceneView.RepaintAll();
            e.Use();
            return;
        }

        bool altOk = !requireAlt || e.alt;

        if (e.type == EventType.MouseDown && e.button == 0 && altOk)
        {
            startPoint = MouseWorld(e.mousePosition);
            endPoint = startPoint;
            hasStart = true;
            e.Use();
            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
        }
        else if (hasStart && e.type == EventType.MouseDrag)
        {
            endPoint = MouseWorld(e.mousePosition);
            e.Use();
        }
        else if (hasStart && e.type == EventType.MouseUp && e.button == 0)
        {
            endPoint = MouseWorld(e.mousePosition);
            hasStart = false;
            CreateArea(startPoint, endPoint);
            e.Use();
            GUIUtility.hotControl = 0;
        }

        if (hasStart) DrawPreview(startPoint, endPoint);

        Handles.BeginGUI();
        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(10, 10, 340, 26));
        EditorGUI.DrawRect(new Rect(0, 0, 340, 26), new Color(0.1f, 0.1f, 0.12f, 0.75f));
        GUILayout.Label($"  绘制{(shape == ObstacleShape.Box ? "方形" : "椭圆")}：{(requireAlt ? "按住 Alt + " : "")}左键拖动，Esc 退出");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private void DrawPreview(Vector3 a, Vector3 b)
    {
        Vector3 min = new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), 0f);
        Vector3 max = new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), 0f);
        Vector2 size = new Vector2(max.x - min.x, max.y - min.y);

        Color fill = isTrigger ? new Color(0.3f, 0.85f, 1f, 0.15f) : new Color(1f, 0.45f, 0.25f, 0.18f);
        Color line = isTrigger ? new Color(0.3f, 0.85f, 1f, 0.8f) : new Color(1f, 0.4f, 0.2f, 0.9f);

        if (shape == ObstacleShape.Box)
        {
            Vector3[] c =
            {
                new Vector3(min.x, min.y, 0f), new Vector3(max.x, min.y, 0f),
                new Vector3(max.x, max.y, 0f), new Vector3(min.x, max.y, 0f)
            };
            Handles.DrawSolidRectangleWithOutline(c, fill, line);
        }
        else
        {
            Vector3 center = (min + max) * 0.5f;
            float rx = size.x * 0.5f, ry = size.y * 0.5f;
            int seg = 40;
            Vector3[] pts = new Vector3[seg];
            for (int i = 0; i < seg; i++)
            {
                float ang = (2f * Mathf.PI) * (i / (float)seg);
                pts[i] = center + new Vector3(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * ry, 0f);
            }
            Handles.DrawAAConvexPolygon(pts);
            Handles.color = line;
            for (int i = 0; i < seg; i++) Handles.DrawLine(pts[i], pts[(i + 1) % seg]);
        }

        Handles.Label(max + Vector3.right * 0.15f, $"{size.x:F2} × {size.y:F2}");
    }

    private void CreateArea(Vector3 a, Vector3 b)
    {
        Vector3 min = new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), 0f);
        Vector3 max = new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), 0f);
        Vector2 size = new Vector2(Mathf.Max(0.1f, max.x - min.x), Mathf.Max(0.1f, max.y - min.y));
        Vector3 center = (min + max) * 0.5f;

        // 注意：名字【不能】以 "Wall_" 开头 —— MapBoundary.RemoveLegacyWalls() 会把
        // 所有 "Wall_" 开头的碰撞体当成你早期手摆的旧墙清掉，那样一进 Play 障碍物就没了。
        string name = (shape == ObstacleShape.Box ? "Obstacle_Box" : "Obstacle_Ellipse");
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "绘制碰撞区域");
        go.transform.position = center;

        GameObject parent = null;
        if (!string.IsNullOrEmpty(containerName))
        {
            parent = GameObject.Find(containerName);
            if (parent == null)
            {
                parent = new GameObject(containerName);
                Undo.RegisterCreatedObjectUndo(parent, "创建障碍容器");
            }
            Undo.SetTransformParent(go.transform, parent.transform, "归到障碍容器");
        }

        ObstacleShape2D ob = Undo.AddComponent<ObstacleShape2D>(go);
        Undo.RecordObject(ob, "设置碰撞区域");
        ob.shape = shape;
        ob.size = size;
        ob.offset = Vector2.zero;
        ob.isTrigger = isTrigger;
        ob.ellipseSegments = ellipseSegments;
        ob.Rebuild();

        Selection.activeGameObject = go;
        EditorUtility.SetDirty(go);
        SceneView.RepaintAll();
        Debug.Log($"[碰撞区域] 已生成 {name}：中心 {center}，尺寸 {size.x:F2} × {size.y:F2}" +
                  (isTrigger ? "（触发器）" : "（实心挡人）"));
    }

    private static Vector3 MouseWorld(Vector2 mousePos)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePos);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);
        if (plane.Raycast(ray, out float dist)) return ray.GetPoint(dist);
        return ray.GetPoint(10f);
    }
}
#endif
