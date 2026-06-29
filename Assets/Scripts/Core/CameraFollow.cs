using System.Collections.Generic;
using System.Linq;
using DungeonShooter.Dungeon;
using UnityEngine;

namespace DungeonShooter
{
    /// <summary>
    /// 摄像机平滑跟随组件。
    ///
    /// 跟随规则：
    /// - 玩家在房间内 → 摄像机边缘不超出房间边界（Clamp）
    /// - 玩家在走廊里 → 正常跟随，不限制
    /// - 小房间（比摄像机视野还小）→ 平滑锁定房间中心
    /// - 房间↔走廊切换时 → 平滑过渡，无突变
    ///
    /// 使用 LateUpdate 确保在玩家移动和协程之后执行，拿到当帧最终位置。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("跟随参数")]
        [Tooltip("跟随速度（值越大越快追上）")]
        [Range(0.1f, 20f)]
        [SerializeField] private float followSpeed = 5f;

        [Tooltip("过渡到小房间中心的速度（秒数），值越大越慢到达")]
        [Range(0.5f, 3f)]
        [SerializeField] private float centerLockSpeed = 1.5f;

        [Header("调试")]
        [Tooltip("在 Scene 视图中绘制房间 clamp 边界")]
        [SerializeField] private bool drawDebugBounds = false;

        // ---- 内部状态 ----
        private Transform target;
        private IReadOnlyList<Room> rooms;
        private Camera cam;

        // SmoothDamp 的 ref 变量，保存速度参考值
        private Vector3 velocityRef;

        // 当前帧被 Clamp 的房间（仅调试用）
        private Room currentRoom;
        private bool isInRoom;

        #region 公共接口

        /// <summary>
        /// 设置跟随目标。
        /// </summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            // 重置 SmoothDamp 速度，避免切换目标时的速度残留
            velocityRef = Vector3.zero;
        }

        /// <summary>
        /// 设置房间列表，用于房间内边界 Clamp。
        /// </summary>
        public void SetRooms(IReadOnlyList<Room> roomList)
        {
            rooms = roomList;
        }

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null)
            {
                Debug.LogError("[CameraFollow] 需要挂载在 Camera 物体上！");
            }
        }

        /// <summary>
        /// LateUpdate：在所有 Update 和协程之后执行，
        /// 保证拿到玩家当帧的最终位置。
        /// </summary>
        private void LateUpdate()
        {
            if (target == null) return;
            if (cam == null) return;
            if (rooms == null || rooms.Count == 0) return;

            // 计算目标位置
            Vector3 targetPos = target.position;
            targetPos.z = transform.position.z; // 保持摄像机 Z 不变

            // 找到玩家所在的房间
            Room containingRoom = FindRoomContaining(target.position);
            isInRoom = containingRoom != null;
            currentRoom = containingRoom;

            if (containingRoom != null)
            {
                // 在房间内 → Clamp
                targetPos = ClampToRoom(targetPos, containingRoom);
            }
            // 在走廊 → 直接跟随 targetPos，不做额外处理

            // 平滑跟随
            transform.position = Vector3.SmoothDamp(
                transform.position, targetPos, ref velocityRef, 1f / followSpeed);
        }

        #endregion

        #region 房间检测

        /// <summary>
        /// 找到包含指定世界坐标的房间（世界坐标 = 地牢瓦片坐标）。
        /// </summary>
        private Room FindRoomContaining(Vector3 worldPos)
        {
            Vector2Int gridPos = new Vector2Int(
                Mathf.FloorToInt(worldPos.x),
                Mathf.FloorToInt(worldPos.y));

            // 用 InnerBounds 判断玩家是否在房间地板区域
            // FloorToInt 保证靠近右/上边缘不会被误判为出界
            return rooms.FirstOrDefault(r => r.InnerBounds.Contains(gridPos));
        }

        #endregion

        #region Clamp 逻辑

        /// <summary>
        /// 将摄像机目标位置限制在房间可视范围内。
        ///
        /// 边界计算：房间左下角 → 摄像机左下角不能超过房间 Bounds 左下角 + 半视口
        ///          房间右上角 → 摄像机右上角不能超过房间 Bounds 右上角 - 半视口
        ///
        /// 这样保证不会拍到墙壁外面的空白区域。
        /// </summary>
        private Vector3 ClampToRoom(Vector3 desiredPos, Room room)
        {
            // ---- 摄像机视口半尺寸 ----
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;

            // ---- 房间边界（用 Bounds，包含墙壁区域）----
            RectInt bounds = room.Bounds; // 整数瓦片边界
            float roomLeft = bounds.xMin;
            float roomRight = bounds.xMax;
            float roomBottom = bounds.yMin;
            float roomTop = bounds.yMax;

            float roomWidth = roomRight - roomLeft;
            float roomHeight = roomTop - roomBottom;

            // ---- 房间太小 → 锁定中心 ----
            bool roomTooNarrow = roomWidth < halfWidth * 2f;
            bool roomTooShort = roomHeight < halfHeight * 2f;

            if (roomTooNarrow || roomTooShort)
            {
                // 小房间：直接锁定到真正的几何中心（浮点，避免 Vector2Int 截断）
                float centerX = room.position.x + room.size.x / 2f;
                float centerY = room.position.y + room.size.y / 2f;
                return new Vector3(centerX, centerY, desiredPos.z);
            }

            // ---- 正常房间 → Clamp 到边界内 ----
            float clampedX = Mathf.Clamp(desiredPos.x, roomLeft + halfWidth, roomRight - halfWidth);
            float clampedY = Mathf.Clamp(desiredPos.y, roomBottom + halfHeight, roomTop - halfHeight);

            return new Vector3(clampedX, clampedY, desiredPos.z);
        }

        #endregion

        #region 调试绘制

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            if (rooms == null) return;

            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;

            foreach (var room in rooms)
            {
                RectInt bounds = room.Bounds;

                // 绘制房间 Bounds（灰色线框）
                Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                Gizmos.DrawWireCube(
                    new Vector3(bounds.center.x, bounds.center.y, 0),
                    new Vector3(bounds.width, bounds.height, 0));

                // 计算 Clamp 后的有效区域
                float roomWidth = bounds.width;
                float roomHeight = bounds.height;
                bool tooNarrow = roomWidth < halfWidth * 2f;
                bool tooShort = roomHeight < halfHeight * 2f;

                if (tooNarrow || tooShort)
                {
                    // 小房间：绘制中心十字（用浮点几何中心，不是 Vector2Int 截断值）
                    Gizmos.color = Color.cyan;
                    Vector3 center = new Vector3(
                        room.position.x + room.size.x / 2f,
                        room.position.y + room.size.y / 2f,
                        0);
                    Gizmos.DrawWireSphere(center, 0.5f);
                }
                else
                {
                    // 正常房间：绘制 Clamp 有效区域（绿色线框）
                    Gizmos.color = Color.green;
                    Vector3 clampCenter = new Vector3(
                        bounds.xMin + halfWidth + (roomWidth - halfWidth * 2) / 2f,
                        bounds.yMin + halfHeight + (roomHeight - halfHeight * 2) / 2f,
                        0);
                    Vector3 clampSize = new Vector3(
                        roomWidth - halfWidth * 2,
                        roomHeight - halfHeight * 2,
                        0);
                    if (clampSize.x > 0 && clampSize.y > 0)
                        Gizmos.DrawWireCube(clampCenter, clampSize);
                }
            }
        }
#endif

        #endregion
    }
}
