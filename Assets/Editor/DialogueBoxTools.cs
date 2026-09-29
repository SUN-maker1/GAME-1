#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 对话框样式 / 人物立绘的一键工具，菜单在 Tools ▸ 农场RPG ▸ 对话 下面。
///
/// 【框体样式】
///   木牌风 / 石板 / 纯黑 三款像素风九宫格贴片，点一下就换，选择记在 PlayerPrefs 里，
///   下次进游戏还是这个样式。想用自己的图：把 Assets/Resources/UI/dialogue_box_xxx.png
///   换成同名的图就行（保持九宫格 border = 16，边框才不会被拉伸变形）。
///
/// 【人物立绘】
///   立绘是你自己画的半身像：底边坐在对话框上沿，身体向上露在框外面。
///   画好丢进工程 → 选中它 → 用菜单「把选中的图片设成对话立绘」自动设好导入参数 →
///   拖到 NPC 的 Inspector ▸ First Dialogue ▸ 每句的 Portrait 上。
/// </summary>
public static class DialogueBoxTools
{
    private const string MenuRoot = "Tools/农场RPG/对话/";

    [MenuItem(MenuRoot + "1. 对话框样式：木牌风（星露谷味）", false, 300)]
    private static void StyleWood() => SetStyle("wood", "木牌风");

    [MenuItem(MenuRoot + "2. 对话框样式：石板", false, 301)]
    private static void StyleStone() => SetStyle("stone", "石板");

    [MenuItem(MenuRoot + "3. 对话框样式：纯黑", false, 302)]
    private static void StyleDark() => SetStyle("dark", "纯黑");

    [MenuItem(MenuRoot + "5. 把选中的图片设成对话立绘", false, 320)]
    private static void SetupPortrait()
    {
        Object obj = Selection.activeObject;
        string path = AssetDatabase.GetAssetPath(obj);

        if (obj == null || string.IsNullOrEmpty(path) || !(obj is Texture2D) ||
            AssetImporter.GetAtPath(path) is TextureImporter == false)
        {
            EditorUtility.DisplayDialog("设成对话立绘",
                "先在 Project 窗口里选中一张图片（png / jpg 都行），再点这个菜单。", "知道了");
            return;
        }

        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.filterMode = FilterMode.Point;              // 像素风：不要平滑
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.alphaIsTransparency = true;
        ti.spritePivot = new Vector2(0.5f, 0f);        // 底边中心，和人物素材一致
        ti.spritePixelsPerUnit = 100;
        ti.maxTextureSize = 1024;
        ti.SaveAndReimport();

        Selection.activeObject = obj;
        Debug.Log($"[对话] {path} 已设成立绘规格（Sprite / 点采样 / 无压缩 / 底边中心轴心）。\n" +
                  "接下来：把它拖到 NPC 的 Inspector ▸ First Dialogue ▸ 某一句 ▸ Portrait 上。\n" +
                  "改立绘大小去 DialogueManager 的 Portrait Height / Width（运行时自动建的那个物体上）。", obj);
    }

    [MenuItem(MenuRoot + "6. 立绘规格说明", false, 321)]
    private static void ShowSpec()
    {
        EditorUtility.DisplayDialog("人物立绘怎么画",
            "1. 透明底 PNG，建议 512×512 或 512×768（半身像够用，别太大）\n" +
            "2. 人物放在画面【靠下】的位置：腰/脚贴着底边，胸以上留出来\n" +
            "   —— 显示时底边会坐在对话框上沿，上半身露在框外面\n" +
            "3. 像素风就用点采样：画好丢进工程，选中它，点菜单第 5 项自动设好导入参数\n" +
            "4. 拖到 NPC 的 Inspector ▸ First Dialogue ▸ 每句的 Portrait\n" +
            "5. 想一句左一句右：改那一句的 Portrait Side（Left / Right）\n\n" +
            "大小不对就调 DialogueManager 上的 Portrait Height / Width / Inset。",
            "知道了");
    }

    // ------------------------------------------------------------------ 内部

    private static void SetStyle(string key, string label)
    {
        // 写进 PlayerPrefs：DialogueManager 建 UI 时会自动读，换一次一直有效
        PlayerPrefs.SetString(DialogueManager.StylePrefKey, key);
        PlayerPrefs.Save();

        // 场上已经有管理器（Play 模式里）就就地刷新，马上能看到效果
        DialogueManager mgr = Object.FindObjectOfType<DialogueManager>();
        if (mgr != null) mgr.ApplyStyle(key);

        Debug.Log($"[对话] 对话框样式已切成「{label}」。" +
                  $"贴片在 Assets/Resources/UI/dialogue_box_{key}.png，可以直接用自己画的图替换它。");
    }
}
#endif
