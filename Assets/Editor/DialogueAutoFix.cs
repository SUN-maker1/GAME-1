using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// 对话系统一键修复工具。
/// 菜单：Tools ▸ 农场RPG ▸ 对话 ▸ 一键修复对话系统（推荐）
///
/// 一次搞定三件最容易漏的事：
///   1. 场景里没有 DialogueUI → 自动搭建对话面板（含 Canvas / 面板 / 名字 / 内容 / 立绘 / 背景）
///   2. DialogueTrigger 的 Collider2D 没勾 Is Trigger → 自动全部勾上
///   3. DialogueUI 没指定中文字体 → 自动找项目里的 TMP 中文字体资源挂上
///
/// 完成后自动保存场景，运行即可对话。
/// </summary>
public static class DialogueAutoFix
{
    [MenuItem("Tools/农场RPG/对话/一键修复对话系统（推荐）", false, 90)]
    public static void FixAll()
    {
        int builtUI = 0;
        int fixedTrigger = 0;
        int fonted = 0;

        // ---- 1. 场景里没有 DialogueUI 就先搭一个 ----
        if (Object.FindObjectOfType<DialogueUI>() == null)
        {
            DialogueUIBuilder.BuildDialogueUI();
            builtUI = 1;
        }

        // ---- 2. 把所有 DialogueTrigger 的 Collider2D 勾成 Is Trigger ----
        DialogueTrigger[] triggers = Object.FindObjectsOfType<DialogueTrigger>();
        foreach (DialogueTrigger t in triggers)
        {
            if (t == null) continue;
            Collider2D col = t.GetComponent<Collider2D>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
                EditorUtility.SetDirty(col);
                fixedTrigger++;
            }
            else if (col == null)
            {
                Debug.LogWarning($"[一键修复] {t.name} 上没有 Collider2D，手动加一个并勾 Is Trigger。", t);
            }
        }

        // ---- 3. 自动挂中文字体（覆盖默认的 LiberationSans，它不含中文）----
        DialogueUI ui = Object.FindObjectOfType<DialogueUI>();
        if (ui != null)
        {
            TMP_FontAsset fa = FindChineseFont();
            if (fa != null)
            {
                ui.fontAsset = fa;
                EditorUtility.SetDirty(ui);
                fonted = 1;
            }
        }

        // ---- 4. 保存场景 ----
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path))
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        string msg = $"修复完成：搭建对话UI {builtUI} 个，勾好 Is Trigger {fixedTrigger} 处，" +
                     $"中文字体{(fonted == 1 ? "已挂载" : "未找到（请手动生成 TMP 中文字体资源）")}。\n\n" +
                     "运行游戏，走近 NPC 按 E 对话。";
        EditorUtility.DisplayDialog("对话系统一键修复", msg, "好的");
        Debug.Log("[DialogueAutoFix] " + msg);
    }

    /// <summary>在项目里找一份含中文的 TMP 字体资源（优先名字带 lifangti / msyh / SDF 的）</summary>
    private static TMP_FontAsset FindChineseFont()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
        TMP_FontAsset fallback = null;

        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset")) continue;

            TMP_FontAsset fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa == null) continue;

            string lower = path.ToLower();
            if (lower.Contains("lifangti") || lower.Contains("msyh") || lower.Contains("sdf"))
                return fa;
            if (fallback == null) fallback = fa;
        }
        return fallback;
    }
}