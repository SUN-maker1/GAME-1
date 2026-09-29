using UnityEditor;
using UnityEngine;

/// <summary>
/// 给 FootCollider2D 加 Scene 视图里的拖拽手柄：
/// 选中物体后，拖左右方块改宽度、拖上方方块改高度、拖下方方块改贴地高度。
/// </summary>
[CustomEditor(typeof(FootCollider2D))]
public class FootCollider2DEditor : Editor
{
    private const float CapSize = 0.07f;

    private void OnSceneGUI()
    {
        FootCollider2D fc = (FootCollider2D)target;
        if (!fc.showGizmo) return;

        Transform t = fc.transform;
        float w = Mathf.Max(0.05f, fc.width);
        float h = Mathf.Max(0.05f, fc.height);
        float b = fc.bottomOffset;
        float cx = fc.centerX;

        Vector3 axisX = t.TransformDirection(Vector3.right);
        Vector3 axisY = t.TransformDirection(Vector3.up);

        Vector3 rightMid = t.TransformPoint(new Vector3(cx + w * 0.5f, b + h * 0.5f, 0f));
        Vector3 topMid = t.TransformPoint(new Vector3(cx, b + h, 0f));
        Vector3 botMid = t.TransformPoint(new Vector3(cx, b, 0f));

        Handles.color = fc.gizmoColor;

        Undo.RecordObject(fc, "调整碰撞体积");

        // 宽度
        EditorGUI.BeginChangeCheck();
        Vector3 movedRight = Handles.Slider(rightMid, axisX, CapSize, Handles.CubeHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            fc.width = Mathf.Max(0.05f, (t.InverseTransformPoint(movedRight).x - cx) * 2f);
            fc.Apply();
        }

        // 高度
        EditorGUI.BeginChangeCheck();
        Vector3 movedTop = Handles.Slider(topMid, axisY, CapSize, Handles.CubeHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            fc.height = Mathf.Max(0.05f, t.InverseTransformPoint(movedTop).y - b);
            fc.Apply();
        }

        // 贴地高度
        EditorGUI.BeginChangeCheck();
        Vector3 movedBot = Handles.Slider(botMid, axisY, CapSize, Handles.CubeHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            fc.bottomOffset = t.InverseTransformPoint(movedBot).y;
            fc.Apply();
        }

        GUIStyle label = new GUIStyle(EditorStyles.boldLabel);
        label.normal.textColor = Color.white;
        Handles.Label(topMid + Vector3.up * 0.18f, "宽 " + fc.width.ToString("0.00") + "   高 " + fc.height.ToString("0.00"), label);
    }

    [MenuItem("Tools/给选中物体加碰撞体积调节器", true)]
    private static bool ValidateAdd()
    {
        return Selection.gameObjects.Length > 0;
    }

    [MenuItem("Tools/给选中物体加碰撞体积调节器")]
    private static void AddToSelected()
    {
        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go.GetComponent<FootCollider2D>() != null) continue;
            Undo.AddComponent<FootCollider2D>(go);
            count++;
        }

        if (count == 0)
        {
            Debug.Log("选中的物体上已经有碰撞调节器了。");
            return;
        }

        Debug.Log("已给 " + count + " 个物体加上碰撞体积调节器。选中它，在 Scene 视图拖绿色方块就能调。"
                  + "如果它是预制体实例，调好后记得 Overrides → Apply All 保存回预制体。");
    }
}
