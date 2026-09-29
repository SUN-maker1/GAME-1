#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 内容导入器 —— 以后自己画的「对话框样式 / 人物立绘 / 对话内容」都从这里进工程。
/// 菜单：Tools ▸ 农场RPG ▸ 资源导入器
///
/// 【为什么要它】
///   手动导入要干三件容易忘的事：① 拷到 Resources 目录（运行时才读得到）
///   ② 改导入参数（Sprite / 点采样 / 无压缩 / 九宫格边框 / 轴心）
///   ③ 再把资源挂到 NPC 身上。这里全自动化，改完立刻能在游戏里看到。
///
/// 【四块用法】
///   1. 对话框样式：拖一张九宫格图 → 起个英文名 → 设边框像素 → 导入为新样式
///      （存到 Assets/Resources/UI/dialogue_box_<名字>.png，导入后自动切过去）
///   2. 人物立绘：拖一张图 → 导入立绘（可选：一键填进选中 NPC 的所有对话）
///   3. 对话内容：按格式写好台词 → 生成一个对话资源 → （可选）一键挂到选中 NPC
///   4. 字体：拖一个 ttf/otf → 拷进 Assets/Resources/Fonts（字符集自动设动态，中文不缺字）
///      → （可选）直接当成对话正文字体
///
/// 【台词格式】
///   老李: 哟，新来的？这片地荒了有些年头了。
///   : 第二句不写名字，就沿用上一句的说话人（连着说几句时很省事）
///   # 井号开头是注释，不会进游戏
///   portrait: Assets/IMAGE/Portrait/laoli.png     ← 给之后所有句子设立绘
///   portrait: laoli.png, xiaomei.png              ← 一句里放两个立绘，逗号分隔
///   expressions: laoli_笑.png, laoli_怒.png       ← 表情列表，按顺序配给 portrait 里的立绘
///                                                    游戏里按 E 依次换脸（换完再翻页），按 Q 随时切
///   box: my_style                                 ← 给之后所有句子换对话框样式
///   style: 回忆风格                               ← 给之后所有句子整套换外观（字体 / 颜色 / 字号一起）
///
/// 【调位置 / 大小】
///   不用在这里填数字：Play 模式下靠近 NPC 按 E 开对话，再按 F7，
///   鼠标直接拖对话框和每个立绘、滚轮改大小，数值会自动存进「对话布局」资源。
/// </summary>
public class ContentImporterWindow : EditorWindow
{
    // ---------------------------------------------------------------- 字段

    private Texture2D boxTexture;
    private string styleName = "my_style";

    private Texture2D portraitTexture;
    private NPCInteractable targetNpc;
    private Font sourceFont;

    private string dialogueText = "";
    private string assetName = "Dialogue_新对话";

    private string[] styleNames = new string[0];
    private int styleIndex;

    private bool autoCrop = true;
    private int borderLeft = 24;
    private int borderBottom = 24;
    private int borderRight = 24;
    private int borderTop = 24;

    private Vector2 scroll;

    // ---------------------------------------------------------------- 菜单

    [MenuItem("Tools/农场RPG/资源导入器（样式 / 立绘 / 对话）", false, 260)]
    private static void Open()
    {
        ContentImporterWindow w = GetWindow<ContentImporterWindow>("农场RPG 资源导入器");
        w.minSize = new Vector2(420f, 620f);
        w.RefreshStyles();
        w.Show();
    }

    private void OnEnable() => RefreshStyles();

