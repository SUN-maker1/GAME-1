using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时「调节器」——想改对话框 / 字体 / 颜色 / 立绘的位置大小，不用再手填数字。
///
/// 【怎么用】
///   1. 进 Play，走到 NPC 旁边按 E 打开对话（如果没开对话，按 F7 会自动播一段示例给你调）
///   2. 按 F7 开关「布局调节」
///   3. 面板三个页：
///        ① 调节位置 —— 鼠标拖 / 滚轮缩放（对话框、名字、正文、提示、每个立绘）
///        ② 字体颜色 —— 选字体、拖滑条改字号和颜色、改描边
///        ③ 模板     —— 把当前样子存成预设，以后一键套用；把调好的立绘位置推广给以后所有对话
///   4. 再按一次 F7 结束 —— 数值已经写在「对话布局」资源上，退出 Play 也还在（Ctrl+S 保存工程就彻底留住）
///
/// 【注意】
///   调节模式下对话会暂停推进，鼠标左键也不会再翻页 —— 这是故意的，免得拖着拖着就把话翻过去了。
/// </summary>
[DefaultExecutionOrder(-100)]
public class DialogueLayoutTuner : MonoBehaviour
{
    [Tooltip("开关调节模式的热键")]
    public KeyCode toggleKey = KeyCode.F7;

    [Tooltip("滚轮滚一格改多少像素")]
    public float wheelStep = 40f;

    /// <summary>现在是不是在调布局（对话进行时会暂停推进，把鼠标让出来）</summary>
    public static bool IsTuning { get; private set; }

    /// <summary>
    /// 数值一变就发这个通知 —— 编辑器那边（DialogueLayoutAutoSaver）收到后会立刻把
    /// 改动写进「对话布局」资源文件，退出 Play 也不会丢。
    /// （运行时脚本不能引用 UnityEditor，所以只能通过这种「运行时发通知 / 编辑器负责存」的方式。）
    /// </summary>
    public static event System.Action LayoutChanged;

    private static void NotifyChanged()
    {
        System.Action e = LayoutChanged;
        if (e != null) e();
    }

    /// <summary>给其它运行时脚本用的存盘通知入口（例如换样式改了面板高度时）</summary>
    public static void NotifyLayoutChanged()
    {
        NotifyChanged();
    }

    // ------------------------------------------------------------------ 存盘

    /// <summary>
    /// 每次改完数值都要走这里：
    ///   1. 立绘的位置顺手写进布局资源（台词写在场景物体上时，退出 Play 会被回滚，只有资源里留得住）
    ///   2. 通知编辑器存盘
    /// </summary>
    private static void Commit(DialogueManager mgr, Kind kind, int portrait)
    {
        if (mgr == null) return;

        if (kind == Kind.Portrait) SavePortrait(mgr, portrait);
        NotifyChanged();
    }

    /// <summary>
    /// 把某个立绘现在的位置存下来，存到哪儿由布局上的 portraitSaveScope 决定：
    ///   PerLine  = 只管这一句
    ///   ByRole   = 这个说话人以后都这样
    ///   BySprite = 同一张图以后都这样（默认，最省事）
    /// 存到「更通用」的一级时，会把比它更具体的旧记录删掉 —— 不然旧记录优先级更高，
    /// 会出现「明明拖了但没变」的假象。
    /// </summary>
    private static void SavePortrait(DialogueManager mgr, int index)
    {
        if (mgr == null) return;

        DialogueLayout lay = mgr.LayoutTarget;
        DialoguePortraitSlot s = mgr.PortraitSlotAt(index);
        if (lay == null || s == null) return;

        DialogueLayout.TemplateScope scope = lay.portraitSaveScope;

        if (scope == DialogueLayout.TemplateScope.PerLine)
        {
            string lineKey = mgr.SlotKey(index);
            if (string.IsNullOrEmpty(lineKey)) return;

            DialogueLayout.DialogueSlotOverride o = lay.GetOrCreateSlotOverride(lineKey);
            o.offsetX = s.offsetX;
            o.offsetY = s.offsetY;
            o.width = s.width;
            o.height = s.height;
            return;
        }

        string key = scope == DialogueLayout.TemplateScope.BySprite
            ? mgr.SpriteTemplateKey(index)
            : mgr.RoleTemplateKey(index);
        if (string.IsNullOrEmpty(key)) return;

        DialogueLayout.DialogueSlotTemplate t = lay.GetOrCreateSlotTemplate(key);
        t.offsetX = s.offsetX;
        t.offsetY = s.offsetY;
        t.width = s.width;
        t.height = s.height;

        // 更具体的旧记录会让这次拖动看起来「没生效」，清掉
        string lineKey2 = mgr.SlotKey(index);
        if (!string.IsNullOrEmpty(lineKey2)) lay.RemoveSlotOverride(lineKey2);

        if (scope == DialogueLayout.TemplateScope.ByRole)
        {
            string spriteKey = mgr.SpriteTemplateKey(index);
            if (!string.IsNullOrEmpty(spriteKey)) lay.RemoveSlotTemplate(spriteKey);
        }
    }

    private enum Kind { None, Frame, Name, Body, Hint, Portrait }

    private class Item
    {
        public Kind kind;
        public string label;
        public RectTransform rt;
        public int portraitIndex = -1;
        public bool active;
    }

    private readonly List<Item> _items = new List<Item>();

    private Image _highlight;
    private Rect _guiRect = new Rect(0f, 0f, 0f, 0f);
    private GUIStyle _labelStyle;
    private GUIStyle _boxStyle;
    private GUIStyle _swatchStyle;

    // 面板窗口：能拖走、能收起，免得挡住要调的立绘
    private Rect _winRect = new Rect(12f, 12f, 560f, 470f);
    private bool _collapsed;

    private Kind _selKind = Kind.None;
    private int _selPortrait = -1;
    private Item _hover;
    private bool _dragging;
    private Vector2 _lastMouse;
    private DialogueLayout _defaults;

    // ---- 面板状态 ----
    private int _tab;
    private string[] _tabLabels = { "① 调节位置", "② 字体颜色", "③ 模板" };

    private bool _promoteClearPerLine;

    private string[] _fontNames = new string[0];
    private bool _fontListReady;
    private bool _includeOsFonts;
    private int _fontPickTarget = -1;       // 0=名字 1=正文 2=提示
    private Vector2 _fontScroll;
    private Vector2 _contentScroll;

