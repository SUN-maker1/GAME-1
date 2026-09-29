using UnityEngine;

/// <summary>
/// 对话「外观」的一整套数值快照 —— 这就是所谓「模板」的第二层：样式模板。
///
/// 里面装的：
///   · 用的什么字体（名字 + 字号）
///   · 名字 / 正文 / 提示 三处的颜色，以及描边（开关、颜色、粗细）
///   · 对话框样式（Resources/UI/dialogue_box_&lt;名字&gt;）
///   · 框体大小位置、文字留白、立绘默认尺寸
///
/// 【它干两件事】
///   1. 把当前调好的样子「拍」下来存成资源（DialoguePreset），以后一键套回去
///   2. 某一句想换样子时，临时盖在这一句的显示上（DialogueLine.preset）
/// </summary>
[System.Serializable]
public class DialogueLookData
{
    // ------------------------------------------------------------ 字体
    [Header("字体（名字；留空 = 自动挑一个能显示中文的系统字体）")]
    public string nameFontName = "";
    public string bodyFontName = "";
    public string hintFontName = "";

    [Header("字号")]
    public float nameFontSize = 34f;
    public float bodyFontSize = 34f;
    public float hintFontSize = 26f;

    // ------------------------------------------------------------ 颜色 / 描边
    [Header("颜色")]
    public Color nameColor = new Color(1f, 0.87f, 0.45f, 1f);
    public Color textColor = new Color(0.97f, 0.95f, 0.90f, 1f);
    public Color hintColor = new Color(0.80f, 0.76f, 0.68f, 0.9f);

    [Header("描边（像素字勾上更清楚）")]
    public bool outlineEnabled = true;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.85f);
    public float outlineX = 1.5f;
    public float outlineY = -1.5f;

    // ------------------------------------------------------------ 框体 / 位置
    [Header("对话框样式")]
    public string boxStyle = "wood";

    [Header("框体")]
    public float panelLeft = 140f;
    public float panelRight = 140f;
    public float panelBottom = 70f;
    public float panelHeight = 280f;

    [Header("文字区留白 + 正文整体偏移")]
    public float contentLeft = 60f;
    public float contentRight = 60f;
    public float contentTop = 100f;
    public float contentBottom = 80f;
    public float bodyOffsetX = 0f;
    public float bodyOffsetY = 0f;

    [Header("说话人名字")]
    public float nameX = 60f;
    public float nameY = 40f;
    public float nameWidth = 700f;
    public float nameHeight = 60f;

    [Header("右下角提示")]
    public float hintX = 60f;
    public float hintY = 30f;
    public float hintWidth = 500f;
    public float hintHeight = 50f;

    [Header("立绘默认尺寸")]
    public float portraitWidth = 420f;
    public float portraitHeight = 520f;
    public float portraitInset = 24f;
    public float portraitSink = 0f;

    // ------------------------------------------------------------ 存取

    /// <summary>把「对话布局」资源里现在的样子拍下来</summary>
    public void CaptureFrom(DialogueLayout lay)
    {
        if (lay == null) return;

        nameFontName = lay.nameFontName;
        bodyFontName = lay.bodyFontName;
        hintFontName = lay.hintFontName;

        nameFontSize = lay.nameFontSize;
        bodyFontSize = lay.bodyFontSize;
        hintFontSize = lay.hintFontSize;

        nameColor = lay.nameColor;
        textColor = lay.textColor;
        hintColor = lay.hintColor;

        outlineEnabled = lay.outlineEnabled;
        outlineColor = lay.outlineColor;
        outlineX = lay.outlineDistance.x;
        outlineY = lay.outlineDistance.y;

        panelLeft = lay.panelLeft;
        panelRight = lay.panelRight;
        panelBottom = lay.panelBottom;
        panelHeight = lay.panelHeight;

        contentLeft = lay.contentLeft;
        contentRight = lay.contentRight;
        contentTop = lay.contentTop;
        contentBottom = lay.contentBottom;
        bodyOffsetX = lay.bodyOffsetX;
        bodyOffsetY = lay.bodyOffsetY;

        nameX = lay.nameX;
        nameY = lay.nameY;
        nameWidth = lay.nameWidth;
        nameHeight = lay.nameHeight;

        hintX = lay.hintX;
        hintY = lay.hintY;
        hintWidth = lay.hintWidth;
        hintHeight = lay.hintHeight;

        portraitWidth = lay.portraitWidth;
        portraitHeight = lay.portraitHeight;
        portraitInset = lay.portraitInset;
        portraitSink = lay.portraitSink;

        // 当前全局选的框体样式（存在 PlayerPrefs 里，换机器前都记得住）
        boxStyle = DialogueManager.CurrentStyle;
    }

    /// <summary>把这套数值写回「对话布局」资源（套用模板时用）</summary>
    public void ApplyTo(DialogueLayout lay)
    {
        if (lay == null) return;

        lay.nameFontName = nameFontName;
        lay.bodyFontName = bodyFontName;
        lay.hintFontName = hintFontName;

        lay.nameFontSize = nameFontSize;
        lay.bodyFontSize = bodyFontSize;
        lay.hintFontSize = hintFontSize;

        lay.nameColor = nameColor;
        lay.textColor = textColor;
        lay.hintColor = hintColor;

        lay.outlineEnabled = outlineEnabled;
        lay.outlineColor = outlineColor;
        lay.outlineDistance = new Vector2(outlineX, outlineY);

        lay.panelLeft = panelLeft;
        lay.panelRight = panelRight;
        lay.panelBottom = panelBottom;
        lay.panelHeight = panelHeight;

        lay.contentLeft = contentLeft;
        lay.contentRight = contentRight;
        lay.contentTop = contentTop;
        lay.contentBottom = contentBottom;
        lay.bodyOffsetX = bodyOffsetX;
        lay.bodyOffsetY = bodyOffsetY;

        lay.nameX = nameX;
        lay.nameY = nameY;
        lay.nameWidth = nameWidth;
        lay.nameHeight = nameHeight;

        lay.hintX = hintX;
        lay.hintY = hintY;
        lay.hintWidth = hintWidth;
        lay.hintHeight = hintHeight;

        lay.portraitWidth = portraitWidth;
        lay.portraitHeight = portraitHeight;
        lay.portraitInset = portraitInset;
        lay.portraitSink = portraitSink;
    }

    /// <summary>把这套数值盖到「本帧要用的布局数值」上（每句临时换样子时用，不改资源）</summary>
    public void ApplyToMetrics(ref DialogueManager.DialogueUiMetrics m)
    {
        m.nameFont = Pick(nameFontName, m.nameFont);
        m.bodyFont = Pick(bodyFontName, m.bodyFont);
        m.hintFont = Pick(hintFontName, m.hintFont);

        m.nameSize = nameFontSize;
        m.bodySize = bodyFontSize;
        m.hintSize = hintFontSize;

        m.nameColor = nameColor;
        m.textColor = textColor;
        m.hintColor = hintColor;

        m.outline = outlineEnabled;
        m.outlineColor = outlineColor;
        m.outlineDistance = new Vector2(outlineX, outlineY);

        m.left = panelLeft;
        m.right = panelRight;
        m.bottom = panelBottom;
        m.height = panelHeight;

        m.contentLeft = contentLeft;
        m.contentRight = contentRight;
        m.contentTop = contentTop;
        m.contentBottom = contentBottom;
        m.bodyOffX = bodyOffsetX;
        m.bodyOffY = bodyOffsetY;

        m.nameX = nameX;
        m.nameY = nameY;
        m.nameW = nameWidth;
        m.nameH = nameHeight;

        m.hintX = hintX;
        m.hintY = hintY;
        m.hintW = hintWidth;
        m.hintH = hintHeight;

        m.portraitWidth = portraitWidth;
        m.portraitHeight = portraitHeight;
        m.portraitInset = portraitInset;
        m.portraitSink = portraitSink;
    }

    private static Font Pick(string fontName, Font fallback)
    {
        Font f = DialogueFontLibrary.Resolve(fontName);
        return f != null ? f : fallback;
    }
}

