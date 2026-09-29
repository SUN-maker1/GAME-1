using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 对话管理器 —— 一个全局单例，负责把对话显示在屏幕下方。
///
/// 【怎么用】
///   什么都不用摆：第一次调用时它会自己建一个 Canvas（代码拼出来的），
///   做完 DontDestroyOnLoad，所以切场景也还在。
///
///   代码里触发对话：
///     DialogueManager.Show(lines, "老李");
///   一般不用自己调 —— NPCInteractable（挂在 NPC 身上）会在你按 E 的时候调。
///
/// 【对话期间】
///   SceneTransition.InputLocked 会被置成 true，人物不再移动、鼠标左键也不会挥刀；
///   对话结束自动解锁。按 E / 空格 / 回车 / 鼠标左键推进；正在一个字一个字往外蹦时
///   按一下会先把整句显示完，再按一下才翻到下一句。
/// </summary>
[DefaultExecutionOrder(50)]
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("打字机")]
    [Tooltip("每秒蹦出多少字。0 = 立刻整句显示")]
    public float charsPerSecond = 34f;

    [Header("布局（可视化拖拽 / 所有 NPC 共用一套）")]
    [Tooltip("留空 = 自动去 Resources/Dialogue/DialogueLayout 找。\n" +
             "有这份资源时，下面「外观」里的数值只作为没有任何布局资源时的兜底。\n" +
             "改布局最省事的办法：Play 模式下按 F7，鼠标拖 + 滚轮缩放。")]
    public DialogueLayout layout;

    [Header("外观（没有布局资源时的兜底数值）")]
    public float panelHeight = 280f;
    public float panelBottomMargin = 70f;
    public float panelSideMargin = 140f;
    public Color frameColor = new Color(0.86f, 0.79f, 0.56f, 1f);
    public Color panelColor = new Color(0.10f, 0.08f, 0.07f, 0.94f);
    public Color nameColor = new Color(1f, 0.87f, 0.45f, 1f);
    public Color textColor = new Color(0.97f, 0.95f, 0.90f, 1f);
    public Color hintColor = new Color(0.80f, 0.76f, 0.68f, 0.9f);
    public int nameFontSize = 34;
    public int bodyFontSize = 34;
    public int hintFontSize = 26;

    [Header("对话框样式（九宫格贴片，自动从 Resources/UI 加载）")]
    [Tooltip("留空 = 自动加载。可选：wood（木牌风）/ stone（石板）/ dark（纯黑）\n" +
             "用菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 切换对话框样式 一键换，会自动记住选择。")]
    public Sprite boxSprite;

    [Header("人物立绘（自己画的半身像）")]
    [Tooltip("立绘默认画在对话框的哪一侧；每一句还能单独覆盖")]
    public DialoguePortraitSide defaultPortraitSide = DialoguePortraitSide.Left;

    [Tooltip("立绘显示高度（参考分辨率 1920x1080 下的像素）。\n" +
             "立绘底边坐在对话框上沿，向上露出上半身 —— 这就是「对话框上有立绘」的效果。")]
    public float portraitHeight = 520f;

    [Tooltip("立绘容器宽度；图按原始比例缩放后居中放进去（不会变形）")]
    public float portraitWidth = 420f;

    [Tooltip("立绘离对话框框边的水平内边距")]
    public float portraitInset = 24f;

    [Tooltip("立绘底边往对话框里压多少像素。0 = 正好坐在框上沿；想让脚踩进框里就给个正值")]
    public float portraitSink = 0f;

    [Header("推进按键（任意一个都行）")]
    public KeyCode[] advanceKeys = { KeyCode.E, KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter };
    [Tooltip("鼠标左键也能推进对话")]
    public bool mouseAdvance = true;

    [Header("表情切换")]
    [Tooltip("手动切换立绘表情的按键（只切表情，不推进对话）。\n" +
             "推进键（E 等）在当前句还有没换过的表情时会先换表情，换完才翻页 —— 每一句可以用\n" +
             "「Expression On Advance」开关单独关掉（关掉后按推进键直接翻页）。")]
    public KeyCode expressionKey = KeyCode.Q;

    [Header("调试")]
    public bool verboseLog = true;

    /// <summary>样式选择存在 PlayerPrefs 里，换一次就一直记住</summary>
    public const string StylePrefKey = "FarmRPG.DialogueBoxStyle";
    private const string StyleResourcePrefix = "UI/dialogue_box_";
    private const string LayoutResourcePath = "Dialogue/DialogueLayout";

    private Canvas _canvas;
    private Text _nameText;
    private Text _bodyText;
    private Text _hintText;
    private Image _frame;
    private Image _panel;
    private GameObject _panelRoot;

    /// <summary>立绘使用的 Image 池，按需要动态增加（一句能放好几个立绘）</summary>
    private readonly List<Image> _portraits = new List<Image>();
    private readonly List<int> _drawOrder = new List<int>();
    private List<DialoguePortraitSlot> _activeSlots = EmptySlotList;
    private static readonly List<DialoguePortraitSlot> EmptySlotList = new List<DialoguePortraitSlot>();

    private bool _layoutTried;
    private float _bodyLeft = 60f;
    private float _bodyRight = 60f;
    private string _sourceAssetName = "";

    private IList<DialogueLine> _lines;
    private int _index;
    private string _fallbackSpeaker;
    private string _lastSpeaker;
    private Action _onEnd;

    private bool _isOpen;
    private bool _uiReady;
    private float _typeTimer;
    private int _shownChars;
    private int _openFrame;
    private int _expressionIndex;   // 当前句的表情序号（0 = 默认表情）

    /// <summary>当前是不是正在对话（NPC 靠它避免重复触发）</summary>
    public static bool IsOpen => Instance != null && Instance._isOpen;

    // ------------------------------------------------------------------ 单例

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // 场上已经有了一个（比如切场景时又加载了一个），多余的撤掉。
            // 只删组件不删物体：万一有人把它和别的脚本挂在同一个物体上，
            // Destroy(gameObject) 会把人家一起干掉。
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUi();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    /// <summary>切场景时把对话收掉，免得 UI 留在屏幕上下不来</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_isOpen)
        {
            if (verboseLog)
                Debug.Log("[DialogueManager] 切场景，对话强制结束。", this);
            Finish(false);
        }
    }

    /// <summary>拿到管理器（没有就创建一个）</summary>
    public static DialogueManager Ensure()
    {
        if (Instance != null)
        {
            if (!Instance._uiReady) Instance.BuildUi();
            return Instance;
        }

        DialogueManager found = FindObjectOfType<DialogueManager>();
        if (found == null)
        {
            GameObject go = new GameObject("DialogueManager (Auto)");
            found = go.AddComponent<DialogueManager>();  // Awake 里会 DontDestroyOnLoad + BuildUi
        }

        Instance = found;
        if (!found._uiReady) found.BuildUi();
        return found;
    }

    /// <summary>
    /// 打开一段对话。
    /// </summary>
    /// <param name="lines">要播的句子</param>
    /// <param name="defaultSpeaker">句子里没写名字时用这个名字</param>
    /// <param name="onEnd">全部播完后回调（可空）</param>
    public static void Show(IList<DialogueLine> lines, string defaultSpeaker = "", Action onEnd = null)
    {
        if (lines == null || lines.Count == 0)
        {
            Debug.LogWarning("[DialogueManager] 传进来的对话是空的，没东西可播。");
            onEnd?.Invoke();
            return;
        }

        Ensure().StartDialogue(lines, defaultSpeaker, onEnd);
    }

    /// <summary>供测试和其它脚本调用：直接播几句话</summary>
    public static void Show(params string[] texts)
    {
        List<DialogueLine> lines = new List<DialogueLine>();
        foreach (string s in texts)
            lines.Add(new DialogueLine { text = s });
        Show(lines, "");
    }

    // ------------------------------------------------------------------ 主流程

    /// <summary>（实例方法）开始一段对话</summary>
    public void StartDialogue(IList<DialogueLine> lines, string defaultSpeaker = "", Action onEnd = null)
    {
        if (!_uiReady) BuildUi();

        _lines = lines;
        _fallbackSpeaker = defaultSpeaker ?? "";
        _lastSpeaker = _fallbackSpeaker;
        _onEnd = onEnd;

        _index = 0;
        _isOpen = true;
        _openFrame = Time.frameCount;
        _sourceAssetName = FindSourceAssetName(lines);

        SceneTransition.InputLocked = true;

        if (_canvas != null) _canvas.enabled = true;
        if (_panelRoot != null) _panelRoot.SetActive(true);

        ShowLine(0);

        if (verboseLog)
            Debug.Log($"[DialogueManager] 开始对话：{lines.Count} 句，说话人「{_fallbackSpeaker}」", this);
    }

    private void Update()
    {
        if (!_isOpen) return;

        // 布局每帧写一次：Play 模式里拖 / 缩放时能立刻看到变化
        ApplyUiMetrics();

        // 调节模式下把手腾出来 —— 不推进对话，鼠标留给拖拽用
        if (DialogueLayoutTuner.IsTuning) return;

        string full = CurrentLineText();

        // ---- 打字机 ----
        if (_shownChars < full.Length)
        {
            if (charsPerSecond <= 0f)
            {
                _shownChars = full.Length;
            }
            else
            {
                _typeTimer += Time.deltaTime * charsPerSecond;
                int n = Mathf.Clamp((int)_typeTimer, 0, full.Length);
                if (n != _shownChars) _shownChars = n;
            }
            _bodyText.text = full.Substring(0, _shownChars);
        }

        // 打开对话的那一帧不吃输入：NPC 也是在这一帧按下的 E，
        // 否则会「刚打开就立刻跳过第一句」。
        if (Time.frameCount <= _openFrame) return;

        // ---- 手动切表情（Q）：只换表情，不推进对话 ----
        if (Input.GetKeyDown(expressionKey) && MaxExpressionIndex > 0)
        {
            CycleExpression();
            return;
        }

        if (!AdvancePressed()) return;

        if (_shownChars < full.Length)
        {
            // 还在蹦字 —— 先把整句显示出来，不翻页
            _shownChars = full.Length;
            _bodyText.text = full;
            return;
        }

        // ---- 这句还有没换过的表情，且允许用推进键换：先换表情，不翻页 ----
        DialogueLine cur = _index >= 0 && _index < _lines.Count ? _lines[_index] : null;
        if (cur != null && cur.expressionOnAdvance && _expressionIndex < MaxExpressionIndex)
        {
            CycleExpression();
            return;
        }

        if (_index + 1 < _lines.Count)
        {
            ShowLine(_index + 1);
        }
        else
        {
            Finish(true);
        }
    }

    private bool AdvancePressed()
    {
        if (advanceKeys != null)
        {
            foreach (KeyCode k in advanceKeys)
            {
                if (Input.GetKeyDown(k)) return true;
            }
        }
        return mouseAdvance && Input.GetMouseButtonDown(0);
    }

    private string CurrentLineText()
    {
        if (_lines == null || _index < 0 || _index >= _lines.Count) return "";
        DialogueLine line = _lines[_index];
        return line != null ? line.text ?? "" : "";
    }

    /// <summary>显示第 i 句</summary>
    private void ShowLine(int i)
    {
        _index = i;
        DialogueLine line = _lines[i];

        string speaker = string.IsNullOrEmpty(line.speakerName) ? _lastSpeaker : line.speakerName;
        _lastSpeaker = speaker;

        if (_nameText != null)
        {
            _nameText.text = speaker ?? "";
            _nameText.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
        }

        // ---- 立绘：一句可以放好几个 ----
        _activeSlots = line != null ? line.GetPortraitSlots() : EmptySlotList;
        ApplySlotOverrides();       // 把 F7 拖好的位置套回来（位置存在布局资源里，不写在台词里）
        ShowPortraits();

        // ---- 外观：这一句自带了「外观预设」就整套套上（字体 / 颜色 / 大小 / 框都换了）----
        //      没带就用布局资源里的默认样子
        string lineStyle = "";
        if (line != null && line.preset != null && line.preset.look != null)
            lineStyle = line.preset.look.boxStyle;
        if (string.IsNullOrEmpty(lineStyle) && line != null)
            lineStyle = line.boxStyle;              // 兼容只在「boxStyle」里写了样式名的老写法

        ApplyBoxSprite(!string.IsNullOrEmpty(lineStyle) ? lineStyle : CurrentStyle, savePref: false);

        ReadMetrics(out DialogueUiMetrics m);
        ComputeBodyMargins(m);
        ApplyMetrics(m);

        _typeTimer = 0f;
        _shownChars = 0;
        _expressionIndex = 0;
        if (_bodyText != null) _bodyText.text = "";

        UpdateHint();
    }

    /// <summary>这段台词属于谁（对话资源名；写在 NPC 身上的就是 NPC 名字）</summary>
    public string OwnerId => string.IsNullOrEmpty(_sourceAssetName) ? _fallbackSpeaker : _sourceAssetName;

    /// <summary>某个立绘在这一句里的位置键：「谁#第几句#第几个立绘」</summary>
    public string SlotKey(int slotIndex)
    {
        return OwnerId + "#" + _index.ToString() + "#" + slotIndex.ToString();
    }

    /// <summary>当前这句显示出来的说话人（这句没写名字时，会显示上一句那个人）</summary>
    public string CurrentSpeaker => _lastSpeaker ?? "";

    /// <summary>第 i 句实际会显示的说话人（自己没写名字就沿用前面最近写过名字的那句）</summary>
    public string SpeakerOf(int index)
    {
        string speaker = _fallbackSpeaker ?? "";
        if (_lines == null || _lines.Count == 0) return speaker;

        int upTo = Mathf.Clamp(index, -1, _lines.Count - 1);
        for (int i = 0; i <= upTo; i++)
        {
            DialogueLine l = _lines[i];
            if (l == null) continue;
            if (!string.IsNullOrEmpty(l.speakerName)) speaker = l.speakerName;
        }
        return speaker;
    }

    /// <summary>当前这一句有几个立绘</summary>
    public int ActiveSlotCount => _activeSlots != null ? _activeSlots.Count : 0;

    /// <summary>
    /// 把调好的立绘位置套回这一句。
    ///
    /// 【套的顺序 —— 越靠后越优先】
    ///   ① 台词里（Inspector）写的原始值
    ///   ② 「这个角色」的位置模板   key = 角色:&lt;说话人&gt;|左/右
    ///   ③ 「同一张图」的位置模板   key = 图:&lt;图片名&gt;   ← 默认存的那种，最通用
    ///   ④ 只管这一句的逐句记录     key = &lt;来源&gt;#第几句#第几个
    ///
    /// 【为什么不直接改台词里的数据】台词多半写在场景里的 NPC 身上（firstDialogue 列表），
    /// Unity 退出 Play 模式会把场景物体的改动整个回滚 —— 写在那里等于白拖。
    /// 所以位置单独存在「对话布局」资源里（资源文件的改动能落盘），每次显示这句时套回去。
    /// </summary>
    private void ApplySlotOverrides()
    {
        DialogueLayout lay = GetLayout();
        if (lay == null || _activeSlots == null) return;

        ReadMetrics(out DialogueUiMetrics m);

        string speaker = CurrentSpeaker;

        for (int i = 0; i < _activeSlots.Count; i++)
        {
            DialoguePortraitSlot s = _activeSlots[i];
            if (s == null) continue;

            // 先把「台词里本来写的值」留个底：模板 / 记录都是在它之上改，不会越叠越歪
            s.CaptureBaseOnce();

            float ox = s.baseOffsetX;
            float oy = s.baseOffsetY;
            float w = s.baseWidth;
            float h = s.baseHeight;

            // ② 角色模板
            bool left = IsLeft(ResolveSide(s.side, m));
            ApplyTemplate(lay.FindSlotTemplate(DialogueLayout.BuildRoleKey(speaker, left)), ref ox, ref oy, ref w, ref h);

            // ③ 同图模板
            ApplyTemplate(lay.FindSlotTemplate(DialogueLayout.BuildSpriteKey(s.sprite)), ref ox, ref oy, ref w, ref h);

            // ④ 逐句记录
            DialogueLayout.DialogueSlotOverride o = lay.FindSlotOverride(SlotKey(i));
            if (o != null)
            {
                ox = o.offsetX;
                oy = o.offsetY;
                w = o.width;
                h = o.height;
            }

            s.offsetX = ox;
            s.offsetY = oy;
            s.width = w;
            s.height = h;
        }
    }

    private static void ApplyTemplate(DialogueLayout.DialogueSlotTemplate t, ref float ox, ref float oy, ref float w, ref float h)
    {
        if (t == null) return;

        ox = t.offsetX;
        oy = t.offsetY;
        // -1 = 「没特别指定大小」，沿用现在的值
        if (t.width >= 0f) w = t.width;
        if (t.height >= 0f) h = t.height;
    }

    // ------------------------------------------------------------------ 模板

    /// <summary>当前这一句第 i 个立绘「同一张图」的模板 key</summary>
    public string SpriteTemplateKey(int i)
    {
        DialoguePortraitSlot s = PortraitSlotAt(i);
        return s != null ? DialogueLayout.BuildSpriteKey(s.sprite) : "";
    }

    /// <summary>当前这一句第 i 个立绘「同一个角色」的模板 key</summary>
    public string RoleTemplateKey(int i)
    {
        ReadMetrics(out DialogueUiMetrics m);
        DialoguePortraitSlot s = PortraitSlotAt(i);
        bool left = s != null && IsLeft(ResolveSide(s.side, m));
        return DialogueLayout.BuildRoleKey(CurrentSpeaker, left);
    }

    /// <summary>
    /// 把整段对话里调好的立绘位置「推广」成模板 —— 之后写的新对话自动就是这套摆放。
    /// </summary>
    /// <param name="scope">BySprite = 按图（推荐）/ ByRole = 按角色</param>
    /// <param name="clearPerLine">勾上就连「只管这一句」的记录也删掉，以后统一由模板管</param>
    /// <returns>写了几条模板</returns>
    public int PromoteTemplates(DialogueLayout.TemplateScope scope, bool clearPerLine)
    {
        DialogueLayout lay = GetLayout();
        if (lay == null || _lines == null) return 0;

        ReadMetrics(out DialogueUiMetrics m);

        int written = 0;

        for (int i = 0; i < _lines.Count; i++)
        {
            DialogueLine line = _lines[i];
            if (line == null) continue;

            List<DialoguePortraitSlot> slots = line.GetPortraitSlots();
            string speaker = SpeakerOf(i);

            for (int j = 0; j < slots.Count; j++)
            {
                DialoguePortraitSlot s = slots[j];
                if (s == null || s.sprite == null) continue;

                bool left = IsLeft(ResolveSide(s.side, m));
                string key = scope == DialogueLayout.TemplateScope.BySprite
                    ? DialogueLayout.BuildSpriteKey(s.sprite)
                    : DialogueLayout.BuildRoleKey(speaker, left);
                if (string.IsNullOrEmpty(key)) continue;

                // 这一句之前单独拖过就拿「拖过的数值」，没有就用它现在的值
                DialogueLayout.DialogueSlotOverride o = lay.FindSlotOverride(BuildSlotKey(i, j));

                DialogueLayout.DialogueSlotTemplate t = lay.GetOrCreateSlotTemplate(key);
                t.offsetX = o != null ? o.offsetX : s.offsetX;
                t.offsetY = o != null ? o.offsetY : s.offsetY;
                t.width = o != null ? o.width : s.width;
                t.height = o != null ? o.height : s.height;
                written++;

                if (clearPerLine && o != null) lay.RemoveSlotOverride(BuildSlotKey(i, j));
            }
        }

        DialogueLayoutTuner.NotifyLayoutChanged();

        // 把新写的模板立刻套回当前这句（不然要等下一句才看得见变化）
        ApplySlotOverrides();
        ApplyUiMetrics();
        return written;
    }

    /// <summary>「第几句 # 第几个立绘」的位置记录 key（不带来源名，内部拼装用）</summary>
    private string BuildSlotKey(int lineIndex, int slotIndex)
    {
        return OwnerId + "#" + lineIndex.ToString() + "#" + slotIndex.ToString();
    }

    /// <summary>右下角的提示文字：有表情可换时提示换表情，否则提示翻页</summary>
    private void UpdateHint()
    {
        if (_hintText == null) return;

        string key = KeyLabel();
        int remaining = MaxExpressionIndex - _expressionIndex;

        if (remaining > 0)
        {
            _hintText.text = $"按 {key} 换表情（还剩 {remaining} 个）· 按 {ExpressionKeyLabel()} 也能切";
        }
        else
        {
            bool last = _index >= _lines.Count - 1;
            _hintText.text = last ? $"按 {key} 结束" : $"按 {key} 继续…";
        }
    }

    private string ExpressionKeyLabel()
    {
        switch (expressionKey)
        {
            case KeyCode.Space: return "空格";
            case KeyCode.Return:
            case KeyCode.KeypadEnter: return "回车";
            case KeyCode.LeftShift:
            case KeyCode.RightShift: return "Shift";
            default: return expressionKey.ToString();
        }
    }

    // ------------------------------------------------------------------ 立绘表情

    /// <summary>当前句全部立绘里最多有几个表情（0 = 这句没有表情可切）</summary>
    public int MaxExpressionIndex
    {
        get
        {
            int max = 0;
            int count = _activeSlots != null ? _activeSlots.Count : 0;
            for (int i = 0; i < count; i++)
            {
                DialoguePortraitSlot s = _activeSlots[i];
                if (s == null || s.expressions == null) continue;
                max = Mathf.Max(max, s.expressions.Count);
            }
            return max;
        }
    }

    /// <summary>当前表情序号（0 = 默认表情）</summary>
    public int ExpressionIndex => _expressionIndex;

    /// <summary>这个立绘在当前表情序号下该显示哪张图</summary>
    private Sprite DisplaySprite(DialoguePortraitSlot slot)
    {
        if (slot == null) return null;
        if (_expressionIndex <= 0 || slot.expressions == null || slot.expressions.Count == 0)
            return slot.sprite;

        // 表情比别的立绘少时，停在最后一张（不闪回默认脸）
        int i = Mathf.Min(_expressionIndex - 1, slot.expressions.Count - 1);
        Sprite sp = i >= 0 ? slot.expressions[i] : slot.sprite;
        return sp != null ? sp : slot.sprite;
    }

    /// <summary>换到下一个表情。返回有没有真的换（没有更多表情时返回 false）</summary>
    public bool CycleExpression()
    {
        if (_expressionIndex >= MaxExpressionIndex) return false;

        _expressionIndex++;
        ApplyUiMetrics();       // 让立绘立刻换脸（ApplyMetrics 每次都会重刷 sprite）

        if (verboseLog)
            Debug.Log($"[DialogueManager] 表情切换 → {_expressionIndex}/{MaxExpressionIndex}", this);
        return true;
    }

    // ------------------------------------------------------------------ 立绘

    /// <summary>把当前句的所有立绘画到屏幕上（没放图就不显示）</summary>
    private void ShowPortraits()
    {
        ReadMetrics(out DialogueUiMetrics m);

        int count = _activeSlots != null ? _activeSlots.Count : 0;

        // 池子里没有就补：一个槽位对应一张 Image
        for (int i = 0; i < count; i++)
        {
            if (i >= _portraits.Count) CreatePortraitImage();
        }

        // 按层序排：数字大的后画（画在上面）
        _drawOrder.Clear();
        for (int i = 0; i < count; i++) _drawOrder.Add(i);
        if (count > 1)
        {
            _drawOrder.Sort((a, b) =>
            {
                int oa = _activeSlots[a] != null ? _activeSlots[a].order : 0;
                int ob = _activeSlots[b] != null ? _activeSlots[b].order : 0;
                return oa.CompareTo(ob);
            });
        }

        for (int i = 0; i < count; i++)
        {
            Image img = _portraits[i];
            if (img != null) img.gameObject.SetActive(false);
        }
        for (int i = count; i < _portraits.Count; i++)
        {
            Image img = _portraits[i];
            if (img != null) img.gameObject.SetActive(false);
        }

        for (int n = 0; n < _drawOrder.Count; n++)
        {
            int idx = _drawOrder[n];
            DialoguePortraitSlot slot = _activeSlots[idx];
            Image img = idx < _portraits.Count ? _portraits[idx] : null;
            if (img == null) continue;

            if (slot == null || DisplaySprite(slot) == null)
            {
                img.gameObject.SetActive(false);
                continue;
            }

            ApplyPortraitSlot(img, slot, m);
            img.gameObject.SetActive(true);
            img.transform.SetAsLastSibling();   // 后画的压在上面
        }
    }

    private void CreatePortraitImage()
    {
        Transform root = UiRoot;
        if (root == null) return;

        Image img = UiKit.MakeImage("Portrait_" + _portraits.Count.ToString(), root, Color.white);
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.gameObject.SetActive(false);
        _portraits.Add(img);
    }

    /// <summary>把一个立绘槽位摆到对话框某一侧：底边坐在框上沿，向上露出上半身</summary>
    private void ApplyPortraitSlot(Image img, DialoguePortraitSlot slot, DialogueUiMetrics m)
    {
        if (img == null || slot == null) return;

        bool left = IsLeft(ResolveSide(slot.side, m));
        RectTransform r = img.rectTransform;

        r.anchorMin = new Vector2(left ? 0f : 1f, 0f);
        r.anchorMax = new Vector2(left ? 0f : 1f, 0f);
        r.pivot = new Vector2(left ? 0f : 1f, 0f);

        float w = slot.width > 0f ? slot.width : m.portraitWidth;
        float h = slot.height > 0f ? slot.height : m.portraitHeight;
        r.sizeDelta = new Vector2(w, h);

        r.anchoredPosition = new Vector2(
            (left ? 1f : -1f) * (m.left + m.portraitInset + slot.offsetX),
            m.bottom + m.height - m.portraitSink + slot.offsetY);

        img.sprite = DisplaySprite(slot);
        img.color = slot.tint;
        img.transform.localScale = new Vector3(slot.flipX ? -1f : 1f, 1f, 1f);
    }

    /// <summary>算出正文两边要让出多少空间给立绘</summary>
    private void ComputeBodyMargins(DialogueUiMetrics m)
    {
        float leftReserve = 0f;
        float rightReserve = 0f;
        int count = _activeSlots != null ? _activeSlots.Count : 0;

        for (int i = 0; i < count; i++)
        {
            DialoguePortraitSlot s = _activeSlots[i];
            if (s == null || DisplaySprite(s) == null) continue;

            float w = s.width > 0f ? s.width : m.portraitWidth;
            float take = m.portraitInset + w + 30f;

            if (IsLeft(ResolveSide(s.side, m)))
                leftReserve = Mathf.Max(leftReserve, take);
            else
                rightReserve = Mathf.Max(rightReserve, take);
        }

        _bodyLeft = m.contentLeft + leftReserve;
        _bodyRight = m.contentRight + rightReserve;
    }

    /// <summary>对话结束（normalEnd = 正常播完；false = 被切场景打断，不触发回调）</summary>
    private void Finish(bool normalEnd)
    {
        _isOpen = false;
        SceneTransition.InputLocked = false;

        if (_panelRoot != null) _panelRoot.SetActive(false);
        if (_canvas != null) _canvas.enabled = false;

        Action cb = _onEnd;
        _onEnd = null;
        _lines = null;

        if (normalEnd)
        {
            if (verboseLog) Debug.Log("[DialogueManager] 对话结束。", this);
            cb?.Invoke();
        }
    }

    private string KeyLabel()
    {
        if (advanceKeys == null || advanceKeys.Length == 0) return "E";
        switch (advanceKeys[0])
        {
            case KeyCode.Space: return "空格";
            case KeyCode.Return:
            case KeyCode.KeypadEnter: return "回车";
            default: return advanceKeys[0].ToString();
        }
    }

    // ------------------------------------------------------------------ 搭 UI

    /// <summary>用代码拼出整个对话框（工程里不需要预先摆 Canvas）</summary>
    private void BuildUi()
    {
        if (_uiReady) return;

        // ---- Canvas ----
        GameObject canvasGo = new GameObject("DialogueCanvas");
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9000;
        _canvas.pixelPerfect = false;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 没有 EventSystem 就建一个（以后往对话框里放按钮要用）
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // ---- 外框（贴片自带边框时才需要它：纯色底时它就是描边）----
        // 先建后建决定谁画在上面：外框要在底板前面（后面 = 被盖住）
        Image frame = UiKit.MakeImage("Frame", canvasGo.transform, frameColor);
        _frame = frame;

        // ---- 底板 ----
        Image panel = UiKit.MakeImage("Panel", canvasGo.transform, panelColor);
        _panelRoot = panel.gameObject;
        _panel = panel;

        // ---- 框体贴片（九宫格，边框不会被拉伸变形）----
        Sprite box = ResolveBoxSprite();
        if (box != null)
        {
            panel.sprite = box;
            panel.type = Image.Type.Sliced;
            panel.color = Color.white;
            frame.gameObject.SetActive(false);      // 贴片自带边框，外框退场
        }

        // ---- 名字 ----
        _nameText = UiKit.MakeText("Name", panel.transform, nameFontSize, nameColor);
        _nameText.alignment = TextAnchor.MiddleLeft;

        // ---- 正文 ----
        _bodyText = UiKit.MakeText("Body", panel.transform, bodyFontSize, textColor);
        _bodyText.alignment = TextAnchor.UpperLeft;

        // ---- 右下角提示 ----
        _hintText = UiKit.MakeText("Hint", panel.transform, hintFontSize, hintColor);
        _hintText.alignment = TextAnchor.MiddleRight;

        // ---- 布局调节器（Play 模式按 F7 就能拖着调）----
        DialogueLayoutTuner tuner = GetComponent<DialogueLayoutTuner>();
        if (tuner == null) gameObject.AddComponent<DialogueLayoutTuner>();

        ApplyUiMetrics();

        // 初始隐藏
        _panelRoot.SetActive(false);
        _canvas.enabled = false;

        _uiReady = true;
    }

    // ------------------------------------------------------------------ 布局数值

    /// <summary>一次布局的全部数值（像素，参考分辨率 1920x1080）</summary>
    public struct DialogueUiMetrics
    {
        public float left, right, bottom, height;
        public float contentLeft, contentRight, contentTop, contentBottom;
        public float bodyOffX, bodyOffY;
        public float nameX, nameY, nameW, nameH, nameSize;
        public float hintX, hintY, hintW, hintH, hintSize;
        public float bodySize;
        public float portraitWidth, portraitHeight, portraitInset, portraitSink;
        public DialoguePortraitSide defaultSide;
        public Color nameColor, textColor, hintColor;

        /// <summary>三处文字各自用什么字体（null = 用 UiKit 自动挑的那一个）</summary>
        public Font nameFont, bodyFont, hintFont;

        /// <summary>描边：开关 + 颜色 + 偏移</summary>
        public bool outline;
        public Color outlineColor;
        public Vector2 outlineDistance;
    }

    /// <summary>当前真正生效的布局资源（可能为 null —— 那时用 Inspector 上的数值）</summary>
    public DialogueLayout LayoutTarget => GetLayout();

    public Transform UiRoot
    {
        get
        {
            if (_canvas != null) return _canvas.transform;
            return transform;
        }
    }

    public Canvas ActiveCanvas => _canvas;

    public RectTransform PanelRect => _panel != null ? _panel.rectTransform : null;
    public RectTransform FrameRect => _frame != null ? _frame.rectTransform : null;
    public RectTransform NameRect => _nameText != null ? _nameText.rectTransform : null;
    public RectTransform BodyRect => _bodyText != null ? _bodyText.rectTransform : null;
    public RectTransform HintRect => _hintText != null ? _hintText.rectTransform : null;
    public string SourceAssetName => _sourceAssetName;

    public int PortraitCount => _portraits.Count;

    public RectTransform PortraitRectAt(int i)
    {
        if (i < 0 || i >= _portraits.Count) return null;
        Image img = _portraits[i];
        return img != null ? img.rectTransform : null;
    }

    public bool IsPortraitVisible(int i)
    {
        if (i < 0 || i >= _portraits.Count) return false;
        Image img = _portraits[i];
        return img != null && img.gameObject.activeSelf;
    }

    public DialoguePortraitSlot PortraitSlotAt(int i)
    {
        if (_activeSlots == null) return null;
        if (i < 0 || i >= _activeSlots.Count) return null;
        return _activeSlots[i];
    }

    /// <summary>第 i 个立绘是不是画在左边（调节拖拽方向要用到）</summary>
    public bool IsPortraitLeft(int i)
    {
        DialoguePortraitSlot s = PortraitSlotAt(i);
        if (s == null) return true;
        ReadMetrics(out DialogueUiMetrics m);
        return IsLeft(ResolveSide(s.side, m));
    }

    /// <summary>拿当前生效的布局资源（Inspector 上没拖就去 Resources 里找）</summary>
    private DialogueLayout GetLayout()
    {
        if (layout != null) return layout;

        if (!_layoutTried)
        {
            _layoutTried = true;
            layout = Resources.Load<DialogueLayout>(LayoutResourcePath);
            if (layout == null && verboseLog)
                Debug.Log("[DialogueManager] 没找到布局资源 Resources/" + LayoutResourcePath + ".asset，" +
                          "先用 Inspector 上的数值。菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 打开对话布局资源 可以建一份。", this);
        }
        return layout;
    }

    /// <summary>
    /// 读出当前该用的布局数值。
    /// 顺序是：布局资源 → 这一句自带的外观预设（写了 preset 的话把字体颜色那些盖上去）。
    /// </summary>
    private void ReadMetrics(out DialogueUiMetrics m)
    {
        ReadLayoutMetrics(out m);
        ApplyLineLook(ref m);
    }

    /// <summary>光是「对话布局」资源里的数值（不管这一句有没有自带预设）</summary>
    private void ReadLayoutMetrics(out DialogueUiMetrics m)
    {
        m = new DialogueUiMetrics();
        DialogueLayout lay = GetLayout();

        if (lay != null)
        {
            m.left = lay.panelLeft;
            m.right = lay.panelRight;
            m.bottom = lay.panelBottom;
            m.height = lay.panelHeight;

            m.contentLeft = lay.contentLeft;
            m.contentRight = lay.contentRight;
            m.contentTop = lay.contentTop;
            m.contentBottom = lay.contentBottom;

            m.bodyOffX = lay.bodyOffsetX;
            m.bodyOffY = lay.bodyOffsetY;

            m.nameX = lay.nameX;
            m.nameY = lay.nameY;
            m.nameW = lay.nameWidth;
            m.nameH = lay.nameHeight;
            m.nameSize = lay.nameFontSize;

            m.hintX = lay.hintX;
            m.hintY = lay.hintY;
            m.hintW = lay.hintWidth;
            m.hintH = lay.hintHeight;
            m.hintSize = lay.hintFontSize;

            m.bodySize = lay.bodyFontSize;

            m.portraitWidth = lay.portraitWidth;
            m.portraitHeight = lay.portraitHeight;
            m.portraitInset = lay.portraitInset;
            m.portraitSink = lay.portraitSink;
            m.defaultSide = lay.defaultSide;

            m.nameColor = lay.nameColor;
            m.textColor = lay.textColor;
            m.hintColor = lay.hintColor;

            m.nameFont = DialogueFontLibrary.Resolve(lay.nameFontName);
            m.bodyFont = DialogueFontLibrary.Resolve(lay.bodyFontName);
            m.hintFont = DialogueFontLibrary.Resolve(lay.hintFontName);

            m.outline = lay.outlineEnabled;
            m.outlineColor = lay.outlineColor;
            m.outlineDistance = lay.outlineDistance;
            return;
        }

        // 没有布局资源 —— 用 Inspector 上的老参数兜底
        m.left = panelSideMargin;
        m.right = panelSideMargin;
        m.bottom = panelBottomMargin;
        m.height = panelHeight;

        m.contentLeft = 60f;
        m.contentRight = 60f;
        m.contentTop = 100f;
        m.contentBottom = 80f;

        m.nameX = 60f;
        m.nameY = 40f;
        m.nameW = 700f;
        m.nameH = 60f;
        m.nameSize = nameFontSize;

        m.hintX = 60f;
        m.hintY = 30f;
        m.hintW = 500f;
        m.hintH = 50f;
        m.hintSize = hintFontSize;

        m.bodySize = bodyFontSize;

        m.portraitWidth = portraitWidth;
        m.portraitHeight = portraitHeight;
        m.portraitInset = portraitInset;
        m.portraitSink = portraitSink;
        m.defaultSide = defaultPortraitSide;

        m.nameColor = nameColor;
        m.textColor = textColor;
        m.hintColor = hintColor;

        m.nameFont = null;
        m.bodyFont = null;
        m.hintFont = null;
        m.outline = true;
        m.outlineColor = new Color(0f, 0f, 0f, 0.85f);
        m.outlineDistance = new Vector2(1.5f, -1.5f);
    }

    /// <summary>
    /// 某一句自带了外观预设（DialogueLine.preset）时，把它的字体 / 颜色 / 字号盖到本帧数值上。
    /// 只影响这一句的显示，不去改「对话布局」资源 —— 下一句没指定就自动还原。
    /// </summary>
    private void ApplyLineLook(ref DialogueUiMetrics m)
    {
        DialogueLine line = CurrentLine;
        if (line == null || line.preset == null) return;
        if (line.preset.look == null) return;

        line.preset.look.ApplyToMetrics(ref m);
    }

    /// <summary>当前正在播的那一句（没在对话就返回 null）</summary>
    private DialogueLine CurrentLine
    {
        get
        {
            if (!_isOpen || _lines == null) return null;
            if (_index < 0 || _index >= _lines.Count) return null;
            return _lines[_index];
        }
    }

    /// <summary>把布局数值写到 UI 上（拖拽时实时跟着变）</summary>
    public void ApplyUiMetrics()
    {
        ReadMetrics(out DialogueUiMetrics m);
        ApplyMetrics(m);
    }

    private void ApplyMetrics(DialogueUiMetrics m)
    {
        if (_panel != null)
        {
            RectTransform r = _panel.rectTransform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.offsetMin = new Vector2(m.left, m.bottom);
            r.offsetMax = new Vector2(-m.right, m.bottom + m.height);
        }

        if (_frame != null)
        {
            RectTransform f = _frame.rectTransform;
            f.anchorMin = new Vector2(0f, 0f);
            f.anchorMax = new Vector2(1f, 0f);
            f.pivot = new Vector2(0.5f, 0f);
            f.offsetMin = new Vector2(m.left - 8f, m.bottom - 8f);
            f.offsetMax = new Vector2(-(m.right - 8f), m.bottom + m.height + 8f);
        }

        if (_nameText != null)
        {
            RectTransform r = _nameText.rectTransform;
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(m.nameW, m.nameH);
            r.anchoredPosition = new Vector2(m.nameX, -m.nameY);

            ApplyTextStyle(_nameText, m.nameFont, m.nameSize, m.nameColor, m);
        }

        if (_bodyText != null)
        {
            // 四边留白定「文字框」的大小和基准位置，bodyOff 是整体平移 ——
            // 拖动时框的大小不变，所以能随便挪，不会被框越拖越小
            UiKit.Stretch(_bodyText.rectTransform,
                          _bodyLeft + m.bodyOffX,
                          m.contentTop - m.bodyOffY,
                          _bodyRight - m.bodyOffX,
                          m.contentBottom + m.bodyOffY);
            ApplyTextStyle(_bodyText, m.bodyFont, m.bodySize, m.textColor, m);
        }

        if (_hintText != null)
        {
            RectTransform r = _hintText.rectTransform;
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.sizeDelta = new Vector2(m.hintW, m.hintH);
            r.anchoredPosition = new Vector2(-m.hintX, m.hintY);

            ApplyTextStyle(_hintText, m.hintFont, m.hintSize, m.hintColor, m);
        }

        // 立绘跟着新数值走（个数和 _activeSlots 对齐）
        int count = _activeSlots != null ? _activeSlots.Count : 0;
        for (int i = 0; i < _portraits.Count; i++)
        {
            Image img = _portraits[i];
            if (img == null || !img.gameObject.activeSelf) continue;

            DialoguePortraitSlot s = i < count ? _activeSlots[i] : PortraitSlotAt(i);
            if (s != null) ApplyPortraitSlot(img, s, m);
        }
    }

    /// <summary>
    /// 把字体 / 字号 / 颜色 / 描边写到一段文字上。
    /// 字体为 null 时用 UiKit 自动挑的中文字体（系统里的雅黑 / 黑体，不然中文全是方块）。
    /// </summary>
    private static void ApplyTextStyle(Text t, Font font, float fontSize, Color color, DialogueUiMetrics m)
    {
        if (t == null) return;

        Font want = font != null ? font : UiKit.CjkFont(Mathf.RoundToInt(fontSize));
        if (want != null && t.font != want) t.font = want;

        int fs = Mathf.RoundToInt(fontSize);
        if (fs > 0 && t.fontSize != fs) t.fontSize = fs;
        t.color = color;

        Outline outline = t.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = m.outline;
            outline.effectColor = m.outlineColor;
            outline.effectDistance = m.outlineDistance;
        }
    }

    private static bool IsLeft(DialoguePortraitSide side)
    {
        return side != DialoguePortraitSide.Right;
    }

    // ------------------------------------------------------------------ 样式 / 立绘

    /// <summary>某一句没指定立绘方向时用默认方向</summary>
    private static DialoguePortraitSide ResolveSide(DialoguePortraitSide perLine, DialogueUiMetrics m)
    {
        return perLine == DialoguePortraitSide.Inherit ? m.defaultSide : perLine;
    }

    /// <summary>当前全局样式名（PlayerPrefs 里记住的那个）</summary>
    public static string CurrentStyle => PlayerPrefs.GetString(StylePrefKey, "wood");

    /// <summary>
    /// 每种样式的「推荐面板高度」。像卷轴这种画布高、纸面居中的图，默认 280 高会把纸压得太扁；
    /// 切到该样式时若当前高度比推荐值矮就抬到推荐值（只抬高不压低，不覆盖用户自己调小的值）。
    /// </summary>
    private static readonly Dictionary<string, float> StyleHeightHint = new Dictionary<string, float>
    {
        { "wood", 280f },
        { "stone", 280f },
        { "dark", 280f },
        { "scroll", 470f },
        { "my_style", 470f },
    };

    /// <summary>取框体贴片：优先 Inspector 拖的，其次 PlayerPrefs 里记住的，最后木牌风</summary>
    private Sprite ResolveBoxSprite()
    {
        if (boxSprite != null) return boxSprite;

        string style = CurrentStyle;
        if (!string.IsNullOrEmpty(style))
        {
            Sprite s = Resources.Load<Sprite>(StyleResourcePrefix + style);
            if (s != null) return s;
        }

        return Resources.Load<Sprite>(StyleResourcePrefix + "wood");
    }

    /// <summary>运行时换框体样式（菜单会调它，改完立刻能看见，并记住为全局样式）</summary>
    public void ApplyStyle(string style)
    {
        PlayerPrefs.SetString(StylePrefKey, style);
        PlayerPrefs.Save();
        ApplyBoxSprite(style, savePref: false);
    }

    /// <summary>
    /// 把某一种样式应用到框体上。savePref=false 时只换当前显示、不动全局记住的样式
    /// （每句自带样式用的就是它，不能一句对话把你的全局选择改掉）。
    /// </summary>
    private void ApplyBoxSprite(string style, bool savePref)
    {
        if (string.IsNullOrEmpty(style)) return;

        Sprite s = Resources.Load<Sprite>(StyleResourcePrefix + style);
        if (s == null)
        {
            Debug.LogWarning($"[DialogueManager] Resources 里找不到 {StyleResourcePrefix}{style}，样式没变。");
            return;
        }

        boxSprite = s;

        // 卷轴这类"画布大、纸面居中"的图需要更高的面板，太矮会把纸面压扁
        if (StyleHeightHint.TryGetValue(style, out float hint) &&
            LayoutTarget != null &&
            LayoutTarget.panelHeight < hint)
        {
            LayoutTarget.panelHeight = hint;
            DialogueLayoutTuner.NotifyLayoutChanged();     // 高度变了也让编辑器存一下
        }

        if (_panel != null)
        {
            _panel.sprite = s;
            _panel.type = Image.Type.Sliced;
            _panel.color = Color.white;
        }
        if (_frame != null) _frame.gameObject.SetActive(false);
        ApplyUiMetrics();
    }

    // ------------------------------------------------------------------ 调试菜单

    /// <summary>
    /// 找出这段对话来自哪个 DialogueAsset（用来提示用户「改的是哪份资源」）。
    /// 运行时拿不到资源路径，只能比较引用 —— 够用了。
    /// </summary>
    private static string FindSourceAssetName(IList<DialogueLine> lines)
    {
        if (lines == null) return "";

        DialogueAsset[] all = Resources.FindObjectsOfTypeAll<DialogueAsset>();
        for (int i = 0; i < all.Length; i++)
        {
            DialogueAsset asset = all[i];
            if (asset != null && asset.lines == lines) return asset.name;
        }
        return "";
    }

    /// <summary>
    /// 调布局用：播一段示例对话。会自动把 Assets/Resources/Portrait 下的图片当立绘用（最多 3 张），
    /// 这样就算 NPC 还没配好也能对着调整位置。
    /// </summary>
    [ContextMenu("播放示例对话（用来调布局，会自动带上 Portrait 目录里的立绘）")]
    public void PlaySampleDialogue()
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>("Portrait");
        int have = sprites != null ? sprites.Length : 0;

        List<DialogueLine> list = new List<DialogueLine>();

        DialogueLine a = new DialogueLine
        {
            speakerName = "示例甲",
            text = "左边这句是示例甲。这句带了表情：按 E 换一张脸，按 Q 随时切，换完再按 E 才翻页。"
        };
        if (have > 0)
        {
            DialoguePortraitSlot sa = new DialoguePortraitSlot { sprite = sprites[0], side = DialoguePortraitSide.Left };
            if (have > 1) sa.expressions.Add(sprites[1]);       // 有第二张图就当表情用
            a.portraits.Add(sa);
        }
        list.Add(a);

        DialogueLine b = new DialogueLine
        {
            speakerName = "示例乙",
            text = "右边这句是示例乙。两人对话可以一句左一句右。",
            expressionOnAdvance = false                          // 这句关掉「按 E 换表情」，按 E 直接翻页
        };
        if (have > 1)
        {
            DialoguePortraitSlot sb = new DialoguePortraitSlot { sprite = sprites[1], side = DialoguePortraitSide.Right };
            if (have > 2) sb.expressions.Add(sprites[2]);
            b.portraits.Add(sb);
        }
        list.Add(b);

        DialogueLine c = new DialogueLine
        {
            speakerName = "旁白",
            text = "第三句演示同一句里放两个立绘 —— 拖其中一个不会影响另一个。"
        };
        if (have > 0) c.portraits.Add(new DialoguePortraitSlot { sprite = sprites[0], side = DialoguePortraitSide.Left, offsetX = 0f });
        if (have > 1)
            c.portraits.Add(new DialoguePortraitSlot { sprite = sprites[1], side = DialoguePortraitSide.Left, offsetX = 300f });
        else if (have > 0)
            c.portraits.Add(new DialoguePortraitSlot { sprite = sprites[0], side = DialoguePortraitSide.Left, offsetX = 300f, flipX = true });
        list.Add(c);

        Show(list, "示例甲");

        if (have == 0)
            Debug.Log("[对话] Resources/Portrait 下没找到图片，示例对话没有立绘。\n" +
                      "把自己的立绘丢进 Assets/Resources/Portrait 再播一次，就能拖着调了。");
    }

    /// <summary>把 Inspector 上那些「外观」数值写进布局资源（老用户迁移用）</summary>
    [ContextMenu("把 Inspector 上的数值同步到布局资源")]
    public void SyncInspectorToLayout()
    {
        DialogueLayout lay = GetLayout();
        if (lay == null)
        {
            Debug.LogWarning("[对话] 还没有布局资源，先用菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 打开对话布局资源 建一份。");
            return;
        }

        lay.panelLeft = panelSideMargin;
        lay.panelRight = panelSideMargin;
        lay.panelBottom = panelBottomMargin;
        lay.panelHeight = panelHeight;
        lay.nameFontSize = nameFontSize;
        lay.bodyFontSize = bodyFontSize;
        lay.hintFontSize = hintFontSize;
        lay.portraitWidth = portraitWidth;
        lay.portraitHeight = portraitHeight;
        lay.portraitInset = portraitInset;
        lay.portraitSink = portraitSink;
        lay.defaultSide = defaultPortraitSide;
        lay.nameColor = nameColor;
        lay.textColor = textColor;
        lay.hintColor = hintColor;

        Debug.Log("[对话] 已把 Inspector 上的数值同步到布局资源：" + lay.name + "。记得 Ctrl+S 保存工程。");
    }

    [ContextMenu("播放一段测试对话")]
    private void ContextTest()
    {
        Show(new List<DialogueLine>
        {
            new DialogueLine { speakerName = "系统", text = "这是测试对话第一句。" },
            new DialogueLine { text = "第二句没写名字，会沿用上一句的名字。" },
            new DialogueLine { speakerName = "老李", text = "第三句：按 E 继续，按到结束为止。" }
        }, "系统");
    }
}
