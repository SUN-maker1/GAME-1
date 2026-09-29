using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话框的「布局资源」——框体位置和大小、文字留白、立绘默认尺寸全都放在这里。
///
/// 【干嘛用的】
///   一份布局可以给所有 NPC 共用，改一次全工程生效。
///   数值不用手填：Play 模式下按 F7 进入调节模式，鼠标拖 / 滚轮缩放，
///   调出来什么样就存成什么样（存在这份资源里，退出游戏也还在）。
///
/// 【在哪】
///   Assets/Resources/Dialogue/DialogueLayout.asset
///   菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 打开对话布局资源
///
/// 【单位】全部是像素，按参考分辨率 1920x1080 算（屏幕上会自动等比缩放）。
///   框体位置：panelLeft / panelRight 是离屏幕左右边多远，panelBottom 离屏幕底边多远。
///   文字留白：content 系列是「文字区」离框体四边的距离。
/// </summary>
[CreateAssetMenu(menuName = "农场RPG/对话布局", fileName = "DialogueLayout")]
public class DialogueLayout : ScriptableObject
{
    // ---------------------------------------------------------------- 框体

    [Header("对话框框体（离屏幕边的距离）")]
    [Tooltip("框的左边离屏幕左边多远")]
    public float panelLeft = 140f;

    [Tooltip("框的右边离屏幕右边多远")]
    public float panelRight = 140f;

    [Tooltip("框的下边离屏幕下边多远")]
    public float panelBottom = 70f;

    [Tooltip("框的高度")]
    public float panelHeight = 280f;

    // ---------------------------------------------------------------- 文字区

    [Header("文字区留白（离框体内边的距离）")]
    public float contentLeft = 60f;
    public float contentRight = 60f;
    public float contentTop = 100f;
    public float contentBottom = 80f;

    [Header("正文整体偏移（自由挪动，正负都行）")]
    [Tooltip("正文整体左右挪多少。正 = 往右。F7 模式拖正文改的就是它，随便拖不受限")]
    public float bodyOffsetX = 0f;

    [Tooltip("正文整体上下挪多少。正 = 往上。")]
    public float bodyOffsetY = 0f;

    [Header("字号")]
    public float nameFontSize = 34f;
    public float bodyFontSize = 34f;
    public float hintFontSize = 26f;

    // ---------------------------------------------------------------- 字体

    [Header("字体（填字体名，留空 = 自动挑一个能显示中文的）")]
    [Tooltip("字体必须先放进 Assets/Resources/Fonts 才会被找到：\n" +
             "菜单 Tools ▸ 农场RPG ▸ 字体 ▸ 把选中的字体拷进 Resources/Fonts（拷的时候会把字符集设成动态，\n" +
             "中文才不会变成方块）。也可以直接填系统里装好的字体名，比如「微软雅黑」「SimHei」。\n" +
             "不想手填：Play 按 F7 →「字体颜色」页里有下拉，点一下就换。")]
    public string nameFontName = "";

    [Tooltip("正文用的字体。留空 = 跟上面一样的自动字体")]
    public string bodyFontName = "";

    [Tooltip("右下角「按 E 继续」提示用的字体")]
    public string hintFontName = "";

    [Header("文字描边（像素风字体建议勾上，深色背景更清楚）")]
    [Tooltip("关掉就完全不画描边")]
    public bool outlineEnabled = true;

    [Tooltip("描边颜色（默认半透明黑）")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.85f);

    [Tooltip("描边偏移。数值越大描边越粗；负的往下。\n" +
             "字号大、图小的话可以给到 2 左右，字号小就 1 左右。")]
    public Vector2 outlineDistance = new Vector2(1.5f, -1.5f);

    // ---------------------------------------------------------------- 名字 / 提示

    [Header("说话人名字（相对框体左上角）")]
    public float nameX = 60f;
    public float nameY = 40f;
    public float nameWidth = 700f;
    public float nameHeight = 60f;

    [Header("「按 E 继续」提示（相对框体右下角）")]
    public float hintX = 60f;
    public float hintY = 30f;
    public float hintWidth = 500f;
    public float hintHeight = 50f;

    // ---------------------------------------------------------------- 立绘

    [Header("人物立绘（默认的统一尺寸）")]
    [Tooltip("立绘容器宽度。图会按原始比例缩放后居中放进去，不会被拉变形")]
    public float portraitWidth = 420f;

