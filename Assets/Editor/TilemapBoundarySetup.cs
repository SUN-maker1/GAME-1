using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 瓦片地图边界的一键工具。
/// 菜单：Tools ▸ 农场RPG ▸ 用瓦片地图层一键定边界…
///      或在 Hierarchy 里选中你的边界图层后，用 Tools ▸ 农场RPG ▸ 用选中的瓦片图层定边界
/// </summary>
public class TilemapBoundarySetup : EditorWindow
{
    private class LayerInfo
    {
        public Tilemap map;
        public int tiles;
        public RectInt rawCells;      // 全部格子（含散砖）
        public RectInt coreCells;     // 去掉散砖后的主体
        public Bounds coreWorld;      // 主体世界范围
        public Bounds rawWorld;       // 精灵外框世界范围
        public List<Vector3Int> strays = new List<Vector3Int>();
    }

    private List<LayerInfo> layers = new List<LayerInfo>();
    private int index = 0;
    private Vector2 scroll;

    private bool useAll = true;
    private bool ignoreStrays = true;
    private bool cellCorners = true;
    private bool manual = false;
    private float mX = 0f, mY = 0f, mW = 10f, mH = 10f;
    private TilemapBoundary.WallPlacement placement = TilemapBoundary.WallPlacement.Outside;
    private float thickness = 1f;
    private float inset = 0f;
    private bool gaps = true;
    private bool clean = true;
    private bool syncCam = true;

    [MenuItem("Tools/农场RPG/用瓦片地图层一键定边界…", false, 270)]
    private static void Open()
    {
        TilemapBoundarySetup win = GetWindow<TilemapBoundarySetup>("瓦片地图边界");
        win.minSize = new Vector2(560, 520);
        win.Refresh();
        win.Show();
    }

    [MenuItem("Tools/农场RPG/用选中的瓦片图层定边界", false, 271)]
    private static void Quick()
    {
        GameObject sel = Selection.activeGameObject;
        Tilemap t = sel != null ? sel.GetComponent<Tilemap>() : null;
        if (t == null)
        {
            Debug.LogWarning("[瓦片边界] 先在 Hierarchy 里选中你标边界的那个 Tilemap 图层，再点这个菜单。");
            return;
        }
        Apply(t, false, true, true, false, new Rect(0, 0, 10, 10),
              TilemapBoundary.WallPlacement.Outside, 1f, 0f, true, true, true);
    }

    [MenuItem("Tools/农场RPG/检查所有图层的散砖", false, 272)]
    private static void CheckStrays()
    {
        Tilemap[] maps = Object.FindObjectsOfType<Tilemap>();
        int found = 0;
        foreach (Tilemap m in maps)
        {
            if (m == null || TilemapBoundary.IsEmpty(m)) continue;
            List<Vector3Int> all = TilemapBoundary.OccupiedCells(m);
            List<Vector3Int> keep = TilemapBoundary.MainCluster(all);
            HashSet<Vector3Int> ks = new HashSet<Vector3Int>(keep);
            List<Vector3Int> stray = new List<Vector3Int>();
            foreach (Vector3Int c in all) if (!ks.Contains(c)) stray.Add(c);

            if (stray.Count == 0) continue;
            found += stray.Count;
            Debug.LogWarning("[瓦片边界] 图层「" + m.name + "」有 " + stray.Count + " 块离主体很远的散砖：\n" +
                             TilemapBoundary.StrayText(stray, 200) +
                             "\n它们会把包围盒撑大。用菜单 Tools ▸ 农场RPG ▸ 用瓦片地图层一键定边界… 里的「擦掉散砖」清掉。", m);
        }
        Debug.Log(found == 0 ? "[瓦片边界] 没发现散砖，很好。" : ("[瓦片边界] 一共发现 " + found + " 块散砖。"));
    }

