#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 选中传送点后，在 Scene 视图里显示可拖的手柄：
///
///   两端圆点  拖动 = 调通道宽度（另一端不动）
///   中间方块  拖动 = 沿地图边挪动门的位置
///   外侧圆点  拖动 = 调边条厚度（往地图外伸多深）
///
/// 拖完自动写回 ScenePortal.lengthAlongEdge / thickness / edge，
/// 并打开 keepPositionAlongEdge（否则运行时贴边会把门挪回边正中间）。
/// </summary>
[CustomEditor(typeof(ScenePortal))]
public class ScenePortalEditor : Editor
{
    private void OnSceneGUI()
    {
        ScenePortal portal = target as ScenePortal;
        if (portal == null) return;

        Bounds map;
        bool hasMap = ScenePortalDesigner.TryGetMapBounds(out map);
        float z = hasMap ? map.center.z : portal.transform.position.z;

        MapEdge edge = ScenePortalDesigner.ResolveEdge(portal, map, hasMap);
        bool vertical = ScenePortalDesigner.IsVerticalEdge(edge);
        Vector3 axis = ScenePortalDesigner.AxisOf(edge);
        Vector3 outward = ScenePortalDesigner.OutwardOf(edge);

        Vector3 center = portal.transform.position;
        center.z = z;

        float length = Mathf.Max(0.3f, portal.lengthAlongEdge);
        float thick = Mathf.Max(0.2f, portal.thickness);

        // 先把当前范围画出来，方便看清楚在拖什么
        Vector2 size = vertical ? new Vector2(thick, length) : new Vector2(length, thick);
        Bounds b = new Bounds(center, new Vector3(size.x, size.y, 0f));
        Vector3[] corners =
        {
            new Vector3(b.min.x, b.min.y, z),
            new Vector3(b.max.x, b.min.y, z),
            new Vector3(b.max.x, b.max.y, z),
            new Vector3(b.min.x, b.max.y, z),
        };
        Handles.DrawSolidRectangleWithOutline(corners,
            new Color(0.3f, 0.85f, 1f, 0.12f),
            new Color(0.3f, 0.85f, 1f, 0.7f));

        float hs = HandleUtility.GetHandleSize(center) * 0.08f;

        Vector3 endA = center - axis * (length * 0.5f);
        Vector3 endB = center + axis * (length * 0.5f);
        Vector3 thickHandle = center + outward * thick;

        EditorGUI.BeginChangeCheck();

        Handles.color = new Color(0.3f, 1f, 0.6f);          // 宽度端点
        Vector3 newA = Handles.Slider(endA, axis, hs, Handles.SphereHandleCap, 0f);
        Vector3 newB = Handles.Slider(endB, axis, hs, Handles.SphereHandleCap, 0f);

        Handles.color = new Color(1f, 0.9f, 0.3f);          // 沿边移动
        Vector3 newCenter = Handles.Slider(center, axis, hs * 1.15f, Handles.CubeHandleCap, 0f);

        Handles.color = new Color(1f, 0.55f, 0.85f);        // 厚度
        Vector3 newThick = Handles.Slider(thickHandle, outward, hs, Handles.SphereHandleCap, 0f);

        if (!EditorGUI.EndChangeCheck()) return;

        float alongA0 = ScenePortalDesigner.AlongOf(endA, edge);
        float alongB0 = ScenePortalDesigner.AlongOf(endB, edge);
        float alongC0 = ScenePortalDesigner.AlongOf(center, edge);

        float newLength = length;
        float newAlong = alongC0;

        float deltaA = ScenePortalDesigner.AlongOf(newA, edge) - alongA0;
        float deltaB = ScenePortalDesigner.AlongOf(newB, edge) - alongB0;
        float deltaC = ScenePortalDesigner.AlongOf(newCenter, edge) - alongC0;

        if (Mathf.Abs(deltaA) > 0.0001f && Mathf.Abs(deltaA) >= Mathf.Abs(deltaB))
        {
            // 拖 A 端：B 端不动 → 中心跟着走，长度跟着变
            float a = ScenePortalDesigner.AlongOf(newA, edge);
            newLength = Mathf.Abs(alongB0 - a);
            newAlong = (a + alongB0) * 0.5f;
        }
        else if (Mathf.Abs(deltaB) > 0.0001f)
        {
            float bb = ScenePortalDesigner.AlongOf(newB, edge);
            newLength = Mathf.Abs(bb - alongA0);
            newAlong = (alongA0 + bb) * 0.5f;
        }
        else if (Mathf.Abs(deltaC) > 0.0001f)
        {
            newAlong = ScenePortalDesigner.AlongOf(newCenter, edge);
        }

        float newThickness = Vector3.Dot(newThick - center, outward);
        if (newThickness < 0.2f) newThickness = thick;

        ScenePortalDesigner.ApplyGeometry(portal, edge, newAlong, newLength, newThickness);

        Handles.Label(center + Vector3.up * hs * 3f,
                      $"{edge} 边　宽 {newLength:F2}　厚 {newThickness:F2}\n→ {portal.targetSceneName}");
    }
}
#endif