    [Tooltip("立绘高度。底边坐在框的上沿，向上露出上半身")]
    public float portraitHeight = 520f;

    [Tooltip("立绘离框体左右边的水平间距")]
    public float portraitInset = 24f;

    [Tooltip("底边往框里压多少。0 = 正好坐在框上沿；想让脚踩进框里就给正数")]
    public float portraitSink = 0f;

    [Tooltip("某一句没指定方向时用这边")]
    public DialoguePortraitSide defaultSide = DialoguePortraitSide.Left;

    // ---------------------------------------------------------------- 每句立绘的位置

    [Header("每句立绘的位置 / 大小（F7 拖完自动写进来）")]
    [Tooltip("每句话的立绘位置存在这里，而不是写回台词里 —— 因为台词多半写在场景里的 NPC 身上，\n" +
             "Unity 退出 Play 模式会把它整个回滚掉，只有存在资源文件里才留得住。\n" +
             "键的格式：「对话来源#第几句#第几个立绘」（不用手填，拖完自动写）")]
    public List<DialogueSlotOverride> slotOverrides = new List<DialogueSlotOverride>();

    /// <summary>查某个立绘有没有存过位置</summary>
    public DialogueSlotOverride FindSlotOverride(string key)
    {
        if (slotOverrides == null || string.IsNullOrEmpty(key)) return null;

        for (int i = 0; i < slotOverrides.Count; i++)
        {
            DialogueSlotOverride o = slotOverrides[i];
            if (o != null && o.key == key) return o;
        }
        return null;
    }

    /// <summary>取某个立绘的位置记录（没有就新建一条）</summary>
    public DialogueSlotOverride GetOrCreateSlotOverride(string key)
    {
        if (slotOverrides == null) slotOverrides = new List<DialogueSlotOverride>();

        DialogueSlotOverride found = FindSlotOverride(key);
        if (found != null) return found;

        found = new DialogueSlotOverride { key = key, offsetX = 0f, offsetY = 0f, width = -1f, height = -1f };
        slotOverrides.Add(found);
        return found;
    }