    [MenuItem("Tools/农场RPG/让玩家直接从入口点开始（挂到玩家身上）", false, 280)]
    private static void AddSpawnAtEntry()
    {
        PersistentPlayer player = Object.FindObjectOfType<PersistentPlayer>();
        if (player == null)
        {
            Debug.LogWarning("[出生点] 当前场景里找不到玩家（身上要有 PersistentPlayer 标记的那个）。");
            return;
        }

        SceneStartSpawn existing = player.GetComponent<SceneStartSpawn>();
        if (existing != null)
        {
            Selection.activeGameObject = player.gameObject;
            Debug.Log("[出生点] 玩家身上已经挂了，直接按 Play 就会从入口点开始。");
            return;
        }

        Undo.AddComponent<SceneStartSpawn>(player.gameObject);
        EditorUtility.SetDirty(player.gameObject);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Selection.activeGameObject = player.gameObject;

        Debug.Log("[出生点] 已挂到玩家「" + player.name + "」身上。现在直接打开这个场景按 Play，\n" +
                  "人也会从入口点开始；从别的场景传送过来时落位逻辑仍会正常接管。");
    }

    // ---------------------------------------------------------------- 数据

    private void Refresh()
    {
        layers = new List<LayerInfo>();
        Tilemap[] maps = Object.FindObjectsOfType<Tilemap>();
        foreach (Tilemap m in maps)
        {
            if (m == null) continue;

            LayerInfo li = new LayerInfo();
            li.map = m;
            List<Vector3Int> all = TilemapBoundary.OccupiedCells(m);
            li.tiles = all.Count;
            li.rawWorld = TilemapBoundary.RawWorldRect(m);

            if (all.Count == 0)
            {
                li.rawCells = new RectInt(0, 0, 0, 0);
                li.coreCells = new RectInt(0, 0, 0, 0);
                li.coreWorld = new Bounds();
                layers.Add(li);
                continue;
            }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Vector3Int c in all)
            {
                if (c.x < minX) minX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x;
                if (c.y > maxY) maxY = c.y;
            }
            li.rawCells = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);

            List<Vector3Int> keep = TilemapBoundary.MainCluster(all);
            HashSet<Vector3Int> ks = new HashSet<Vector3Int>(keep);
            foreach (Vector3Int c in all) if (!ks.Contains(c)) li.strays.Add(c);

            li.coreWorld = TilemapBoundary.CoreWorldRect(m, true);

