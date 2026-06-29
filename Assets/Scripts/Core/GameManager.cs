using System.Linq;
using DungeonShooter.Dungeon;
using DungeonShooter.Player;
using UnityEngine;

namespace DungeonShooter
{
    /// <summary>
    /// 游戏入口管理器（单例）。
    /// 负责游戏初始化流程：生成地牢 → 放置玩家 → 关联摄像机。
    ///
    /// 设计考量：
    /// - 单例模式：整个游戏只有一个入口，避免重复初始化
    /// - 职责单一：只做"启动"，不参与具体系统逻辑
    /// - 依赖注入：通过 SerializeField 拖拽引用，不使用 FindObjectOfType
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        #region 单例

        public static GameManager Instance { get; private set; }

        #endregion

        [Header("地牢")]
        [Tooltip("地牢生成器组件引用")]
        [SerializeField] private DungeonGenerator dungeonGenerator;

        [Header("玩家")]
        [Tooltip("玩家预制体（需挂载 PlayerController）")]
        [SerializeField] private GameObject playerPrefab;

        [Header("摄像机")]
        [Tooltip("摄像机跟随组件引用")]
        [SerializeField] private CameraFollow cameraFollow;

        private void Awake()
        {
            // 单例注册
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameManager] 场景中存在多个 GameManager，销毁多余实例");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // 检查必要引用
            if (dungeonGenerator == null)
            {
                Debug.LogError("[GameManager] DungeonGenerator 未赋值！");
                return;
            }
            if (playerPrefab == null)
            {
                Debug.LogError("[GameManager] Player Prefab 未赋值！");
                return;
            }
            if (cameraFollow == null)
            {
                Debug.LogError("[GameManager] CameraFollow 未赋值！");
                return;
            }

            // 初始化流程
            InitializeGame();
        }

        /// <summary>
        /// 游戏初始化主流程。
        /// 步骤：生成地牢 → 找到出生房间 → 生成玩家 → 设置摄像机目标。
        /// </summary>
        private void InitializeGame()
        {
            // 第 1 步：生成地牢
            dungeonGenerator.Generate();

            // 第 2 步：找到出生房间
            Room startRoom = dungeonGenerator.Rooms.FirstOrDefault(r => r.type == RoomType.Start);
            if (startRoom == null)
            {
                Debug.LogError("[GameManager] 未找到出生房间（RoomType.Start）！");
                return;
            }

            // 第 3 步：在出生房间中心生成玩家
            // 地牢坐标系与 Unity 世界坐标系一致（1 格 = 1 单位）
            Vector3 spawnPosition = new Vector3(startRoom.Center.x, startRoom.Center.y, 0);
            GameObject player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
            player.name = "Player";

            // 第 4 步：设置摄像机跟随目标
            cameraFollow.SetTarget(player.transform);
            cameraFollow.SetRooms(dungeonGenerator.Rooms);

            Debug.Log($"[GameManager] 游戏初始化完成！玩家出生在 {startRoom.Center}");
        }
    }
}
