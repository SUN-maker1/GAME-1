using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 玩家精灵「自动适配」工具。
///
/// 解决两件很常见、手动调很痛苦的事：
///
///  ① 【尺寸不统一】走路/站立/攻击几张图是分开画的，角色在每张图里的像素高度
///     往往不一样（本项目实测：walk_* 约 511/496/493px，attack 只有 267px，差近一倍）。
///     Pixels Per Unit 相同 → 世界高度就不同 → 一攻击角色立刻缩一圈。
///     本工具按「角色内容的真实像素高度」反算 PPU，让所有贴图渲染出同一个身高。
///
///  ② 【脚底对不齐 / 左右飘】每张切片的 pivot 都统一设成
///     「内容水平居中 + 内容最低点（脚底）」，
///     这样不管素材里角色画在切片框的哪个位置，脚都稳稳踩在原点，
///     也不会像统一 pivot 那样因为逐帧内容偏移而左右晃动。
///
/// 用法：Tools ▸ 农场RPG ▸ 玩家 ▸ 精灵尺寸与脚底自动适配…
/// </summary>
public class SpriteFitTool : EditorWindow
{
    private const string DefaultControllerPath = "Assets/Animation/Player.controller";

    private RuntimeAnimatorController controller;
    // 默认就是本项目 walk_* 现在的身高（内容高 ~511px ÷ PPU 400 ≈ 1.28），
    // 这样跑一遍走路图几乎不动，只把不匹配的图（如 attack）拉到同一身高。
    private float targetHeight = 1.28f;
    private bool alignFeet = true;       // 同时把 pivot 对齐到「内容居中 + 脚底」
    private Vector2 scroll;
    private string report = "";

    [MenuItem("Tools/农场RPG/玩家/精灵尺寸与脚底自动适配…", false, 60)]
    private static void Open()
    {
        SpriteFitTool win = GetWindow<SpriteFitTool>(false, "精灵适配", true);
        win.minSize = new Vector2(430, 380);
        win.Show();
    }

    private void OnEnable()
    {
        if (controller == null)
            controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DefaultControllerPath);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("让玩家的所有动画帧渲染出同一个身高", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "原理：逐张贴图扫描角色「实际不透明像素」的高度，\n" +
            "再用  内容高度 ÷ 目标身高  反算 Pixels Per Unit。\n" +
            "可选：把每帧的 pivot 对齐到脚底 + 水平居中，走路就不再左右晃。",
            MessageType.Info);

