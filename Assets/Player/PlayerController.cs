using UnityEngine;

/// <summary>
/// WASD / 方向键控制的人物移动。
/// 依赖 Rigidbody2D（重力设为 0，锁定旋转），这样撞到树的碰撞体会自然停住，不会穿模。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("移动")]
    [Tooltip("移动速度，单位是「世界单位/秒」。地面一格是 1，所以 5 大概是每秒 5 格")]
    public float moveSpeed = 5f;

    [Tooltip("斜着走时减速，避免对角线比直线快 1.4 倍")]
    public bool normalizeDiagonal = true;

    [Header("表现")]
    [Tooltip("按左右方向自动翻转贴图（换侧面图后就有用了）")]
    public bool flipByDirection = true;

    private Rigidbody2D _rb;
    private SpriteRenderer _sr;
    private Vector2 _input;
    private bool _facingRight = true;

    /// <summary>当前输入方向，其他脚本（比如动画）可以读它</summary>
    public Vector2 CurrentInput => _input;

    /// <summary>当前是否在移动</summary>
    public bool IsMoving => _input.sqrMagnitude > 0.0001f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _sr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        float h = 0f;
        float v = 0f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;

        _input = new Vector2(h, v);
        if (normalizeDiagonal && _input.sqrMagnitude > 1f)
            _input.Normalize();

        if (flipByDirection && _sr != null && Mathf.Abs(h) > 0.01f)
        {
            bool wantRight = h > 0f;
            if (wantRight != _facingRight)
            {
                _facingRight = wantRight;
                _sr.flipX = !wantRight;
            }
        }
    }

    private void FixedUpdate()
    {
        // 直接设速度：松键立刻停，不会有滑步
        _rb.velocity = _input * moveSpeed;
    }
}
