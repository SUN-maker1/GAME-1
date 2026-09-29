using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 编辑器工具：一键生成可 WASD 操作的玩家角色，并放进当前打开的场景。
/// 菜单：Tools → 创建玩家角色(WASD控制)
/// </summary>
public static class PlayerSetup
{
    private const string SpritePath = "Assets/Player/player.png";
    private const string PrefabPath = "Assets/Player/Player.prefab";

    [MenuItem("Tools/创建玩家角色(WASD控制)")]
    private static void CreatePlayer()
    {
        PlayerController existing = Object.FindObjectOfType<PlayerController>();
        if (existing != null)
        {
            Debug.LogWarning("场景里已经有玩家了（" + existing.name + "），不再重复创建。");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite == null)
        {
            Debug.LogError("找不到角色贴图：" + SpritePath);
            return;
        }

        SetupSpriteImport();

        GameObject go = new GameObject("Player");

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.spriteSortPoint = SpriteSortPoint.Pivot;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        // 碰撞只做脚下一小块。之后选中 Player 就能在 Inspector 改数值，
        // 或在 Scene 视图里直接拖绿色方块调整
        FootCollider2D foot = go.AddComponent<FootCollider2D>();
        foot.shape = FootCollider2D.ShapeType.Capsule;
        foot.width = 0.5f;
        foot.height = 0.35f;
        foot.bottomOffset = 0f;
        foot.Apply();

        go.AddComponent<PlayerController>();

        // 复用树的 Y 轴排序：走到树前面就遮住树，走到树后面就被树遮住
        TreeSortOrder sort = go.AddComponent<TreeSortOrder>();
        sort.useFootPosition = true;
        sort.updateEveryFrame = true;
        sort.Refresh();

        // 出现在相机看得见的地方
        Camera cam = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
        if (cam != null)
            go.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

        PrefabUtility.SaveAsPrefabAssetAndConnect(go, PrefabPath, InteractionMode.UserAction);

        if (cam != null)
        {
            CameraFollow2D follow = cam.GetComponent<CameraFollow2D>();
            if (follow == null)
                follow = cam.gameObject.AddComponent<CameraFollow2D>();
            follow.target = go.transform;
            EditorUtility.SetDirty(follow);
        }

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("玩家已创建。按 Play，用 WASD 或方向键移动；想改速度选中 Player 调 Move Speed。");
    }

    private static void SetupSpriteImport()
    {
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 16;
        importer.spritePivot = new Vector2(0.5f, 0f);
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
}
