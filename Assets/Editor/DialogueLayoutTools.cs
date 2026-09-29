#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 对话布局资源的一键工具（菜单 Tools ▸ 农场RPG ▸ 对话）。
///
/// 【这份资源是干嘛的】
///   对话框的大小位置、文字留白、立绘默认尺寸都存在这里。
///   改它最快的方式是：Play 模式下按 F7，鼠标拖 / 滚轮缩放（见 DialogueLayoutTuner），
///   不用在这里手填数字；这里主要用来「看一下 / 恢复默认 / 复制一份换着用」。
/// </summary>
public static class DialogueLayoutTools
{
    internal const string RootMenu = "Tools/农场RPG/对话/";

    internal const string DialogueFolder = "Assets/Resources/Dialogue";
    internal const string LayoutAssetPath = "Assets/Resources/Dialogue/DialogueLayout.asset";
    internal const string PortraitFolder = "Assets/Resources/Portrait";

    [MenuItem(RootMenu + "7. 打开对话布局资源（F7 调出来的数值存在这儿）", false, 340)]
    public static void OpenLayout()
    {
        DialogueLayout lay = EnsureLayoutAsset();
        Selection.activeObject = lay;
        EditorGUIUtility.PingObject(lay);
        EditorUtility.FocusProjectWindow();
        Debug.Log("[对话] 布局资源在这儿：" + LayoutAssetPath +
                  "\n小贴士：数值不用手填 —— Play 模式下靠近 NPC 按 E 开对话，再按 F7 用鼠标拖就行。");
    }

    [MenuItem(RootMenu + "8. 布局恢复默认数值", false, 341)]
    public static void ResetLayout()
    {
        DialogueLayout lay = EnsureLayoutAsset();
        lay.ResetToDefaults();
        EditorUtility.SetDirty(lay);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[对话] 布局已恢复默认数值。");
    }

    [MenuItem(RootMenu + "10. 立刻保存对话布局（把 F7 拖出来的数值写进资源）", false, 343)]
    public static void SaveNowMenu()
    {
        SaveNow(true);
    }

    [MenuItem(RootMenu + "11. 清除所有立绘位置记录", false, 344)]
    public static void ClearSlotOverrides()
    {
        DialogueLayout lay = EnsureLayoutAsset();
        int n = lay.slotOverrides != null ? lay.slotOverrides.Count : 0;
        if (lay.slotOverrides != null) lay.slotOverrides.Clear();
        EditorUtility.SetDirty(lay);
        AssetDatabase.SaveAssets();
        Debug.Log("[对话] 已清除 " + n + " 条立绘位置记录（重新拖就会再记一遍）。");
    }

    /// <summary>
    /// 把「对话布局」资源的改动写进磁盘。
    ///
    /// 【为什么必须先 SetDirty】AssetDatabase.SaveAssets() 只写「被标记过脏」的资源。
    /// Play 模式里改的数值不会自动标记，所以光调 SaveAssets() 等于没存 —— 之前就是这么丢的。
    /// </summary>
    public static bool SaveNow(bool log = false)
    {
        try
        {
            int count = 0;
            DialogueLayout[] lays = Resources.FindObjectsOfTypeAll<DialogueLayout>();
            for (int i = 0; i < lays.Length; i++)
            {
                DialogueLayout lay = lays[i];
                if (lay == null) continue;
                EditorUtility.SetDirty(lay);
                count++;
            }

            AssetDatabase.SaveAssets();

            // 改完顺手刷一下 Inspector：如果这份资源正开着，能看见数值在动
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            if (log)
            {
                Debug.Log(count > 0
                    ? "[对话] 已把布局数值写进资源：" + LayoutAssetPath + "（Ctrl+S 保存工程彻底留住）"
                    : "[对话] 场上没有正在用的布局资源，先点第 7 项打开它。");
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[对话] 保存布局失败：" + e.Message + "（手动 Ctrl+S 也能保存）");
            return false;
        }
    }
    [MenuItem(RootMenu + "9. 复制一份布局（给不同场景换着用）", false, 342)]
    public static void DuplicateLayout()
    {
        DialogueLayout lay = EnsureLayoutAsset();
        string path = AssetDatabase.GenerateUniqueAssetPath(LayoutAssetPath.Replace(".asset", "_2.asset"));
        if (AssetDatabase.CopyAsset(LayoutAssetPath, path))
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DialogueLayout copy = AssetDatabase.LoadAssetAtPath<DialogueLayout>(path);
            Selection.activeObject = copy;
            EditorGUIUtility.PingObject(copy);
            Debug.Log("[对话] 已复制一份：" + path +
                      "\n想用它就把它拖到 DialogueManager 的 Layout 字段上。");
        }
        else
        {
            Debug.LogWarning("[对话] 复制失败，检查一下 " + LayoutAssetPath + " 还在不在。");
        }
    }

