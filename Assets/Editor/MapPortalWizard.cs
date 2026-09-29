#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 「在地图图片边缘放一个传送点」的一键工具。
/// 菜单：Tools ▸ 农场RPG ▸ 星露谷式：在地图边缘新建传送点…
///
/// 它会：
///   1. 读出当前场景地图图片的【真实世界矩形】（不管你换多大的图）
///   2. 在指定边的正中间生成一条触发边条，刚好横跨整条边 / 或指定宽度
///   3. 挂上 ScenePortal 并填好目标场景和入口编号
/// 生成的传送点带 autoSnapToEdge，下次换图会自动重新贴边，不用回来改坐标。
/// </summary>
public class MapPortalWizard : EditorWindow
{
    private SpriteRenderer mapRenderer;
    private MapEdge edge = MapEdge.Right;
    private string targetScene = "";
    private string entryID = "FromFarm";
    private float lengthAlongEdge = 6f;
    private float thickness = 1.5f;
    private int sceneIndex = 0;
    private string[] sceneNames = new string[0];
    private bool useSceneList = true;

    [MenuItem("Tools/农场RPG/星露谷式：在地图边缘新建传送点…", false, 220)]
    public static void Open()
    {
        MapPortalWizard w = GetWindow<MapPortalWizard>("地图边缘传送点");
        w.Refresh();
        w.Show();
    }

    [MenuItem("Tools/农场RPG/星露谷式：体检地图边界", false, 240)]
    public static void Diagnose()
    {
        MapPortalWizard w = GetWindow<MapPortalWizard>("地图边缘传送点");
        w.Refresh();
        w.DoDiagnose();
        w.Show();
    }

