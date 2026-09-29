#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 对话「外观预设」和字体的一键工具（菜单 Tools ▸ 农场RPG ▸ 字体 / 对话外观）。
///
/// 【外观预设是什么】
///   字体、字号、颜色、描边、框体大小、立绘尺寸 —— 一整套外观打包成一个资源。
///   调好一次存下来，以后想换就一键套回去；新写的对话直接照着这套来。
///   平时最省事的存法：Play 模式按 F7 →「模板」页 → 起个名字点「保存当前样子为预设」，
///   运行时脚本建不了资源文件，就是这个类在后面帮忙落盘。
///
/// 【字体放哪才会被认到】
///   Assets/Resources/Fonts —— 打包到游戏里也还在。
///   拷过去的时候会把字符集设成「动态」，不然缺中文字会显示成方块（这是最容易踩的坑）。
/// </summary>
public static class DialoguePresetTools
{
    internal const string PresetFolder = "Assets/Resources/Dialogue/Presets";
    internal const string FontFolder = "Assets/Resources/Fonts";

    private const string FontMenu = "Tools/农场RPG/字体/";
    private const string PresetMenu = "Tools/农场RPG/对话外观/";

    // ---------------------------------------------------------------- 字体

    [MenuItem(FontMenu + "① 把选中的字体拷进 Resources/Fonts（自动设成动态字符集）", false, 240)]
    public static void ImportSelectedFonts()
    {
        List<string> paths = SelectedFontPaths();
        if (paths.Count == 0)
        {
            Debug.LogWarning("[字体] 先在 Project 里选中字体文件（.ttf / .otf）再点这个菜单。");
            return;
        }

        if (!EnsureFolder(FontFolder)) return;

        int ok = 0;
        List<string> names = new List<string>();

        foreach (string src in paths)
        {
            string file = Path.GetFileName(src);
            string dst = FontFolder + "/" + file;

            if (src == dst)
            {
                Debug.Log("[字体] " + file + " 本来就在 Resources/Fonts 里了，只更新导入参数。");
            }
            else
            {
                // 同名已存在就先删：AssetDatabase.CopyAsset 遇到同名会失败，「替换」就是这么实现的
                if (File.Exists(dst)) AssetDatabase.DeleteAsset(dst);

                if (!AssetDatabase.CopyAsset(src, dst))
                {
                    Debug.LogWarning("[字体] 复制失败：" + src + " → " + dst);
                    continue;
                }
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            }

            SetupFontImporter(dst);

            Font f = AssetDatabase.LoadAssetAtPath<Font>(dst);
            if (f != null) names.Add(f.name);
            ok++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        DialogueFontLibrary.Refresh();      // 运行时那边也刷新一下：F7 面板的下拉立即可见

        Debug.Log($"[字体] 已导入 {ok} 个字体到 {FontFolder}：{(names.Count > 0 ? string.Join("、", names.ToArray()) : "（无）")}\n" +
                  "接下来：进 Play 按 F7 →「字体颜色」页，名字 / 正文 / 提示三处都能选到它。");
    }

    [MenuItem(FontMenu + "② 打开字体目录", false, 241)]
    public static void OpenFontFolder()
    {
        EnsureFolder(FontFolder);
        Object folder = AssetDatabase.LoadAssetAtPath<Object>(FontFolder);
        Selection.activeObject = folder;
        EditorGUIUtility.PingObject(folder);
        EditorUtility.FocusProjectWindow();
    }

    /// <summary>Project 里选中的字体路径（含 Foldout 多选）</summary>
    private static List<string> SelectedFontPaths()
    {
        List<string> paths = new List<string>();

        foreach (string guid in Selection.assetGUIDs)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(p)) continue;

            string ext = Path.GetExtension(p).ToLower();
            bool isFontFile = ext == ".ttf" || ext == ".otf" || ext == ".ttc" || ext == ".dfont";
            bool isFontAsset = AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(Font);

            if (isFontFile || isFontAsset)
            {
                if (!paths.Contains(p)) paths.Add(p);
            }
        }

