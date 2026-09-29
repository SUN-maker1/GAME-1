using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 批量碰撞体积工具：Tools → 批量碰撞体积…
/// 自动读每个物体的贴图轮廓，取底部一小段当碰撞范围，不用手动填数字。
/// </summary>
public class BulkColliderWindow : EditorWindow
{
    private enum Scope { 选中的物体, 整个场景, 所有预制体资产 }

    private Scope scope = Scope.选中的物体;
    private FootCollider2D.ShapeType shape = FootCollider2D.ShapeType.Capsule;
    private float bottomRatio = 0.25f;
    private float widthScale = 0.8f;
    private bool removeOthers = true;
    private string result = "";

    [MenuItem("Tools/批量碰撞体积…")]
    private static void Open()
    {
        BulkColliderWindow win = GetWindow<BulkColliderWindow>("批量碰撞体积");
        win.minSize = new Vector2(320, 300);
        win.Show();
    }

    [MenuItem("Tools/一键自动碰撞(选中物体)")]
    private static void QuickApply()
    {
        int n = new BulkColliderWindow().Run(Scope.选中的物体);
        Debug.Log("[批量碰撞] 已按贴图自动算好 " + n + " 个物体的脚下碰撞。");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("自动按贴图算脚下碰撞", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "读每个物体贴图的实际轮廓，取底部一小段当碰撞范围。\n换了贴图重新点一次应用就行。",
            MessageType.Info);

        EditorGUILayout.Space(6);
        scope = (Scope)EditorGUILayout.EnumPopup("作用范围", scope);
        shape = (FootCollider2D.ShapeType)EditorGUILayout.EnumPopup("碰撞形状", shape);
        bottomRatio = EditorGUILayout.Slider("碰撞高度(贴图底部比例)", bottomRatio, 0.05f, 1f);
        widthScale = EditorGUILayout.Slider("宽度缩放", widthScale, 0.1f, 2f);
        removeOthers = EditorGUILayout.Toggle("清理多余碰撞体", removeOthers);

        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.6f);
        if (GUILayout.Button("应用", GUILayout.Height(32)))
        {
            int n = Run(scope);
            result = "已处理 " + n + " 个物体。选中任意一个可以看到绿色碰撞框。";
        }
        GUI.backgroundColor = Color.white;

        if (!string.IsNullOrEmpty(result))
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(result, MessageType.Info);
        }
    }

    private int Run(Scope useScope)
    {
        List<GameObject> targets = new List<GameObject>();

        if (useScope == Scope.选中的物体)
        {
            targets.AddRange(Selection.gameObjects);
        }
        else if (useScope == Scope.整个场景)
        {
            HashSet<GameObject> seen = new HashSet<GameObject>();
            SpriteRenderer[] renderers = Object.FindObjectsOfType<SpriteRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (seen.Add(renderers[i].gameObject))
                    targets.Add(renderers[i].gameObject);
            }
        }

        if (useScope != Scope.所有预制体资产)
        {
            int count = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (Setup(targets[i], true)) count++;
            }
            if (count > 0) EditorSceneManager.MarkAllScenesDirty();
            return count;
        }

        return SetupAllPrefabs();
    }

    private bool Setup(GameObject go, bool withUndo)
    {
        if (go == null) return false;
        if (go.GetComponent<SpriteRenderer>() == null) return false;

        FootCollider2D fc = go.GetComponent<FootCollider2D>();
        if (fc == null)
        {
            if (withUndo) fc = Undo.AddComponent<FootCollider2D>(go);
            else fc = go.AddComponent<FootCollider2D>();
        }

        fc.shape = shape;
        fc.autoFromSprite = true;
        fc.autoBottomRatio = bottomRatio;
        fc.autoWidthScale = widthScale;
        fc.removeOtherColliders = removeOthers;
        fc.Apply();

        EditorUtility.SetDirty(go);
        return true;
    }

    private int SetupAllPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        int count = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject preview = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (preview == null || preview.GetComponent<SpriteRenderer>() == null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (Setup(root, false)) count++;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        return count;
    }
}