    // ---------------------------------------------------------------- 界面

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("① 对话框样式（九宫格贴片）", EditorStyles.boldLabel);
        boxTexture = (Texture2D)EditorGUILayout.ObjectField("框体图片", boxTexture, typeof(Texture2D), false);
        styleName = EditorGUILayout.TextField("样式名（英文）", styleName);
        autoCrop = EditorGUILayout.Toggle(new GUIContent("自动裁掉透明边距", "很多图是画在大画布正中间的（四周透明）。\n" +
            "不裁掉的话九宫格边框会落在透明区里，整个图被拉扁。勾上就自动裁到画面本体。"), autoCrop);
        EditorGUILayout.BeginHorizontal();
        borderLeft = EditorGUILayout.IntField("边框 左", borderLeft);
        borderBottom = EditorGUILayout.IntField("下", borderBottom);
        borderRight = EditorGUILayout.IntField("右", borderRight);
        borderTop = EditorGUILayout.IntField("上", borderTop);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("图会被拷到 Assets/Resources/UI/dialogue_box_<样式名>.png\n" +
                                "边框 = 四条边里「不许拉伸」的装饰厚度（滚动卷轴的两头、描边角这些）。\n" +
                                "四个方向的边框可以不一样大；拿不准就先大概填，进游戏按 F7 看效果再回来改。\n" +
                                "想替换现有样式，就用同名再导一次。", MessageType.None);
        if (GUILayout.Button("导入为新样式并立刻应用", GUILayout.Height(28)))
            ImportBoxStyle();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("切换已导入的样式", EditorStyles.boldLabel);
        if (styleNames.Length == 0)
        {
            EditorGUILayout.LabelField("（Resources/UI 下还没有 dialogue_box_*.png）");
        }
        else
        {
            styleIndex = EditorGUILayout.Popup("当前样式", styleIndex, styleNames);
            if (GUILayout.Button("应用这个样式", GUILayout.Height(24)))
                ApplyStyleByName(styleNames[styleIndex]);
        }

        EditorGUILayout.Space(14);
        EditorGUILayout.LabelField("② 人物立绘", EditorStyles.boldLabel);
        portraitTexture = (Texture2D)EditorGUILayout.ObjectField("立绘图片", portraitTexture, typeof(Texture2D), false);
        targetNpc = (NPCInteractable)EditorGUILayout.ObjectField("要挂到哪个 NPC（可选）", targetNpc, typeof(NPCInteractable), true);
        EditorGUILayout.HelpBox("立绘规格：透明底 PNG，人物靠下（腰/脚贴底边），512×512 起。\n" +
                                "填了 NPC 就会把立绘塞进他所有台词的 Portrait 字段。", MessageType.None);
        if (GUILayout.Button("导入立绘", GUILayout.Height(28)))
            ImportPortrait();

        EditorGUILayout.Space(14);
        EditorGUILayout.LabelField("③ 对话内容", EditorStyles.boldLabel);
        assetName = EditorGUILayout.TextField("资源名", assetName);
        EditorGUILayout.LabelField("台词（一行一句）");
        dialogueText = EditorGUILayout.TextArea(dialogueText, GUILayout.MinHeight(160));
        EditorGUILayout.HelpBox("格式： 名字: 内容\n没写名字 = 沿用上一句说话人；# 开头是注释；\n" +
                                "portrait: 路径 给之后的句子设立绘（多个用逗号分隔）；\n" +
                                "expressions: 路径1, 路径2 表情列表，按 E / Q 在游戏里换脸；\n" +
                                "box: 样式名 给之后的句子换对话框（如 box: my_style，回忆戏可换暗色框）；\n" +
                                "style: 预设名 给之后的句子整套换外观（字体 / 颜色 / 字号 / 框一起换）；\n" +
                                "位置大小不用在这写：Play 里按 F7 用鼠标拖。", MessageType.None);
        if (GUILayout.Button("填入示例模板", GUILayout.Height(22)))
            dialogueText = Template();
        if (GUILayout.Button("生成对话资源", GUILayout.Height(28)))
            ImportDialogue();

