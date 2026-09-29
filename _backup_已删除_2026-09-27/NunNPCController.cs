using UnityEngine;

/// <summary>
/// 修女 NPC 行为脚本：待机 / 祈祷 / 行走 三套动作。
/// 动作本体在 Animator 里，这个脚本只负责「发指令」，所以逻辑很薄。
///
/// 它用到的两个 Animator 参数（NunNPC.controller 里已经建好了）：
///   Speed     Float   0 = 站住不动，> 0 = 正在走   → 决定 Idle 和 Walk 之间怎么切
///   IsPraying Bool    true = 要祈祷                → 决定 Idle → IdleToPray → Pray → PrayToIdle → Idle
///
/// 用法：
///   1. 把本脚本挂到带 SpriteRenderer + Animator 的物体上
///      （菜单 Tools ▸ 修女NPC ▸ ② 在场景里生成 NPC 物体 可以一键生成）；
///   2. Animator 的 Controller 指向 Assets/Animation/NunNPC/NunNPC.controller；
///   3. 进游戏后：WASD / 方向键 走动，按 E 进入 / 退出祈祷。
///      如果这个 NPC 不需要你操作，把「移动」和「祈祷」里的两个勾去掉，
///      只用 PlayPrayAnimation() / StopPrayAnimation() 从外部控制就行。
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class NunNPCController : MonoBehaviour
{
    // ───────────────────────── 移动 ─────────────────────────
    [Header("移动（WASD / 方向键）")]
    [Tooltip("勾上＝脚本自己读键盘并移动这个物体；去掉＝只播动画，移动交给别的脚本（例如 PlayerMovement）")]
    public bool controlMovement = true;

    [Tooltip("移动速度（单位 / 秒）")]
    public float moveSpeed = 4f;

    [Tooltip("往左走时把贴图水平翻转，做出转身效果")]
    public bool flipWhenTurnLeft = true;

    [Tooltip("顶视角 2D 游戏要关掉重力，否则人物会一直往下掉")]
    public bool forceZeroGravity = true;

    // ───────────────────────── 祈祷 ─────────────────────────
    [Header("祈祷")]
    [Tooltip("勾上＝按 E 键进入 / 退出祈祷")]
    public bool manualPrayKey = true;

    [Tooltip("触发祈祷的按键")]
    public KeyCode prayKey = KeyCode.E;

    [Tooltip("勾上＝没人管的时候自己也会祈祷一会儿（纯 NPC 用）")]
    public bool autoPray = false;

    [Tooltip("待机多少秒之后开始祈祷")]
    public float autoPrayIdleSeconds = 6f;

    [Tooltip("保持祈祷多少秒之后自动转回待机")]
    public float autoPrayHoldSeconds = 8f;

    // ─────────────────── Animator 参数名 ───────────────────
    [Header("Animator 参数名（和控制器里保持一致，一般不用改）")]
    public string speedParam = "Speed";
    public string prayingParam = "IsPraying";
    public string idleStateName = "Idle";
    public string prayStateName = "Pray";

    // ───────────────────────── 内部状态 ─────────────────────────
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D body;

    private Vector2 moveInput;    // 当前这一帧的输入方向
    private bool wantPray;        // 我们希望 NPC 处于「祈祷」状态
    private bool hasSpeedParam;
    private bool hasPrayParam;
    private float idleTimer;      // 已经站了多久 / 已经祈祷了多久

    /// <summary>当前是否停在「保持祈祷」这个动作里</summary>
    public bool IsPraying =>
        animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName(prayStateName);

    /// <summary>当前是否在走</summary>
    public bool IsWalking => moveInput.sqrMagnitude > 0.01f;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        body = GetComponent<Rigidbody2D>();

        // 控制器里没建对应参数就不同步，免得刷警告
        hasSpeedParam = HasParameter(animator, speedParam);
        hasPrayParam = HasParameter(animator, prayingParam);

        if (body != null && forceZeroGravity)
        {
            body.gravityScale = 0f;                                        // 俯视视角：不要重力
            body.freezeRotation = true;                                    // 撞墙也别转圈
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;      // 移动更顺滑
        }
    }

    private void Update()
    {
        // 1. 读键盘：A/D 是水平轴，W/S 是垂直轴（默认 Input Manager 已配好，不需要装包）
        if (controlMovement)
        {
            moveInput.x = Input.GetAxisRaw("Horizontal");
            moveInput.y = Input.GetAxisRaw("Vertical");

            // 斜着走时归一化，否则对角线速度会是根号 2 倍
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
        }
        else
        {
            moveInput = Vector2.zero;
        }

        // 2. 左右转身：往左走翻转贴图
        if (flipWhenTurnLeft && spriteRenderer != null && Mathf.Abs(moveInput.x) > 0.01f)
            spriteRenderer.flipX = moveInput.x < 0f;

        // 3. 走路优先：一动起来就打断祈祷，免得边走边祷告很怪
        if (IsWalking)
        {
            wantPray = false;
            idleTimer = 0f;
        }

        // 4. 手动按 E 切换祈祷（走动过程中不响应）
        if (manualPrayKey && !IsWalking && Input.GetKeyDown(prayKey))
        {
            wantPray = !wantPray;
            idleTimer = 0f;
        }

        // 5. 自动祈祷：发呆久了就自己祷告一会儿
        TickAutoPray();

        // 6. 把结果交给 Animator，剩下的交给状态机自己跑
        if (hasSpeedParam) animator.SetFloat(speedParam, moveInput.magnitude);
        if (hasPrayParam) animator.SetBool(prayingParam, wantPray);
    }

    /// <summary>自动祈祷计时：只在「已经站定」的时候算，而且要在 Idle / Pray 真正到位之后才开始数。</summary>
    private void TickAutoPray()
    {
        if (!autoPray || IsWalking) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        if (!wantPray)
        {
            // 待机够久 → 开始祈祷
            if (state.IsName(idleStateName))
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= autoPrayIdleSeconds)
                {
                    wantPray = true;
                    idleTimer = 0f;
                }
            }
        }
        else
        {
            // 祈祷够久 → 转回待机
            if (state.IsName(prayStateName))
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= autoPrayHoldSeconds)
                {
                    wantPray = false;
                    idleTimer = 0f;
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (!controlMovement) return;

        if (body != null)
        {
            // 物理移动：直接改速度，这样才会正常产生碰撞
            body.velocity = moveInput * moveSpeed;
        }
        else
        {
            // 没有刚体就直接挪 Transform（不会撞墙，适合纯测试）
            transform.Translate(moveInput * moveSpeed * Time.fixedDeltaTime, Space.World);
        }
    }

    // ─────────────────---- 给外部调用的接口 ----─────────────────

    /// <summary>开始祈祷。会依次播 Idle → IdleToPray → Pray。</summary>
    public void PlayPrayAnimation() => wantPray = true;

    /// <summary>结束祈祷。会依次播 Pray → PrayToIdle → Idle。</summary>
    public void StopPrayAnimation() => wantPray = false;

    /// <summary>直接指定要 / 不要祈祷（对话、剧情触发用）。</summary>
    public void SetPraying(bool value) => wantPray = value;

    private static bool HasParameter(Animator anim, string name)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;

        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.name == name) return true;
        }
        return false;
    }
}