/// <summary>
/// 对话外观预设（样式模板）—— 存一份 DialogueLookData，随时一键套用。
///
/// 【怎么建】
///   ① 游戏里 Play 模式按 F7 → 「模板」页 → 起个名字点「保存当前样子为预设」（推荐，所见即所得）
///   ② 菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 外观预设 ▸ 基于当前布局新建一份预设
///   ③ Project 里右键 ▸ Create ▸ 农场RPG ▸ 对话外观预设，然后手动填
///
/// 【怎么用】游戏里 F7 → 「模板」页下拉里选一个点「套用」；也可以在台词里写 style: 预设名。
/// 【放哪】必须在 Resources 底下（推荐 Assets/Resources/Dialogue/Presets），
///        运行时才 Resources.LoadAll 得到 —— 「另存为预设」按钮会自动放对位置。
/// </summary>
[CreateAssetMenu(menuName = "农场RPG/对话外观预设（字体 / 颜色 / 框体）", fileName = "DialoguePreset_新预设")]
public class DialoguePreset : ScriptableObject
{
    [Tooltip("预设名字。显示在 F7 面板的下拉里，也用在台词里写 style: 这个名字")]
    public string presetName = "";

    [Tooltip("这套外观的具体数值。别手填 —— 在 F7 面板调好之后点「保存当前样子为预设」最省事")]
    public DialogueLookData look = new DialogueLookData();

    /// <summary>下拉列表里显示的名字（没填 presetName 就用文件名）</summary>
    public string DisplayName
    {
        get { return string.IsNullOrEmpty(presetName) ? name : presetName; }
    }

    /// <summary>找不到的别名也不放过：文件名本身也能当 key 用</summary>
    public bool Matches(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return key == name || key == presetName;
    }

    private void OnValidate()
    {
        if (look == null) look = new DialogueLookData();
        if (string.IsNullOrEmpty(presetName)) presetName = name;
    }
}

/// <summary>
/// 运行时 ↔ 编辑器 的桥 —— 运行时脚本不能引用 UnityEditor，没法自己建资源文件，
/// 所以用静态事件把「请帮我存一个预设」的请求发出去，编辑器那边接住后落盘。
/// （跟现有的 DialogueLayoutTuner.LayoutChanged 一套路子。）
/// </summary>
public static class DialoguePresetBridge
{
    /// <summary>参数：预设名、要存的外观数值</summary>
    public static event System.Action<string, DialogueLookData> CreateRequested;

    /// <summary>编辑器新建 / 删除预设后发回来，让 F7 面板的下拉刷新</summary>
    public static event System.Action PresetListChanged;

    public static void RequestCreate(string presetName, DialogueLookData look)
    {
        System.Action<string, DialogueLookData> e = CreateRequested;
        if (e != null) e(presetName, look);
    }

    public static void NotifyPresetListChanged()
    {
        System.Action e = PresetListChanged;
        if (e != null) e();
    }
}
