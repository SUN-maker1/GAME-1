using UnityEditor;
using UnityEngine;

/// <summary>
/// 可互动物体创建工具 —— 菜单 Tools ▸ 农场RPG ▸ 互动 ▸ 创建可互动物体
/// 在场景中创建一个带 Interactable 组件的物体，玩家走近后按 E 或鼠标左键即可互动。
/// </summary>
public static class InteractableCreator
{
    [MenuItem("Tools/农场RPG/互动/创建可互动物体")]
    public static void CreateInteractable()
    {
        GameObject go = new GameObject("Interactable");
        go.AddComponent<Interactable>();

        // 尝试在玩家位置附近创建，方便测试
        PlayerMovement player = Object.FindObjectOfType<PlayerMovement>();
        if (player != null)
            go.transform.position = player.transform.position + Vector3.right * 2f;

        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Interactable");
    }

    [MenuItem("Tools/农场RPG/互动/创建带精灵的可互动物体")]
    public static void CreateInteractableWithSprite()
    {
        GameObject go = new GameObject("Interactable");

        // 添加一个默认 SpriteRenderer（白色方块，方便在场景中看到）
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (sr.sprite == null)
            sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("Default-Sprite.psd");

        go.AddComponent<Interactable>();

        // 尝试在玩家位置附近创建
        PlayerMovement player = Object.FindObjectOfType<PlayerMovement>();
        if (player != null)
            go.transform.position = player.transform.position + Vector3.right * 2f;

        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Interactable With Sprite");
    }

    /// <summary>
    /// 右键菜单：给选中的物体添加互动组件
    /// 在 Hierarchy 中选中一个或多个物体，右键 ▸ 农场RPG ▸ 添加互动组件
    /// </summary>
    [MenuItem("GameObject/农场RPG/添加互动组件", false, 10)]
    public static void AddInteractableToSelection()
    {
        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            if (go.GetComponent<Interactable>() != null) continue;

            Undo.AddComponent<Interactable>(go);
            count++;
        }

        if (count > 0)
            Debug.Log($"[Interactable] 已给 {count} 个物体添加互动组件");
    }

    /// <summary>
    /// 右键菜单：给选中的物体添加互动组件（带精灵）
    /// </summary>
    [MenuItem("GameObject/农场RPG/添加互动组件（带精灵）", false, 10)]
    public static void AddInteractableWithSpriteToSelection()
    {
        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            if (go.GetComponent<Interactable>() != null) continue;

            if (go.GetComponent<SpriteRenderer>() == null)
            {
                SpriteRenderer sr = Undo.AddComponent<SpriteRenderer>(go);
                sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                if (sr.sprite == null)
                    sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("Default-Sprite.psd");
            }

            Undo.AddComponent<Interactable>(go);
            count++;
        }

        if (count > 0)
            Debug.Log($"[Interactable] 已给 {count} 个物体添加互动组件（带精灵）");
    }

    /// <summary>
    /// 验证：选中的物体是否可以添加互动组件
    /// </summary>
    [MenuItem("GameObject/农场RPG/添加互动组件", true)]
    public static bool ValidateAddInteractableToSelection()
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            if (go.GetComponent<Interactable>() == null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 验证：选中的物体是否可以添加互动组件（带精灵）
    /// </summary>
    [MenuItem("GameObject/农场RPG/添加互动组件（带精灵）", true)]
    public static bool ValidateAddInteractableWithSpriteToSelection()
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            if (go.GetComponent<Interactable>() == null)
                return true;
        }
        return false;
    }
}
