using UnityEngine;
using UnityEditor;
using TMPro;

/// <summary>
/// 一键把中文 TMP 字体挂到场景里的 DialogueUI 上。
///
/// 菜单：Tools ▸ 农场RPG ▸ 对话 ▸ 自动挂载中文字体
///
/// 自动完成：
///   1. 在工程里找 lifangti_SDF.asset（俐方体11号 TMP 字体）
///   2. 在场景里找 DialogueUI 组件
///   3. 把字体赋给它的 Font Asset 字段
/// </summary>
public static class DialogueFontAssigner
{
    [MenuItem("Tools/农场RPG/对话/自动挂载中文字体")]
    public static void AssignFont()
    {
        // 1. 找字体资源
        string fontPath = "Assets/Resources/font/lifangti_SDF.asset";
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (font == null)
        {
            // 找不到精确路径就全局搜一下
            string[] guids = AssetDatabase.FindAssets("lifangti_SDF t:TMP_FontAsset");
            if (guids.Length > 0)
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        if (font == null)
        {
            EditorUtility.DisplayDialog("找不到字体",
                "工程里找不到 lifangti_SDF.asset。\n请先用 Window ▸ TextMeshPro ▸ Font Asset Creator 生成字体资源。", "好的");
            return;
        }

        // 2. 找场景里的 DialogueUI
        DialogueUI ui = Object.FindObjectOfType<DialogueUI>();
        if (ui == null)
        {
            EditorUtility.DisplayDialog("找不到对话面板",
                "场景里没有 DialogueUI 组件。\n请先用菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 自动搭建对话UI 生成。", "好的");
            return;
        }

        // 3. 赋字体
        Undo.RecordObject(ui, "挂载中文字体");
        ui.fontAsset = font;
        EditorUtility.SetDirty(ui);

        // 同时赋给名字和内容文本（双保险）
        if (ui.nameText != null) ui.nameText.font = font;
        if (ui.contentText != null) ui.contentText.font = font;

        Debug.Log($"[DialogueFontAssigner] 中文字体「{font.name}」已挂到 {ui.gameObject.name} 的 DialogueUI 上。");
        EditorUtility.DisplayDialog("完成", $"中文字体「{font.name}」已成功挂到对话面板上！", "好的");
    }
}