        EditorGUILayout.Space(14);
        EditorGUILayout.LabelField("④ 字体", EditorStyles.boldLabel);
        sourceFont = (Font)EditorGUILayout.ObjectField("字体文件（ttf / otf）", sourceFont, typeof(Font), false);
        EditorGUILayout.HelpBox("会把字体拷到 Assets/Resources/Fonts，并把字符集设成「动态」——\n" +
                                "不设动态的话中文会显示成方块（最容易踩的坑）。\n" +
                                "导完之后：进 Play 按 F7 →「字体颜色」页，名字 / 正文 / 提示都能选到它。", MessageType.None);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("导入字体", GUILayout.Height(26)))
            ImportFont(false);
        if (GUILayout.Button("导入并立刻用作正文字体", GUILayout.Height(26)))
            ImportFont(true);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        EditorGUILayout.EndScrollView();
    }

    private static string Template()
    {
        return "# 这是一行注释，不会进游戏\n" +
               "老李: 哟，新来的？这片地荒了有些年头了。\n" +
               ": 往南那条路能走到小镇上，杂货铺什么种子都卖。\n" +
               "老李: 种下去别忘了浇水——连着干三天，苗就全完了。\n" +
               "# portrait: Assets/IMAGE/Portrait/laoli.png\n" +
               "# expressions: Assets/IMAGE/Portrait/laoli_笑.png, Assets/IMAGE/Portrait/laoli_怒.png\n" +
               "# box: my_style   ← 给之后的句子换对话框样式\n" +
               "# style: 回忆风格  ← 给之后的句子整套换外观（字体 / 颜色 / 字号一起换）";
    }

    // ---------------------------------------------------------------- ① 样式

    private void ImportBoxStyle()
    {
        if (boxTexture == null)
        {
            Debug.LogWarning("[资源导入器] 先把框体图片拖到「框体图片」那一栏。");
            return;
        }

        string src = AssetDatabase.GetAssetPath(boxTexture);
        string name = string.IsNullOrEmpty(styleName) ? "custom" : styleName.Trim();

        string dir = "Assets/Resources/UI";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string dst = dir + "/dialogue_box_" + name + ".png";
        int[] border = { Mathf.Max(0, borderLeft), Mathf.Max(0, borderBottom), Mathf.Max(0, borderRight), Mathf.Max(0, borderTop) };
        Vector4 borderV = new Vector4(border[0], border[1], border[2], border[3]);   // Unity 顺序：左、下、右、上

        if (autoCrop)
        {
            // 自动裁剪：直接从磁盘读 PNG、裁掉透明边距、写出紧贴画面的图。
            // 好处是九宫格边框从此以「画面本体」为基准，不会再落进透明区把图拉扁。
            if (!TryWriteCroppedPng(src, dst, border))
            {
                Debug.LogError($"[资源导入器] 处理 {src} 失败了（图片必须是 PNG）。");
                return;
            }
            AssetDatabase.ImportAsset(dst);
        }
        else if (src != dst)
        {
            // 同名样式已存在就先删掉，否则 CopyAsset 会失败（这就是「替换现有样式」的用法）
            if (File.Exists(dst)) AssetDatabase.DeleteAsset(dst);

            if (!AssetDatabase.CopyAsset(src, dst))
            {
                Debug.LogError($"[资源导入器] 复制 {src} 到 {dst} 失败了。");
                return;
            }
            AssetDatabase.ImportAsset(dst);
        }

        // 九宫格：轴心居中，PPU 100（UI 里 1 精灵像素 = 1 画布像素）；尺寸上限放到 2048，大图不糊
        SetSpriteImport(dst, borderV, new Vector2(0.5f, 0.5f), 100, 2048);
        RefreshStyles();
        ApplyStyleByName(name);

        Debug.Log($"[资源导入器] 对话框样式「{name}」已导入：{dst}（九宫格边框 左{border[0]}/下{border[1]}/右{border[2]}/上{border[3]}px" +
                  $"{(autoCrop ? "，已自动裁掉透明边距" : "")}），已切换过去。");
    }

    /// <summary>
    /// 读 src 的 PNG → 裁掉透明边距 → 写到 dst。
    /// 边框如果是按「未裁剪画布」估的，裁剪后依然成立：返回值里会把裁掉量带上，由调用方决定要不要平移边框。
    /// 这里选择更直观的语义：用户填的边框就是相对「裁剪后画面」的。
    /// </summary>
    private static bool TryWriteCroppedPng(string src, string dst, int[] border)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(src);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false)) return false;

            Color32[] px = tex.GetPixels32();
            int w = tex.width, h = tex.height;

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < 0) return false;     // 全透明，没东西可裁

            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            Color32[] cropped = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    cropped[y * cw + x] = px[(y + minY) * w + (x + minX)];

            Texture2D outTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            outTex.SetPixels32(cropped);
            outTex.Apply();
            byte[] outBytes = outTex.EncodeToPNG();
            File.WriteAllBytes(dst, outBytes);

            // 边框别超过图本身（裁剪后尺寸变小了要夹一下）
            border[0] = Mathf.Min(border[0], cw / 2 - 1);
            border[1] = Mathf.Min(border[1], ch / 2 - 1);
            border[2] = Mathf.Min(border[2], cw / 2 - 1);
            border[3] = Mathf.Min(border[3], ch / 2 - 1);
            for (int i = 0; i < 4; i++) border[i] = Mathf.Max(1, border[i]);

            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(outTex);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[资源导入器] 裁剪图片出错：{e.Message}");
            return false;
        }
    }

    /// <summary>扫描 Resources/UI 里所有 dialogue_box_* 样式</summary>
    private void RefreshStyles()
    {
        string dir = Application.dataPath + "/Resources/UI";
        if (!Directory.Exists(dir))
        {
            styleNames = new string[0];
            return;
        }

        List<string> names = new List<string>();
        foreach (string f in Directory.GetFiles(dir, "dialogue_box_*.png"))
        {
            string n = Path.GetFileNameWithoutExtension(f);
            names.Add(n.Replace("dialogue_box_", ""));
        }

        names.Sort();
        styleNames = names.ToArray();
        if (styleIndex >= styleNames.Length) styleIndex = 0;
    }

    private void ApplyStyleByName(string name)
    {
        PlayerPrefs.SetString(DialogueManager.StylePrefKey, name);
        PlayerPrefs.Save();

        // Play 模式里场上已经有管理器就就地刷新，马上能看到
        DialogueManager mgr = Object.FindObjectOfType<DialogueManager>();
        if (mgr != null) mgr.ApplyStyle(name);

        Repaint();
        Debug.Log($"[资源导入器] 对话框样式已切成「{name}」。");
    }

    // ---------------------------------------------------------------- ② 立绘

    private void ImportPortrait()
    {
        if (portraitTexture == null)
        {
            Debug.LogWarning("[资源导入器] 先把立绘图片拖到「立绘图片」那一栏。");
            return;
        }

        string path = AssetDatabase.GetAssetPath(portraitTexture);
        // 立绘：底边中心轴心（和人物素材一致），不设九宫格边框
        SetSpriteImport(path, Vector4.zero, new Vector2(0.5f, 0f), 100, 1024);

        Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sp == null)
        {
            Debug.LogError($"[资源导入器] {path} 没能变成 Sprite，检查一下是不是图片。");
            return;
        }

        int filled = 0;
        if (targetNpc != null)
        {
            filled += Fill(targetNpc.firstDialogue, sp);
            filled += Fill(targetNpc.repeatDialogue, sp);
            EditorUtility.SetDirty(targetNpc);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(targetNpc.gameObject.scene);
        }

        Selection.activeObject = sp;
        Debug.Log($"[资源导入器] 立绘已导入：{path}" +
                  (targetNpc != null ? $"，并填进 {targetNpc.name} 的 {filled} 句台词。" : "。\n" +
                   "接下来：把它拖到 NPC 台词里每一句的 Portrait 字段上。"));
    }

    private static int Fill(List<DialogueLine> lines, Sprite sp)
    {
        if (lines == null) return 0;
        int n = 0;
        foreach (DialogueLine l in lines)
        {
            if (l == null) continue;
            SetPortrait(l, sp);
            n++;
        }
        return n;
    }

    /// <summary>
    /// 给一句话设立绘。已经有立绘槽位就替换第一个，没有就新建一个。
    /// 【注意】每句话都单独 new 一个槽位 —— 如果几句共用同一个槽位对象，
    /// 在 Play 模式用 F7 拖其中一个，其它几句会跟着一起动（因为改的是同一个对象）。
    /// </summary>
    private static void SetPortrait(DialogueLine line, Sprite sp)
    {
        if (line.portraits == null) line.portraits = new List<DialoguePortraitSlot>();

        if (line.portraits.Count > 0 && line.portraits[0] != null)
        {
            line.portraits[0].sprite = sp;
            return;
        }

        line.portraits.Add(new DialoguePortraitSlot { sprite = sp });
    }

    // ---------------------------------------------------------------- ③ 对话

    private void ImportDialogue()
    {
        if (string.IsNullOrWhiteSpace(dialogueText))
        {
            Debug.LogWarning("[资源导入器] 台词是空的，先写点内容（或点「填入示例模板」）。");
            return;
        }

        string defaultSpeaker;
        List<DialogueLine> lines = ParseDialogue(dialogueText, out defaultSpeaker);

        if (lines.Count == 0)
        {
            Debug.LogWarning("[资源导入器] 没解析出任何一句台词。");
            return;
        }

        string dir = "Assets/Dialogue";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string path = dir + "/" + (string.IsNullOrEmpty(assetName) ? "Dialogue_新对话" : assetName.Trim()) + ".asset";

        DialogueAsset asset = ScriptableObject.CreateInstance<DialogueAsset>();
        asset.defaultSpeaker = defaultSpeaker;
        asset.lines = lines;

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        if (targetNpc != null)
        {
            targetNpc.dialogueAsset = asset;
            EditorUtility.SetDirty(targetNpc);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(targetNpc.gameObject.scene);
        }

        Selection.activeObject = asset;
        Debug.Log($"[资源导入器] 对话资源已生成：{path}（{lines.Count} 句，默认说话人「{defaultSpeaker}」）" +
                  (targetNpc != null ? $"，已挂到 {targetNpc.name}。" : "。把它拖到 NPC 的 Dialogue Asset 上即可。"));
    }

    /// <summary>把台词文本解析成一串 DialogueLine</summary>
    private static List<DialogueLine> ParseDialogue(string raw, out string defaultSpeaker)
    {
        List<DialogueLine> list = new List<DialogueLine>();
        string speaker = "";
        string currentBoxStyle = "";
        DialoguePreset currentPreset = null;
        List<Sprite> portraits = new List<Sprite>();
        List<Sprite> expressions = new List<Sprite>();

        string[] rows = raw.Replace("\r\n", "\n").Split('\n');
        foreach (string row in rows)
        {
            string s = row.Trim();
            if (s.Length == 0) continue;
            if (s.StartsWith("#")) continue;

            // portrait: 路径 —— 给之后所有句子设立绘
            // 想一句里放好几个：portrait: a.png, b.png（逗号分隔，按从左到右叠）
            if (s.StartsWith("portrait:") || s.StartsWith("portrait："))
            {
                string p = s.Substring(s.IndexOf(':') + 1).Trim();
                if (s.StartsWith("portrait：")) p = s.Substring(s.IndexOf('：') + 1).Trim();

                List<Sprite> found = new List<Sprite>();
                string[] parts = p.Split(',');
                for (int t = 0; t < parts.Length; t++)
                {
                    string one = parts[t].Trim();
                    if (one.Length == 0) continue;

                    Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(one);
                    if (sp != null) found.Add(sp);
                    else Debug.LogWarning($"[资源导入器] 立绘路径找不到：{one}");
                }

                if (found.Count > 0) portraits = found;
                continue;
            }

            // expressions: 路径1, 路径2 —— 表情列表（按 E / Q 切换的那几张脸）
            // 会依次配给 portrait 里的第 1、2、… 个立绘当表情
            if (s.StartsWith("expressions:") || s.StartsWith("expressions："))
            {
                string p = s.Substring(s.IndexOf(':') + 1).Trim();
                if (s.StartsWith("expressions：")) p = s.Substring(s.IndexOf('：') + 1).Trim();

                List<Sprite> found = new List<Sprite>();
                string[] parts = p.Split(',');
                for (int t = 0; t < parts.Length; t++)
                {
                    string one = parts[t].Trim();
                    if (one.Length == 0) continue;

                    Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(one);
                    if (sp != null) found.Add(sp);
                    else Debug.LogWarning($"[资源导入器] 表情图路径找不到：{one}");
                }

                if (found.Count > 0) expressions = found;
                continue;
            }

            // box: 样式名 —— 给之后所有句子换对话框样式（像立绘一样每句可以不同）
            if (s.StartsWith("box:") || s.StartsWith("box："))
            {
                string b = s.Substring(s.IndexOf(':') + 1).Trim();
                if (s.StartsWith("box：")) b = s.Substring(s.IndexOf('：') + 1).Trim();
                currentBoxStyle = b;
                continue;
            }

            // style: 预设名 —— 给之后所有句子整套换外观（字体 / 字号 / 颜色 / 描边 / 框一起变）
            if (s.StartsWith("style:") || s.StartsWith("style："))
            {
                string nm = s.Substring(s.IndexOf(':') + 1).Trim();
                if (s.StartsWith("style：")) nm = s.Substring(s.IndexOf('：') + 1).Trim();

                if (nm.Length == 0)
                {
                    currentPreset = null;                       // 写 style:（空）就是「还给默认样子」
                }
                else
                {
                    DialoguePreset found = DialoguePresetTools.FindPreset(nm);
                    if (found != null) currentPreset = found;
                    else Debug.LogWarning($"[资源导入器] 找不到叫「{nm}」的外观预设，这一句先不改外观。" +
                                          "\n预设要在 Project 里有（Resources 底下最好），菜单 Tools ▸ 农场RPG ▸ 对话外观 ▸ ② 列出已有的外观预设 能看清单。");
                }
                continue;
            }

            // 名字: 内容（中英文冒号都认，只切第一个）
            int i = s.IndexOf(':');
            int j = s.IndexOf('：');
            int k = -1;
            if (i >= 0 && j >= 0) k = Mathf.Min(i, j);
            else k = Mathf.Max(i, j);   // 两个都是 -1 时也是 -1

            string text = s;
            if (k > 0)
            {
                string maybeName = s.Substring(0, k).Trim();
                // 名字里带空格或太长就不当名字（避免把整句英文台词切成名字）
                if (maybeName.Length <= 12 && maybeName.IndexOf(' ') < 0)
                {
                    speaker = maybeName;
                    text = s.Substring(k + 1).Trim();
                }
            }

            // 每句话都 new 自己的槽位：不然 Play 模式拖一个，其它几句跟着一起动
            DialogueLine dl = new DialogueLine { speakerName = speaker, text = text };
            for (int t = 0; t < portraits.Count; t++)
            {
                DialoguePortraitSlot slot = new DialoguePortraitSlot { sprite = portraits[t] };
                // 第 t 个表情配给第 t 个立绘（portrait 和 expressions 按顺序一一对应）
                if (t < expressions.Count && expressions[t] != null)
                    slot.expressions.Add(expressions[t]);
                dl.portraits.Add(slot);
            }
            if (!string.IsNullOrEmpty(currentBoxStyle)) dl.boxStyle = currentBoxStyle;
            dl.preset = currentPreset;
            list.Add(dl);
        }

        defaultSpeaker = speaker;
        return list;
    }

    /// <summary>把字体拷进 Resources/Fonts（自己放进来的字体运行时才找得到），可选直接当成对话正文字体</summary>
    private void ImportFont(bool applyToBody)
    {
        if (sourceFont == null)
        {
            Debug.LogWarning("[资源导入器] 先把字体文件拖到「字体文件」那一栏。");
            return;
        }

        string src = AssetDatabase.GetAssetPath(sourceFont);
        if (string.IsNullOrEmpty(src))
        {
            Debug.LogWarning("[资源导入器] 这个字体没有对应的资源文件，先从磁盘拖进来。");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts"))
            AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

        string file = Path.GetFileName(src);
        string dst = "Assets/Resources/Fonts/" + file;

        if (src == dst)
        {
            Debug.Log("[资源导入器] " + file + " 本来就在 Resources/Fonts 里了，更新一下导入参数。");
        }
        else
        {
            if (File.Exists(dst)) AssetDatabase.DeleteAsset(dst);

            if (!AssetDatabase.CopyAsset(src, dst))
            {
                Debug.LogError("[资源导入器] 复制字体失败：" + src + " → " + dst);
                return;
            }
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        }

        // 字符集设成动态 —— 不然中文会显示成方块
        DialoguePresetTools.SetupFontImporter(dst);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        DialogueFontLibrary.Refresh();

        Font f = AssetDatabase.LoadAssetAtPath<Font>(dst);

        if (applyToBody && f != null)
        {
            DialogueLayout lay = DialogueLayoutTools.EnsureLayoutAsset();
            lay.bodyFontName = f.name;
            EditorUtility.SetDirty(lay);
            AssetDatabase.SaveAssets();

            // Play 模式里开着对话的话，立刻能看见
            DialogueManager mgr = Object.FindObjectOfType<DialogueManager>();
            if (mgr != null) mgr.ApplyUiMetrics();
        }

        Selection.activeObject = f;
        Debug.Log($"[资源导入器] 字体已导入：{dst}（字符集 = 动态，中文不会缺字）" +
                  (applyToBody ? "，并已设为对话正文字体。" : "。\n然后在 Play 按 F7 →「字体颜色」页把它选给名字 / 正文 / 提示。"));
    }

    // ---------------------------------------------------------------- 通用

    /// <summary>统一设好精灵导入参数（像素风：点采样 + 无压缩）</summary>
    private static void SetSpriteImport(string path, Vector4 border, Vector2 pivot, int ppu, int maxSize)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null)
        {
            Debug.LogWarning($"[资源导入器] {path} 不是图片，导入参数没改成。");
            return;
        }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.alphaIsTransparency = true;
        ti.spritePivot = pivot;
        ti.spriteBorder = border;       // x=左 y=下 z=右 w=上
        ti.spritePixelsPerUnit = ppu;
        ti.maxTextureSize = maxSize;
        ti.SaveAndReimport();
    }
}
#endif
