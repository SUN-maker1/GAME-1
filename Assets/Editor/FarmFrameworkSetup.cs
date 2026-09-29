#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键搭建「Sun Haven 式」多区域地图框架。
///
/// 菜单：Tools / 农场RPG / 1. 一键搭建多场景框架
/// 会做这些事：
///   1. 建好 Background / Ground / Entities / Foreground 四个 Sorting Layer
///   2. 建好 Player 标签
///   3. 生成两张占位地图贴图（农场绿 / 小镇灰），之后你把自己的 png 替换掉即可
///   4. 生成 Area_Farm、Area_Town 两个场景，各自含：
///      地图背景 + 四周墙 + 相机跟随 + 玩家 + 传送点 + 入口点 + SceneLoader
///   5. 把两个场景加进 Build Settings
/// </summary>
public static class FarmFrameworkSetup
{
    private const string MenuRoot = "Tools/农场RPG/";
    private const string SceneDir = "Assets/Scenes";
    private const string PlaceholderDir = "Assets/IMAGE/_Placeholder";
    private const string PlayerSpritePath = "Assets/IMAGE/player1.png";
    private const string PlayerControllerPath = "Assets/IMAGE/Square.controller";

    private const float MapSize = 64f;     // 每张区域地图的世界尺寸（单位）
    private const int TextureSize = 512;   // 占位贴图边长
    private const int SpritePPU = 8;       // 占位贴图 PPU，512 / 8 = 64 世界单位

