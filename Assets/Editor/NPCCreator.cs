using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// NPC 一键生成器 —— 菜单在 Tools ▸ 农场RPG ▸ NPC 下面。
///
/// 用编辑器 API 建对象，所以建完是真正落在场景里的（可以 Ctrl+S 保存、可以撤销），
/// 不会出现「改了文件但 Unity 里没变」那种情况。
/// </summary>
public static class NPCCreator
{
    private const string SpritePath = "Assets/IMAGE/_Placeholder/NPC_Villager.png";
    private const string MenuRoot = "Tools/农场RPG/NPC/";

    [MenuItem(MenuRoot + "1. 在当前场景创建示例 NPC", false, 200)]
    public static void CreateInActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("创建示例 NPC", "当前没有打开任何场景，先打开一个场景再来。", "知道了");
            return;
        }

        GameObject npc = Create(scene);
        if (npc == null) return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[NPCCreator] 已在场景 {scene.name} 里创建 {npc.name}（位置 {npc.transform.position}），场景已保存。");
    }

    [MenuItem(MenuRoot + "2. 给 Build Settings 里所有场景创建示例 NPC", false, 201)]
    public static void CreateInAllScenes()
    {
        if (EditorBuildSettings.scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("创建示例 NPC", "Build Settings 里一个场景都没有，先去 File ▸ Build Settings 把场景加进去。", "知道了");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;   // 用户取消了保存

        string original = SceneManager.GetActiveScene().path;
        int created = 0;

        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
        {
            if (entry == null || !entry.enabled) continue;

            Scene scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            if (Create(scene, false) != null) created++;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (!string.IsNullOrEmpty(original))
            EditorSceneManager.OpenScene(original, OpenSceneMode.Single);

        Debug.Log($"[NPCCreator] 一共在 {created} 个场景里创建了示例 NPC。");
    }

    [MenuItem(MenuRoot + "3. 把选中物体变成 NPC", false, 202)]
    public static void TurnSelectionIntoNpc()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("变成 NPC", "先在 Hierarchy 里选中一个物体。", "知道了");
            return;
        }

        NPCInteractable npc = go.GetComponent<NPCInteractable>();
        if (npc == null)
        {
            npc = Undo.AddComponent<NPCInteractable>(go);
            npc.npcName = string.IsNullOrEmpty(go.name) ? "村民" : go.name;
            npc.firstDialogue = DefaultDialogue(npc.npcName);
        }

        if (go.GetComponent<Collider2D>() == null && go.GetComponent<SpriteRenderer>() != null)
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            BoxCollider2D col = Undo.AddComponent<BoxCollider2D>(go);
            col.size = new Vector2(sr.sprite.bounds.size.x * 0.6f, sr.sprite.bounds.size.y * 0.5f);
            col.offset = new Vector2(0f, col.size.y * 0.5f);
        }

        Selection.activeGameObject = go;
        Scene scene = go.scene;
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[NPCCreator] {go.name} 已经是 NPC 了，去 Inspector 里填对话内容吧。", go);
    }

    // ------------------------------------------------------------------ 内部

    /// <summary>在指定场景里建一个示例 NPC；已存在就返回 null</summary>
    private static GameObject Create(Scene scene, bool warnIfExists = true)
    {
        foreach (NPCInteractable existing in Object.FindObjectsOfType<NPCInteractable>())
        {
            if (existing != null && existing.gameObject.scene == scene)
            {
                if (warnIfExists)
                {
                    Selection.activeGameObject = existing.gameObject;
                    EditorUtility.DisplayDialog("创建示例 NPC",
                        $"场景 {scene.name} 里已经有 NPC「{existing.name}」了，先帮你选中它。\n" +
                        "想要第二个的话，先给已有的那个改个名或删掉。", "知道了");
                }
                return null;
            }
        }

        Sprite sprite = PrepareSprite(scene);
        if (sprite == null) return null;

        PlayerMovement player = FindInScene<PlayerMovement>(scene);
        float targetHeight = 1.3f;
        int sortingLayerID = 0;
        int sortingOrder = 0;

        if (player != null)
        {
            SpriteRenderer psr = player.GetComponent<SpriteRenderer>();
            if (psr != null && psr.sprite != null)
            {
                targetHeight = psr.bounds.size.y;
                sortingLayerID = psr.sortingLayerID;
                sortingOrder = psr.sortingOrder;
            }
        }

        Vector3 pos = PickPosition(scene, player);

        GameObject go = new GameObject("NPC_老李");
        go.transform.position = pos;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerID = sortingLayerID;
        sr.sortingOrder = sortingOrder;

        // 脚底对齐：把精灵底部放到物体原点上（和玩家的 pivot 保持一致）
        sr.transform.position = pos;

        NPCInteractable npc = go.AddComponent<NPCInteractable>();
        npc.npcName = "老李";
        npc.firstDialogue = DefaultDialogue("老李");
        npc.repeatDialogue = new List<DialogueLine>
        {
            new DialogueLine { text = "地里的活儿还顺手吧？有空常来找我聊天。" }
        };
        npc.interactRange = Mathf.Max(1.5f, targetHeight * 1.8f);
        npc.promptHeight = targetHeight + 0.25f;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(sprite.bounds.size.x * 0.55f, targetHeight * 0.5f);
        col.offset = new Vector2(0f, col.size.y * 0.5f);

        Undo.RegisterCreatedObjectUndo(go, "创建示例 NPC");
        Selection.activeGameObject = go;

        return go;
    }

    /// <summary>在玩家右边 / 地图中心挑一个位置，并保证落在地图里</summary>
    private static Vector3 PickPosition(Scene scene, PlayerMovement player)
    {
        Vector3 pos = player != null
            ? player.transform.position + new Vector3(3f, 0f, 0f)
            : Vector3.zero;

        SpriteRenderer map = FindMap(scene);
        if (map != null)
        {
            Bounds b = map.bounds;
            pos.x = Mathf.Clamp(pos.x, b.min.x + 1.5f, b.max.x - 1.5f);
            pos.y = Mathf.Clamp(pos.y, b.min.y + 1.5f, b.max.y - 1.5f);

            // 玩家旁边没地方就退回到地图中心偏右
            if (player == null)
            {
                pos.x = b.center.x + Mathf.Min(4f, b.size.x * 0.2f);
                pos.y = b.center.y;
            }
        }

        pos.z = 0f;
        return pos;
    }

    private static SpriteRenderer FindMap(Scene scene)
    {
        foreach (SpriteRenderer sr in Object.FindObjectsOfType<SpriteRenderer>())
        {
            if (sr == null || sr.gameObject.scene != scene) continue;
            string n = sr.name;
            if (n.IndexOf("Map", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return sr;
        }
        return null;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (T c in Object.FindObjectsOfType<T>())
        {
            if (c != null && c.gameObject.scene == scene) return c;
        }

        // 兜底：本场景确实没有（比如 Player 是从别处搬来的），任意一个也能拿来当参考
        foreach (T c in Object.FindObjectsOfType<T>())
        {
            if (c != null) return c;
        }
        return null;
    }

    /// <summary>准备好 NPC 用的精灵：设好 PPU / 轴心 / 点采样</summary>
    private static Sprite PrepareSprite(Scene scene)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);

        if (sprite == null)
        {
            Debug.LogError($"[NPCCreator] 找不到 {SpritePath}，示例 NPC 没有素材可用。\n" +
                           "也可以自己建一个物体、挂上 SpriteRenderer 和 NPCInteractable，用菜单第 3 项。");
            return null;
        }

        TextureImporter ti = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (ti == null) return sprite;

        float targetHeight = 1.3f;
        PlayerMovement player = FindInScene<PlayerMovement>(scene);
        if (player != null)
        {
            SpriteRenderer psr = player.GetComponent<SpriteRenderer>();
            if (psr != null && psr.sprite != null && psr.bounds.size.y > 0.05f)
                targetHeight = psr.bounds.size.y;
        }

        int ppu = Mathf.Max(1, Mathf.RoundToInt(sprite.texture.height / targetHeight));

        bool dirty = false;
        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; dirty = true; }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
        if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
        if (ti.spritePivot != new Vector2(0.5f, 0f)) { ti.spritePivot = new Vector2(0.5f, 0f); dirty = true; }
        if (Mathf.Abs(ti.spritePixelsPerUnit - ppu) > 0.5f) { ti.spritePixelsPerUnit = ppu; dirty = true; }

        if (dirty)
        {
            ti.SaveAndReimport();
            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            Debug.Log($"[NPCCreator] 已按玩家身高 {targetHeight:F2} 单位把 NPC 素材的 PPU 设成 {ppu}，轴心设为底部中心。");
        }

        return sprite;
    }

    /// <summary>示例对话。以后自己在 Inspector 里改就行</summary>
    private static List<DialogueLine> DefaultDialogue(string npcName)
    {
        return new List<DialogueLine>
        {
            new DialogueLine
            {
                speakerName = npcName,
                text = "哟，新来的？这片地荒了有些年头了。"
            },
            new DialogueLine
            {
                text = "往南那条路能走到小镇上，杂货铺什么种子都卖。"
            },
            new DialogueLine
            {
                text = "种下去别忘了浇水——连着干三天，苗就全完了。"
            },
            new DialogueLine
            {
                text = "缺工具就来找我，我这儿有把旧锄头，先借你用。"
            }
        };
    }
}
