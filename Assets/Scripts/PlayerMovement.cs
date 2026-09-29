using UnityEngine;

/// <summary>
/// 挂在「人物」GameObject 上，用 WASD / 方向键移动。
/// 有 Rigidbody2D 时走物理移动（能正确产生碰撞），没有就直接改 Transform。
///
/// 【动画参数】控制器 Assets/Animation/Player.controller 用 4 个参数：
///   Speed   = float，移动强度（0 = 站立，~1 = 走动）
///   DirX    = float，水平分量（-1 左 / +1 右），走动时才非 0
///   DirY    = float，垂直分量（-1 下 / +1 上），走动时才非 0
///   FaceDir = int，角色朝向：0 前(下) / 1 后(上) / 2 左 / 3 右
///             —— 这个是「记忆值」：松开按键后不会归零，
///                所以往哪个方向走停下来的，就保持面朝那个方向站着。
///
/// 【状态】Idle_Front / Idle_Back / Idle_Left / Idle_Right（四个朝向的站立）
///        Walk_Front / Walk_Back / Walk_Left / Walk_Right（四个朝向的走路）
///        Attack_Front / Attack_Back / Attack_Left / Attack_Right（攻击，鼠标左键触发，
///        由 FaceDir 选状态；四个状态目前共用 Player_Attack_Front 动画，以后有
///        朝向攻击图就替换各状态 Motion）
///
/// 【左右镜像】见 Inspector 的 Mirror Sprite Left/Right：素材的「向左/右走」
///            画反了（角色面朝移动方向的反侧）时勾选对应开关即可，默认都不勾。
///            攻击图是另一套素材，用 Mirror Attack Left/Right 单独控制（默认只勾左边）。
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    /// <summary>朝向枚举，对应控制器里的 FaceDir 整数参数</summary>
    public enum Facing { Front = 0, Back = 1, Left = 2, Right = 3 }

    [Header("移动速度（单位 / 秒）—— 可在 Inspector 直接拖动调节")]
    [Range(0f, 20f)]
    public float moveSpeed = 5f;

    [Header("Animator 参数名")]
    public string speedParamName = "Speed";
    public string dirXParamName = "DirX";
    public string dirYParamName = "DirY";
    public string faceDirParamName = "FaceDir";

    [Header("朝向")]
    [Tooltip("进入游戏时角色面朝的方向")]
    public Facing initialFacing = Facing.Front;

    [Tooltip("勾选 = 松手停下后保持最后走的那个朝向（不会自动转回正面）")]
    public bool keepFacingWhenStopped = true;

    [Header("左右朝向镜像补救（素材画反了就用它）")]
    [Tooltip("往左走时角色却在倒退（面朝右）=「向左走」那张素材画反了，勾选它把精灵水平翻转。\n往右走时同理勾右边的。上下朝向不受影响，永远不翻转。")]
    public bool mirrorSpriteLeft = false;
    public bool mirrorSpriteRight = false;

    [Tooltip("攻击图和走路图是两套素材，所以要单独设：\n" +
             "攻击图只画了朝下/朝右的话，朝左挥刀就需要水平翻转 —— 默认勾上左边的。\n" +
             "上下朝向攻击不翻转。")]
    public bool mirrorAttackLeft = true;
    public bool mirrorAttackRight = false;

    [Header("攻击（鼠标左键单击）")]
    [Tooltip("勾选 = 鼠标左键点一下触发 Attack trigger，播攻击动画")]
    public bool enableAttack = true;

    [Tooltip("攻击触发参数名（控制器里的 Trigger）")]
    public string attackTriggerName = "Attack";

    [Tooltip("攻击中布尔参数名（控制器里的 Bool）。攻击期间用它把站立/走路过渡全部锁住")]
    public string attackingBoolName = "Attacking";

    [Tooltip("攻击动作锁定移动的时长（秒）。\nattack.png 是 5 帧 @ 12fps ≈ 0.42 秒；以后换了帧数不同的攻击图记得改这里")]
    public float attackDuration = 0.42f;

    [Header("地图边界（useBoundary 勾上才生效）")]
    [Tooltip("边界矩形，左下角 xMin/yMin，右上角 xMax/yMax。")]
    public Rect boundary = new Rect(-32f, -32f, 64f, 64f);
    public bool useBoundary = false;

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Animator animator;

    private bool hasSpeedParam;
    private bool hasDirXParam;
    private bool hasDirYParam;
    private bool hasFaceDirParam;
    private bool hasAttackTrigger;
    private bool hasAttackingBool;

    private Vector2 moveInput;

    /// <summary>当前朝向（停手后不会被清掉）</summary>
    private Facing facing;

    /// <summary>攻击剩余时间；> 0 表示攻击动作进行中（移动被锁）</summary>
    private float attackTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();

        hasSpeedParam   = animator != null && HasParameter(animator, speedParamName, AnimatorControllerParameterType.Float);
        hasDirXParam    = animator != null && HasParameter(animator, dirXParamName, AnimatorControllerParameterType.Float);
        hasDirYParam    = animator != null && HasParameter(animator, dirYParamName, AnimatorControllerParameterType.Float);
        hasFaceDirParam = animator != null && HasParameter(animator, faceDirParamName, AnimatorControllerParameterType.Int);
        hasAttackTrigger = animator != null && HasParameter(animator, attackTriggerName, AnimatorControllerParameterType.Trigger);
        hasAttackingBool = animator != null && HasParameter(animator, attackingBoolName, AnimatorControllerParameterType.Bool);

        facing = initialFacing;
        ApplyFacingSprite();
        PushFaceDir();

        // 排障用：镜像/朝向参数一旦不对，进游戏第一眼就能在 Console 看到真正生效的值
        Debug.Log($"[PlayerMovement] {name}（场景 {gameObject.scene.name}）：" +
                  $"MirrorSpriteLeft={mirrorSpriteLeft}  MirrorSpriteRight={mirrorSpriteRight}  " +
                  $"MirrorAttackLeft={mirrorAttackLeft}  MirrorAttackRight={mirrorAttackRight}  " +
                  $"flipX={sr != null && sr.flipX}  KeepFacing={keepFacingWhenStopped}", this);

        if (animator == null)
            Debug.LogWarning($"[PlayerMovement] {name} 上没有 Animator，动画不会播。", this);
        else if (!hasFaceDirParam)
            Debug.LogWarning($"[PlayerMovement] 控制器里找不到 int 参数「{faceDirParamName}」，\n" +
                             "朝向记不住 —— 停下后角色会固定变回正面。请确认 Player 挂的是 Assets/Animation/Player.controller。", this);
    }

    private void Update()
    {
        // ---- 攻击 ----
        // 先处理「上一刀还在挥」的计时：期间移动被锁、Attacking 布尔锁住站立/走路过渡
        bool attacking = attackTimer > 0f;
        if (attacking)
        {
            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                attackTimer = 0f;
                if (hasAttackingBool) animator.SetBool(attackingBoolName, false);
                // 连点时如果在攻击状态里按下的，trigger 会留在缓冲区，落地 Idle 后会莫名多打一刀 —— 清掉
                if (hasAttackTrigger) animator.ResetTrigger(attackTriggerName);

                // 攻击结束：flipX 从「攻击那组镜像」切回「走路那组」
                ApplyFacingSprite();
            }
        }

        if (enableAttack && attackTimer <= 0f && !SceneTransition.InputBlocked)
            TryAttack();

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        // 切场景过场 / 打开背包时锁输入（朝向保持不变）
        if (SceneTransition.InputBlocked)
        {
            horizontal = 0f;
            vertical = 0f;
        }

        // 攻击动作进行中锁移动（星露谷式：挥刀那一下站定）
        if (attackTimer > 0f)
        {
            horizontal = 0f;
            vertical = 0f;
        }

        moveInput.x = horizontal;
        moveInput.y = vertical;
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        bool moving = moveInput.sqrMagnitude > 0.0001f;

        // 走动时才更新朝向；停手时保持上一次的值 —— 这是「停下来不回头」的关键
        if (moving)
        {
            facing = ResolveFacing(moveInput);
            ApplyFacingSprite();
        }
        else if (!keepFacingWhenStopped)
        {
            facing = initialFacing;
            ApplyFacingSprite();
        }

        if (animator != null)
        {
            if (hasSpeedParam)   animator.SetFloat(speedParamName, moveInput.magnitude);
            if (hasDirXParam)    animator.SetFloat(dirXParamName, moveInput.x);
            if (hasDirYParam)    animator.SetFloat(dirYParamName, moveInput.y);
            PushFaceDir();
        }
    }

    /// <summary>
    /// 鼠标左键单击 → 触发控制器里的 Attack trigger。
    /// 攻击时长用 attackDuration 计时，结束时把 Attacking 布尔松开。
    /// </summary>
    private void TryAttack()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        if (animator == null || !hasAttackTrigger)
        {
            // 只警告一次：每帧点鼠标都刷屏太吵
            if (Time.frameCount % 60 == 0)
                Debug.LogWarning($"[PlayerMovement] 控制器里找不到 Trigger 参数「{attackTriggerName}」，" +
                                 "鼠标攻击无效。请确认 Player 挂的是带攻击状态的 Player.controller。", this);
            return;
        }

        attackTimer = Mathf.Max(0.05f, attackDuration);
        animator.SetTrigger(attackTriggerName);
        if (hasAttackingBool) animator.SetBool(attackingBoolName, true);

        // 攻击用的是另一组镜像开关（朝左挥刀通常要把朝下/朝右的攻击图翻过来）
        ApplyFacingSprite();
    }

    private void FixedUpdate()
    {
        if (rb != null)
        {
            rb.velocity = moveInput * moveSpeed;

            if (useBoundary)
            {
                Vector2 pos = rb.position;
                float clampedX = Mathf.Clamp(pos.x, boundary.xMin, boundary.xMax);
                float clampedY = Mathf.Clamp(pos.y, boundary.yMin, boundary.yMax);

                if (!Mathf.Approximately(pos.x, clampedX) || !Mathf.Approximately(pos.y, clampedY))
                {
                    rb.position = new Vector2(clampedX, clampedY);
                    if ((clampedX != pos.x && moveInput.x != 0f) || (clampedY != pos.y && moveInput.y != 0f))
                        rb.velocity = Vector2.zero;
                }
            }
        }
        else
        {
            Vector3 next = transform.position + (Vector3)(moveInput * moveSpeed * Time.fixedDeltaTime);
            if (useBoundary)
            {
                next.x = Mathf.Clamp(next.x, boundary.xMin, boundary.xMax);
                next.y = Mathf.Clamp(next.y, boundary.yMin, boundary.yMax);
            }
            transform.position = next;
        }
    }

    /// <summary>
    /// 由输入向量定出朝向。斜着走时取分量更大的那根轴，
    /// 上下与左右同分量时优先上下（和控制器里 Walk_* 的判定顺序一致）。
    /// </summary>
    private static Facing ResolveFacing(Vector2 input)
    {
        float ax = Mathf.Abs(input.x);
        float ay = Mathf.Abs(input.y);

        if (ay >= ax)
            return input.y > 0f ? Facing.Back : Facing.Front;

        return input.x < 0f ? Facing.Left : Facing.Right;
    }

    private void PushFaceDir()
    {
        if (hasFaceDirParam)
            animator.SetInteger(faceDirParamName, (int)facing);
    }

    /// <summary>
    /// 外部设置朝向（传送落地时用：从哪条边的门进来就面朝地图内侧）。
    /// 站立不动时朝向不会被 Update 覆盖，所以能保持住。
    /// </summary>
    public void SetFacing(Facing f)
    {
        facing = f;
        ApplyFacingSprite();
        PushFaceDir();
    }

    /// <summary>
    /// 左右朝向的镜像补救：哪边的素材画反了，就只翻转那边。
    /// 上下朝向一律不翻转 —— 正/背面是独立素材，
    /// 留着上次左右时设的 flipX 会让站立帧也被莫名镜像。
    ///
    /// 攻击时用的是另一组开关（mirrorAttack*）：攻击图和走路图是两套素材，
    /// 走路图朝左画对了，不代表攻击图也朝左 —— 分开控制才不会互相打架。
    /// </summary>
    private void ApplyFacingSprite()
    {
        if (sr == null) return;

        bool left = attackTimer > 0f ? mirrorAttackLeft : mirrorSpriteLeft;
        bool right = attackTimer > 0f ? mirrorAttackRight : mirrorSpriteRight;

        sr.flipX = (facing == Facing.Left && left)
                   || (facing == Facing.Right && right);
    }

    private static bool HasParameter(Animator anim, string name, AnimatorControllerParameterType type)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.name == name && p.type == type) return true;
        }
        return false;
    }
}