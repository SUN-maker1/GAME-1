using UnityEngine;

/// <summary>
/// 运行时搭 UI 的小工具（不依赖任何场景里预先摆好的资源）。
/// 对话框、头顶提示都是代码临时拼出来的，所以工程里不用先摆 Canvas 也能用。
/// </summary>
public static class UiKit
{
    private static Sprite _white;

    /// <summary>一张纯白 4x4 贴图做的 Sprite，给 Image 当底板用</summary>
    public static Sprite WhiteSprite()
    {
        if (_white != null) return _white;

        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color32[] cols = new Color32[16];
        for (int i = 0; i < cols.Length; i++)
            cols[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(cols);
        tex.Apply(false, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;

        _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        _white.hideFlags = HideFlags.HideAndDontSave;
        return _white;
    }

    private static Font _font;
    private static bool _fontTried;

    /// <summary>
    /// 取一个能显示中文的字体。
    /// uGUI 默认用的 Arial 里没有汉字，直接写中文会全是方块 —— 所以这里优先
    /// 用系统里的雅黑 / 黑体 / 宋体（Font.CreateDynamicFontFromOSFont）动态造一个，
    /// 都找不到才退回 Unity 内置字体（那时中文会显示不出来，Console 里会警告）。
    /// </summary>
    public static Font CjkFont(int size = 32)
    {
        if (_font != null) return _font;
        if (_fontTried) return BuiltinFont();

        _fontTried = true;

        string[] candidates =
        {
            "Microsoft YaHei", "微软雅黑",
            "SimHei", "黑体",
            "Noto Sans CJK SC", "Source Han Sans SC",
            "PingFang SC", "Hiragino Sans GB",
            "SimSun", "宋体"
        };

        foreach (string name in candidates)
        {
            Font f = Font.CreateDynamicFontFromOSFont(name, size);
            if (f != null)
            {
                f.hideFlags = HideFlags.HideAndDontSave;
                _font = f;
                return f;
            }
        }

        Debug.LogWarning("[UiKit] 系统里没找到中文字体（雅黑/黑体/宋体），对话框里的中文可能显示成方块。\n" +
                         "解决办法：在 Project 里导入一个中文字体（ttf/otf），把它拖到对话框 Text 的 Font 上，\n" +
                         "或者改用 TextMeshPro 并生成一个中文字体资源。");
        return BuiltinFont();
    }

    private static Font BuiltinFont()
    {
        // Unity 2022 内置字体叫 LegacyRuntime.ttf，老版本叫 Arial.ttf
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    /// <summary>新建一个 Image（自动带上白底 sprite）</summary>
    public static UnityEngine.UI.Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        UnityEngine.UI.Image img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = WhiteSprite();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>新建一个 Text（自动带上中文字体和黑色描边）</summary>
    public static UnityEngine.UI.Text MakeText(string name, Transform parent, int fontSize, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        UnityEngine.UI.Text t = go.AddComponent<UnityEngine.UI.Text>();
        t.font = CjkFont(fontSize);
        t.fontSize = fontSize;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;

        UnityEngine.UI.Outline outline = go.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    /// <summary>把 RectTransform 设成「四边相对父物体拉伸」并给出内边距</summary>
    public static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
