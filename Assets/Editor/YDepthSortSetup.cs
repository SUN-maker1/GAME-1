using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 「前景遮挡人物」的一键工具。
///
/// 原理：人物在 Entities 排序图层，道具默认在 Default 图层 —— 图层不一样，
/// 排序号调破天也没用，道具永远压在人物下面。所以要做两件事：
///   1. 把道具也搬到 Entities 图层（和人物同一层）
///   2. 给它挂 TreeSortOrder，按「脚下 Y」动态算 sortingOrder
///      Y 越小（越靠屏幕下方＝越靠前）→ order 越大 → 画得越靠上 → 遮住人物
///
/// 菜单：Tools ▸ 农场RPG ▸ 让选中的道具遮挡人物（Y 轴排序）
/// </summary>
public class YDepthSortSetup : EditorWindow
{
    private const string LayerName = "Entities";   // 必须和人物的排序图层一致
    private const int OrderBase = 20000;           // 基准，保证压得住手填的小 order
    private const float OrderPerUnit = 100f;

    private bool includeChildren = true;
    private bool everyFrame = false;
    private int offset = 0;

    [MenuItem("Tools/农场RPG/让选中的道具遮挡人物（Y 轴排序）", false, 300)]
    private static void Quick()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            Debug.LogWarning("[遮挡排序] 先在 Hierarchy 里选中要处理的道具（盆栽 / 镜子 …），再点这个菜单。");
            return;
        }

        int n = 0;
        foreach (GameObject go in sel)
            n += Apply(go, true, false, 0);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[遮挡排序] 已给 " + n + " 个物体挂上 Y 轴排序（图层 " + LayerName + "）。Ctrl+S 保存。");
    }

    [MenuItem("Tools/农场RPG/遮挡排序：高级选项…", false, 301)]
    private static void Open()
    {
        YDepthSortSetup win = GetWindow<YDepthSortSetup>("遮挡排序");
        win.minSize = new Vector2(420, 260);
        win.Show();
    }

    [MenuItem("Tools/农场RPG/遮挡排序：刷新整场", false, 302)]
    private static void RefreshAll()
    {
        TreeSortOrder[] all = Object.FindObjectsOfType<TreeSortOrder>();
        foreach (TreeSortOrder t in all)
            if (t != null) t.Refresh();
        Debug.Log("[遮挡排序] 已刷新 " + all.Length + " 个 Y 轴排序物体。");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("让选中的道具按 Y 轴遮挡人物", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "人物在 Entities 图层，道具默认在 Default 图层，图层不同永远遮不住。\n" +
            "这里把它们搬到同一图层，再挂上按脚下 Y 动态排序的脚本。",
            MessageType.Info);

        EditorGUILayout.Space(6);
        includeChildren = EditorGUILayout.Toggle("连子物体一起改", includeChildren);
        everyFrame = EditorGUILayout.Toggle("每帧重算（会动的物体才勾）", everyFrame);
        offset = EditorGUILayout.IntField("手调偏移（越大越靠前）", offset);

        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.6f);
        if (GUILayout.Button("应用到选中的物体", GUILayout.Height(32)))
        {
            GameObject[] sel = Selection.gameObjects;
            int n = 0;
            if (sel != null)
                foreach (GameObject go in sel)
                    n += Apply(go, includeChildren, everyFrame, offset);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[遮挡排序] 已处理 " + n + " 个物体。Ctrl+S 保存。");
        }
        GUI.backgroundColor = Color.white;
    }

    /// <summary>返回实际处理了多少个物体</summary>
    private static int Apply(GameObject go, bool children, bool every, int off)
    {
        if (go == null) return 0;

        List<GameObject> list = new List<GameObject>();
        list.Add(go);
        if (children)
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t != null && t.gameObject != go) list.Add(t.gameObject);

        int n = 0;
        foreach (GameObject g in list)
        {
            if (g.GetComponent<SpriteRenderer>() == null) continue;

            Undo.RecordObject(g, "加 Y 轴遮挡排序");

            TreeSortOrder sort = g.GetComponent<TreeSortOrder>();
            if (sort == null) sort = Undo.AddComponent<TreeSortOrder>(g);

            sort.useFootPosition = true;
            sort.orderBase = OrderBase;
            sort.orderPerUnit = OrderPerUnit;
            sort.orderOffset = off;
            sort.includeChildren = false;    // 逐个物体自己挂，避免重复处理
            sort.sortingLayerName = LayerName;
            sort.updateEveryFrame = every;
            sort.CacheRenderers();
            sort.Refresh();

            EditorUtility.SetDirty(g);
            n++;
        }
        return n;
    }
}