    [MenuItem("Tools/农场RPG/星露谷式：把地图边界挂到场景里（可调参数）", false, 260)]
    public static void AttachBoundary()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogWarning("[MapBoundary] 没有打开的场景。");
            return;
        }

        MapBoundary existing = FindObjectOfType<MapBoundary>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log($"[MapBoundary] 场景里已经有 {existing.gameObject.name}，已经帮你选中了。");
            return;
        }

        GameObject go = new GameObject("MapBoundary");
        MapBoundary mb = go.AddComponent<MapBoundary>();
        Undo.RegisterCreatedObjectUndo(go, "Attach MapBoundary");
        EditorSceneManager.MarkSceneDirty(scene);

        Selection.activeGameObject = go;
        Debug.Log("[MapBoundary] 已经挂好了。现在可以在 Inspector 里调：\n" +
                  "  边界留口 / 墙厚 / 传送点留口 / 相机是否自动贴合地图。\n" +
                  "挂了这个之后，运行时就不会再自动生成那个 Auto 的了。");
    }

    private void OnEnable() => Refresh();

    private void Refresh()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;

        mapRenderer = null;
        SpriteRenderer named = null, biggest = null;
        float biggestArea = 0f;

        foreach (SpriteRenderer sr in FindObjectsOfType<SpriteRenderer>())
        {
            if (sr.sprite == null) continue;
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

        mapRenderer = named != null ? named : biggest;

        sceneNames = new string[EditorBuildSettings.scenes.Length];
        for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
        {
            string p = EditorBuildSettings.scenes[i].path;
            sceneNames[i] = System.IO.Path.GetFileNameWithoutExtension(p);
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("在地图边缘开一个传送门", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        Scene scene = SceneManager.GetActiveScene();
        mapRenderer = (SpriteRenderer)EditorGUILayout.ObjectField("地图背景", mapRenderer, typeof(SpriteRenderer), true);

        if (mapRenderer != null && mapRenderer.sprite != null)
        {
            Bounds b = MapBoundary.WorldBounds(mapRenderer);
            EditorGUILayout.HelpBox(
                $"场景：{scene.name}\n" +
                $"地图：{mapRenderer.sprite.name}  {mapRenderer.sprite.rect.width}×{mapRenderer.sprite.rect.height} px\n" +
                $"世界尺寸：{b.size.x:F2} × {b.size.y:F2} 单位\n" +
                $"范围：X[{b.min.x:F2}, {b.max.x:F2}]  Y[{b.min.y:F2}, {b.max.y:F2}]",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox("当前场景里没找到地图背景。先打开一个 Area_ 场景再点这个菜单。",
                                    MessageType.Warning);
        }

        EditorGUILayout.Space();
        edge = (MapEdge)EditorGUILayout.EnumPopup("开在哪条边", edge);

        GUIStyle helpStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField("Auto = 看你现在把这个对象摆在哪边（新建时建议直接选边）。", helpStyle);

        lengthAlongEdge = EditorGUILayout.FloatField("通道宽度（沿边多长）", lengthAlongEdge);
        thickness = EditorGUILayout.Slider("边条厚度", thickness, 0.5f, 6f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("传送去哪", EditorStyles.boldLabel);

        if (sceneNames.Length > 0 && useSceneList)
        {
            sceneIndex = EditorGUILayout.Popup("目标场景", sceneIndex, sceneNames);
            if (sceneIndex >= 0 && sceneIndex < sceneNames.Length)
                targetScene = sceneNames[sceneIndex];
        }
        else
        {
            targetScene = EditorGUILayout.TextField("目标场景名", targetScene);
        }

        if (sceneNames.Length > 0)
            useSceneList = EditorGUILayout.Toggle("从 Build Settings 里选场景", useSceneList);

        entryID = EditorGUILayout.TextField("目标场景入口编号", entryID);

        EditorGUILayout.Space();

        GUI.enabled = mapRenderer != null && !string.IsNullOrEmpty(targetScene);
        if (GUILayout.Button("生成传送点", GUILayout.Height(28)))
            CreatePortal();
        GUI.enabled = true;

        EditorGUILayout.Space();

        if (GUILayout.Button("体检：检查边界和已有传送点", GUILayout.Height(22)))
            DoDiagnose();

        EditorGUILayout.Space();
        if (GUILayout.Button("把地图边界挂到场景里（之后可在 Inspector 调参数）", GUILayout.Height(22)))
            AttachBoundary();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "提示：传送点生成后就带 autoSnapToEdge，以后换地图图片，它会自动重新贴到新的边缘上。",
            helpStyle);
    }

    private void CreatePortal()
    {
        if (mapRenderer == null || mapRenderer.sprite == null) return;

        Bounds b = MapBoundary.WorldBounds(mapRenderer);
        MapEdge real = edge == MapEdge.Auto ? MapEdge.Right : edge;

        string goName = "Portal_To_" + targetScene;
        Transform parent = EnsureContainer();

        GameObject go = new GameObject(goName);
        Undo.RegisterCreatedObjectUndo(go, "Create Map Portal");
        go.transform.SetParent(parent, false);

        Vector3 pos = Vector3.zero;
        Vector2 size;

        switch (real)
        {
            case MapEdge.Left:
            case MapEdge.Right:
                pos = new Vector3(real == MapEdge.Left ? b.min.x : b.max.x, b.center.y, 0f);
                size = new Vector2(thickness, lengthAlongEdge);
                break;
            default:
                pos = new Vector3(b.center.x, real == MapEdge.Bottom ? b.min.y : b.max.y, 0f);
                size = new Vector2(lengthAlongEdge, thickness);
                break;
        }

        go.transform.position = pos;

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = size;

        ScenePortal portal = go.AddComponent<ScenePortal>();
        portal.targetSceneName = targetScene;
        portal.entryID = entryID;
        portal.autoSnapToEdge = true;
        portal.edge = real;
        portal.lengthAlongEdge = lengthAlongEdge;
        portal.thickness = thickness;
        portal.keepPositionAlongEdge = false;

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[传送点] 已在 {real} 边生成 {goName} → {targetScene}（入口 {entryID}）\n" +
                  $"  位置 {pos}，通道 {size.x:F2}×{size.y:F2}\n" +
                  $"  地图 {b.size.x:F2}×{b.size.y:F2} 单位，玩家走到这条边就会被传送。");
    }

    private Transform EnsureContainer()
    {
        GameObject existing = GameObject.Find("Portals");
        if (existing != null) return existing.transform;

        GameObject go = new GameObject("Portals");
        Undo.RegisterCreatedObjectUndo(go, "Create Portal Container");
        return go.transform;
    }

    private void DoDiagnose()
    {
        if (mapRenderer == null || mapRenderer.sprite == null)
        {
            Debug.LogWarning("[体检] 当前场景里没有地图背景。");
            return;
        }

        Bounds b = MapBoundary.WorldBounds(mapRenderer);
        string msg = $"[体检] {SceneManager.GetActiveScene().name}\n" +
                     $"  地图 {mapRenderer.name}：{b.size.x:F2} × {b.size.y:F2} 单位\n" +
                     $"  X[{b.min.x:F2}, {b.max.x:F2}]  Y[{b.min.y:F2}, {b.max.y:F2}]";

        ScenePortal[] portals = FindObjectsOfType<ScenePortal>();
        msg += $"\n  传送点 {portals.Length} 个：";
        foreach (ScenePortal p in portals)
        {
            Collider2D col = p.GetComponent<Collider2D>();
            if (col == null) continue;

            float outside = DistanceOutside(b, col.bounds);
            string state = outside <= 0.01f
                ? "贴在地图内/边缘 ✓"
                : $"在地图外 {outside:F2} 单位 ✗（玩家要走这么远才碰到）";

            msg += $"\n    · {p.name} → {p.targetSceneName} [{p.entryID}]：{state}";
        }

        SceneEntryPoint[] entries = FindObjectsOfType<SceneEntryPoint>();
        msg += $"\n  入口点 {entries.Length} 个：";
        foreach (SceneEntryPoint e in entries)
        {
            bool inside = b.Contains(e.transform.position);
            msg += $"\n    · {e.name} [{e.entryID}]：{(inside ? "在地图内 ✓" : "在地图外 ✗")} at {e.transform.position}";
        }

        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float halfH = cam.orthographicSize;
            float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
            bool tooBig = halfH * 2f > b.size.y + 0.01f || halfH * aspect * 2f > b.size.x + 0.01f;
            msg += $"\n  相机视野 {halfH * 2f:F2} 高 × {halfH * aspect * 2f:F2} 宽：" +
                   (tooBig ? "比地图大，会露出黑边（运行时 MapBoundary 会自动收紧）" : "能装进地图 ✓");
        }

        Debug.Log(msg);
    }

    private static float DistanceOutside(Bounds map, Bounds item)
    {
        float dx = Mathf.Max(map.min.x - item.max.x, item.min.x - map.max.x, 0f);
        float dy = Mathf.Max(map.min.y - item.max.y, item.min.y - map.max.y, 0f);
        return Mathf.Max(dx, dy);
    }
}
#endif