    private List<DialoguePreset> _presets = new List<DialoguePreset>();
    private int _presetIndex;
    private string _newPresetName = "我的样式";
    private bool _presetListReady;

    // ------------------------------------------------------------------ 主循环

    private void OnEnable() => DialoguePresetBridge.PresetListChanged += OnPresetListChanged;
    private void OnDisable() => DialoguePresetBridge.PresetListChanged -= OnPresetListChanged;

    private void OnPresetListChanged()
    {
        _presetListReady = false;
        Debug.Log("[对话] 外观预设列表已刷新。");
    }

    private void Update()
    {
        DialogueManager mgr = DialogueManager.Instance;
        if (mgr == null) return;

        if (Input.GetKeyDown(toggleKey))
        {
            Toggle(mgr);
            return;
        }

        if (!IsTuning)
        {
            SetHighlight(false);
            return;
        }

        EnsureHighlight(mgr);
        RefreshItems(mgr);

        // 鼠标在左上角那个操作面板上时，不要去拖底下的东西
        if (OverGui(Input.mousePosition))
        {
            _dragging = false;
            SetHighlight(false);
            return;
        }

        _hover = PickUnderMouse(Input.mousePosition);
        HandleClick();
        HandleDrag(mgr);
        HandleWheel(mgr);
        HandleArrowKeys(mgr);
        UpdateHighlight();
    }

    private void Toggle(DialogueManager mgr)
    {
        IsTuning = !IsTuning;
        _dragging = false;

        if (IsTuning)
        {
            DialogueLayout lay = mgr.LayoutTarget;
            if (lay == null)
            {
                Debug.LogWarning("[对话] 还没有「对话布局」资源，没法拖着调。\n" +
                                 "先用菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 打开对话布局资源 建一份（会自动填好默认值）。");
                IsTuning = false;
                return;
            }

            if (!DialogueManager.IsOpen)
            {
                Debug.Log("[对话] 当前没开着对话，先播一段示例对话方便你调。\n" +
                          "把自己的立绘丢进 Assets/Resources/Portrait，示例对话会自动用上。");
                mgr.PlaySampleDialogue();
            }

            // 面板位置 / 收起状态也写在布局资源里，下次开还是上次那个位置
            _winRect.x = lay.tunerPanelX;
            _winRect.y = lay.tunerPanelY;
            _collapsed = lay.tunerPanelCollapsed;

            RefreshFontNames();
            RefreshPresetList();

            Debug.Log("[对话] 布局调节：开。\n" +
                      "· 「调节位置」页：鼠标移到要调的东西上 → 左键拖动 = 挪位置；滚轮 = 高度 / 字号；\n" +
                      "  Shift + 滚轮 = 宽度；选中后方向键微调（Shift = 一次 10 像素）\n" +
                      "· 「字体颜色」页：换字体、拉滑条改字号和颜色\n" +
                      "· 「模板」页：把现在的样子存成预设，或者把立绘位置推广给以后所有对话\n" +
                      "再按一次 " + toggleKey + " 结束，数值会记住。");
        }
        else
        {
            mgr.ApplyUiMetrics();
            Debug.Log("[对话] 布局调节：关。调好的数值已经写在「对话布局」资源上，Ctrl+S 保存工程就留住了。");
        }
    }

    // ------------------------------------------------------------------ 元素表

    private void RefreshItems(DialogueManager mgr)
    {
        _items.Clear();

        AddItem(Kind.Frame, "对话框框体", mgr.PanelRect, -1, true);
        AddItem(Kind.Name, "说话人名字", mgr.NameRect, -1, IsVisible(mgr.NameRect));
        AddItem(Kind.Body, "正文文字", mgr.BodyRect, -1, true);
        AddItem(Kind.Hint, "「按 E 继续」提示", mgr.HintRect, -1, true);

        int count = mgr.PortraitCount;
        for (int i = 0; i < count; i++)
        {
            AddItem(Kind.Portrait, "立绘 #" + i.ToString(), mgr.PortraitRectAt(i), i, mgr.IsPortraitVisible(i));
        }
    }

    private void AddItem(Kind kind, string label, RectTransform rt, int portraitIndex, bool active)
    {
        if (rt == null) return;
        Item it = new Item();
        it.kind = kind;
        it.label = label;
        it.rt = rt;
        it.portraitIndex = portraitIndex;
        it.active = active;
        _items.Add(it);
    }

    private static bool IsVisible(RectTransform rt)
    {
        return rt != null && rt.gameObject.activeInHierarchy;
    }

