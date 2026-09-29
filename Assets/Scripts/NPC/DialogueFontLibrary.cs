using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话字体库 —— 找出工程里 / 系统里所有能用的字体，按名字取出来给 Text 用。
///
/// 【字体放哪才会被找到】按顺序查：
///   ① Assets/Resources/Fonts/ 下的字体（推荐。打包到游戏里也还在，最稳）
///      —— 复制过去不用手动：菜单 Tools ▸ 农场RPG ▸ 字体 ▸ 把选中的字体拷进 Resources/Fonts
///   ② 系统里装好的字体（微软雅黑 / 黑体 / 宋体 ……）
///   ③ 都找不到就退回 UiKit 自动挑的中文字体
///
/// 【为什么要缓存】每一帧刷布局都会来查一次字体，系统字体现造很贵，
/// 所以查到一次就记住（创建出来的临时字体标记成 HideAndDontSave，不会污染场景）。
/// </summary>
public static class DialogueFontLibrary
{
    /// <summary>Resources 下的字体目录（相对 Resources）</summary>
    public const string FontResourcePath = "Fonts";

    private static Font[] _resourceFonts;
    private static bool _resourceLoaded;

    private static readonly Dictionary<string, Font> _resolved = new Dictionary<string, Font>();
    private static string[] _osNames;
    private static bool _osLoaded;

    // ---------------------------------------------------------------- 列表

    private static void EnsureResourceLoaded()
    {
        if (_resourceLoaded) return;
        _resourceLoaded = true;
        _resourceFonts = Resources.LoadAll<Font>(FontResourcePath);
        if (_resourceFonts == null) _resourceFonts = new Font[0];
    }

    /// <summary>Resources/Fonts 下的字体（运行时可用，打包也带着）</summary>
    public static IReadOnlyList<Font> ResourceFonts
    {
        get
        {
            EnsureResourceLoaded();
            return _resourceFonts;
        }
    }

    /// <summary>Resources/Fonts 下的字体名列表</summary>
    public static List<string> ResourceFontNames()
    {
        EnsureResourceLoaded();
        List<string> names = new List<string>();
        for (int i = 0; i < _resourceFonts.Length; i++)
        {
            Font f = _resourceFonts[i];
            if (f == null) continue;
            if (!names.Contains(f.name)) names.Add(f.name);
        }
        names.Sort();
        return names;
    }

    /// <summary>Resources/Fonts 下有没有这个字体</summary>
    public static bool HasResourceFont(string name)
    {
        return ResourceFontNames().Contains(name);
    }

    /// <summary>系统里装的字体名（可能很多，默认不全列）</summary>
    public static string[] OsFontNames()
    {
        if (_osLoaded) return _osNames;
        _osLoaded = true;

        try
        {
            _osNames = Font.GetOSInstalledFontNames();
        }
        catch (System.Exception)
        {
            _osNames = new string[0];
        }

        if (_osNames == null) _osNames = new string[0];
        return _osNames;
    }

    /// <summary>
    /// 给调节面板用的候选字体名：第 0 个恒为「（自动）」，后面是 Resources/Fonts 里的。
    /// 需要看系统字体就把 includeOs 打开。
    /// </summary>
    public static List<string> CandidateNames(bool includeOs = false)
    {
        List<string> list = ResourceFontNames();

        if (includeOs)
        {
            string[] os = OsFontNames();
            for (int i = 0; i < os.Length; i++)
            {
                string n = os[i];
                if (string.IsNullOrEmpty(n)) continue;
                if (!list.Contains(n)) list.Add(n);
            }
        }

        list.Insert(0, AutoFontLabel);     // 第 0 个 = 自动（UiKit 挑的中文字体）
        return list;
    }

    /// <summary>下拉里「自动」那一项的显示名</summary>
    public const string AutoFontLabel = "（自动：系统中文字体）";

    // ---------------------------------------------------------------- 取字体

    /// <summary>按名字找字体。找不到返回 null（调用方自己退回 UiKit.CjkFont）。</summary>
    public static Font Resolve(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (name == AutoFontLabel) return null;

        Font cached;
        if (_resolved.TryGetValue(name, out cached) && cached != null) return cached;

        Font found = Lookup(name);
        if (found != null) _resolved[name] = found;
        return found;
    }

    private static Font Lookup(string name)
    {
        EnsureResourceLoaded();

        for (int i = 0; i < _resourceFonts.Length; i++)
        {
            Font f = _resourceFonts[i];
            if (f != null && f.name == name) return f;
        }

        // Resources/Fonts 是异步加载后才有的（比如刚拷进去还没刷新），再补一次
        Font direct = Resources.Load<Font>(FontResourcePath + "/" + name);
        if (direct != null) return direct;

        // 系统字体：只在真的叫这个名字时才造（避免造出一堆没用的）
        string[] os = OsFontNames();
        for (int i = 0; i < os.Length; i++)
        {
            if (os[i] != name) continue;

            try
            {
                Font f = Font.CreateDynamicFontFromOSFont(name, 32);
                if (f != null)
                {
                    f.hideFlags = HideFlags.HideAndDontSave;
                    return f;
                }
            }
            catch (System.Exception)
            {
                // 有些字体名 Unity 造不出来，跳过就好
            }
            break;
        }

        return null;
    }

    /// <summary>忘了之前缓存的结果（刚往 Resources/Fonts 里拷了字体就调一下）</summary>
    public static void Refresh()
    {
        _resourceLoaded = false;
        _osLoaded = false;
        _resolved.Clear();
        EnsureResourceLoaded();
    }
}
