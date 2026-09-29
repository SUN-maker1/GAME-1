#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 房间家具「前景遮挡」一键摆放 —— 让人在桌子后面走时，桌子会把人挡住（星露谷式遮挡）。
///
/// 【原理】
///   整个房间是一张压平的图，桌子和地板在同一张图里，没法和人做前后排序。
///   所以把桌子单独抠成一张透明贴片（Assets/IMAGE/Occlusion/room_table_front.png），
///   摆在和人「同一个 Sorting Layer、同一个 Order」的位置上：
///   渲染器开启「按 Y 轴透明排序」后，谁的 Y 更低（更靠屏幕下方）谁就画在上面 ——
///   人在桌子下沿以下 → 人盖住桌子；人走到桌子上方（身后）→ 桌子盖住人。
///
/// 【遮挡为什么会对】
///   人物精灵轴心在身体中心，桌子贴片轴心在底边中心（底部 = 桌脚地面）。
///   人物从后面接近时身体中心远高于桌底 → 人先画、桌子后画 → 被挡；
///   从前面接近时身体中心低于桌底 → 人后画 → 人挡桌子。
///   碰撞体把人物拦在「不会串位」的距离上，所以两个方向的排序永远正确。
///
/// 【用法】
///   打开 Area_Room 场景 → 菜单 Tools ▸ 农场RPG ▸ 遮挡 ▸ 一键：摆好房间桌子前景贴片
///   跑完 Ctrl+S 保存场景。重复执行安全（先删旧的再摆，不会叠好几层）。
/// </summary>
public static class RoomOcclusionSetup
{
    // 桌子贴片在 room.png 里的像素位置（左上角原点，和生成贴片时完全一致）
    const int CropX = 723;
    const int CropY = 731;
    const int CropW = 492;
    const int CropH = 276;

    const string MapPath = "Assets/IMAGE/_Placeholder/room.png";
    const string SpritePath = "Assets/IMAGE/Occlusion/room_table_front.png";
    const string ObjName = "前景_桌子";
    const string ContainerName = "_Furniture";

    [MenuItem("Tools/农场RPG/遮挡/一键：摆好房间桌子前景贴片", false, 300)]
    private static void Run()
    {
        // ---- 1. 找地图背景 ----
        GameObject mapGo = GameObject.Find("Map_Background");
        if (mapGo == null)
        {
            Debug.LogError("[前景遮挡] 场景里没有 Map_Background，请先打开 Area_Room 场景。");
            return;
        }
        SpriteRenderer mapSr = mapGo.GetComponent<SpriteRenderer>();
        if (mapSr == null || mapSr.sprite == null)
        {
            Debug.LogError("[前景遮挡] Map_Background 上没有 SpriteRenderer / 精灵。", mapGo);
            return;
        }

        // ---- 2. 找桌子贴片精灵 ----
        Sprite cutout = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (cutout == null)
        {
            AssetDatabase.ImportAsset(SpritePath);
            cutout = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }
        if (cutout == null)
        {
            Debug.LogError($"[前景遮挡] 找不到桌子贴片 {SpritePath}。");
            return;
        }

        // ---- 3. 算摆放位置：贴片在地图贴图上的像素框 → 世界坐标 ----
        Rect mapRect = mapSr.sprite.rect;                 // 单张精灵 = 整张贴图
        Bounds mapBounds = mapSr.sprite.bounds;
        Transform mapT = mapSr.transform;

        float pxCenterX = CropX + CropW * 0.5f;           // 贴片水平中心（像素）
        float pxBottomY = CropY + CropH;                  // 贴片底边（像素，自顶部算）

        float worldX = mapT.position.x + (pxCenterX / mapRect.width - 0.5f) * mapBounds.size.x;
        float worldY = mapT.position.y + (0.5f - pxBottomY / mapRect.height) * mapBounds.size.y;

        // ---- 4. 找玩家的排序层（必须和人同层同序，Y 排序才会在两者之间生效）----
        int layerId = 0;
        int order = 0;
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            SpriteRenderer playerSr = player.GetComponentInChildren<SpriteRenderer>();
            if (playerSr != null)
            {
                layerId = playerSr.sortingLayerID;
                order = playerSr.sortingOrder;
            }
        }
        else
        {
            Debug.LogWarning("[前景遮挡] 场景里没找到 Player，贴片先用默认排序层；摆好后请手动把它的 Sorting Layer 改成和人物一致。");
        }

        // ---- 5. 重复执行安全：先删旧的 ----
        GameObject old = GameObject.Find(ObjName);
        if (old != null)
            Undo.DestroyObjectImmediate(old);

        GameObject container = GameObject.Find(ContainerName);
        if (container == null)
            container = new GameObject(ContainerName);

        // ---- 6. 创建贴片物体 ----
        GameObject go = new GameObject(ObjName);
        Undo.RegisterCreatedObjectUndo(go, "生成桌子前景贴片");
        go.transform.SetParent(container.transform, true);
        go.transform.position = new Vector3(worldX, worldY, mapT.position.z);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = cutout;
        sr.sortingLayerID = layerId;
        sr.sortingOrder = order;

        Selection.activeGameObject = go;
        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

        Debug.Log($"[前景遮挡] 已生成 {ObjName}：位置 ({worldX:F2}, {worldY:F2})，" +
                  $"尺寸 {cutout.bounds.size.x:F2}×{cutout.bounds.size.y:F2}，" +
                  $"排序层与玩家一致（layerID {layerId}，order {order}）。\n" +
                  "进 Play 从桌子后面走过就能看到遮挡；确认没问题后 Ctrl+S 保存场景。", go);
    }
}
#endif
