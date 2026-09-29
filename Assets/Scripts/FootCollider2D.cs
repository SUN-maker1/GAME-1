using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 脚下碰撞体。默认「自动按贴图算」：读贴图的实际轮廓，取底部一小段作为碰撞范围，
/// 不用手动填数字。挂上就完事，也可以批量挂到所有树 / 角色上。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class FootCollider2D : MonoBehaviour
{
    public enum ShapeType { Capsule, Box }

    [Header("形状")]
    public ShapeType shape = ShapeType.Capsule;
    public bool isTrigger = false;

    [Header("自动按贴图计算")]
    [Tooltip("勾上就不用管下面的数字了：读贴图轮廓，自动算出脚下那一块的碰撞范围")]
    public bool autoFromSprite = true;

    [Tooltip("取贴图底部多少比例作为碰撞高度。0.25 = 底部四分之一；树的树干矮、人想贴地就调小")]
    [Range(0.05f, 1f)] public float autoBottomRatio = 0.25f;

    [Tooltip("算出的宽度再乘这个系数。想让碰撞比看起来更宽松就调小，比如 0.7")]
    [Range(0.1f, 2f)] public float autoWidthScale = 0.8f;

    [Header("手动尺寸（关掉自动后生效，地面一格 = 1）")]
    [Min(0.05f)] public float width = 0.45f;
    [Min(0.05f)] public float height = 0.3f;

    [Header("位置")]
    [Tooltip("碰撞体底部相对物体原点的高度。角色 pivot 在脚底，自动算出来一般是 0")]
    public float bottomOffset = 0f;

    [Tooltip("水平偏移，自动算出来一般也是 0")]
    public float centerX = 0f;

    [Header("清理")]
    [Tooltip("把物体上其他碰撞体删掉，避免和这个重复的挡路（比如树自带的 PolygonCollider）")]
    public bool removeOtherColliders = true;

    [Header("显示")]
    public bool showGizmo = true;
    public Color gizmoColor = new Color(0.15f, 0.85f, 0.5f, 0.9f);

    /// <summary>碰撞体中心（局部坐标）</summary>
    public Vector2 LocalCenter => new Vector2(centerX, bottomOffset + Mathf.Max(0.05f, height) * 0.5f);

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    /// <summary>重新计算并写入碰撞体。改完参数或批量处理后调用。</summary>
    public void Apply()
    {
        if (autoFromSprite)
        {
            if (TryAutoFit(out float aw, out float ah, out float ab, out float acx))
            {
                width = aw;
                height = ah;
                bottomOffset = ab;
                centerX = acx;
            }
        }

        float w = Mathf.Max(0.05f, width);
        float h = Mathf.Max(0.05f, height);
        Vector2 size = new Vector2(w, h);
        Vector2 offset = new Vector2(centerX, bottomOffset + h * 0.5f);

        Collider2D keep;

        if (shape == ShapeType.Capsule)
        {
            CapsuleCollider2D cap = GetComponent<CapsuleCollider2D>();
            if (cap == null) cap = gameObject.AddComponent<CapsuleCollider2D>();
            cap.size = size;
            cap.offset = offset;
            cap.isTrigger = isTrigger;
            keep = cap;
            RemoveAll<BoxCollider2D>();
        }
        else
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) box = gameObject.AddComponent<BoxCollider2D>();
            box.size = size;
            box.offset = offset;
            box.isTrigger = isTrigger;
            keep = box;
            RemoveAll<CapsuleCollider2D>();
        }

        if (removeOtherColliders && !HasComponentRequiringCollider2D())
        {
            Collider2D[] all = GetComponents<Collider2D>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i] == keep) continue;
                SmartDestroy(all[i]);
            }
        }
    }

    private bool TryAutoFit(out float w, out float h, out float bottom, out float cx)
    {
        w = width;
        h = height;
        bottom = bottomOffset;
        cx = centerX;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        Sprite sprite = sr != null ? sr.sprite : null;
        if (sprite == null) return false;

        List<Vector2> pts = new List<Vector2>();
        int pathCount = sprite.GetPhysicsShapeCount();
        for (int i = 0; i < pathCount; i++)
        {
            List<Vector2> one = new List<Vector2>();
            sprite.GetPhysicsShape(i, one);
            pts.AddRange(one);
        }

        // 有轮廓数据：取底部一段的点算宽度
        if (pts.Count >= 3)
        {
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i].y < minY) minY = pts[i].y;
                if (pts[i].y > maxY) maxY = pts[i].y;
            }

            float cutoff = minY + (maxY - minY) * autoBottomRatio;

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            bool any = false;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i].y > cutoff) continue;
                if (pts[i].x < minX) minX = pts[i].x;
                if (pts[i].x > maxX) maxX = pts[i].x;
                any = true;
            }
            if (!any) return false;

            bottom = minY;
            h = cutoff - minY;
            cx = (minX + maxX) * 0.5f;
            w = (maxX - minX) * autoWidthScale;
            return true;
        }

        // 没有轮廓数据：按贴图矩形兜底
        float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
        float rectW = sprite.rect.width / ppu;
        float rectH = sprite.rect.height / ppu;

        bottom = -sprite.pivot.y / ppu;
        h = rectH * autoBottomRatio;
        cx = (sprite.rect.width * 0.5f - sprite.pivot.x) / ppu;
        w = rectW * autoWidthScale;
        return true;
    }

    private void RemoveAll<T>() where T : Collider2D
    {
        T[] others = GetComponents<T>();
        for (int i = 0; i < others.Length; i++)
            SmartDestroy(others[i]);
    }

    /// <summary>
    /// 检查物体上是否有其他组件通过 [RequireComponent] 依赖 Collider2D。
    /// 如果有，就不能删掉其他碰撞体，否则那些组件会报错甚至被 Unity 移除。
    /// </summary>
    private bool HasComponentRequiringCollider2D()
    {
        foreach (Component comp in GetComponents<Component>())
        {
            if (comp == null || comp == this) continue;
            foreach (RequireComponent attr in comp.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (RequireComponentNeedsType(attr, typeof(Collider2D)))
                    return true;
            }
        }
        return false;
    }

    private static bool RequireComponentNeedsType(RequireComponent attr, System.Type type)
    {
        var fields = typeof(RequireComponent).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (f.FieldType == typeof(System.Type) && f.GetValue(attr) == type)
                return true;
        }
        return false;
    }

    private static void SmartDestroy(Component c)
    {
        if (c == null) return;
        if (Application.isPlaying) Destroy(c);
        else DestroyImmediate(c);
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmo) return;

        float w = Mathf.Max(0.05f, width);
        float h = Mathf.Max(0.05f, height);

        Vector3 center = transform.TransformPoint(new Vector3(LocalCenter.x, LocalCenter.y, 0f));
        Vector3 worldSize = Vector3.Scale(new Vector3(w, h, 0f), transform.lossyScale);

        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(center, worldSize);
    }
}