    /// <summary>删掉某个立绘的位置记录（调节器「重置这一项」时用）</summary>
    public bool RemoveSlotOverride(string key)
    {
        if (slotOverrides == null) return false;

        for (int i = slotOverrides.Count - 1; i >= 0; i--)
        {
            DialogueSlotOverride o = slotOverrides[i];
            if (o != null && o.key == key)
            {
                slotOverrides.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>某句某个立绘的位置 / 大小记录。width / height 为 -1 表示沿用默认尺寸。</summary>
    [System.Serializable]
    public class DialogueSlotOverride
    {
        public string key;
        public float offsetX;
        public float offsetY;
        public float width = -1f;
        public float height = -1f;
    }

    // ---------------------------------------------------------------- 立绘位置模板

    /// <summary>
    /// F7 拖完之后，把立绘的位置记到哪儿去。
    ///   PerLine = 只管当前这一句（老办法，新写的对话还得再拖一遍）
    ///   ByRole  = 同一个说话人通用（换剧本也照用）
    ///   BySprite= 同一张立绘图通用（最省事：图不变，位置就一直是对的）← 默认
    /// 优先级反过来：本句 &gt; 同一张图 &gt; 同一个角色 &gt; 布局里的统一默认值。
    /// </summary>
    public enum TemplateScope
    {
        PerLine = 0,
        ByRole = 1,
        BySprite = 2
    }

    [Header("拖动默认存成哪种模板（F7 面板里随时能改）")]
    [Tooltip("BySprite（默认）= 同一张立绘图以后都按这个位置来，新写的对话自动是对的，不用再一个个拖。\n" +
             "想让某一句特殊摆一下，就把这一项临时改成 PerLine。")]
    public TemplateScope portraitSaveScope = TemplateScope.BySprite;

    /// <summary>立绘位置模板。key 用 BuildSpriteKey / BuildRoleKey 生成。</summary>
    [System.Serializable]
    public class DialogueSlotTemplate
    {
        public string key;
        public float offsetX;
        public float offsetY;
        public float width = -1f;
        public float height = -1f;
        public bool flipX;
        public Color tint = Color.white;
    }

    [Header("立绘位置模板（一次调好，之后所有新对话自动套）")]
    [Tooltip("这里存的是「某张图 / 某个角色」该摆哪儿。\n" +
             "key = 图:&lt;图片名&gt; 或 角色:&lt;说话人&gt;|L/R，不用手填 —— F7 面板里拖完按一下保存就行。")]
    public List<DialogueSlotTemplate> slotTemplates = new List<DialogueSlotTemplate>();

    /// <summary>「同一张图」的模板 key（推荐用这个：一张图摆好，用到它的地方全跟着）</summary>
    public static string BuildSpriteKey(Sprite sprite)
    {
        return sprite != null ? "图:" + sprite.name : "";
    }

    /// <summary>「同一个角色」的模板 key（同一个人的所有台词统一位置）</summary>
    public static string BuildRoleKey(string speaker, bool left)
    {
        if (string.IsNullOrEmpty(speaker)) return "";
        return "角色:" + speaker + (left ? "|L" : "|R");
    }

    /// <summary>查模板</summary>
    public DialogueSlotTemplate FindSlotTemplate(string key)
    {
        if (slotTemplates == null || string.IsNullOrEmpty(key)) return null;

        for (int i = 0; i < slotTemplates.Count; i++)
        {
            DialogueSlotTemplate t = slotTemplates[i];
            if (t != null && t.key == key) return t;
        }
        return null;
    }

    /// <summary>取模板（没有就新建一条）</summary>
    public DialogueSlotTemplate GetOrCreateSlotTemplate(string key)
    {
        if (slotTemplates == null) slotTemplates = new List<DialogueSlotTemplate>();

        DialogueSlotTemplate found = FindSlotTemplate(key);
        if (found != null) return found;

        found = new DialogueSlotTemplate { key = key, width = -1f, height = -1f, tint = Color.white };
        slotTemplates.Add(found);
        return found;
    }

    /// <summary>删模板</summary>
    public bool RemoveSlotTemplate(string key)
    {
        if (slotTemplates == null) return false;

        for (int i = slotTemplates.Count - 1; i >= 0; i--)
        {
            DialogueSlotTemplate t = slotTemplates[i];
            if (t != null && t.key == key)
            {
                slotTemplates.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- 调节面板

    [Header("F7 调节面板（自己记住上次拖到哪了）")]
    public float tunerPanelX = 12f;
    public float tunerPanelY = 12f;
    public bool tunerPanelCollapsed = false;

    // ---------------------------------------------------------------- 颜色

    [Header("颜色")]
    public Color nameColor = new Color(1f, 0.87f, 0.45f, 1f);
    public Color textColor = new Color(0.97f, 0.95f, 0.90f, 1f);
    public Color hintColor = new Color(0.80f, 0.76f, 0.68f, 0.9f);

    // ---------------------------------------------------------------- 默认

    /// <summary>把所有数值恢复成出厂默认值</summary>
    public void ResetToDefaults()
    {
        panelLeft = 140f;
        panelRight = 140f;
        panelBottom = 70f;
        panelHeight = 280f;

        contentLeft = 60f;
        contentRight = 60f;
        contentTop = 100f;
        contentBottom = 80f;

        bodyOffsetX = 0f;
        bodyOffsetY = 0f;

        nameFontSize = 34f;
        bodyFontSize = 34f;
        hintFontSize = 26f;

        nameX = 60f;
        nameY = 40f;
        nameWidth = 700f;
        nameHeight = 60f;

        hintX = 60f;
        hintY = 30f;
        hintWidth = 500f;
        hintHeight = 50f;

        portraitWidth = 420f;
        portraitHeight = 520f;
        portraitInset = 24f;
        portraitSink = 0f;
        defaultSide = DialoguePortraitSide.Left;

        portraitSaveScope = TemplateScope.BySprite;

        nameFontName = "";
        bodyFontName = "";
        hintFontName = "";
        outlineEnabled = true;
        outlineColor = new Color(0f, 0f, 0f, 0.85f);
        outlineDistance = new Vector2(1.5f, -1.5f);

        nameColor = new Color(1f, 0.87f, 0.45f, 1f);
        textColor = new Color(0.97f, 0.95f, 0.90f, 1f);
        hintColor = new Color(0.80f, 0.76f, 0.68f, 0.9f);
    }

    /// <summary>造一份装着默认数值的临时布局（调节器拿它当「重置」的模板）</summary>
    public static DialogueLayout CreateDefaultsTemplate()
    {
        DialogueLayout t = ScriptableObject.CreateInstance<DialogueLayout>();
        t.ResetToDefaults();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }
}