    [MenuItem(MenuRoot + "1. 一键搭建多场景框架（Area_Farm + Area_Town）", false, 10)]
    public static void BuildAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[农场RPG] 已取消：请先处理未保存的场景改动。");
            return;
        }

        EnsureDirectories();
        EnsureSortingLayers();
        EnsureTag("Player");

        Sprite farmBg = MakePlaceholderSprite("Placeholder_Farm",
            new Color32(122, 170, 96, 255), new Color32(96, 140, 74, 255));
        Sprite townBg = MakePlaceholderSprite("Placeholder_Town",
            new Color32(154, 150, 158, 255), new Color32(118, 114, 122, 255));

        // 农场：右侧出口去小镇；玩家从镇上回来时落在右侧入口
        BuildArea("Area_Farm", farmBg,
            portalTarget: "Area_Town", targetEntryID: "FromFarm",
            localEntryID: "FromTown",
            portalPos: new Vector2(MapSize * 0.5f - 2f, 0f),
            localEntryPos: new Vector2(MapSize * 0.5f - 6f, 0f));

        // 小镇：左侧出口回农场；玩家从农场过来时落在左侧入口
        BuildArea("Area_Town", townBg,
            portalTarget: "Area_Farm", targetEntryID: "FromTown",
            localEntryID: "FromFarm",
            portalPos: new Vector2(-MapSize * 0.5f + 2f, 0f),
            localEntryPos: new Vector2(-MapSize * 0.5f + 6f, 0f));

        AddToBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorSceneManager.OpenScene($"{SceneDir}/Area_Farm.unity");

        Debug.Log("[农场RPG] 搭建完成！两个场景：Area_Farm、Area_Town。" +
                  "直接点 Play，用 WASD 走到地图右边的蓝色方块就能切到小镇。" +
                  "占位地图在 Assets/IMAGE/_Placeholder，换成你自己的 png 即可。");
    }

    [MenuItem(MenuRoot + "在当前场景添加：传送点 Portal", false, 30)]
    public static void AddPortalHere()
    {
        GameObject go = new GameObject("Portal_New");
        BoxCollider2D bc = go.AddComponent<BoxCollider2D>();
        bc.isTrigger = true;
        bc.size = new Vector2(2f, 4f);
        go.AddComponent<ScenePortal>();
        PlaceInFrontOfSceneView(go);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    [MenuItem(MenuRoot + "在当前场景添加：入口点 Entry", false, 31)]
    public static void AddEntryHere()
    {
        GameObject go = new GameObject("Entry_New");
        go.AddComponent<SceneEntryPoint>();
        PlaceInFrontOfSceneView(go);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    [MenuItem(MenuRoot + "2. 修复图片清晰度（像素风：Point 过滤 + 不压缩）", false, 20)]
    public static void FixPixelArtSharpness()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/IMAGE" });
        int changedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;

            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite)
            { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.filterMode != FilterMode.Point)
            { ti.filterMode = FilterMode.Point; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (ti.mipmapEnabled)
            { ti.mipmapEnabled = false; changed = true; }

            if (changed) { ti.SaveAndReimport(); changedCount++; }
        }

        Debug.Log($"[农场RPG] 已把 {changedCount} 张图片设为像素清晰模式（Point 过滤 / 不压缩 / 关 Mipmap）。" +
                  "新导入的图片如果发糊，对它单独执行同样设置即可。");
    }

    // ------------------------------------------------------------ 像素完美

    /// <summary>
    /// 像素清晰的两条独立规则，别混着看：
    ///
    ///   ① 过滤方式：Filter Mode 必须是 Point。Bilinear 会自己插值出中间色，
    ///      即使放大倍率是整数，看上去也是柔的。
    ///
    ///   ② 缩放倍率：贴图被放大 / 缩小的倍数必须尽量是整数。
    ///      屏幕上一共显示几个像素 = 贴图分辨率 × (相机正交半高 × 2 ÷ 参考屏幕高度 ÷ PPU)
    ///      反过来说，【放大倍率 = PPU ÷ 每世界单位的屏幕像素数】。
    ///      只要这个比值不是整数，就有像素被拉成 2 格宽、有的只有 1 格，
    ///      配合相机平滑跟随，走动时就会抖、发糊 —— 这才是「移动才糊」的真凶。
    /// </summary>

    [MenuItem(MenuRoot + "3. 像素校准：算出当前正确的 PPU", false, 21)]
    public static void ReportPixelPerfectPPU()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
        {
            Debug.LogWarning("[农场RPG] 当前场景没有正交相机，先放一个 Main Camera 再来校准。");
            return;
        }

        // 参考屏幕高度统一按 1080 算，这样窗口拉大小 PPU 不变，画面比例才稳定
        const float refHeight = 1080f;
        float pixelsPerUnit = refHeight / (2f * cam.orthographicSize);

        Debug.Log($"───── 像素校准结果 ─────\n" +
                  $"相机正交半高   : {cam.orthographicSize}\n" +
                  $"参考屏幕高度   : {refHeight} px\n" +
                  $"每世界单位像素 : {pixelsPerUnit:F2}\n" +
                  $"理想 PPU（1:1）: {Mathf.Round(pixelsPerUnit)}\n" +
                  $"2 倍放大的 PPU : {Mathf.Round(pixelsPerUnit * 2f)}\n\n" +
                  "贴图的 PPU 就等于上面这个数（或它的整数倍），缩放倍率就是整数，画面最清楚。\n" +
                  "改完记得在 Project 窗口右上角点 Apply。");
    }

    [MenuItem(MenuRoot + "4. 一键把 Assets/IMAGE 的 PPU 设为 1:1", false, 22)]
    public static void ApplyPixelPerfectPPU()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
        {
            Debug.LogWarning("[农场RPG] 当前场景没有正交相机，先放一个 Main Camera 再来校准。");
            return;
        }

        const float refHeight = 1080f;
        int targetPPU = Mathf.RoundToInt(refHeight / (2f * cam.orthographicSize));

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/IMAGE" });
        int changed = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // 只跳过脚本自己生成的临时占位图，你自己放进去的图一定要一起校准
            if (path.Contains("Placeholder_Farm") || path.Contains("Placeholder_Town")) continue;

            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;

            if (ti.textureType != TextureImporterType.Sprite)
                ti.textureType = TextureImporterType.Sprite;
            if (ti.spriteImportMode != SpriteImportMode.Single)
                ti.spriteImportMode = SpriteImportMode.Single;
            if (ti.filterMode != FilterMode.Point)
                ti.filterMode = FilterMode.Point;
            if (ti.mipmapEnabled)
                ti.mipmapEnabled = false;

            Vector2 pivot = ti.spritePivot;
            ti.spritePixelsPerUnit = targetPPU;
            ti.spritePivot = pivot;
            ti.SaveAndReimport();
            changed++;
        }

        Debug.Log($"[农场RPG] 已把 {changed} 张图的 PPU 设为 {targetPPU}（1:1 不缩放）。" +
                  $"\n注意：PPU 变了，物体的世界尺寸也会变（世界尺寸 = 贴图像素 ÷ PPU）。" +
                  $"地图变大就调小相机 Orthographic Size，反之调大。");
    }

    // ------------------------------------------------------------ 地图对齐

    /// <summary>
    /// 【最容易踩的坑，这个工具就是为它准备的】
    ///
    /// 地图的世界尺寸 = 贴图像素 ÷ PPU，这是由导入设置决定的，场景文件里改不了。
    /// 换了一张图却没改 PPU，地图的实际尺寸就说变就变：
    ///   把 1280px 的图按 PPU=100 导入 → 地图只有 12.8 × 10.7 世界单位
    /// 可墙、传送点、入口点、相机边界还是按原尺寸摆在 ±30 的地方 —— 全跑到地图外面去了。
    /// 表现就是：角色走出图片边界很远都碰不到传送点，相机也不跟着动。
    ///
    /// 这个菜单会读取地图贴图【实际】的世界尺寸，然后把所有相关物体重新摆一遍，
    /// 不管你换什么图，点一下就全对齐。
    /// </summary>
    [MenuItem(MenuRoot + "5. 一键对齐地图布局（按实际贴图尺寸重排）", false, 23)]
    public static void AlignLayoutToMapSprite()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            Debug.LogWarning("[农场RPG] 请先打开一个已保存的场景（比如 Area_Farm）再执行对齐。");
            return;
        }

        GameObject[] roots = scene.GetRootGameObjects();

        // ---- 1. 量出地图实际尺寸
        float mapW = MapSize, mapH = MapSize;
        float mapCX = 0f, mapCY = 0f;
        string mapInfo = "（没找到 Map_Background，沿用默认 64×64）";

        foreach (GameObject go in roots)
        {
            if (go.name != "Map_Background") continue;
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) break;

            Vector2 size = sr.sprite.bounds.size;
            mapW = size.x; mapH = size.y;
            mapCX = sr.transform.position.x; mapCY = sr.transform.position.y;
            mapInfo = $"{sr.sprite.name}：{sr.sprite.texture.width}×{sr.sprite.texture.height} px，" +
                      $"世界尺寸 {mapW:F1}×{mapH:F1} 单位";
            break;
        }

        float hw = mapW * 0.5f;
        float hh = mapH * 0.5f;
        const float thickness = 2f;

        // ---- 2. 四周墙：贴着地图边界摆，别再按旧的固定坐标
        Rect wallRect = new Rect(mapCX - hw - thickness, mapCY - hh - thickness,
                                 mapW + thickness * 2f, mapH + thickness * 2f);
        SetWall(roots, "Wall_Top", new Vector2(mapCX, mapCY + hh + thickness * 0.5f),
                new Vector2(wallRect.width, thickness));
        SetWall(roots, "Wall_Bottom", new Vector2(mapCX, mapCY - hh - thickness * 0.5f),
                new Vector2(wallRect.width, thickness));
        SetWall(roots, "Wall_Left", new Vector2(mapCX - hw - thickness * 0.5f, mapCY),
                new Vector2(thickness, wallRect.height));
        SetWall(roots, "Wall_Right", new Vector2(mapCX + hw + thickness * 0.5f, mapCY),
                new Vector2(thickness, wallRect.height));

        // ---- 3. 传送点：挪到边界内侧，高度跟着地图走
        foreach (GameObject go in roots)
        {
            ScenePortal portal = go.GetComponent<ScenePortal>();
            if (portal == null) continue;

            bool onRight = go.transform.position.x >= 0f;
            float sign = onRight ? 1f : -1f;

            go.transform.position = new Vector3(mapCX + sign * (hw - 1f), mapCY, 0f);

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc != null) bc.size = new Vector2(2f, Mathf.Clamp(mapH * 0.5f, 3f, 8f));
        }

        // ---- 4. 入口点：放在传送点那一侧的内侧
        foreach (GameObject go in roots)
        {
            SceneEntryPoint ep = go.GetComponent<SceneEntryPoint>();
            if (ep == null) continue;
            if (ep.entryID == "Start") continue;   // 主入口留在原地

            ScenePortal portal = Object.FindObjectOfType<ScenePortal>();
            float sign = portal != null ? Mathf.Sign(portal.transform.position.x - mapCX) : 1f;
            if (sign == 0f) sign = 1f;

            go.transform.position = new Vector3(mapCX + sign * (hw - 4f), mapCY, 0f);
        }

        // ---- 5. 玩家碰撞体：别让它比通道还宽
        // 碰撞体是按贴图比例算的，贴图一换（比如换成 2048px 的大图）就会变得离谱地大，
        // 结果角色一出生就卡在墙里、走不动。这里统一压到合理范围。
        const float maxCollider = 1.6f;
        foreach (GameObject go in roots)
        {
            if (go.name != "Player") continue;

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc == null || bc.isTrigger) continue;

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            Vector2 fit = sr != null && sr.sprite != null
                ? sr.sprite.bounds.size
                : new Vector2(1f, 1f);

            bc.size = new Vector2(Mathf.Clamp(fit.x * 0.5f, 0.3f, maxCollider),
                                  Mathf.Clamp(fit.y * 0.5f, 0.3f, maxCollider));
        }

        // ---- 6. 相机边界 & 玩家活动范围：跟地图实际尺寸一致
        Rect mapBounds = new Rect(mapCX - hw, mapCY - hh, mapW, mapH);
        foreach (GameObject go in roots)
        {
            CameraFollow follow = go.GetComponent<CameraFollow>();
            if (follow != null)
            {
                follow.useManualBounds = true;
                follow.manualBounds = mapBounds;
            }

            PlayerMovement pm = go.GetComponent<PlayerMovement>();
            if (pm != null)
            {
                pm.useBoundary = true;
                pm.boundary = mapBounds;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // ---- 7. 体检报告：把明显异常的物体挑出来
        ReportLayoutIssues(roots, mapBounds);

        Debug.Log($"[农场RPG] 已按地图实际尺寸对齐：{mapInfo}\n" +
                  $"地图边界 X[{mapBounds.min.x:F1}, {mapBounds.max.x:F1}] " +
                  $"Y[{mapBounds.min.y:F1}, {mapBounds.max.y:F1}]，" +
                  $"墙 / 传送点 / 入口点 / 相机边界已重排并保存。");
    }

    private static void SetWall(GameObject[] roots, string name, Vector2 pos, Vector2 size)
    {
        foreach (GameObject go in roots)
        {
            if (go.name != name) continue;
            go.transform.position = new Vector3(pos.x, pos.y, 0f);

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc != null) bc.size = size;
            else go.AddComponent<BoxCollider2D>().size = size;
        }
    }

    /// <summary>把尺寸明显不对劲的东西列出来，省得玩家自己一个个量</summary>
    private static void ReportLayoutIssues(GameObject[] roots, Rect bounds)
    {
        var issues = new List<string>();

        // 渲染组件缺失是最致命的：场景里没有 SpriteRenderer，地图和角色就是隐形的，
        // 而且 Hierarchy 上看不出来，很容易被当成「东西丢了」。这里直接点名。
        bool hasMapRenderer = false;
        bool hasPlayerRenderer = false;

        foreach (GameObject go in roots)
        {
            if (go.name == "Map_Background" && go.GetComponent<SpriteRenderer>() != null)
                hasMapRenderer = true;
            if (go.name == "Player" && go.GetComponent<SpriteRenderer>() != null)
                hasPlayerRenderer = true;
        }

        if (!hasMapRenderer)
            issues.Add($"Map_Background 上没有 SpriteRenderer —— 地图是隐形的。" +
                       $"选中它，Add Component → Sprite Renderer，把地图 png 拖到 Sprite 槽。");
        if (!hasPlayerRenderer)
            issues.Add($"Player 上没有 SpriteRenderer —— 角色是隐形的。" +
                       $"选中它，Add Component → Sprite Renderer，把角色图拖到 Sprite 槽；" +
                       $"有动画就再 Add Component → Animator，把控制器拖到 Controller 槽。");

        foreach (GameObject go in roots)
        {
            // 角色贴图如果在地图里比地图还大，基本就是 PPU 填错了
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) continue;

            Vector2 s = sr.sprite.bounds.size;
            if (s.x > bounds.width * 0.5f || s.y > bounds.height * 0.5f)
            {
                issues.Add($"{go.name}：贴图实际 {s.x:F1}×{s.y:F1} 单位，已经超过地图的一半。" +
                           $"角色应该只有 1~3 单位，去把这张图的 PPU 调大（PPU = 像素 ÷ 想要的世界尺寸）。");
            }
        }

        foreach (GameObject go in roots)
        {
            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) continue;

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc == null) continue;
            if (bc.isTrigger) continue;

            if (bc.size.x > 4f || bc.size.y > 4f)
                issues.Add($"{go.name}：碰撞体 {bc.size.x:F1}×{bc.size.y:F1} 太大，" +
                           $"会把通道堵死、还会误触传送点。角色碰撞体建议 0.6~1.5 单位。");
        }

        foreach (string s in issues)
            Debug.LogWarning("[农场RPG] " + s);

        if (issues.Count == 0)
            Debug.Log("[农场RPG] 布局体检通过：没有发现尺寸异常。");
    }

    /// <summary>
    /// 把地图贴图缩放到指定的世界尺寸。换图之后如果地图太小 / 太大，用它一步归位。
    /// 例：1280px 的图想铺满 64 单位宽 → 填 64，PPU 会自动算成 20。
    /// </summary>
    [MenuItem(MenuRoot + "6. 把地图贴图缩放到指定世界尺寸", false, 24)]
    public static void RescaleMapToTargetSize()
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject go in roots)
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null || go.name != "Map_Background") continue;

            Texture tex = sr.sprite.texture;
            int width = tex.width;

            // 从已有的世界尺寸反算当前 PPU，再按目标尺寸换算出新的 PPU
            float currentPPu = width / Mathf.Max(0.0001f, sr.sprite.bounds.size.x);
            float targetWorldWidth = MapSize;   // 默认目标：64 单位
            float newPPu = width / targetWorldWidth;

            string path = AssetDatabase.GetAssetPath(tex);
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"[农场RPG] 找不到 {path} 的导入设置，无法改 PPU。");
                return;
            }

            Vector2 pivot = ti.spritePivot;
            ti.spritePixelsPerUnit = newPPu;
            ti.spritePivot = pivot;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();

            Debug.Log($"[农场RPG] {tex.name} 的 PPU 已从 {currentPPu:F1} 改为 {newPPu:F1}，" +
                      $"地图现在是 {targetWorldWidth:F1} × {tex.height / newPPu:F1} 单位。\n" +
                      "改完请再执行一次「5. 一键对齐地图布局」，让墙和传送点跟上新尺寸。");
            return;
        }

        Debug.LogWarning("[农场RPG] 当前场景里没有 Map_Background，先确认它是根级物体。");
    }

    // ------------------------------------------------------------------ 场景

    private static void BuildArea(
        string sceneName, Sprite background,
        string portalTarget, string targetEntryID,
        string localEntryID,
        Vector2 portalPos, Vector2 localEntryPos)
    {
        float half = MapSize * 0.5f;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 地图背景
        GameObject map = new GameObject("Map_Background");
        SpriteRenderer sr = map.AddComponent<SpriteRenderer>();
        sr.sprite = background;
        sr.sortingLayerName = "Background";
        sr.sortingOrder = -10;
        map.transform.position = Vector3.zero;

        // ---- 四周墙
        GameObject walls = new GameObject("Walls");
        float t = 2f;
        MakeWall(walls.transform, "Wall_Top", new Vector2(0f, half + t * 0.5f), new Vector2(MapSize + t * 2f, t));
        MakeWall(walls.transform, "Wall_Bottom", new Vector2(0f, -half - t * 0.5f), new Vector2(MapSize + t * 2f, t));
        MakeWall(walls.transform, "Wall_Left", new Vector2(-half - t * 0.5f, 0f), new Vector2(t, MapSize));
        MakeWall(walls.transform, "Wall_Right", new Vector2(half + t * 0.5f, 0f), new Vector2(t, MapSize));

        // ---- 入口点
        MakeEntryPoint("Entry_Start", "Start", Vector2.zero);
        MakeEntryPoint($"Entry_{localEntryID}", localEntryID, localEntryPos);

        // ---- 传送点
        GameObject portalGo = new GameObject($"Portal_To_{portalTarget}");
        portalGo.transform.position = new Vector3(portalPos.x, portalPos.y, 0f);
        BoxCollider2D pbc = portalGo.AddComponent<BoxCollider2D>();
        pbc.isTrigger = true;
        pbc.size = new Vector2(2f, 6f);
        ScenePortal portal = portalGo.AddComponent<ScenePortal>();
        portal.targetSceneName = portalTarget;
        portal.entryID = targetEntryID;

        // ---- 玩家
        MakePlayer(Vector3.zero);

        // ---- 相机
        MakeCamera(half);

        // ---- 系统物体
        GameObject systems = new GameObject("_Systems");
        systems.AddComponent<SceneLoader>();

        EditorSceneManager.SaveScene(scene, $"{SceneDir}/{sceneName}.unity");
        Debug.Log($"[农场RPG] 场景已生成：{SceneDir}/{sceneName}.unity");
    }

    private static void MakeWall(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        BoxCollider2D bc = go.AddComponent<BoxCollider2D>();
        bc.size = size;
    }

    private static void MakeEntryPoint(string goName, string entryID, Vector2 pos)
    {
        GameObject go = new GameObject(goName);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        SceneEntryPoint ep = go.AddComponent<SceneEntryPoint>();
        ep.entryID = entryID;
    }

    private static void MakePlayer(Vector3 pos)
    {
        GameObject player = new GameObject("Player");
        player.transform.position = pos;

        Sprite playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlayerSpritePath);
        SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
        sr.sprite = playerSprite;
        sr.sortingLayerName = "Entities";
        sr.sortingOrder = 0;

        Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;             // 俯视视角必须关重力
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        BoxCollider2D bc = player.AddComponent<BoxCollider2D>();
        if (playerSprite != null)
        {
            Vector2 s = playerSprite.bounds.size;
            bc.size = new Vector2(Mathf.Max(0.2f, s.x * 0.7f), Mathf.Max(0.2f, s.y * 0.7f));
        }

        player.AddComponent<PersistentPlayer>();
        player.AddComponent<PlayerMovement>();

        RuntimeAnimatorController ctrl =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
        if (ctrl != null)
        {
            Animator anim = player.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
        }

        try
        {
            player.tag = "Player";
        }
        catch (UnityException)
        {
            Debug.LogWarning("[农场RPG] Player 标签不存在，玩家改由 PersistentPlayer 识别。");
        }
    }

    private static void MakeCamera(float half)
    {
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);

        camGo.AddComponent<AudioListener>();

        CameraFollow follow = camGo.AddComponent<CameraFollow>();
        follow.useManualBounds = true;
        follow.manualBounds = new Rect(-half, -half, MapSize, MapSize);
        follow.autoFindPlayer = true;
        follow.smoothTime = 0.25f;
    }

    // ------------------------------------------------------------ 占位贴图

    private static Sprite MakePlaceholderSprite(string name, Color baseColor, Color lineColor)
    {
        EnsureDirectories();
        string path = $"{PlaceholderDir}/{name}.png";

        Texture2D tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        Color subtle = Color.Lerp(baseColor, lineColor, 0.35f);

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                bool major = (x % 128 == 0) || (y % 128 == 0);
                bool minor = (x % 32 == 0) || (y % 32 == 0);
                tex.SetPixel(x, y, major ? lineColor : (minor ? subtle : baseColor));
            }
        }
        tex.Apply();

        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePivot = new Vector2(0.5f, 0.5f);
            ti.spritePixelsPerUnit = SpritePPU;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ------------------------------------------------------------ 工程设置

    private static void EnsureDirectories()
    {
        if (!Directory.Exists(SceneDir)) Directory.CreateDirectory(SceneDir);
        if (!Directory.Exists(PlaceholderDir)) Directory.CreateDirectory(PlaceholderDir);
    }

    private static void EnsureSortingLayers()
    {
        string[] wanted = { "Background", "Ground", "Entities", "Foreground" };
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("[农场RPG] 找不到 TagManager.asset，跳过自动建 Sorting Layer。");
            return;
        }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("m_SortingLayers");
        if (layers == null)
        {
            Debug.LogWarning("[农场RPG] TagManager 里没有 m_SortingLayers，跳过。");
            return;
        }

        foreach (string name in wanted)
        {
            bool exists = false;
            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty p = layers.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (p != null && p.stringValue == name) { exists = true; break; }
            }
            if (exists) continue;

            layers.InsertArrayElementAtIndex(layers.arraySize);
            SerializedProperty elem = layers.GetArrayElementAtIndex(layers.arraySize - 1);
            elem.FindPropertyRelative("name").stringValue = name;
            elem.FindPropertyRelative("uniqueID").intValue = NextSortingLayerID(layers);
            elem.FindPropertyRelative("locked").boolValue = false;
        }

        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    private static int NextSortingLayerID(SerializedProperty layers)
    {
        int max = 0;
        for (int i = 0; i < layers.arraySize; i++)
        {
            SerializedProperty p = layers.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID");
            if (p != null && p.intValue > max) max = p.intValue;
        }
        return max + 1;
    }

    private static void EnsureTag(string tag)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty tags = tagManager.FindProperty("m_Tags");
        if (tags == null) return;

        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
        }

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    private static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();

        foreach (string path in new[] { $"{SceneDir}/Area_Farm.unity", $"{SceneDir}/Area_Town.unity" })
        {
            if (!scenes.Any(s => s.path == path))
                scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void PlaceInFrontOfSceneView(GameObject go)
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
            go.transform.position = view.pivot;
    }
}
#endif
