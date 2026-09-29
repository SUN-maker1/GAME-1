using UnityEngine;

/// <summary>碰撞区域的形状：方形 / 椭圆。</summary>
public enum ObstacleShape
{
    Box = 0,
    Ellipse = 1
}

/// <summary>
/// 自定义碰撞区域 —— 给地图上任意一块地方（家具、水池、花坛、柜台……）加碰撞，人物走不过去。
///
/// 【怎么用】
///   菜单 Tools ▸ 农场RPG ▸ 碰撞 ▸ 绘制碰撞区域… 打开窗口，
///   按住 Alt 在 Scene 视图里拖一下，就画出一块区域并自动生成碰撞体。
///   也可以直接给任意物体 Add Component ▸ 农场RPG ▸ 碰撞区域。
///
/// 【调整】
///   选中挂了这个组件的物体，Scene 视图里会出现手柄：
///     圆点 = 拉宽 / 拉高（从中心对称缩放）
///     角上圆点 = 同时改宽高
///     中间方块 = 挪动区域（改的是 Offset，物体本身不动）
///   改完自动重建碰撞体，不用手动点任何按钮。
///
/// 【实现说明】
///   方形 → BoxCollider2D；椭圆 → PolygonCollider2D（用 ellipseSegments 个点近似椭圆）。
///   碰撞体 = 你选中的区域「原样」：玩家自身的碰撞体会被物理自动顶在框边，
///   身体边缘恰好停在你选的框上 —— 你选多大，小人就走不到多大的地方（完全贴合）。
///   碰撞体由本组件托管：切形状时会把旧的删掉换成新的，不会留下两份。
///   只要角色身上有 Rigidbody2D + Collider2D，就能被挡住（静态碰撞体不需要刚体）。
/// </summary>
[ExecuteAlways]
[AddComponentMenu("农场RPG/碰撞区域 Obstacle Shape 2D")]
public class ObstacleShape2D : MonoBehaviour
{
    [Header("形状")]
    public ObstacleShape shape = ObstacleShape.Box;

    [Tooltip("方形的宽高；椭圆的两条直径（世界单位）")]
    public Vector2 size = new Vector2(2f, 1.5f);

    [Tooltip("区域中心相对物体原点的偏移")]
    public Vector2 offset = Vector2.zero;

    [Header("碰撞")]
    [Tooltip("勾上 = 只做触发检测不挡人（比如水面、传送感应区）；不勾 = 实心挡人")]
    public bool isTrigger = false;

    [Tooltip("椭圆用多少个点近似，越多越圆、开销略大")]
    [Range(6, 64)]
    public int ellipseSegments = 24;

    public PhysicsMaterial2D material;

    [SerializeField, HideInInspector] private BoxCollider2D boxCollider;
    [SerializeField, HideInInspector] private PolygonCollider2D polyCollider;

    // 改了参数就打标记，下一帧统一重建。
    // 不在 OnValidate 里直接 AddComponent —— 那会和序列化检查打架，可能报错。
    // [ExecuteAlways] 保证编辑模式下 Update 也会跑，所以改完立刻就能看到碰撞体更新。
    private bool pendingRebuild;

    private void Reset()
    {
        size = new Vector2(2f, 1.5f);
        pendingRebuild = true;
    }

    private void Awake()
    {
        if (Application.isPlaying) Rebuild();
    }

    private void OnEnable()
    {
        pendingRebuild = true;
    }

    private void OnValidate()
    {
        pendingRebuild = true;
    }

    private void Update()
    {
        if (!pendingRebuild) return;
        pendingRebuild = false;
        Rebuild();
    }

    /// <summary>按当前设置重建碰撞体。外部（编辑器工具）也可以直接调用。</summary>
    public void Rebuild()
    {
        size.x = Mathf.Max(0.05f, size.x);
        size.y = Mathf.Max(0.05f, size.y);

        if (shape == ObstacleShape.Box)
        {
            if (polyCollider != null)
            {
                DestroyManaged(polyCollider);
                polyCollider = null;
            }

            // 注意：不能用 "GetComponent<T>() ?? AddComponent" —— Unity 的 GetComponent
            // 找不到组件时返回假 null（C# 引用非 null），?? 不会触发，后面一访问就抛
            // MissingComponentException。必须用 Unity 重载过的 == 显式判断。
            if (boxCollider == null)
                boxCollider = GetComponent<BoxCollider2D>();
            if (boxCollider == null)
                boxCollider = gameObject.AddComponent<BoxCollider2D>();

            boxCollider.hideFlags = HideFlags.NotEditable;
            boxCollider.isTrigger = isTrigger;
            boxCollider.offset = offset;
            // 碰撞体 = 你选中的区域「原样」。玩家自身的碰撞体会被物理自动顶在框边，
            // 身体边缘恰好停在你选的框上 —— 你选多大，小人就走不到多大的地方（完全贴合）。
            boxCollider.size = size;
            boxCollider.sharedMaterial = material;
            boxCollider.usedByComposite = false;
        }
        else
        {
            if (boxCollider != null)
            {
                DestroyManaged(boxCollider);
                boxCollider = null;
            }

            if (polyCollider == null)
                polyCollider = GetComponent<PolygonCollider2D>();
            if (polyCollider == null)
                polyCollider = gameObject.AddComponent<PolygonCollider2D>();

            polyCollider.hideFlags = HideFlags.NotEditable;
            polyCollider.isTrigger = isTrigger;
            polyCollider.offset = offset;
            // 椭圆半径 = 你选的两条直径的一半，原样生成。
            polyCollider.SetPath(0, EllipsePoints(
                size.x * 0.5f,
                size.y * 0.5f,
                ellipseSegments));
            polyCollider.sharedMaterial = material;
            polyCollider.usedByComposite = false;
        }
    }

    /// <summary>椭圆的近似点集（半径 rx / ry，顺时针一圈）。</summary>
    public static Vector2[] EllipsePoints(float rx, float ry, int segments)
    {
        int n = Mathf.Max(6, segments);
        Vector2[] pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = (2f * Mathf.PI) * (i / (float)n);
            pts[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return pts;
    }

    private void DestroyManaged(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    /// <summary>Scene 视图里没有被选中时也能看到轮廓（选中后由编辑器脚本画出带填充的效果）。</summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = isTrigger ? new Color(0.35f, 0.9f, 1f, 0.5f) : new Color(1f, 0.4f, 0.2f, 0.55f);

        if (shape == ObstacleShape.Box)
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(offset.x, offset.y, 0f), new Vector3(size.x, size.y, 0f));
            Gizmos.matrix = old;
        }
        else
        {
            Vector2[] pts = EllipsePoints(size.x * 0.5f, size.y * 0.5f, ellipseSegments);
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 a = transform.TransformPoint(new Vector3(pts[i].x, pts[i].y, 0f));
                Vector3 b = transform.TransformPoint(new Vector3(pts[(i + 1) % pts.Length].x, pts[(i + 1) % pts.Length].y, 0f));
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
