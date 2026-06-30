using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonShooter.Player
{
    /// <summary>
    /// 冲刺残影的对象池管理器。
    ///
    /// 职责：预创建 N 个 Ghost → Queue 管理 → 冲刺时定时吐出 → 淡出后回收复用。
    ///
    /// 设计考量：
    /// - Queue（先进先出）天然适合"先用最早创建的"这种复用模式
    /// - Ghost 子对象放在专属容器下，保持层级整洁
    /// - 通过 GetComponent 获取玩家的 SpriteRenderer，无需手动拖拽
    /// </summary>
    public class AfterimageEffect : MonoBehaviour
    {
        #region 序列化字段

        [Header("池配置")]
        [Tooltip("预创建的残影数量")]
        [Range(3, 30)]
        [SerializeField] private int poolSize = 10;

        [Header("生成节奏")]
        [Tooltip("残影产生间隔（秒），越小越密集")]
        [Range(0.02f, 0.2f)]
        [SerializeField] private float spawnInterval = 0.05f;

        [Header("残影外观")]
        [Tooltip("每个残影的淡出持续时间（秒）")]
        [Range(0.1f, 1f)]
        [SerializeField] private float ghostLifetime = 0.3f;

        [Tooltip("残影起始颜色（Alpha 控制透明度）")]
        [SerializeField] private Color ghostColor = new Color(1f, 1f, 1f, 0.5f);

        [Header("预制体")]
        [Tooltip("残影预制体（需挂载 AfterimageGhost + SpriteRenderer）")]
        [SerializeField] private GameObject ghostPrefab;

        [Tooltip("残影子对象容器（不指定则自动创建）")]
        [SerializeField] private Transform ghostContainer;

        #endregion

        #region 内部状态

        /// <summary>对象池队列</summary>
        private Queue<AfterimageGhost> pool;

        /// <summary>玩家的 SpriteRenderer，残影依赖它来拷贝精灵</summary>
        private SpriteRenderer playerSpriteRenderer;

        /// <summary>生成协程引用，用于停止</summary>
        private Coroutine spawnCoroutine;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            // 从父物体（Player）获取 SpriteRenderer
            playerSpriteRenderer = GetComponentInParent<SpriteRenderer>();
            if (playerSpriteRenderer == null)
            {
                Debug.LogError("[AfterimageEffect] 需要挂载在 Player 或其子物体上，且 Player 须有 SpriteRenderer！");
            }

            // 创建容器（如未指定）
            if (ghostContainer == null)
            {
                GameObject containerGo = new GameObject("GhostPool");
                containerGo.transform.SetParent(transform, worldPositionStays: false);
                ghostContainer = containerGo.transform;
            }

            // 预创建池
            CreatePool();
        }

        #endregion

        #region 池初始化

        /// <summary>
        /// 预创建 poolSize 个 Ghost 对象，放入 Queue 并初始为不激活。
        /// </summary>
        private void CreatePool()
        {
            if (ghostPrefab == null)
            {
                Debug.LogError("[AfterimageEffect] Ghost Prefab 未赋值！");
                return;
            }

            pool = new Queue<AfterimageGhost>(poolSize);

            for (int i = 0; i < poolSize; i++)
            {
                GameObject go = Instantiate(ghostPrefab, ghostContainer);
                go.name = $"Ghost_{i:D2}";

                AfterimageGhost ghost = go.GetComponent<AfterimageGhost>();
                if (ghost == null)
                {
                    Debug.LogError("[AfterimageEffect] Ghost 预制体未挂载 AfterimageGhost 组件！");
                    Destroy(go);
                    continue;
                }

                go.SetActive(false);
                pool.Enqueue(ghost);
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 开始产生残影。由 PlayerController 在冲刺开始时调用。
        /// </summary>
        public void StartSpawning()
        {
            if (ghostPrefab == null) return;
            if (pool == null || pool.Count == 0) return;

            // 停掉上一次可能还在跑的协程
            if (spawnCoroutine != null)
            {
                StopCoroutine(spawnCoroutine);
            }

            spawnCoroutine = StartCoroutine(SpawnRoutine());
        }

        /// <summary>
        /// 停止产生残影。由 PlayerController 在冲刺结束时调用。
        /// </summary>
        public void StopSpawning()
        {
            if (spawnCoroutine != null)
            {
                StopCoroutine(spawnCoroutine);
                spawnCoroutine = null;
            }
        }

        #endregion

        #region 生成与回收

        /// <summary>
        /// 定时从池中取 Ghost，拷贝玩家外观并激活。
        /// 每隔 spawnInterval 秒执行一次，由 StopSpawning 通过 StopCoroutine 终止。
        /// </summary>
        private IEnumerator SpawnRoutine()
        {
            while (true)
            {
                SpawnOne();
                yield return new WaitForSeconds(spawnInterval);
            }
        }

        /// <summary>
        /// 从池中取出一个 Ghost 并激活。
        /// 如果池空了（所有 Ghost 都在淡出中），跳过本次。
        /// </summary>
        private void SpawnOne()
        {
            if (pool.Count == 0) return;
            if (playerSpriteRenderer == null) return;

            // 出池
            AfterimageGhost ghost = pool.Dequeue();

            // 脱离 Player 容器，挂到世界根下（避免残影跟随 Player 移动）
            ghost.transform.SetParent(null);

            // 拷贝玩家当前外观
            ghost.Show(
                sprite: playerSpriteRenderer.sprite,
                position: playerSpriteRenderer.transform.position,
                flipX: playerSpriteRenderer.flipX,
                color: ghostColor,
                lifetime: ghostLifetime,
                callback: Return
            );

            // Show 内部已调用 SetActive(true)
        }

        /// <summary>
        /// 将 Ghost 归还池。由 AfterimageGhost 淡出完毕后通过回调调用。
        /// </summary>
        private void Return(AfterimageGhost ghost)
        {
            ghost.gameObject.SetActive(false);
            // 归还时重新挂回容器，保持层级整齐
            ghost.transform.SetParent(ghostContainer);
            pool.Enqueue(ghost);
        }

        #endregion
    }
}