    // ------------------------------------------------------------------ 内部

    /// <summary>确保布局资源存在（没有就建一份带默认数值的）</summary>
    public static DialogueLayout EnsureLayoutAsset()
    {
        EnsureFolderDialogue();

        DialogueLayout lay = AssetDatabase.LoadAssetAtPath<DialogueLayout>(LayoutAssetPath);
        if (lay != null) return lay;

        lay = ScriptableObject.CreateInstance<DialogueLayout>();
        lay.ResetToDefaults();
        AssetDatabase.CreateAsset(lay, LayoutAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[对话] 已自动创建布局资源：" + LayoutAssetPath);
        return lay;
    }

    private static void EnsureFolderDialogue()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(DialogueFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "Dialogue");
        if (!AssetDatabase.IsValidFolder(PortraitFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "Portrait");
    }
}

/// <summary>
/// 进编辑器时自动把布局资源和立绘目录准备好 —— 省得先去找菜单点一下。
/// 退出 Play 模式时自动存盘一次：F7 拖出来的数值不会因为忘按 Ctrl+S 丢掉。
/// </summary>
[InitializeOnLoad]
public class DialogueLayoutAutoSetup
{
    static DialogueLayoutAutoSetup()
    {
        EditorApplication.delayCall += Prepare;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Application.isPlaying) return;

        try
        {
            DialogueLayoutTools.EnsureLayoutAsset();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[对话] 自动准备布局资源失败：" + e.Message +
                             "\n手动点菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 打开对话布局资源 也能建。");
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        // 刚从 Play 退出来：把拖过的布局数值落到磁盘上
        if (state != PlayModeStateChange.EnteredEditMode) return;

        try
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[对话] 自动保存布局失败：" + e.Message + "（手动 Ctrl+S 也能保存）");
        }
    }
}
/// <summary>
/// 自动存盘：F7 拖动/缩放时，运行时那边会发 LayoutChanged 通知（运行时脚本不能引用 UnityEditor，
/// 所以只能这样配合），这里收到就标脏 + 写盘 —— 拖完立刻落盘，退出 Play 不会丢。
/// 为了不拖一次写一次磁盘，加了 0.4 秒节流。
/// </summary>
[InitializeOnLoad]
public static class DialogueLayoutAutoSaver
{
    private const double SaveInterval = 0.4;

    private static bool _pending;
    private static double _nextTime;

    static DialogueLayoutAutoSaver()
    {
        DialogueLayoutTuner.LayoutChanged += RequestSave;
        EditorApplication.update += Flush;
    }

    private static void RequestSave()
    {
        _pending = true;
    }

    private static void Flush()
    {
        if (!_pending) return;

        double now = EditorApplication.timeSinceStartup;
        if (now < _nextTime) return;

        _pending = false;
        _nextTime = now + SaveInterval;
        DialogueLayoutTools.SaveNow(false);
    }
}
#endif