        return paths;
    }

    /// <summary>
    /// 把字体的导入参数设成「能显示中文」：字符集 = 动态（不然中文全是方块），把字体数据打进包里。
    /// 这里用反射设，不直接写 TrueTypeFontImporter —— 不同 Unity 版本类名 / 命名空间会变，反射最稳。
    /// </summary>
    public static void SetupFontImporter(string assetPath)
    {
        AssetImporter imp = AssetImporter.GetAtPath(assetPath);
        if (imp == null) return;

        try
        {
            SetEnumValue(imp, "characterSet", "Dynamic");        // 动态字符集：用到什么字现造，中文不会缺
            SetValue(imp, "includeFontData", true);              // 字体数据一起打包，换机也能显示
            SetValue(imp, "userData", "");
            imp.SaveAndReimport();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[字体] 设置 " + assetPath + " 的导入参数失败：" + e.Message +
                             "\n手动改也行：选中字体 → Inspector → Character 选 Unicode / Dynamic。");
        }
    }

    private static void SetValue(object target, string propName, object value)
    {
        PropertyInfo pi = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (pi == null || !pi.CanWrite) return;

        object v = pi.PropertyType == typeof(int) ? System.Convert.ToInt32(value) : value;
        pi.SetValue(target, v, null);
    }

    private static void SetEnumValue(object target, string propName, string enumName)
    {
        PropertyInfo pi = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (pi == null || !pi.CanWrite || !pi.PropertyType.IsEnum) return;

        pi.SetValue(target, System.Enum.Parse(pi.PropertyType, enumName), null);
    }

    // ---------------------------------------------------------------- 外观预设

    [MenuItem(PresetMenu + "① 基于当前对话布局新建一份外观预设", false, 250)]
    public static void CreatePresetFromLayout()
    {
        DialogueLayout lay = DialogueLayoutTools.EnsureLayoutAsset();
        DialogueLookData look = new DialogueLookData();
        look.CaptureFrom(lay);

        DialoguePreset preset = CreateOrUpdatePreset("我的样式", look, false);
        if (preset == null) return;

        Selection.activeObject = preset;
        EditorGUIUtility.PingObject(preset);
        EditorUtility.FocusProjectWindow();

        Debug.Log("[对话] 外观预设已建好：" + AssetDatabase.GetAssetPath(preset) +
                  "\n改文案直接改它，也可以在 Play 模式 F7 →「模板」页改完再存回去。");
    }

    [MenuItem(PresetMenu + "② 列出已有的外观预设", false, 251)]
    public static void ListPresets()
    {
        DialoguePreset[] all = LoadAllPresets();
        if (all.Length == 0)
        {
            Debug.Log("[对话] 还没有外观预设。\n菜单 Tools ▸ 农场RPG ▸ 对话外观 ▸ ① 基于当前对话布局新建一份外观预设。" +
                      "\n或者 Play 模式按 F7 →「模板」页，调好之后点「保存当前样子为预设」。");
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("======== 对话外观预设 " + all.Length.ToString() + " 份 =====");
        foreach (DialoguePreset p in all)
        {
            string path = AssetDatabase.GetAssetPath(p);
            sb.AppendLine("· " + p.DisplayName + "（" + path + "）");
            sb.AppendLine("    框=" + (string.IsNullOrEmpty(p.look.boxStyle) ? "(继承当前)" : p.look.boxStyle) +
                          "  正文字号=" + p.look.bodyFontSize.ToString("0") +
                          "  正文色=" + ColorTag(p.look.textColor) +
                          "  字体=" + (string.IsNullOrEmpty(p.look.bodyFontName) ? "自动" : p.look.bodyFontName));
        }
        sb.AppendLine("用法：Play 按 F7 →「模板」页下拉里选一个点「套用」；或者在台词里写 style: <预设名>。");
        Debug.Log(sb.ToString());
    }

    [MenuItem(PresetMenu + "③ 清除所有立绘位置模板（保留逐句记录）", false, 252)]
    public static void ClearTemplates()
    {
        DialogueLayout lay = DialogueLayoutTools.EnsureLayoutAsset();
        int n = lay.slotTemplates != null ? lay.slotTemplates.Count : 0;
        if (lay.slotTemplates != null) lay.slotTemplates.Clear();

        EditorUtility.SetDirty(lay);
        AssetDatabase.SaveAssets();
        Debug.Log("[对话] 已清除 " + n.ToString() + " 条立绘位置模板（「只这一句」的记录没动）。");
    }

    /// <summary>找出工程里所有的对话外观预设</summary>
    public static DialoguePreset[] LoadAllPresets()
    {
        List<DialoguePreset> list = new List<DialoguePreset>();

        foreach (string guid in AssetDatabase.FindAssets("t:DialoguePreset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            DialoguePreset p = AssetDatabase.LoadAssetAtPath<DialoguePreset>(path);
            if (p != null) list.Add(p);
        }

        return list.ToArray();
    }

    /// <summary>按名字找预设（预设名 / 文件名都认）</summary>
    public static DialoguePreset FindPreset(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        foreach (DialoguePreset p in LoadAllPresets())
        {
            if (p != null && p.Matches(key)) return p;
        }
        return null;
    }

    /// <summary>
    /// 建（或更新）一份预设。
    /// 同名已经有的话就直接把新数值写回去 —— 「存一次、改一次」都是同一个文件，不会堆出一堆副本。
    /// </summary>
    public static DialoguePreset CreateOrUpdatePreset(string presetName, DialogueLookData look, bool silent)
    {
        if (look == null) return null;
        if (!EnsureFolder(PresetFolder)) return null;

        string name = string.IsNullOrEmpty(presetName) ? "未命名预设" : presetName.Trim();
        if (name.Length == 0) name = "未命名预设";

        DialoguePreset existing = FindPreset(name);
        if (existing != null)
        {
            existing.look = look;
            existing.presetName = name;
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DialoguePresetBridge.NotifyPresetListChanged();

            if (!silent)
                Debug.Log("[对话] 预设「" + name + "」已更新：" + AssetDatabase.GetAssetPath(existing));
            return existing;
        }

        string file = SanitizeFileName(name);
        string path = PresetFolder + "/DialoguePreset_" + file + ".asset";
        path = AssetDatabase.GenerateUniqueAssetPath(path);

        DialoguePreset preset = ScriptableObject.CreateInstance<DialoguePreset>();
        preset.presetName = name;
        preset.look = look;

        AssetDatabase.CreateAsset(preset, path);
        EditorUtility.SetDirty(preset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        DialoguePresetBridge.NotifyPresetListChanged();

        if (!silent)
            Debug.Log("[对话] 预设「" + name + "」已存到 " + path);

        return preset;
    }

    /// <summary>文件名里不能用的字符换成下划线</summary>
    private static string SanitizeFileName(string s)
    {
        char[] bad = { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\n', '\r', '\t' };
        string r = s;
        foreach (char c in bad) r = r.Replace(c.ToString(), "_");
        return r.Trim();
    }

    private static string ColorTag(Color c)
    {
        return "rgb(" + ((int)(c.r * 255f)).ToString() + "," + ((int)(c.g * 255f)).ToString() + "," +
               ((int)(c.b * 255f)).ToString() + ")";
    }

    /// <summary>
    /// 以前一句句拖出来的位置记录（Owner#第几句#第几个），一次性汇总成「按图」的位置模板。
    /// 之后但凡用到同一张立绘的句子，自动照这套摆 —— 不用再一个个拖了。
    /// 只认工程里的 DialogueAsset 资源（台词写在场景 NPC 身上的那种，进 Play 开他那段对话
    /// 按 F7 →「调节位置」页 →「整段对话都推广」更快）。
    /// </summary>
    [MenuItem(PresetMenu + "④ 把已调好的「逐句」位置汇总成「按图」模板", false, 253)]
    public static void PromoteAllOverridesToTemplates()
    {
        DialogueLayout lay = DialogueLayoutTools.EnsureLayoutAsset();

        if (lay.slotOverrides == null || lay.slotOverrides.Count == 0)
        {
            Debug.Log("[对话] 还没有任何「逐句」位置记录，没东西可汇总。");
            return;
        }

        Dictionary<string, DialogueLayout.DialogueSlotOverride> map =
            new Dictionary<string, DialogueLayout.DialogueSlotOverride>();
        foreach (DialogueLayout.DialogueSlotOverride o in lay.slotOverrides)
        {
            if (o == null || string.IsNullOrEmpty(o.key)) continue;
            map[o.key] = o;
        }

        int written = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:DialogueAsset"))
        {
            DialogueAsset asset = AssetDatabase.LoadAssetAtPath<DialogueAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null || asset.lines == null) continue;

            for (int i = 0; i < asset.lines.Count; i++)
            {
                DialogueLine line = asset.lines[i];
                if (line == null) continue;

                List<DialoguePortraitSlot> slots = line.GetPortraitSlots();
                for (int j = 0; j < slots.Count; j++)
                {
                    DialoguePortraitSlot s = slots[j];
                    if (s == null || s.sprite == null) continue;

                    string key = asset.name + "#" + i.ToString() + "#" + j.ToString();
                    DialogueLayout.DialogueSlotOverride o;
                    if (!map.TryGetValue(key, out o)) continue;

                    string tkey = DialogueLayout.BuildSpriteKey(s.sprite);
                    if (string.IsNullOrEmpty(tkey)) continue;

                    DialogueLayout.DialogueSlotTemplate t = lay.GetOrCreateSlotTemplate(tkey);
                    t.offsetX = o.offsetX;
                    t.offsetY = o.offsetY;
                    t.width = o.width;
                    t.height = o.height;
                    written++;
                }
            }
        }

        EditorUtility.SetDirty(lay);
        AssetDatabase.SaveAssets();

        Debug.Log(written > 0
            ? $"[对话] 已把 {written} 条「逐句」记录汇总成位置模板（按图） —— 之后新写的对话自动套用。\n" +
              "想把台词写在场景 NPC 身上的也一并汇总，就进 Play 打开他那段对话，按 F7 →「调节位置」页 →「整段对话都推广」。"
            : "[对话] 逐句记录的键和现有对话资源对不上（台词多半写在场景里的 NPC 身上）。\n" +
              "那种情况进 Play 打开那段对话，按 F7 →「调节位置」页 →「整段对话都推广（按图）」就行。");
    }

    /// <summary>确保目录存在（顺手补上 Assets/Resources）</summary>
    private static bool EnsureFolder(string path)
    {
        try
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Dialogue"))
                AssetDatabase.CreateFolder("Assets/Resources", "Dialogue");

            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string leaf = Path.GetFileName(path);

            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, leaf);

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[对话] 建目录失败：" + path + " —— " + e.Message);
            return false;
        }
    }
}

/// <summary>
/// 编辑器这边接住运行时发来的请求 —— F7 面板上点「保存当前样子为预设」时，
/// 运行时脚本没法自己建资源（不能引用 UnityEditor），就由这里负责写文件。
/// </summary>
[InitializeOnLoad]
public static class DialoguePresetBridgeHost
{
    static DialoguePresetBridgeHost()
    {
        DialoguePresetBridge.CreateRequested += OnCreateRequested;
    }

    private static void OnCreateRequested(string presetName, DialogueLookData look)
    {
        try
        {
            DialoguePreset p = DialoguePresetTools.CreateOrUpdatePreset(presetName, look, true);
            if (p != null)
            {
                Selection.activeObject = p;
                Debug.Log("[对话] 预设「" + p.DisplayName + "」已存：" + AssetDatabase.GetAssetPath(p) +
                          "\n以后 F7 →「模板」页下拉里选它点「套用」就能换成这套样子。");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[对话] 存预设失败：" + e.Message +
                             "\n也可以用菜单 Tools ▸ 农场RPG ▸ 对话外观 ▸ ① 基于当前对话布局新建一份外观预设。");
        }
    }
}
#endif
