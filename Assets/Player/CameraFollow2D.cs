using UnityEngine;

/// <summary>
/// 相机平滑跟随目标。挂到 Main Camera 上，把 target 拖成玩家即可。
/// </summary>
public class CameraFollow2D : MonoBehaviour
{
    [Tooltip("跟随的目标，一般是玩家")]
    public Transform target;

    [Tooltip("跟随的跟手程度，越大越紧贴，越小越飘")]
    public float smoothSpeed = 8f;

    [Tooltip("相机相对目标的偏移。z 决定相机离场景多远，2D 一般是 -10")]
    public Vector3 offset = new Vector3(0f, 0f, -10f);

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 wanted = target.position + offset;

        // 用 1-exp 做插值，跟随速度和帧率无关
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        Vector3 next = Vector3.Lerp(transform.position, wanted, t);
        next.z = offset.z;

        transform.position = next;
    }
}
