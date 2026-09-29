using UnityEngine;

/// <summary>
/// 2D 俯视相机跟随：平滑跟住玩家，并把相机钳制在地图范围内，永远不会看到地图外的黑边。
///
/// 核心逻辑：
///   1. 每帧算出「玩家位置 + 偏移」作为相机的目标点
///   2. 用 SmoothDamp 平滑靠过去（不会生硬跟死）
///   3. 按相机的可视半宽/半高，把目标点 Clamp 进地图矩形 —— 这就是「走到边缘相机停住」的原理
///
/// 边界两种给法二选一：
///   - 手动矩形 manualBounds：简单直接，矩形地图用这个
///   - 拖一个 PolygonCollider2D 到 boundaryShape：不规则地图用这个（勾掉 useManualBounds）
///     注意这个 collider 只是用来算范围的，别让它真的挡住玩家：把它放在单独的物体上，
///     并且不要给那个物体加 Rigidbody2D。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("跟随目标")]
    [Tooltip("留空会自动找场上的 PersistentPlayer")]
    public Transform target;
    public bool autoFindPlayer = true;

    [Header("跟随手感")]
    [Tooltip("平滑时间，越小跟得越紧，0.25 左右比较舒服")]
    public float smoothTime = 0.25f;
    public Vector2 offset = Vector2.zero;

    [Tooltip("按玩家移动方向往前多看一点，0 = 关闭")]
    [Range(0f, 6f)]
    public float lookAheadDistance = 0f;
    public float lookAheadSmoothTime = 0.35f;

    [Header("边界")]
    public bool useManualBounds = true;
    public Rect manualBounds = new Rect(-32f, -32f, 64f, 64f);
    [Tooltip("不规则地图用这个；存在时优先于 manualBounds")]
    public PolygonCollider2D boundaryShape;

    [Header("其他")]
    [Tooltip("用 FixedUpdate 跟随，和物理步进同步，角色高速移动时更稳")]
    public bool useFixedUpdate = false;
    [Tooltip("地图比屏幕小时，自动把相机居中，避免边界计算抖动")]
    public bool centerIfMapSmallerThanScreen = true;

    [Header("像素完美（只给纯像素风游戏开）")]
    [Tooltip("把相机位置对齐到像素网格。只在你画的是 16×16 / 32×32 那种像素风 sprite 时才开。" +
             "平滑插画 / 手绘风 chibi 角色要关掉，否则相机按像素格跳，走动会抖。")]
    public bool pixelPerfect = false;

    [Tooltip("图片里「每 1 世界单位占几个像素」，就是贴图的 Pixels Per Unit（PPU）。" +
             "所有图都用同一个值，画面才统一。角色和地图的 PPU 填这里。")]
    public float pixelsPerUnit = 100f;

    [Tooltip("勾选后自动把 Orthographic Size 设成 屏幕高 ÷ (2 × PPU)，让 1 世界单位 = PPU 屏幕像素。" +
             "只在像素风游戏里开。高分辨率手绘插画请关掉，自己手动设 Orthographic Size（比如 6），" +
             "否则高分辨率贴图会被 1:1 放大到满屏，看起来又糊又大。")]
    public bool autoFitOrthoSize = false;

    [Tooltip("自动模式下的参考屏幕高度。窗口高度和它不一致时，会按比例微调视野，" +
             "保证缩放倍率始终是整数。默认 1080。")]
    public float referenceScreenHeight = 1080f;

    /// <summary>自动模式实际使用的屏幕高度：窗口越大视野越大，但每单位像素数不变</summary>
    private float EffectiveScreenHeight()
        => autoFitOrthoSize ? (float)Screen.height : referenceScreenHeight;

    private Camera cam;
    private Rigidbody2D targetBody;
    private Vector3 velocity;
    private Vector2 lookAhead;
    private Vector2 lookAheadVelocity;

    private void Reset()
    {
        cam = GetComponent<Camera>();
        useManualBounds = true;
        manualBounds = new Rect(-32f, -32f, 64f, 64f);
    }

    private void OnEnable()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        if (!Application.isPlaying) return;
        AcquireTarget();
        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (useFixedUpdate) return;
        if (!Application.isPlaying) return;
        UpdateOrthoSize();
        Follow(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (!useFixedUpdate) return;
        if (!Application.isPlaying) return;
        UpdateOrthoSize();
        Follow(Time.fixedDeltaTime);
    }

    /// <summary>
    /// 让「1 世界单位 = PPU 个屏幕像素」，也就是缩放倍率恒为 1.0。
    /// 屏幕高 H、正交半高 S 时，每单位像素数 = H / (2S)。
    /// 令它等于 PPU → S = H / (2 × PPU)。
    /// 这样贴图既不被放大也不被缩小，是像素最干净的状态。
    /// </summary>
    private void UpdateOrthoSize()
    {
        if (!autoFitOrthoSize || cam == null || !cam.orthographic) return;

        float h = EffectiveScreenHeight();
        if (h <= 0f || pixelsPerUnit <= 0f) return;

        float wanted = h / (2f * pixelsPerUnit);
        if (Mathf.Abs(cam.orthographicSize - wanted) > 0.0001f)
            cam.orthographicSize = wanted;
    }

    /// <summary>找到要跟随的玩家</summary>
    public void AcquireTarget()
    {
        if (target == null)
        {
            // 优先用跨场景存活的玩家
            if (autoFindPlayer && PersistentPlayer.Instance != null)
                target = PersistentPlayer.Instance.transform;

            // 其次按标签找（标签不存在时 FindGameObjectWithTag 会抛异常，这里兜住）
            if (target == null && autoFindPlayer)
            {
                try
                {
                    GameObject p = GameObject.FindGameObjectWithTag("Player");
                    if (p != null) target = p.transform;
                }
                catch (UnityException)
                {
                    // 工程里没有 Player 标签，忽略
                }
            }
        }

        // 目标可能中途被换掉，这里保证刚体引用跟着更新
        if (target != null && (targetBody == null || targetBody.transform != target))
            targetBody = target.GetComponent<Rigidbody2D>();
    }

    private void Follow(float deltaTime)
    {
        AcquireTarget();
        if (target == null || cam == null) return;

        Vector3 desired = target.position + (Vector3)offset;

        // 前瞻：按玩家当前速度方向往前挪一点，让玩家看到前方更多路
        if (lookAheadDistance > 0f)
        {
            Vector2 dir = Vector2.zero;
            if (targetBody != null)
            {
                if (targetBody.velocity.sqrMagnitude > 0.01f)
                    dir = targetBody.velocity.normalized;
            }
            Vector2 wanted = dir * lookAheadDistance;
            lookAhead = Vector2.SmoothDamp(lookAhead, wanted, ref lookAheadVelocity, lookAheadSmoothTime, Mathf.Infinity, deltaTime);
            desired += (Vector3)lookAhead;
        }

        Vector3 smoothed = Vector3.SmoothDamp(transform.position, desired, ref velocity, Mathf.Max(0.0001f, smoothTime), Mathf.Infinity, deltaTime);

        Vector3 clamped = ClampToBounds(smoothed);
        Vector3 snapped = AlignToPixel(clamped);
        transform.position = new Vector3(snapped.x, snapped.y, transform.position.z);
    }

    /// <summary>
    /// 把相机位置吸附到像素网格上。
    /// 不清这个，相机停在第 1.37 个像素的位置，精灵的像素就会被拉成 1.5 个屏幕像素宽，
    /// 走动时看起来就是糊的、边缘发毛。开了之后每个像素都是完整的方块。
    /// </summary>
    private Vector3 AlignToPixel(Vector3 pos)
    {
        if (!pixelPerfect || cam == null || !cam.orthographic) return pos;

        float unit = GetWorldPerPixel();
        if (unit <= 0f) return pos;

        float x = Mathf.Round(pos.x / unit) * unit;
        float y = Mathf.Round(pos.y / unit) * unit;
        return new Vector3(x, y, pos.z);
    }

    /// <summary>1 个屏幕像素代表多少世界单位。越小说明画面被放得越大。</summary>
    public float GetWorldPerPixel()
    {
        if (cam == null || !cam.orthographic) return 0f;
        return (2f * cam.orthographicSize) / EffectiveScreenHeight();
    }

    /// <summary>当前每 1 个世界单位占几个屏幕像素。想让贴图 1:1 不缩放，PPU 就该等于它。</summary>
    public float GetPixelsPerUnit()
    {
        float unit = GetWorldPerPixel();
        return unit > 0f ? 1f / unit : 0f;
    }

    /// <summary>立刻贴到目标位置，不做平滑。切场景落位时用，避免相机飞过去。</summary>
    public void SnapToTarget()
    {
        AcquireTarget();
        if (target == null || cam == null) return;

        lookAhead = Vector2.zero;
        lookAheadVelocity = Vector2.zero;
        velocity = Vector3.zero;

        Vector3 desired = target.position + (Vector3)offset;
        Vector3 clamped = ClampToBounds(desired);
        Vector3 snapped = AlignToPixel(clamped);
        transform.position = new Vector3(snapped.x, snapped.y, transform.position.z);
    }

    private Vector3 ClampToBounds(Vector3 pos)
    {
        if (!cam.orthographic) return pos;

        Rect b = GetBounds();
        if (b.width <= 0f || b.height <= 0f) return pos;

        Vector2 half = GetCameraHalfSize();

        float minX = b.min.x + half.x;
        float maxX = b.max.x - half.x;
        float minY = b.min.y + half.y;
        float maxY = b.max.y - half.y;

        float x, y;

        if (minX > maxX)
            x = centerIfMapSmallerThanScreen ? b.center.x : Mathf.Clamp(pos.x, maxX, minX);
        else
            x = Mathf.Clamp(pos.x, minX, maxX);

        if (minY > maxY)
            y = centerIfMapSmallerThanScreen ? b.center.y : Mathf.Clamp(pos.y, maxY, minY);
        else
            y = Mathf.Clamp(pos.y, minY, maxY);

        return new Vector3(x, y, pos.z);
    }

    /// <summary>相机可视范围的一半（世界单位）</summary>
    private Vector2 GetCameraHalfSize()
    {
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        return new Vector2(halfW, halfH);
    }

    /// <summary>当前生效的地图边界</summary>
    public Rect GetBounds()
    {
        if (!useManualBounds && boundaryShape != null)
        {
            Bounds bb = boundaryShape.bounds;
            return new Rect(bb.min.x, bb.min.y, bb.size.x, bb.size.y);
        }
        return manualBounds;
    }

    private void OnDrawGizmosSelected()
    {
        Rect b = GetBounds();
        Vector3 c = new Vector3(b.center.x, b.center.y, 0f);
        Vector3 s = new Vector3(b.size.x, b.size.y, 0f);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(c, s);
    }
}