        EditorGUILayout.Space(4);
        controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
            "玩家控制器", controller, typeof(RuntimeAnimatorController), false);

        EditorGUILayout.BeginHorizontal();
        targetHeight = EditorGUILayout.FloatField("目标身高（世界单位）", targetHeight);
        if (GUILayout.Button("用走路图当前身高", GUILayout.Width(130)))
            targetHeight = MeasureReferenceHeight();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField($"  参考：正交相机 Orthographic Size 6 → 屏幕高 12 单位，" +
                                   $"当前身高占屏幕 {targetHeight / 12f * 100f:F0}%", EditorStyles.miniLabel);

        alignFeet = EditorGUILayout.Toggle("同时对齐脚底 / 水平居中（推荐）", alignFeet);

        EditorGUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("① 只测量（不改动）", GUILayout.Height(26)))
            report = Run(false);
        if (GUILayout.Button("② 应用适配", GUILayout.Height(26)))
        {
            report = Run(true);
            Debug.Log(report);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        if (!string.IsNullOrEmpty(report))
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    // ------------------------------------------------------------------ 主流程

    private string Run(bool apply)
    {
        if (controller == null)
            return "请先指定玩家的 Animator Controller。";

        if (targetHeight < 0.05f)
            return "目标身高太小了。";

        // 收集控制器里所有 clip 用到的精灵 → 它们所属的贴图
        Dictionary<string, List<Sprite>> byTexture = CollectSprites(controller, out HashSet<string> clipSeen);

        if (byTexture.Count == 0)
            return "控制器里没找到任何精灵帧 —— 确认动画 clip 里有 PPtr 精灵曲线（空 clip 会这样）。";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(apply ? "【已应用适配】" : "【测量结果（未改动）】");
        sb.AppendLine($"控制器：{AssetDatabase.GetAssetPath(controller)}   目标身高：{targetHeight:F3} 单位");
        sb.AppendLine($"扫描到 {clipSeen.Count} 个 clip，涉及 {byTexture.Count} 张贴图");
        sb.AppendLine();

        List<string> paths = new List<string>(byTexture.Keys);
        paths.Sort(StringComparer.Ordinal);

        foreach (string texPath in paths)
        {
            TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti == null)
            {
                sb.AppendLine($"· {texPath}\n    不是贴图导入器，跳过。");
                continue;
            }

            Measure(ti, out int imgW, out int imgH, out int maxContentH, out string detail);

            // Measure 里为了读像素会 SaveAndReimport，导入器对象可能被换掉 —— 重新取一份再用
            ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti == null) continue;

            if (maxContentH <= 0)
            {
                sb.AppendLine($"· {Path.GetFileName(texPath)}\n    {detail}");
                continue;
            }

            float oldPPU = ti.spritePixelsToUnits;
            float newPPU = maxContentH / targetHeight;
            float oldWorld = maxContentH / oldPPU;

            sb.AppendLine($"· {Path.GetFileName(texPath)}");
            sb.AppendLine($"    原图 {imgW}×{imgH}   角色内容高度 {maxContentH}px");
            sb.AppendLine($"    PPU {oldPPU:F1} → {newPPU:F1}    世界身高 {oldWorld:F3} → {targetHeight:F3} 单位");

            if (detail.Length > 0) sb.AppendLine($"    {detail}");

            if (apply)
                ApplyTo(ti, newPPU, alignFeet, sb);
        }

        if (apply)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            sb.AppendLine();
            sb.AppendLine("已保存并重新导入。切回 Scene 看效果（Unity 会自动 reimport 一次）。");
        }

        return sb.ToString();
    }

    /// <summary>扫控制器里所有 clip，收集用到的精灵，按所属贴图分组</summary>
    private static Dictionary<string, List<Sprite>> CollectSprites(RuntimeAnimatorController ctrl,
                                                                   out HashSet<string> clipNames)
    {
        Dictionary<string, List<Sprite>> byTexture = new Dictionary<string, List<Sprite>>();
        clipNames = new HashSet<string>();

        if (ctrl == null) return byTexture;

        foreach (AnimationClip clip in ctrl.animationClips)
        {
            if (clip == null) continue;
            clipNames.Add(clip.name);

            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null) continue;

                foreach (ObjectReferenceKeyframe k in keys)
                {
                    Sprite sp = k.value as Sprite;
                    if (sp == null) continue;

                    string tex = AssetDatabase.GetAssetPath(sp);
                    if (string.IsNullOrEmpty(tex)) continue;

                    if (!byTexture.TryGetValue(tex, out List<Sprite> list))
                    {
                        list = new List<Sprite>();
                        byTexture[tex] = list;
                    }
                    if (!list.Contains(sp)) list.Add(sp);
                }
            }
        }

        return byTexture;
    }

    /// <summary>
    /// 以「走路图」现在的身高当目标：这样跑一遍走路图基本不动，只把不匹配的图拉齐。
    /// 优先找名字含 walk_front / walk 的贴图，否则取第一张。
    /// </summary>
    private float MeasureReferenceHeight()
    {
        Dictionary<string, List<Sprite>> byTexture = CollectSprites(controller, out HashSet<string> _);
        if (byTexture.Count == 0)
        {
            Debug.LogWarning("[精灵适配] 控制器里没找到精灵帧，用不上参考贴图。");
            return targetHeight;
        }

        List<string> paths = new List<string>(byTexture.Keys);
        paths.Sort(StringComparer.Ordinal);

        string pick = null;
        foreach (string p in paths)
        {
            if (p.IndexOf("walk_front", StringComparison.OrdinalIgnoreCase) >= 0) { pick = p; break; }
            if (p.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0 && pick == null) pick = p;
        }
        if (pick == null) pick = paths[0];

        TextureImporter ti = AssetImporter.GetAtPath(pick) as TextureImporter;
        if (ti == null) return targetHeight;

        Measure(ti, out int _, out int _, out int contentH, out string detail);
        if (contentH <= 0)
        {
            Debug.LogWarning($"[精灵适配] 参考贴图 {pick} 量不到内容高度：{detail}");
            return targetHeight;
        }

        float h = contentH / ti.spritePixelsToUnits;
        Debug.Log($"[精灵适配] 以 {Path.GetFileName(pick)} 为准：内容高 {contentH}px ÷ PPU {ti.spritePixelsToUnits:F1} = {h:F3} 单位");
        return h;
    }

    /// <summary>扫描一张贴图里每个切片的真实内容包围盒</summary>
    private static void Measure(TextureImporter ti, out int imgW, out int imgH,
                                out int maxContentH, out string detail)
    {
        imgW = imgH = 0;
        maxContentH = 0;
        detail = "";

        string path = ti.assetPath;

        bool wasReadable = ti.isReadable;
        if (!wasReadable)
        {
            ti.isReadable = true;
            ti.SaveAndReimport();
        }

        // 下面的分支很多，用 finally 保证 readable 一定复原，别把贴图留在可读状态（占内存）
        try
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                detail = "读不到贴图，跳过。";
                return;
            }

            int pngW, pngH;
            if (!TryReadPngSize(path, out pngW, out pngH))
            {
                pngW = tex.width;
                pngH = tex.height;
            }
            imgW = pngW;
            imgH = pngH;

            // 导入时可能被 Max Size 压缩 → 切片 rect 是原图坐标，像素是导入后坐标
            float scale = pngW > 0 ? tex.width / (float)pngW : 1f;
            if (scale <= 0f) scale = 1f;

            Color32[] px = tex.GetPixels32();

            SpriteMetaData[] sheet = ti.spritesheet;
            if (sheet == null || sheet.Length == 0)
            {
                // 单图模式：整张当一个精灵
                Recti box = Scan(px, tex.width, tex.height, 0, 0, tex.width, tex.height);
                maxContentH = box.height > 0 ? Mathf.RoundToInt(box.height / scale) : 0;
                return;
            }

            int empty = 0;
            foreach (SpriteMetaData sm in sheet)
            {
                Rect r = sm.rect;
                int x0 = Mathf.Clamp(Mathf.RoundToInt(r.x * scale), 0, tex.width - 1);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(r.y * scale), 0, tex.height - 1);
                int w = Mathf.Clamp(Mathf.RoundToInt(r.width * scale), 1, tex.width - x0);
                int h = Mathf.Clamp(Mathf.RoundToInt(r.height * scale), 1, tex.height - y0);

                Recti box = Scan(px, tex.width, tex.height, x0, y0, w, h);
                if (box.width <= 0 || box.height <= 0)
                {
                    empty++;
                    continue;
                }

                int contentH = Mathf.RoundToInt(box.height / scale);
                if (contentH > maxContentH) maxContentH = contentH;
            }

            if (empty > 0)
                detail = $"有 {empty} 个切片是空的（没画东西），已忽略。";
        }
        finally
        {
            if (!wasReadable)
            {
                ti.isReadable = false;
                ti.SaveAndReimport();
            }
        }
    }

    /// <summary>写入 PPU + 可选脚底 pivot</summary>
    private static void ApplyTo(TextureImporter ti, float ppu, bool alignFeet,
                                System.Text.StringBuilder sb)
    {
        string path = ti.assetPath;

        bool wasReadable = ti.isReadable;
        if (alignFeet && !wasReadable)
        {
            ti.isReadable = true;
            ti.SaveAndReimport();
        }

        ti.spritePixelsToUnits = ppu;

        if (alignFeet && ti.spriteImportMode == SpriteImportMode.Multiple)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int pngW;
            if (!TryReadPngSize(path, out pngW, out int _)) pngW = tex != null ? tex.width : 0;
            float scale = pngW > 0 && tex != null ? tex.width / (float)pngW : 1f;
            if (scale <= 0f) scale = 1f;

            Color32[] px = tex != null ? tex.GetPixels32() : null;
            SpriteMetaData[] sheet = ti.spritesheet;

            if (px != null && sheet != null)
            {
                int changed = 0;
                for (int i = 0; i < sheet.Length; i++)
                {
                    SpriteMetaData sm = sheet[i];
                    Rect r = sm.rect;

                    int x0 = Mathf.Clamp(Mathf.RoundToInt(r.x * scale), 0, tex.width - 1);
                    int y0 = Mathf.Clamp(Mathf.RoundToInt(r.y * scale), 0, tex.height - 1);
                    int w = Mathf.Clamp(Mathf.RoundToInt(r.width * scale), 1, tex.width - x0);
                    int h = Mathf.Clamp(Mathf.RoundToInt(r.height * scale), 1, tex.height - y0);

                    Recti box = Scan(px, tex.width, tex.height, x0, y0, w, h);
                    if (box.width <= 0 || box.height <= 0) continue;

                    // pivot 用「切片框内的归一化坐标」表达：
                    //   x = 内容中心相对框左边 / 框宽   → 角色在框里水平居中
                    //   y = 内容最低点相对框下边 / 框高 → 脚底正好落在原点
                    float px0 = (box.x - x0 + box.width * 0.5f) / w;
                    float py0 = (box.y - y0) / (float)h;

                    px0 = Mathf.Clamp01(px0);
                    py0 = Mathf.Clamp01(py0);

                    sm.alignment = (int)SpriteAlignment.Custom;
                    sm.pivot = new Vector2(px0, py0);
                    sheet[i] = sm;
                    changed++;
                }

                ti.spritesheet = sheet;
                sb.AppendLine($"    pivot 已对齐 {changed} 个切片的脚底 + 水平中心");
            }
        }

        ti.SaveAndReimport();

        if (alignFeet && !wasReadable)
        {
            ti.isReadable = false;
            ti.SaveAndReimport();
        }
    }

    private struct Recti
    {
        public int x, y, width, height;
    }

    /// <summary>在指定矩形里找 alpha 超过阈值的像素包围盒（左下原点）</summary>
    private static Recti Scan(Color32[] px, int texW, int texH, int x0, int y0, int w, int h)
    {
        Recti r = new Recti { x = 0, y = 0, width = 0, height = 0 };

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        for (int y = y0; y < y0 + h && y < texH; y++)
        {
            int row = y * texW;
            for (int x = x0; x < x0 + w && x < texW; x++)
            {
                if (px[row + x].a > 16)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0) return r;

        r.x = minX;
        r.y = minY;
        r.width = maxX - minX + 1;
        r.height = maxY - minY + 1;
        return r;
    }

    /// <summary>只解 PNG 的文件头拿原始宽高（不用解压，很轻）</summary>
    private static bool TryReadPngSize(string path, out int w, out int h)
    {
        w = h = 0;
        try
        {
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] head = new byte[24];
                if (fs.Read(head, 0, 24) != 24) return false;
                if (head[0] != 137 || head[1] != 80 || head[2] != 78 || head[3] != 71) return false;

                w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return w > 0 && h > 0;
            }
        }
        catch
        {
            return false;
        }
    }
}