            minX = int.MaxValue; minY = int.MaxValue; maxX = int.MinValue; maxY = int.MinValue;
            foreach (Vector3Int c in keep)
            {
                if (c.x < minX) minX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x;
                if (c.y > maxY) maxY = c.y;
            }
            li.coreCells = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            layers.Add(li);
        }
        if (index >= layers.Count) index = 0;
    }

    // ---------------------------------------------------------------- 界面

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("按瓦片地图层确定地图边界", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "普通的地图边界脚本只认 SpriteRenderer，看不懂瓦片地图，所以会挑错物体、把墙建到奇怪的位置。\n" +
            "这里直接读 Tilemap 里实际画了砖的格子范围来建墙。",
            MessageType.Info);

        EditorGUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("刷新图层列表")) Refresh();
        if (GUILayout.Button("检查散砖")) CheckStrays();
        GUILayout.EndHorizontal();

        if (layers.Count == 0)
        {
            EditorGUILayout.HelpBox("当前场景里没有 Tilemap。先打开你的地图场景，再点刷新。", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("当前场景的瓦片图层", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(150));

        for (int i = 0; i < layers.Count; i++)
        {
            LayerInfo li = layers[i];
            bool on = i == index;
            GUI.backgroundColor = on ? new Color(0.55f, 0.85f, 1f) : Color.white;

            string head = li.tiles == 0
                ? li.map.name + "　（空图层）"
                : li.map.name + "　砖 " + li.tiles + "　主体 " + li.coreCells.width + "×" + li.coreCells.height +
                  " 格　世界 " + li.coreWorld.size.x.ToString("F1") + "×" + li.coreWorld.size.y.ToString("F1");

            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (GUILayout.Button(on ? "●" : "○", GUILayout.Width(22))) { index = i; GUI.changed = true; }
            if (GUILayout.Button(head, EditorStyles.label)) index = i;
            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;

            if (li.tiles > 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("　父级", li.map.transform.parent != null ? li.map.transform.parent.name : "（无）");
                EditorGUILayout.LabelField("　全部格子", "X[" + li.rawCells.xMin + ", " + (li.rawCells.xMax - 1) + "]  Y[" +
                                           li.rawCells.yMin + ", " + (li.rawCells.yMax - 1) + "]　= " +
                                           li.rawCells.width + "×" + li.rawCells.height);
                EditorGUILayout.LabelField("　去掉散砖", "X[" + li.coreCells.xMin + ", " + (li.coreCells.xMax - 1) + "]  Y[" +
                                           li.coreCells.yMin + ", " + (li.coreCells.yMax - 1) + "]　= " +
                                           li.coreCells.width + "×" + li.coreCells.height);
                EditorGUILayout.LabelField("　精灵外框", li.rawWorld.size.x.ToString("F2") + " × " + li.rawWorld.size.y.ToString("F2") +
                                           "　格子范围 " + li.coreWorld.size.x.ToString("F2") + " × " + li.coreWorld.size.y.ToString("F2"));
                if (li.strays.Count > 0)
                {
                    EditorGUILayout.HelpBox("有 " + li.strays.Count + " 块散砖把范围撑大了：" +
                                            TilemapBoundary.StrayText(li.strays, 10), MessageType.Warning);
                    if (GUILayout.Button("擦掉这个图层的散砖（可撤销）"))
                        EraseStrays(li);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(3);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("算范围的方式", EditorStyles.boldLabel);
        useAll = EditorGUILayout.Toggle("合并所有图层（推荐）", useAll);
        if (useAll)
            EditorGUILayout.HelpBox("单个图层常常只画了地图的一部分（比如只有墙、只有地板），\n" +
                                    "合并所有图层才是整张地图的范围。", MessageType.None);
        else
        {
            index = EditorGUILayout.Popup("只用哪个图层", index, LayerNames());
            EditorGUILayout.HelpBox("只按上面选中的这一个图层算。", MessageType.None);
        }
        ignoreStrays = EditorGUILayout.Toggle("忽略离主体很远的散砖（强烈建议开）", ignoreStrays);
        cellCorners = EditorGUILayout.Toggle("按格子算（不是按精灵外框）", cellCorners);

        Bounds preview = PreviewBounds();
        EditorGUILayout.HelpBox("算出来的地图范围：" + preview.size.x.ToString("F2") + " × " + preview.size.y.ToString("F2") +
                                "　X[" + preview.min.x.ToString("F2") + ", " + preview.max.x.ToString("F2") + "]　Y[" +
                                preview.min.y.ToString("F2") + ", " + preview.max.y.ToString("F2") + "]", MessageType.Info);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("兜底：手动指定矩形", EditorStyles.boldLabel);
        manual = EditorGUILayout.Toggle("不用瓦片算，直接填矩形", manual);
        if (manual)
        {
            if (GUILayout.Button("把上面算出的范围填进来"))
            {
                mX = preview.min.x; mY = preview.min.y; mW = preview.size.x; mH = preview.size.y;
            }
            mX = EditorGUILayout.FloatField("左下角 X", mX);
            mY = EditorGUILayout.FloatField("左下角 Y", mY);
            mW = EditorGUILayout.FloatField("宽", mW);
            mH = EditorGUILayout.FloatField("高", mH);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("墙", EditorStyles.boldLabel);
        placement = (TilemapBoundary.WallPlacement)EditorGUILayout.EnumPopup("墙贴在哪一侧", placement);
        if (placement == TilemapBoundary.WallPlacement.Outside)
            EditorGUILayout.HelpBox("整块墙在地图外侧：人能走到最边上一格，\n" +
                                    "但画出来的碰撞框会比地图大 2×厚度（这就是你觉得「超出」的原因）。", MessageType.None);
        else if (placement == TilemapBoundary.WallPlacement.OnEdge)
            EditorGUILayout.HelpBox("墙骑在边线上：画出来的框最贴合地图，\n" +
                                    "代价是最外面一格站不上去（被墙压掉半个厚度）。", MessageType.None);
        thickness = EditorGUILayout.Slider("墙的厚度", thickness, 0.2f, 6f);
        inset = EditorGUILayout.Slider("墙往内收（-外扩）", inset, -3f, 6f);
        gaps = EditorGUILayout.Toggle("传送点处留缺口", gaps);
        clean = EditorGUILayout.Toggle("清掉错位的旧墙", clean);
        syncCam = EditorGUILayout.Toggle("相机限位到地图", syncCam);
        EditorGUILayout.HelpBox("玩家的移动范围钳制会自动同步成这里的地图范围（修复被存进 prefab 的小钳制）。", MessageType.None);

        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.6f);
        if (GUILayout.Button("生成边界", GUILayout.Height(34)))
        {
            Tilemap pick = (index < layers.Count) ? layers[index].map : null;
            Apply(pick, useAll, ignoreStrays, cellCorners, manual, new Rect(mX, mY, mW, mH),
                  placement, thickness, inset, gaps, clean, syncCam);
            Refresh();
        }
        GUI.backgroundColor = Color.white;
    }

    /// <summary>按当前设置预估一下地图范围（不写场景，只用来显示）</summary>
    private Bounds PreviewBounds()
    {
        if (manual) return new Bounds(new Vector3(mX + mW * 0.5f, mY + mH * 0.5f, 0f), new Vector3(mW, mH, 0f));

        Bounds result = new Bounds();
        bool has = false;
        foreach (LayerInfo li in layers)
        {
            if (li.map == null || li.tiles == 0) continue;
            Bounds b = cellCorners ? li.coreWorld : li.rawWorld;
            if (!useAll && li.map != layers[index].map) continue;
            if (b.size.x <= 0.001f) continue;
            if (!has) { result = b; has = true; }
            else result.Encapsulate(b);
        }
        return result;
    }

    private string[] LayerNames()
    {
        string[] n = new string[layers.Count];
        for (int i = 0; i < layers.Count; i++)
            n[i] = layers[i].map.name + "（" + layers[i].tiles + " 砖）";
        return n;
    }

    private void EraseStrays(LayerInfo li)
    {
        if (li.strays.Count == 0) return;
        if (!EditorUtility.DisplayDialog("擦掉散砖",
            "将在图层「" + li.map.name + "」上擦掉 " + li.strays.Count + " 块离主体很远的瓦片：\n" +
            TilemapBoundary.StrayText(li.strays, 20) + "\n\n可以 Ctrl+Z 撤销。确定吗？", "擦掉", "算了"))
            return;

        Undo.RecordObject(li.map, "擦掉散砖");
        foreach (Vector3Int c in li.strays) li.map.SetTile(c, null);
        EditorUtility.SetDirty(li.map);
        EditorSceneManager.MarkSceneDirty(li.map.gameObject.scene);
        Refresh();
        Debug.Log("[瓦片边界] 已在「" + li.map.name + "」上擦掉 " + li.strays.Count + " 块散砖。");
    }

    // ---------------------------------------------------------------- 生成

    private static void Apply(Tilemap map, bool allLayers, bool dropStrays, bool byCells,
                              bool useManual, Rect manualR, TilemapBoundary.WallPlacement place,
                              float th, float ins, bool gap, bool cln, bool cam)
    {
        TilemapBoundary existing = Object.FindObjectOfType<TilemapBoundary>();
        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
            Undo.RecordObject(existing, "更新瓦片地图边界");
        }
        else
        {
            go = new GameObject("TilemapBoundary");
            Undo.RegisterCreatedObjectUndo(go, "创建瓦片地图边界");
            existing = go.AddComponent<TilemapBoundary>();
        }

        existing.boundaryTilemap = map;
        existing.useAllLayers = allLayers;
        existing.ignoreStrayTiles = dropStrays;
        existing.useCellCorners = byCells;
        existing.useManualRect = useManual;
        existing.manualRect = manualR;
        existing.wallPlacement = place;
        existing.wallThickness = th;
        existing.wallInset = ins;
        existing.leaveGapsForPortals = gap;
        existing.clearForeignWalls = cln;
        existing.syncCamera = cam;
        existing.Rebuild();

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;

        Bounds b = existing.MapBounds;
        Debug.Log("[瓦片边界] 已按" + existing.BoundsSourceName + " 生成边界：" +
                  b.size.x.ToString("F2") + " × " + b.size.y.ToString("F2") + " 单位\n" +
                  "  X[" + b.min.x.ToString("F2") + ", " + b.max.x.ToString("F2") + "]  " +
                  "Y[" + b.min.y.ToString("F2") + ", " + b.max.y.ToString("F2") + "]\n" +
                  "保存一下场景（Ctrl+S）即可。");
    }
}
