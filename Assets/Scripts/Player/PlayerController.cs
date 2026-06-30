using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonShooter.Player
{
    /// <summary>
    /// 玩家控制器。
    /// 职责：读取输入 → 驱动刚体移动 → 控制动画 → 处理冲刺。
    ///
    /// 移动使用 Rigidbody2D.velocity（物理驱动，自然碰撞响应），
    /// 冲刺使用协程（定时序列：加速 → 持续 → 无敌 → 冷却）。
    ///
    /// 精灵朝向：左右移动时面朝移动方向，无左右移动时面朝鼠标。
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        #region 序列化字段

        [Header("配置")]
        [Tooltip("玩家参数 ScriptableObject")]
        [SerializeField] private PlayerData data;

        #endregion

        #region 组件引用

        private Rigidbody2D rb;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private AfterimageEffect afterimageEffect;

        #endregion

        #region 输入

        private InputAction moveAction;
        private InputAction dashAction;
        private Vector2 moveInput;

        #endregion

        #region 状态

        private bool isDashing;
        private bool isInvincible;
        private bool canDash = true;
        /// <summary>冲刺时或上次移动时的方向（用于不按方向键时冲刺）</summary>
        private Vector2 lastMoveDirection = Vector2.right;

        #endregion

        #region Animator 参数哈希

        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

        #endregion

        #region 公共属性

        /// <summary>是否处于无敌状态（供战斗系统查询）</summary>
        public bool IsInvincible => isInvincible;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            // 获取必要组件（在预制体上配置好的）
            rb = GetComponent<Rigidbody2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
            afterimageEffect = GetComponentInChildren<AfterimageEffect>();

            // 设置刚体参数：无重力、小阻尼停稳更平滑、插值消除抖动
            if (rb != null)
            {
                rb.gravityScale = 0f;
                rb.drag = 3f; // 松手后约 0.3 秒停稳，方向切换更平滑
                rb.interpolation = RigidbodyInterpolation2D.Interpolate; // 物理帧与渲染帧不同步时平滑过渡
            }
        }

        private void OnEnable()
        {
            // 创建输入动作（New Input System 代码式绑定，无需 .inputactions 文件）
            moveAction = new InputAction("Move");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            moveAction.performed += OnMovePerformed;
            moveAction.canceled += OnMoveCanceled;
            moveAction.Enable();

            dashAction = new InputAction("Dash", InputActionType.Button, "<Keyboard>/space");
            dashAction.performed += OnDashPerformed;
            dashAction.Enable();
        }

        private void OnDisable()
        {
            // 清理输入，防止内存泄漏
            CleanupInput();
        }

        private void OnDestroy()
        {
            CleanupInput();
        }

        private void Update()
        {
            UpdateFacing();
            UpdateAnimation();
        }

        private void FixedUpdate()
        {
            ApplyMovement();
        }

        #endregion

        #region 输入回调

        /// <summary>
        /// 移动输入回调。使用 performed（而非持续采样），
        /// 在值变化时更新缓存方向，避免每帧分配。
        /// </summary>
        private void OnMovePerformed(InputAction.CallbackContext ctx)
        {
            Vector2 input = ctx.ReadValue<Vector2>();
            moveInput = input;

            // 记录最后有效移动方向（用于无方向键冲刺时选方向）
            if (input.sqrMagnitude > 0.01f)
            {
                lastMoveDirection = input.normalized;
            }
        }

        private void OnMoveCanceled(InputAction.CallbackContext ctx)
        {
            moveInput = Vector2.zero;
        }

        /// <summary>
        /// 冲刺输入回调（Space 按下瞬间触发）。
        /// </summary>
        private void OnDashPerformed(InputAction.CallbackContext ctx)
        {
            if (!canDash || isDashing) return;

            // 冲刺方向：优先当前移动方向，否则用上次移动方向
            Vector2 dashDir = moveInput.sqrMagnitude > 0.01f
                ? moveInput.normalized
                : lastMoveDirection;

            StartCoroutine(DashRoutine(dashDir));
        }

        #endregion

        #region 移动

        /// <summary>
        /// FixedUpdate 中执行移动，保证物理模拟一致性。
        /// 正常时用 moveInput * moveSpeed，冲刺时跳过（由协程接管 velocity）。
        /// </summary>
        private void ApplyMovement()
        {
            if (isDashing) return;

            rb.velocity = moveInput * data.moveSpeed;
        }

        #endregion

        #region 朝向

        /// <summary>
        /// 精灵朝向规则：
        /// 1. 有左右方向输入时（moveInput.x != 0）→ 面朝移动方向
        ///    - 同时按 A+D 时 moveInput.x = 0，走规则 2
        /// 2. 无左右方向输入时 → 面朝鼠标位置
        /// </summary>
        private void UpdateFacing()
        {
            if (moveInput.x > 0.01f)
            {
                // 输入向右 → 面朝右
                spriteRenderer.flipX = false;
            }
            else if (moveInput.x < -0.01f)
            {
                // 输入向左 → 面朝左
                spriteRenderer.flipX = true;
            }
            else
            {
                // 无左右输入（包括 A+D 同时按互相抵消 → x=0）→ 面朝鼠标
                Camera cam = Camera.main;
                if (cam == null) return;

                Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
                mouseWorld.z = 0f;
                spriteRenderer.flipX = mouseWorld.x < transform.position.x;
            }
        }

        #endregion

        #region 动画

        /// <summary>
        /// 设置 Animator 参数。
        /// IsMoving: 移动中或冲刺中为 true，动画切到 Run 状态。
        /// </summary>
        private void UpdateAnimation()
        {
            if (animator == null) return;

            bool isMoving = moveInput.sqrMagnitude > 0.01f || isDashing;
            animator.SetBool(IsMovingHash, isMoving);
        }

        #endregion

        #region 冲刺（协程）

        /// <summary>
        /// 冲刺协程。
        /// 时序：全速冲刺（dashDuration 秒）→ 结束冲刺，短暂无敌延续
        /// → 冷却等待（dashCooldown 秒）→ 允许再次冲刺。
        ///
        /// 使用协程而非 Update 状态机的理由：
        /// - 时间序列天然清晰，"先做什么再做什么"一目了然
        /// - 避免 if/else 嵌套和 int 状态值
        /// - yield return WaitForSeconds 精确控制等待时间
        /// </summary>
        private IEnumerator DashRoutine(Vector2 direction)
        {
            // ---- 阶段 1：冲刺 ----
            isDashing = true;
            isInvincible = true;
            canDash = false;

            // 开始产出残影
            if (afterimageEffect != null)
                afterimageEffect.StartSpawning();

            float timer = 0f;
            while (timer < data.dashDuration)
            {
                rb.velocity = direction * data.dashSpeed;
                timer += Time.deltaTime;
                yield return null; // 每帧检查一次时间
            }

            isDashing = false;
            rb.velocity = moveInput * data.moveSpeed;

            // 停止产出残影
            if (afterimageEffect != null)
                afterimageEffect.StopSpawning();

            // ---- 阶段 2：无敌延续（防止冲刺结束瞬间中招）----
            yield return new WaitForSeconds(data.invincibilityPadding);
            isInvincible = false;

            // ---- 阶段 3：冷却 ----
            yield return new WaitForSeconds(data.dashCooldown);
            canDash = true;
        }

        #endregion

        #region 工具

        private void CleanupInput()
        {
            if (moveAction != null)
            {
                moveAction.performed -= OnMovePerformed;
                moveAction.canceled -= OnMoveCanceled;
                moveAction.Disable();
                moveAction.Dispose();
                moveAction = null;
            }

            if (dashAction != null)
            {
                dashAction.performed -= OnDashPerformed;
                dashAction.Disable();
                dashAction.Dispose();
                dashAction = null;
            }
        }

        #endregion
    }
}
