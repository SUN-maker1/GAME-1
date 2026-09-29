#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 选中碰撞区域后，Scene 视图里出现可拖手柄：
///
///   上下左右圆点    拖动 = 拉高 / 拉宽（从中心对称缩放）
///   角上圆点        拖动 = 同时改宽高
///   中间方块        拖动 = 整块区域挪位置（改 Offset，物体本身不动）
///
/// 橙色 = 实心墙（挡人），青色 = 触发器（只感应不挡人）。
/// 拖完自动重建碰撞体并写回 ObstacleShape2D.size / offset。
/// </summary>
[CustomEditor(typeof(ObstacleShape2D))]
public class ObstacleShape2DEditor : Editor
{
    private void OnSceneGUI()
    {
        ObstacleShape2D ob = target as ObstacleShape2D;
        if (ob == null) return;

        Transform t = ob.transform;
        Vector3 center = t.position + (Vector3)ob.offset;
        center.z = t.position.z;

        float hx = ob.size.x * 0.5f;
        float hy = ob.size.y * 0.5f;
        float hs = HandleUtility.GetHandleSize(center) * 0.07f;

        bool trigger = ob.isTrigger;
        Color fill = trigger ? new Color(0.30f, 0.85f, 1f, 0.13f) : new Color(1f, 0.45f, 0.25f, 0.15f);
        Color line = trigger ? new Color(0.30f, 0.85f, 1f, 0.75f) : new Color(1f, 0.40f, 0.20f, 0.85f);

        Vector3 axisX = t.right;
        Vector3 axisY = t.up;

        if (ob.shape == ObstacleShape.Box)
        {
            Vector3[] corners =
            {
                center - axisX * hx - axisY * hy,
                center + axisX * hx - axisY * hy,
                center + axisX * hx + axisY * hy,
                center - axisX * hx + axisY * hy,
            };
            Handles.DrawSolidRectangleWithOutline(corners, fill, line);
        }
        else
        {
            DrawEllipse(center, axisX, axisY, hx, hy, fill, line);
        }

        EditorGUI.BeginChangeCheck();

        Vector2 newSize = ob.size;
        Vector2 newOffset = ob.offset;

        // —— 宽 / 高手柄（对称缩放）——
        Handles.color = new Color(0.35f, 1f, 0.65f);
        Vector3 right = Handles.Slider(center + axisX * hx, axisX, hs, Handles.SphereHandleCap, 0f);
        Vector3 up = Handles.Slider(center + axisY * hy, axisY, hs, Handles.SphereHandleCap, 0f);
        Vector3 left = Handles.Slider(center - axisX * hx, -axisX, hs, Handles.SphereHandleCap, 0f);
        Vector3 down = Handles.Slider(center - axisY * hy, -axisY, hs, Handles.SphereHandleCap, 0f);

        float dRight = Vector3.Dot(right - center, axisX);
        float dLeft = -Vector3.Dot(left - center, axisX);
        float dUp = Vector3.Dot(up - center, axisY);
        float dDown = -Vector3.Dot(down - center, axisY);

        if (Mathf.Abs(dRight - hx) > 0.0001f) newSize.x = Mathf.Max(0.05f, dRight * 2f);
        else if (Mathf.Abs(dLeft - hx) > 0.0001f) newSize.x = Mathf.Max(0.05f, dLeft * 2f);
        if (Mathf.Abs(dUp - hy) > 0.0001f) newSize.y = Mathf.Max(0.05f, dUp * 2f);
        else if (Mathf.Abs(dDown - hy) > 0.0001f) newSize.y = Mathf.Max(0.05f, dDown * 2f);

        // —— 角手柄：同时改宽高（保持比例）——
        Handles.color = new Color(1f, 0.85f, 0.30f);
        Vector3 diag = (axisX * hx + axisY * hy).normalized;
        if (diag.sqrMagnitude > 0.0001f)
        {
            Vector3 corner = Handles.Slider(center + axisX * hx + axisY * hy, diag, hs, Handles.SphereHandleCap, 0f);
            float dc = Vector3.Dot(corner - center, axisX);
            if (Mathf.Abs(dc - hx) > 0.0001f)
            {
                float k = Mathf.Max(0.05f, dc) / Mathf.Max(0.0001f, hx);
                newSize.x = Mathf.Max(0.05f, ob.size.x * k);
                newSize.y = Mathf.Max(0.05f, ob.size.y * k);
            }
        }

        // —— 中心方块：挪位置（改 offset）——
        Handles.color = new Color(1f, 1f, 1f, 0.9f);
        Vector3 moved = Handles.FreeMoveHandle(center, hs * 1.25f, Vector3.zero, Handles.CubeHandleCap);
        if ((moved - center).sqrMagnitude > 0.000001f)
        {
            Vector3 local = moved - t.position;
            newOffset = new Vector2(Vector3.Dot(local, axisX), Vector3.Dot(local, axisY));
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(ob, "调整碰撞区域");
        ob.size = newSize;
        ob.offset = newOffset;
        ob.Rebuild();
        EditorUtility.SetDirty(ob);

        Handles.Label(center + axisY * (hy + hs * 3f),
            $"{(ob.shape == ObstacleShape.Box ? "方形" : "椭圆")}　{ob.size.x:F2} × {ob.size.y:F2}\n" +
            $"{(ob.isTrigger ? "触发器（不挡人）" : "实体墙（人物身体无法进入，边缘恰好停在框上）")}");
    }

    private static void DrawEllipse(Vector3 center, Vector3 axisX, Vector3 axisY,
                                    float hx, float hy, Color fill, Color outline)
    {
        int seg = 40;
        Vector3[] pts = new Vector3[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = (2f * Mathf.PI) * (i / (float)seg);
            pts[i] = center + axisX * (Mathf.Cos(a) * hx) + axisY * (Mathf.Sin(a) * hy);
        }
        Handles.DrawAAConvexPolygon(pts);          // 半透明填充
        Handles.color = outline;
        for (int i = 0; i < seg; i++)
            Handles.DrawLine(pts[i], pts[(i + 1) % seg]);
    }

    [MenuItem("Tools/农场RPG/碰撞/把选中物体变成碰撞区域（按精灵大小）", false, 210)]
    private static void FromSelection()
    {
        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null)
            {
                Debug.LogWarning($"[碰撞区域] {go.name} 上没有 SpriteRenderer，跳过。");
                continue;
            }

            ObstacleShape2D ob = go.GetComponent<ObstacleShape2D>();
            if (ob == null) ob = Undo.AddComponent<ObstacleShape2D>(go);

            Undo.RecordObject(ob, "生成碰撞区域");
            Bounds b = sr.bounds;                                  // 世界包围盒
            Vector2 size = new Vector2(b.size.x, b.size.y);
            Vector2 offset = (Vector2)(go.transform.InverseTransformPoint(b.center));
            ob.shape = ObstacleShape.Box;
            ob.size = size;
            ob.offset = offset;
            ob.Rebuild();
            EditorUtility.SetDirty(ob);
            count++;
        }

        if (count > 0)
            Debug.Log($"[碰撞区域] 已给 {count} 个物体按精灵大小生成碰撞区域。");
    }

    [MenuItem("Tools/农场RPG/碰撞/把选中物体变成碰撞区域（按精灵大小）", true)]
    private static bool FromSelectionValid()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }
}
#endif