    private Item FindItem(Kind kind, int portraitIndex)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            Item it = _items[i];
            if (it.kind == kind && it.portraitIndex == portraitIndex) return it;
        }
        return null;
    }

    /// <summary>
    /// 找出鼠标底下该调哪个元素。
    /// 规则：都包含鼠标时取「面积最小」的那个 —— 鼠标停在正文中就是调正文，
    /// 停在框体留白 / 边框上就是调框体，符合直觉。
    /// </summary>
    private Item PickUnderMouse(Vector2 mouse)
    {
        Item best = null;
        float bestArea = float.MaxValue;

        for (int i = 0; i < _items.Count; i++)
        {
            Item it = _items[i];
            if (it == null || !it.active || it.rt == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(it.rt, mouse, null)) continue;

            float area = Mathf.Abs(it.rt.rect.width * it.rt.rect.height);
            // 面积一样时后加的优先（画在上面的优先）
            if (best != null && area >= bestArea) continue;

            best = it;
            bestArea = area;
        }
        return best;
    }

    // ------------------------------------------------------------------ 拖动 / 缩放

    private void HandleClick()
    {
        if (Input.GetMouseButtonDown(0) && _hover != null)
        {
            _selKind = _hover.kind;
            _selPortrait = _hover.portraitIndex;
            _dragging = true;
            _lastMouse = Input.mousePosition;
        }
        if (!Input.GetMouseButton(0)) _dragging = false;
    }

    private void HandleDrag(DialogueManager mgr)
    {
        if (!_dragging || _selKind == Kind.None) return;

        Vector2 mouse = Input.mousePosition;
        float inv = 1f / CanvasScale();
        float dx = (mouse.x - _lastMouse.x) * inv;
        float dy = (mouse.y - _lastMouse.y) * inv;
        _lastMouse = mouse;

        if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f) return;

        DialogueLayout lay = mgr.LayoutTarget;
        if (lay == null) return;

        ApplyMove(lay, mgr, _selKind, _selPortrait, dx, dy);
        mgr.ApplyUiMetrics();
        Commit(mgr, _selKind, _selPortrait);
    }

    private void HandleWheel(DialogueManager mgr)
    {
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) < 0.0001f) return;

        Item hit = _hover;
        if (hit == null) return;

        _selKind = hit.kind;
        _selPortrait = hit.portraitIndex;

        DialogueLayout lay = mgr.LayoutTarget;
        if (lay == null) return;

        bool wideMode = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float amount = wheel * wheelStep * 10f;

        ApplyScale(lay, mgr, hit.kind, hit.portraitIndex, amount, wideMode);
        mgr.ApplyUiMetrics();
        Commit(mgr, hit.kind, hit.portraitIndex);
    }

    private void HandleArrowKeys(DialogueManager mgr)
    {
        if (_selKind == Kind.None) return;

        float mx = 0f;
        float my = 0f;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) mx = -1f;
        else if (Input.GetKeyDown(KeyCode.RightArrow)) mx = 1f;
        if (Input.GetKeyDown(KeyCode.UpArrow)) my = 1f;
        else if (Input.GetKeyDown(KeyCode.DownArrow)) my = -1f;

        if (mx == 0f && my == 0f) return;

        DialogueLayout lay = mgr.LayoutTarget;
        if (lay == null) return;

        float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 10f : 1f;
        ApplyMove(lay, mgr, _selKind, _selPortrait, mx * step, my * step);
        mgr.ApplyUiMetrics();
        Commit(mgr, _selKind, _selPortrait);
    }

    private static void ApplyMove(DialogueLayout lay, DialogueManager mgr, Kind kind, int portrait, float dx, float dy)
    {
        switch (kind)
        {
            case Kind.Frame:
                lay.panelLeft += dx;
                lay.panelRight -= dx;
                lay.panelBottom += dy;
                break;

            case Kind.Name:
                lay.nameX += dx;
                lay.nameY -= dy;
                break;

            case Kind.Body:
                // 拖正文 = 整体平移（框的大小不变），想怎么拖就怎么拖
                lay.bodyOffsetX += dx;
                lay.bodyOffsetY += dy;
                break;

            case Kind.Hint:
                lay.hintX -= dx;
                lay.hintY += dy;
                break;

            case Kind.Portrait:
                DialoguePortraitSlot s = mgr.PortraitSlotAt(portrait);
                if (s == null) break;
                float dir = mgr.IsPortraitLeft(portrait) ? 1f : -1f;
                s.offsetX += dx * dir;
                s.offsetY += dy;
                break;
        }
    }

    private static void ApplyScale(DialogueLayout lay, DialogueManager mgr, Kind kind, int portrait, float amount, bool wideMode)
    {
        switch (kind)
        {
            case Kind.Frame:
                if (wideMode)
                {
                    lay.panelLeft -= amount;
                    lay.panelRight -= amount;
                }
                else
                {
                    lay.panelHeight += amount;
                }
                break;

            case Kind.Name:
                if (wideMode)
                {
                    lay.nameWidth += amount;
                    lay.nameHeight += amount;
                }
                else
                {
                    lay.nameFontSize += amount * 0.2f;
                }
                break;

            case Kind.Body:
                if (wideMode)
                {
                    // Shift + 滚轮 = 改文字框宽度（两边一起收 / 放）
                    lay.contentLeft -= amount;
                    lay.contentRight -= amount;
                }
                else
                {
                    lay.bodyFontSize += amount * 0.2f;
                }
                break;

            case Kind.Hint:
                if (wideMode)
                {
                    lay.hintWidth += amount;
                    lay.hintHeight += amount;
                }
                else
                {
                    lay.hintFontSize += amount * 0.2f;
                }
                break;

            case Kind.Portrait:
                DialoguePortraitSlot s = mgr.PortraitSlotAt(portrait);
                if (s == null) break;
                // 第一次改大小时把默认值固化下来，之后这个立绘就用自己专属的尺寸
                if (s.height <= 0f) s.height = lay.portraitHeight;
                if (s.width <= 0f) s.width = lay.portraitWidth;
                s.height += amount;
                if (wideMode) s.width += amount;
                break;
        }
    }

    // ------------------------------------------------------------------ 高亮框

    private void EnsureHighlight(DialogueManager mgr)
    {
        if (_highlight != null) return;

        Transform root = mgr.UiRoot;
        if (root == null) return;

        Image img = UiKit.MakeImage("LayoutTunerHighlight", root, new Color(1f, 0.95f, 0.32f, 0.16f));
        RectTransform r = img.rectTransform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.zero;
        r.pivot = new Vector2(0.5f, 0.5f);
        img.raycastTarget = false;
        _highlight = img;
    }

    private void SetHighlight(bool on)
    {
        if (_highlight == null) return;
        if (_highlight.gameObject.activeSelf != on) _highlight.gameObject.SetActive(on);
    }

    private void UpdateHighlight()
    {
        if (_highlight == null) return;

        Item target = _hover != null ? _hover : FindItem(_selKind, _selPortrait);
        if (target == null || target.rt == null || !target.active)
        {
            SetHighlight(false);
            return;
        }

        SetHighlight(true);

        Vector3[] c = new Vector3[4];
        target.rt.GetWorldCorners(c);

        Vector3 mn = c[0];
        Vector3 mx = c[0];
        for (int i = 1; i < 4; i++)
        {
            mn = Vector3.Min(mn, c[i]);
            mx = Vector3.Max(mx, c[i]);
        }

        RectTransform h = _highlight.rectTransform;
        h.position = (mn + mx) * 0.5f;

        float s = CanvasScale();
        h.sizeDelta = new Vector2((mx.x - mn.x) / s, (mx.y - mn.y) / s);
        h.SetAsLastSibling();
    }

    private float CanvasScale()
    {
        DialogueManager mgr = DialogueManager.Instance;
        Canvas c = mgr != null ? mgr.ActiveCanvas : null;
        if (c == null) return 1f;
        float s = c.scaleFactor;
        return s > 0.001f ? s : 1f;
    }

    // ------------------------------------------------------------------ 面板

    private bool OverGui(Vector2 screen)
    {
        if (_guiRect.width <= 0f) return false;
        Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
        return _guiRect.Contains(gui);
    }

    private void OnGUI()
    {
        if (!IsTuning)
        {
            _guiRect = new Rect(0f, 0f, 0f, 0f);
            return;
        }

        // 可拖动窗口：按住顶部一栏拖走；挡住立绘时拖到别处或收起
        _winRect.height = _collapsed ? 46f : 480f;
        Rect moved = GUI.Window(GetInstanceID(), _winRect, DrawTunerWindow, GUIContent.none, BoxStyle());
        _winRect = moved;
        _guiRect = moved;

        // 面板挪动过 / 收起状态变了就记下来并落盘
        DialogueManager mgr = DialogueManager.Instance;
        if (mgr == null) return;

        DialogueLayout lay = mgr.LayoutTarget;
        if (lay == null) return;

        if (Mathf.Abs(moved.x - lay.tunerPanelX) > 0.5f ||
            Mathf.Abs(moved.y - lay.tunerPanelY) > 0.5f ||
            lay.tunerPanelCollapsed != _collapsed)
        {
            lay.tunerPanelX = moved.x;
            lay.tunerPanelY = moved.y;
            lay.tunerPanelCollapsed = _collapsed;
            NotifyChanged();
        }
    }

    private void DrawTunerWindow(int id)
    {
        GUIStyle label = LabelStyle();
        DialogueManager mgr = DialogueManager.Instance;
        DialogueLayout lay = mgr != null ? mgr.LayoutTarget : null;

        GUILayout.BeginArea(new Rect(10f, 8f, _winRect.width - 20f, _winRect.height - 16f));

        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>对话调节</b>（再按 " + toggleKey.ToString() + " 关）· 按住这行字可拖走面板", label, GUILayout.ExpandWidth(true));
        if (GUILayout.Button(_collapsed ? "展开" : "收起", GUILayout.Width(48f), GUILayout.Height(22f)))
            _collapsed = !_collapsed;
        GUILayout.EndHorizontal();

        if (_collapsed)
        {
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0f, 0f, _winRect.width, 34f));
            return;
        }

        if (lay == null)
        {
            GUILayout.Label("没有找到「对话布局」资源。\n菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 打开对话布局资源 建一份。", label);
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0f, 0f, _winRect.width, 34f));
            return;
        }

        // ---- 页签 ----
        int newTab = GUILayout.Toolbar(_tab, _tabLabels, GUILayout.Height(26f));
        if (newTab != _tab)
        {
            _tab = newTab;
            if (_tab == 1) RefreshFontNames();
            if (_tab == 2) RefreshPresetList();
        }

        GUILayout.Space(4f);

        _contentScroll = GUILayout.BeginScrollView(_contentScroll);

        switch (_tab)
        {
            case 0: DrawTabAdjust(mgr, lay, label); break;
            case 1: DrawTabLook(mgr, lay, label); break;
            default: DrawTabTemplate(mgr, lay, label); break;
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        // 顶部 34 像素是拖拽区（按钮不受影响）
        GUI.DragWindow(new Rect(0f, 0f, _winRect.width, 34f));
    }

    // ------------------------------------------------------------------ 页 ① 调节位置

    private void DrawTabAdjust(DialogueManager mgr, DialogueLayout lay, GUIStyle label)
    {
        GUILayout.Label("左键拖 = 挪位置（正文现在是整体平移，随便拖）", label);
        GUILayout.Label("滚轮 = 改高度 / 字号，Shift + 滚轮 = 改宽度", label);
        GUILayout.Label("选中后方向键微调（Shift = 一次 10 像素）", label);
        GUILayout.Space(6f);
        GUILayout.Label("<color=#ffe27a>" + DescribeSelection(lay, mgr) + "</color>", label);
        GUILayout.Space(8f);

        GUILayout.Label("<b>立绘位置存到哪：</b>", label);
        int scope = Toolbar(new[] { "只这一句", "同角色通用", "同一张图通用" }, (int)lay.portraitSaveScope);
        if (scope != (int)lay.portraitSaveScope)
        {
            lay.portraitSaveScope = (DialogueLayout.TemplateScope)scope;
            NotifyChanged();
        }
        GUILayout.Label("「同一张图通用」= 这张立绘以后出现在哪句里都按这个位置来，新写的对话自动是对的。", label);
        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("这句推广给同图", GUILayout.Height(24f))) PromoteSelected(mgr, lay, DialogueLayout.TemplateScope.BySprite);
        if (GUILayout.Button("这句推广给同角色", GUILayout.Height(24f))) PromoteSelected(mgr, lay, DialogueLayout.TemplateScope.ByRole);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("整段对话都推广（按图）", GUILayout.Height(24f))) PromoteWhole(mgr, lay, DialogueLayout.TemplateScope.BySprite);
        if (GUILayout.Button("整段对话都推广（按角色）", GUILayout.Height(24f))) PromoteWhole(mgr, lay, DialogueLayout.TemplateScope.ByRole);
        GUILayout.EndHorizontal();

        _promoteClearPerLine = GUILayout.Toggle(_promoteClearPerLine, "推广时顺手删掉原来的「只这一句」记录（推荐）");
        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("重置这一项", GUILayout.Height(26f))) ResetSelected(lay, mgr);
        if (GUILayout.Button("全部重置", GUILayout.Height(26f)))
        {
            lay.ResetToDefaults();
            mgr.ApplyUiMetrics();
            NotifyChanged();
        }
        if (GUILayout.Button("打印数值", GUILayout.Height(26f))) PrintValues(lay, mgr);
        GUILayout.EndHorizontal();
    }

    private void PromoteSelected(DialogueManager mgr, DialogueLayout lay, DialogueLayout.TemplateScope scope)
    {
        if (_selKind != Kind.Portrait || _selPortrait < 0)
        {
            Debug.Log("[对话] 先在画面里点一下要推广的那个立绘再按这个按钮。");
            return;
        }

        SavePortraitScope(mgr, lay, scope);
    }

    private void PromoteWhole(DialogueManager mgr, DialogueLayout lay, DialogueLayout.TemplateScope scope)
    {
        int n = mgr.PromoteTemplates(scope, _promoteClearPerLine);
        mgr.ApplyUiMetrics();
        NotifyChanged();

        Debug.Log(n > 0
            ? $"[对话] 已把当前这段对话的 {n} 个立绘位置推广成「{(scope == DialogueLayout.TemplateScope.BySprite ? "同一张图" : "同一个角色")}」模板。\n" +
              "以后新写的对话只要用同一张立绘（或同一个说话人），位置自动就是这套。"
            : "[对话] 这段对话里没有带立绘的句子，没东西可推广。");
    }

    /// <summary>按指定范围存一次当前选中的立绘（不受面板上的默认范围影响）</summary>
    private void SavePortraitScope(DialogueManager mgr, DialogueLayout lay, DialogueLayout.TemplateScope scope)
    {
        DialoguePortraitSlot s = mgr.PortraitSlotAt(_selPortrait);
        if (s == null) return;

        DialogueLayout.TemplateScope old = lay.portraitSaveScope;
        lay.portraitSaveScope = scope;
        SavePortrait(mgr, _selPortrait);
        lay.portraitSaveScope = old;

        mgr.ApplyUiMetrics();
        NotifyChanged();

        string where = scope == DialogueLayout.TemplateScope.BySprite ? "同一张图" :
                       scope == DialogueLayout.TemplateScope.ByRole ? "同一个角色" : "这一句";
        Sprite sp = s.sprite;
        string who = sp != null ? sp.name : "立绘";
        Debug.Log($"[对话] 已把「{who}」的位置存成模板（{where}通用）：\n" +
                  "以后凡是同一张图（或同一个说话人）的句子都按这个位置摆，新写的对话不用再一个个拖。");
    }

    // ------------------------------------------------------------------ 页 ② 字体颜色

    private void DrawTabLook(DialogueManager mgr, DialogueLayout lay, GUIStyle label)
    {
        GUILayout.Label("<b>字体</b>（放进 Assets/Resources/Fonts 才会出现在这里）", label);

        DrawFontPickerRow("名字", lay.nameFontName, 0, lay, mgr, SetNameFont);
        DrawFontPickerRow("正文", lay.bodyFontName, 1, lay, mgr, SetBodyFont);
        DrawFontPickerRow("提示", lay.hintFontName, 2, lay, mgr, SetHintFont);

        _includeOsFonts = GUILayout.Toggle(_includeOsFonts, "把系统里装的字体也列进来");
        if (GUILayout.Button("刷新字体列表", GUILayout.Height(22f)))
        {
            DialogueFontLibrary.Refresh();
            RefreshFontNames(true);
        }
        GUILayout.Space(6f);

        GUILayout.Label("<b>字号</b>", label);
        float nfs = SliderRow("名字", lay.nameFontSize, 12f, 90f);
        if (Mathf.Abs(nfs - lay.nameFontSize) > 0.01f) { lay.nameFontSize = nfs; Changed(mgr); }

        float bfs = SliderRow("正文", lay.bodyFontSize, 12f, 90f);
        if (Mathf.Abs(bfs - lay.bodyFontSize) > 0.01f) { lay.bodyFontSize = bfs; Changed(mgr); }

        float hfs = SliderRow("提示", lay.hintFontSize, 10f, 70f);
        if (Mathf.Abs(hfs - lay.hintFontSize) > 0.01f) { lay.hintFontSize = hfs; Changed(mgr); }

        GUILayout.Space(6f);
        GUILayout.Label("<b>颜色</b>", label);

        Color nc = RgbRow("名字", lay.nameColor);
        if (!SameColor(nc, lay.nameColor)) { lay.nameColor = nc; Changed(mgr); }

        Color bc = RgbRow("正文", lay.textColor);
        if (!SameColor(bc, lay.textColor)) { lay.textColor = bc; Changed(mgr); }

        Color hc = RgbRow("提示", lay.hintColor);
        if (!SameColor(hc, lay.hintColor)) { lay.hintColor = hc; Changed(mgr); }

        GUILayout.Space(6f);
        GUILayout.Label("<b>描边</b>", label);

        bool ol = GUILayout.Toggle(lay.outlineEnabled, "画描边");
        if (ol != lay.outlineEnabled) { lay.outlineEnabled = ol; Changed(mgr); }

        float thick = SliderRow("粗细", Mathf.Abs(lay.outlineDistance.x), 0f, 4f);
        if (Mathf.Abs(Mathf.Abs(lay.outlineDistance.x) - thick) > 0.01f)
        {
            lay.outlineDistance = new Vector2(thick, -thick);
            Changed(mgr);
        }

        Color oc = RgbRow("颜色", lay.outlineColor);
        if (!SameColor(oc, lay.outlineColor)) { lay.outlineColor = oc; Changed(mgr); }
    }

    private void DrawFontPickerRow(string title, string current, int target, DialogueLayout lay, DialogueManager mgr, System.Action<DialogueLayout, string> setter)
    {
        GUILayout.BeginHorizontal();

        GUILayout.Label(title, GUILayout.Width(34f));

        string show = string.IsNullOrEmpty(current) ? DialogueFontLibrary.AutoFontLabel : current;
        if (GUILayout.Button(show, GUILayout.ExpandWidth(true), GUILayout.Height(20f)))
        {
            if (_fontPickTarget == target) _fontPickTarget = -1;     // 再点一次收起来
            else { _fontPickTarget = target; RefreshFontNames(); }
        }

        if (GUILayout.Button("自动", GUILayout.Width(44f), GUILayout.Height(20f)))
        {
            setter(lay, "");
            Changed(mgr);
        }

        GUILayout.EndHorizontal();

        if (_fontPickTarget != target) return;

        GUILayout.BeginVertical();
        _fontScroll = GUILayout.BeginScrollView(_fontScroll, GUILayout.Height(140f));

        for (int i = 0; i < _fontNames.Length; i++)
        {
            if (!GUILayout.Button(_fontNames[i], GUILayout.Height(19f))) continue;

            string pick = i == 0 ? "" : _fontNames[i];
            setter(lay, pick);
            _fontPickTarget = -1;
            Changed(mgr);
            Debug.Log("[对话] " + title + "字体 → " + (string.IsNullOrEmpty(pick) ? "自动（系统中文字体）" : pick));
            break;
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static void SetNameFont(DialogueLayout lay, string n) { lay.nameFontName = n; }
    private static void SetBodyFont(DialogueLayout lay, string n) { lay.bodyFontName = n; }
    private static void SetHintFont(DialogueLayout lay, string n) { lay.hintFontName = n; }

    private void RefreshFontNames(bool force = false)
    {
        if (!force && _fontListReady && _fontNames.Length > 0) return;

        List<string> names = DialogueFontLibrary.CandidateNames(_includeOsFonts);
        _fontNames = names.ToArray();
        _fontListReady = _fontNames.Length > 0;

        if (_fontNames.Length <= 1)
            Debug.Log("[对话] Resources/Fonts 下还没有字体 —— 先把 ttf/otf 拷进去：\n" +
                      "菜单 Tools ▸ 农场RPG ▸ 字体 ▸ 把选中的字体拷进 Resources/Fonts（拷的时候会自动设成动态字符集，中文不会变方块）。");
    }

    // ------------------------------------------------------------------ 页 ③ 模板

    private void DrawTabTemplate(DialogueManager mgr, DialogueLayout lay, GUIStyle label)
    {
        GUILayout.Label("<b>把现在的样子存成预设</b>（字体 / 颜色 / 字号 / 框体 / 立绘尺寸 全打包）", label);

        GUILayout.BeginHorizontal();
        GUILayout.Label("名字", GUILayout.Width(34f));
        _newPresetName = GUILayout.TextField(_newPresetName, GUILayout.ExpandWidth(true), GUILayout.Height(20f));
        GUILayout.EndHorizontal();

        GUI.enabled = Application.isEditor;
        if (GUILayout.Button("保存当前样子为预设", GUILayout.Height(26f)))
        {
            string name = string.IsNullOrEmpty(_newPresetName) ? "我的样式" : _newPresetName.Trim();

            DialogueLookData look = new DialogueLookData();
            look.CaptureFrom(lay);

            if (Application.isEditor)
            {
                DialoguePresetBridge.RequestCreate(name, look);
                Debug.Log("[对话] 已让编辑器帮你存预设「" + name + "」，存完这里的下拉会自己刷新。");
            }
            else
            {
                Debug.LogWarning("[对话] 打包出来的游戏里没法新建资源文件 —— 预设要在编辑器里建。");
            }
        }
        GUI.enabled = true;

        GUILayout.Space(8f);
        GUILayout.Label("<b>套用已有预设</b>", label);

        RefreshPresetList();

        if (_presets.Count == 0)
        {
            GUILayout.Label("（还没有预设。上面存一个，或者菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 外观预设 ▸ 基于当前布局新建一份预设）", label);
        }
        else
        {
            if (_presetIndex >= _presets.Count) _presetIndex = 0;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(30f), GUILayout.Height(22f))) _presetIndex = Prev(_presetIndex, _presets.Count);
            GUILayout.Label("<color=#ffe27a>" + _presets[_presetIndex].DisplayName + "</color>  (" + (_presetIndex + 1).ToString() + "/" + _presets.Count.ToString() + ")", label, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(30f), GUILayout.Height(22f))) _presetIndex = (_presetIndex + 1) % _presets.Count;
            GUILayout.EndHorizontal();

            if (GUILayout.Button("套用这个预设", GUILayout.Height(26f))) ApplyPreset(mgr, lay, _presets[_presetIndex]);
        }

        if (GUILayout.Button("刷新列表", GUILayout.Height(22f))) RefreshPresetList(true);

        GUILayout.Space(8f);
        GUILayout.Label("<b>立绘位置模板</b>", label);

        int tpl = lay.slotTemplates != null ? lay.slotTemplates.Count : 0;
        int per = lay.slotOverrides != null ? lay.slotOverrides.Count : 0;
        GUILayout.Label("位置模板 " + tpl.ToString() + " 条（图 / 角色通用）· 逐句记录 " + per.ToString() + " 条", label);

        if (GUILayout.Button("清除所有位置模板（保留逐句记录）", GUILayout.Height(24f)))
        {
            if (lay.slotTemplates != null) lay.slotTemplates.Clear();
            mgr.ApplyUiMetrics();
            NotifyChanged();
            Debug.Log("[对话] 已清除所有立绘位置模板。");
        }
    }

    private void ApplyPreset(DialogueManager mgr, DialogueLayout lay, DialoguePreset preset)
    {
        if (preset == null || preset.look == null) return;

        preset.look.ApplyTo(lay);

        if (!string.IsNullOrEmpty(preset.look.boxStyle) && mgr != null)
            mgr.ApplyStyle(preset.look.boxStyle);

        if (mgr != null) mgr.ApplyUiMetrics();
        NotifyChanged();

        Debug.Log("[对话] 已套用预设「" + preset.DisplayName + "」。数值写进「对话布局」资源了，Ctrl+S 保存工程就留住。");
    }

    private void RefreshPresetList(bool force = false)
    {
        if (!force && _presetListReady) return;

        _presets.Clear();
        DialoguePreset[] all = Resources.LoadAll<DialoguePreset>("");
        for (int i = 0; i < all.Length; i++)
        {
            DialoguePreset p = all[i];
            if (p != null) _presets.Add(p);
        }

        if (_presetIndex >= _presets.Count) _presetIndex = 0;
        _presetListReady = true;
    }

    private static int Prev(int i, int count)
    {
        return count <= 0 ? 0 : (i - 1 + count) % count;
    }

    // ------------------------------------------------------------------ 小控件

    private void Changed(DialogueManager mgr)
    {
        if (mgr != null) mgr.ApplyUiMetrics();
        NotifyChanged();
    }

    /// <summary>横向一排按钮当单选用（IMGUI 里没有 EditorGUILayout.Toolbar 的运行时替代）</summary>
    private static int Toolbar(string[] labels, int selected)
    {
        int result = selected;

        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            Color old = GUI.color;
            if (i == selected) GUI.color = new Color(1.25f, 1.15f, 0.75f, 1f);
            bool on = GUILayout.Toggle(i == selected, labels[i], GUI.skin.button, GUILayout.ExpandWidth(true), GUILayout.Height(22f));
            GUI.color = old;

            if (on && i != selected) result = i;
        }
        GUILayout.EndHorizontal();

        return result;
    }

    private float SliderRow(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(50f));
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
        GUILayout.Label(((int)v).ToString(), GUILayout.Width(38f));
        GUILayout.EndHorizontal();
        return v;
    }

    private Color RgbRow(string label, Color c)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(50f));

        float r = GUILayout.HorizontalSlider(c.r, 0f, 1f, GUILayout.Width(92f));
        float g = GUILayout.HorizontalSlider(c.g, 0f, 1f, GUILayout.Width(92f));
        float b = GUILayout.HorizontalSlider(c.b, 0f, 1f, GUILayout.Width(92f));

        Color now = new Color(r, g, b, c.a);
        DrawSwatch(now);

        GUILayout.Label(((int)(r * 255f)).ToString() + "," + ((int)(g * 255f)).ToString() + "," + ((int)(b * 255f)).ToString(),
                        GUILayout.Width(78f));
        GUILayout.EndHorizontal();

        return now;
    }

    private void DrawSwatch(Color c)
    {
        if (_swatchStyle == null)
        {
            _swatchStyle = new GUIStyle(GUI.skin.box);
            _swatchStyle.normal.background = MakeSolidTex(Color.white);
        }

        Color old = GUI.color;
        GUI.color = new Color(c.r, c.g, c.b, 1f);
        GUILayout.Box(GUIContent.none, _swatchStyle, GUILayout.Width(24f), GUILayout.Height(16f));
        GUI.color = old;
    }

    private static bool SameColor(Color a, Color b)
    {
        return Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b);
    }

    private GUIStyle LabelStyle()
    {
        if (_labelStyle != null) return _labelStyle;

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.richText = true;
        _labelStyle.fontSize = 15;
        _labelStyle.normal.textColor = new Color(0.97f, 0.97f, 0.95f);
        _labelStyle.wordWrap = true;
        return _labelStyle;
    }

    private GUIStyle BoxStyle()
    {
        if (_boxStyle != null) return _boxStyle;

        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.normal.background = MakeSolidTex(new Color(0.06f, 0.07f, 0.10f, 0.88f));
        return _boxStyle;
    }

    private static Texture2D MakeSolidTex(Color c)
    {
        Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, c);
        t.Apply(false, true);
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    private string SelectedLabel()
    {
        Item it = _hover != null ? _hover : FindItem(_selKind, _selPortrait);
        return it != null ? it.label : "（还没选中东西）";
    }

    private string DescribeSelection(DialogueLayout lay, DialogueManager mgr)
    {
        string label = SelectedLabel();

        if (_selKind == Kind.Portrait)
        {
            DialoguePortraitSlot s = mgr != null ? mgr.PortraitSlotAt(_selPortrait) : null;
            if (s == null) return label + "\n（这一句没有这个立绘）";

            string owner = DescribeTemplateSource(lay, mgr, s);

            return label +
                   "\n位置 offsetX=" + s.offsetX.ToString("0") + "（正=往中间）  offsetY=" + s.offsetY.ToString("0") + "（正=往上）" +
                   "\n大小 高=" + (s.height > 0f ? s.height.ToString("0") : "默认 " + lay.portraitHeight.ToString("0")) +
                   "  宽=" + (s.width > 0f ? s.width.ToString("0") : "默认 " + lay.portraitWidth.ToString("0")) +
                   "  翻转=" + (s.flipX ? "是" : "否") +
                   "\n表情 " + (s.expressions != null ? s.expressions.Count : 0).ToString() + " 张（当前第 " +
                   (mgr != null ? mgr.ExpressionIndex : 0).ToString() + " 张）· 游戏里按 E / Q 切换" +
                   "\n这套摆放来自：" + owner;
        }

        switch (_selKind)
        {
            case Kind.Frame:
                return label +
                       "\n离屏幕：左 " + lay.panelLeft.ToString("0") + "  右 " + lay.panelRight.ToString("0") +
                       "  下 " + lay.panelBottom.ToString("0") +
                       "\n高度 " + lay.panelHeight.ToString("0");

            case Kind.Name:
                return label +
                       "\n位置 x=" + lay.nameX.ToString("0") + "  y=" + lay.nameY.ToString("0") +
                       "\n字号 " + lay.nameFontSize.ToString("0") +
                       "  框 " + lay.nameWidth.ToString("0") + " x " + lay.nameHeight.ToString("0") +
                       "\n字体 " + FontLabel(lay.nameFontName);

            case Kind.Body:
                return label +
                       "\n偏移 x=" + lay.bodyOffsetX.ToString("0") + "  y=" + lay.bodyOffsetY.ToString("0") + "（拖动 = 改这个，随便挪）" +
                       "\n文字框留白：左 " + lay.contentLeft.ToString("0") + "  右 " + lay.contentRight.ToString("0") +
                       "  上 " + lay.contentTop.ToString("0") + "  下 " + lay.contentBottom.ToString("0") +
                       "\n字号 " + lay.bodyFontSize.ToString("0") + "（Shift + 滚轮 = 改文字框宽度；左边还会自动给立绘让位）" +
                       "\n字体 " + FontLabel(lay.bodyFontName);

            case Kind.Hint:
                return label +
                       "\n位置 x=" + lay.hintX.ToString("0") + "  y=" + lay.hintY.ToString("0") +
                       "\n字号 " + lay.hintFontSize.ToString("0") +
                       "\n字体 " + FontLabel(lay.hintFontName);

            default:
                return "当前选中：" + label;
        }
    }

    private static string FontLabel(string name)
    {
        return string.IsNullOrEmpty(name) ? DialogueFontLibrary.AutoFontLabel : name;
    }

    /// <summary>这一套摆放是哪儿来的（逐句 / 同图模板 / 同角色模板 / 默认值），面板上告诉用户</summary>
    private string DescribeTemplateSource(DialogueLayout lay, DialogueManager mgr, DialoguePortraitSlot s)
    {
        if (lay == null || mgr == null) return "默认";

        string key = mgr.SlotKey(_selPortrait);
        if (!string.IsNullOrEmpty(key) && lay.FindSlotOverride(key) != null)
            return "只这一句（key " + key + "）";

        string spriteKey = DialogueLayout.BuildSpriteKey(s.sprite);
        if (!string.IsNullOrEmpty(spriteKey) && lay.FindSlotTemplate(spriteKey) != null)
            return "同一张图「" + (s.sprite != null ? s.sprite.name : "?") + "」（换句对话也照用）";

        string roleKey = DialogueLayout.BuildRoleKey(mgr.CurrentSpeaker, mgr.IsPortraitLeft(_selPortrait));
        if (!string.IsNullOrEmpty(roleKey) && lay.FindSlotTemplate(roleKey) != null)
            return "同一个角色「" + mgr.CurrentSpeaker + "」";

        return "布局默认值（还没存成模板）";
    }

    // ------------------------------------------------------------------ 重置 / 打印

    private DialogueLayout Defaults()
    {
        if (_defaults == null) _defaults = DialogueLayout.CreateDefaultsTemplate();
        return _defaults;
    }

    private void ResetSelected(DialogueLayout lay, DialogueManager mgr)
    {
        DialogueLayout d = Defaults();

        switch (_selKind)
        {
            case Kind.Frame:
                lay.panelLeft = d.panelLeft;
                lay.panelRight = d.panelRight;
                lay.panelBottom = d.panelBottom;
                lay.panelHeight = d.panelHeight;
                break;

            case Kind.Name:
                lay.nameX = d.nameX;
                lay.nameY = d.nameY;
                lay.nameWidth = d.nameWidth;
                lay.nameHeight = d.nameHeight;
                lay.nameFontSize = d.nameFontSize;
                break;

            case Kind.Body:
                lay.contentLeft = d.contentLeft;
                lay.contentRight = d.contentRight;
                lay.contentTop = d.contentTop;
                lay.contentBottom = d.contentBottom;
                lay.bodyOffsetX = 0f;
                lay.bodyOffsetY = 0f;
                lay.bodyFontSize = d.bodyFontSize;
                break;

            case Kind.Hint:
                lay.hintX = d.hintX;
                lay.hintY = d.hintY;
                lay.hintWidth = d.hintWidth;
                lay.hintHeight = d.hintHeight;
                lay.hintFontSize = d.hintFontSize;
                break;

            case Kind.Portrait:
                DialoguePortraitSlot s = mgr != null ? mgr.PortraitSlotAt(_selPortrait) : null;
                if (s != null) s.ResetTransform();

                // 位置记录 / 模板也删掉，不然下一句一显示又会被套回来
                if (mgr != null && lay != null)
                {
                    string key = mgr.SlotKey(_selPortrait);
                    if (!string.IsNullOrEmpty(key)) lay.RemoveSlotOverride(key);

                    string spriteKey = mgr.SpriteTemplateKey(_selPortrait);
                    if (!string.IsNullOrEmpty(spriteKey)) lay.RemoveSlotTemplate(spriteKey);

                    string roleKey = mgr.RoleTemplateKey(_selPortrait);
                    if (!string.IsNullOrEmpty(roleKey)) lay.RemoveSlotTemplate(roleKey);
                }
                break;
        }

        if (mgr != null) mgr.ApplyUiMetrics();
        Commit(mgr, _selKind, _selPortrait);
    }

    private static void PrintValues(DialogueLayout lay, DialogueManager mgr)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("======== 当前对话布局数值（像素，1920x1080 基准）========");
        sb.AppendLine("框体： 左距 " + lay.panelLeft.ToString("0") + "  右距 " + lay.panelRight.ToString("0") +
                      "  下距 " + lay.panelBottom.ToString("0") + "  高度 " + lay.panelHeight.ToString("0"));
        sb.AppendLine("文字区留白： 左 " + lay.contentLeft.ToString("0") + "  右 " + lay.contentRight.ToString("0") +
                      "  上 " + lay.contentTop.ToString("0") + "  下 " + lay.contentBottom.ToString("0") +
                      "  正文偏移 x " + lay.bodyOffsetX.ToString("0") + "  y " + lay.bodyOffsetY.ToString("0"));
        sb.AppendLine("名字： x=" + lay.nameX.ToString("0") + " y=" + lay.nameY.ToString("0") +
                      " 字号 " + lay.nameFontSize.ToString("0") + " 框 " + lay.nameWidth.ToString("0") + "x" + lay.nameHeight.ToString("0") +
                      " 字体 " + FontLabel(lay.nameFontName));
        sb.AppendLine("正文： 字号 " + lay.bodyFontSize.ToString("0") + "  字体 " + FontLabel(lay.bodyFontName));
        sb.AppendLine("提示： x=" + lay.hintX.ToString("0") + " y=" + lay.hintY.ToString("0") +
                      " 字号 " + lay.hintFontSize.ToString("0") + "  字体 " + FontLabel(lay.hintFontName));
        sb.AppendLine("描边： " + (lay.outlineEnabled ? "开" : "关") + "  粗细 " + lay.outlineDistance.x.ToString("0.0") +
                      "  颜色 " + ColorToText(lay.outlineColor));
        sb.AppendLine("颜色： 名字 " + ColorToText(lay.nameColor) + "  正文 " + ColorToText(lay.textColor) +
                      "  提示 " + ColorToText(lay.hintColor));
        sb.AppendLine("立绘默认： 宽 " + lay.portraitWidth.ToString("0") + "  高 " + lay.portraitHeight.ToString("0") +
                      "  内边距 " + lay.portraitInset.ToString("0") + "  下沉 " + lay.portraitSink.ToString("0") +
                      "  默认边 " + lay.defaultSide.ToString());

        if (lay.slotTemplates != null)
        {
            sb.AppendLine("--- 立绘位置模板 " + lay.slotTemplates.Count.ToString() + " 条（图 / 角色通用）---");
            for (int i = 0; i < lay.slotTemplates.Count; i++)
            {
                DialogueLayout.DialogueSlotTemplate t = lay.slotTemplates[i];
                if (t == null) continue;
                sb.AppendLine("  " + t.key + "： x=" + t.offsetX.ToString("0") + " y=" + t.offsetY.ToString("0") +
                              " 宽=" + t.width.ToString("0") + " 高=" + t.height.ToString("0"));
            }
        }

        if (mgr != null)
        {
            for (int i = 0; i < mgr.PortraitCount; i++)
            {
                DialoguePortraitSlot s = mgr.PortraitSlotAt(i);
                if (s == null) continue;
                sb.AppendLine("立绘 #" + i.ToString() + "： offsetX=" + s.offsetX.ToString("0") +
                              " offsetY=" + s.offsetY.ToString("0") +
                              " 高=" + s.height.ToString("0") + " 宽=" + s.width.ToString("0") +
                              " 翻转=" + s.flipX.ToString() + " 层序=" + s.order.ToString());
            }
        }

        sb.AppendLine("------------------------------------------------------");
        sb.AppendLine("这些数值可以直接在 Resources/Dialogue/DialogueLayout.asset 的 Inspector 里改。");
        Debug.Log(sb.ToString());
    }

    private static string ColorToText(Color c)
    {
        return "(" + ((int)(c.r * 255f)).ToString() + "," + ((int)(c.g * 255f)).ToString() + "," +
               ((int)(c.b * 255f)).ToString() + ")";
    }
}
