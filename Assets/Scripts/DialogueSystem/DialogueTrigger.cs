using UnityEngine;

/// <summary>
/// 对话触发器 —— 挂在 NPC / 可互动物体上。
///
/// 工作流程：
///   1. 玩家（Tag = Player）走进本物体的 Trigger 碰撞范围 → 显示「按 E 对话」提示
///   2. 玩家在范围内按 E → 锁定玩家移动，把对话数据交给场景里的 DialogueUI 播放
///   3. 对话播放完毕 → DialogueUI 回调，解锁玩家移动
///   4. 玩家走出范围 → 提示消失，不能再触发
///
/// 【前置条件】
///   - 本物体：Collider2D（勾选 Is Trigger），范围大小自己拉
///   - 玩家：身上要有 Collider2D + Rigidbody2D，且 Tag 设为 Player
///     （2D 触发检测要求至少一方有 Rigidbody2D，一般在玩家身上）
///   - 场景里有一个搭好的 DialogueUI（菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 自动搭建对话UI）
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DialogueTrigger : MonoBehaviour
{
    [Header("对话数据")]
    [Tooltip("对话数据配置文件（Project 右键 ▸ Create ▸ Dialogue ▸ Dialogue Data 创建）")]
    public DialogueData dialogueData;

    [Header("交互设置")]
    [Tooltip("触发/互动按键")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("靠近时是否显示提示")]
    public bool showPrompt = true;

    [Header("提示设置")]
    [Tooltip("提示预制体（可选）：一个带 TMP 文本的小牌子，留空则不显示提示")]
    public GameObject promptPrefab;

    [Tooltip("提示在本物体头顶的偏移高度（世界单位）")]
    public float promptHeightOffset = 1.5f;

    // ------------------------------------------------------------------ 内部状态

    private DialogueUI _dialogueUI;
    private GameObject _promptInstance;
    private bool _playerInRange;
    private bool _dialogueActive;
    private bool _warnedNoData;   // 没配对话数据只警告一次，防止刷屏

    // ------------------------------------------------------------------ 生命周期

    private void Start()
    {
        // 自动找场景里的 DialogueUI（场景里只需要一个）
        _dialogueUI = FindObjectOfType<DialogueUI>();
        if (_dialogueUI == null)
            Debug.LogError("[DialogueTrigger] 场景里找不到 DialogueUI！请用菜单 Tools ▸ 农场RPG ▸ 对话 ▸ 自动搭建对话UI 生成。", this);

        // 自己的 Collider2D 必须是 Trigger，否则永远收不到触发事件
        Collider2D col = GetComponent<Collider2D>();
        if (!col.isTrigger)
            Debug.LogWarning($"[DialogueTrigger] {name} 的 Collider2D 没勾选 Is Trigger，玩家靠近不会被检测到！", this);

        // 有提示预制体就实例化一个挂在头顶（默认隐藏）
        if (showPrompt && promptPrefab != null)
        {
            _promptInstance = Instantiate(promptPrefab, transform);
            _promptInstance.transform.localPosition = new Vector3(0f, promptHeightOffset, 0f);
            _promptInstance.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ 触发检测（Unity 内置 2D 物理）

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        _playerInRange = true;
        if (_promptInstance != null && !_dialogueActive)
            _promptInstance.SetActive(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        // 离开范围：不能触发对话，提示隐藏
        _playerInRange = false;
        if (_promptInstance != null)
            _promptInstance.SetActive(false);
    }

    // ------------------------------------------------------------------ 更新

    private void Update()
    {
        if (!_playerInRange) return;      // 不在范围内不响应
        if (_dialogueActive) return;      // 对话中不重复触发
        if (_dialogueUI == null) return;
        if (_dialogueUI.IsDialogueActive) return; // 别的对话正在播也不抢

        if (dialogueData == null || !dialogueData.HasLines)
        {
            if (!_warnedNoData)
            {
                _warnedNoData = true;
                Debug.LogWarning($"[DialogueTrigger] {name} 没分配 DialogueData 或对话列表为空。", this);
            }
            return;
        }

        if (Input.GetKeyDown(interactKey))
            StartDialogue();
    }

    // ------------------------------------------------------------------ 对话控制

    private void StartDialogue()
    {
        _dialogueActive = true;
        if (_promptInstance != null)
            _promptInstance.SetActive(false);

        PlayerDialogueLock.Lock();   // 锁定玩家移动（写到 SceneTransition.InputLocked）
        _dialogueUI.StartDialogue(dialogueData.lines, OnDialogueEnd);
    }

    /// <summary>对话播放完毕的回调</summary>
    private void OnDialogueEnd()
    {
        _dialogueActive = false;
        PlayerDialogueLock.Unlock(); // 解锁玩家移动
    }
}
