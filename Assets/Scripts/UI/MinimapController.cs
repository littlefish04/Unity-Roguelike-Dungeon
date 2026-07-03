using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DungeonShooter.Dungeon;
using DungeonShooter.Player;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonShooter.UI
{
    /// <summary>
    /// 小地图系统主控制器。
    ///
    /// 职责：管理迷雾状态 → 程序化生成地图贴图 → 处理全屏地图 UI → 玩家标记。
    ///
    /// 使用方式：由 GameManager 在 InitializeGame() 末尾 AddComponent 并传入 MinimapConfig。
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        #region 序列化字段

        [Header("配置")]
        [Tooltip("小地图参数（ScriptableObject）")]
        [SerializeField] private MinimapConfig config;

        #endregion

        #region 外部引用

        private DungeonGenerator dungeonGenerator;
        private Transform playerTransform;
        private PlayerController playerController;

        #endregion

        #region UI 元素（程序化创建）

        private Canvas mapCanvas;
        private Image backgroundImage;
        private RectTransform viewportRect;
        private RawImage mapRawImage;
        private RectTransform mapRawImageRect;
        private Image playerDotImage;
        private RectTransform playerDotRect;
        /// <summary>玩家圆点的程序化纹理（需手动销毁）</summary>
        private Texture2D playerDotTexture;
        /// <summary>玩家圆点的程序化 Sprite（需手动销毁）</summary>
        private Sprite playerDotSprite;

        #endregion

        #region 状态

        /// <summary>已揭示的地板瓦片坐标</summary>
        private HashSet<Vector2Int> revealedTiles = new HashSet<Vector2Int>();

        /// <summary>已访问过的房间索引（去重用，防止重复揭示）</summary>
        private HashSet<int> visitedRoomIndices = new HashSet<int>();

        /// <summary>地板格 → 所属房间索引（-1 表示走廊）</summary>
        private Dictionary<Vector2Int, int> floorToRoomIndex;

        /// <summary>地图是否处于打开状态</summary>
        private bool isMapOpen;

        /// <summary>当前是否处于 2× 缩放（false = 1×）</summary>
        private bool isZoomedIn;

        /// <summary>平移偏移量（像素），仅在 2× 缩放时有效</summary>
        private Vector2 panOffset;

        #endregion

        #region 贴图

        private Texture2D mapTexture;
        private Color32[] texturePixels;
        private int texWidth;
        private int texHeight;
        private Vector2Int dungeonSize;

        /// <summary>是否有脏像素需要 Apply</summary>
        private bool hasDirtyPixels;

        #endregion

        #region 缓存的 Color32（避免每帧从 config.Color 转换）

        private Color32 unexploredC32;
        private Color32 corridorC32;
        private Color32 normalRoomC32;
        private Color32 bossRoomC32;
        private Color32 startRoomC32;
        private Color32 treasureRoomC32;

        #endregion

        #region 公共属性

        /// <summary>地图是否打开（供外部查询，如暂停游戏逻辑）</summary>
        public bool IsMapOpen => isMapOpen;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            // 查找 DungeonGenerator（硬依赖，必须存在）
            dungeonGenerator = FindObjectOfType<DungeonGenerator>();
            if (dungeonGenerator == null || dungeonGenerator.dungeonData == null)
            {
                Debug.LogError("[MinimapController] 未找到 DungeonGenerator 或其 DungeonData！");
                enabled = false;
                return;
            }

            // 玩家可能尚未生成，先尝试查找（可空，协程里会重试）
            playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
            playerController = FindObjectOfType<PlayerController>();
        }

        /// <summary>
        /// 初始化小地图系统。由 GameManager 在 AddComponent 之后调用，
        /// 传入 MinimapConfig 并触发所有依赖 config 的初始化逻辑。
        ///
        /// 拆分为 Awake + Initialize 而非全部放在 Awake 的原因：
        /// GameManager 使用 AddComponent 动态创建本组件，AddComponent
        /// 会同步触发 Awake，但此时 config 尚未赋值（它不是场景里拖好的引用）。
        /// 因此把 config 相关逻辑延后到 Initialize 中执行。
        /// </summary>
        public void Initialize(MinimapConfig mapConfig)
        {
            config = mapConfig;

            dungeonSize = dungeonGenerator.dungeonData.DungeonSize;
            texWidth = config.textureWidth;
            texHeight = Mathf.RoundToInt(texWidth * (float)dungeonSize.y / dungeonSize.x);

            CacheColors32();
            CreateMapUI();
            CreateMapTexture();
            BuildFloorLookup();

            mapCanvas.enabled = false;
            isMapOpen = false;

            Debug.Log($"[MinimapController] 初始化完成：贴图 {texWidth}×{texHeight}，地牢 {dungeonSize.x}×{dungeonSize.y}");
        }

        private void Start()
        {
            if (config == null) return; // 等待 Initialize 调用

            StartCoroutine(FogUpdateCoroutine());
            StartCoroutine(PlayerDotBlinkCoroutine());
        }

        private void Update()
        {
            // M 键开关地图
            if (Input.GetKeyDown(config.toggleKey))
            {
                ToggleMap();
            }

            if (!isMapOpen) return;

            // Tab 键切换缩放（1× ↔ 2×）
            if (Input.GetKeyDown(config.zoomKey))
            {
                ToggleZoom();
            }

            // WASD 平移（仅在 2× 缩放时有效）
            if (isZoomedIn)
            {
                HandlePanInput();
            }
        }

        private void OnDestroy()
        {
            if (mapTexture != null)
                Destroy(mapTexture);
            if (playerDotSprite != null)
                Destroy(playerDotSprite);
            if (playerDotTexture != null)
                Destroy(playerDotTexture);
        }

        #endregion

        #region UI 创建

        /// <summary>
        /// 用代码创建完整的小地图 UI 层级：
        /// MapCanvas → Background (全屏遮罩) → Viewport (裁剪区域) → MapRawImage (贴图) → PlayerDot (圆点)
        ///
        /// 层级关系：
        /// - Background 和 Viewport 是 Canvas 的平级子物体（Background 先渲染在下层）
        /// - MapRawImage 是 Viewport 的子物体，受 RectMask2D 裁剪
        /// - PlayerDot 是 MapRawImage 的子物体，随地图平移/缩放自动移动
        /// </summary>
        private void CreateMapUI()
        {
            // ===== Canvas =====
            GameObject canvasGO = new GameObject("MapCanvas");
            canvasGO.transform.SetParent(transform);
            mapCanvas = canvasGO.AddComponent<Canvas>();
            mapCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            mapCanvas.sortingOrder = 100; // 确保在所有游戏 UI 之上

            var canvasScaler = canvasGO.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1200);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // ===== Background（全屏半透明遮罩，阻挡鼠标穿透到游戏世界）=====
            GameObject bgGO = new GameObject("Background");
            bgGO.transform.SetParent(canvasGO.transform, false);
            RectTransform bgRT = bgGO.AddComponent<RectTransform>();
            SetFullScreen(bgRT);
            backgroundImage = bgGO.AddComponent<Image>();
            backgroundImage.color = new Color(0f, 0f, 0f, config.backgroundAlpha);
            backgroundImage.raycastTarget = true;

            // ===== Viewport（地图的可视裁剪区域，居中 + padding）=====
            GameObject vpGO = new GameObject("Viewport");
            vpGO.transform.SetParent(canvasGO.transform, false);
            viewportRect = vpGO.AddComponent<RectTransform>();
            SetupViewportRect(viewportRect);

            // RectMask2D 在 2× 缩放时裁剪超出 Viewport 的地图内容
            vpGO.AddComponent<RectMask2D>();

            // 给 Viewport 加一层半透明底色，标记地图区域边界
            Image vpImage = vpGO.AddComponent<Image>();
            vpImage.color = new Color(0.15f, 0.15f, 0.18f, 0.5f);

            // ===== MapRawImage（显示程序化贴图）=====
            GameObject mapGO = new GameObject("MapImage");
            mapGO.transform.SetParent(vpGO.transform, false);
            mapRawImageRect = mapGO.AddComponent<RectTransform>();
            // 锚定中心，初始尺寸与 Viewport 一致
            mapRawImageRect.anchorMin = new Vector2(0.5f, 0.5f);
            mapRawImageRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapRawImageRect.pivot = new Vector2(0.5f, 0.5f);
            mapRawImageRect.sizeDelta = viewportRect.sizeDelta;
            mapRawImageRect.anchoredPosition = Vector2.zero;
            mapRawImage = mapGO.AddComponent<RawImage>();
            mapRawImage.raycastTarget = false;

            // ===== PlayerDot（玩家标记圆点，MapRawImage 的子物体）=====
            GameObject dotGO = new GameObject("PlayerDot");
            dotGO.transform.SetParent(mapGO.transform, false);
            playerDotRect = dotGO.AddComponent<RectTransform>();
            playerDotRect.anchorMin = new Vector2(0.5f, 0.5f);
            playerDotRect.anchorMax = new Vector2(0.5f, 0.5f);
            playerDotRect.pivot = new Vector2(0.5f, 0.5f);
            float dotSize = config.playerDotRadius * 2 + 2;
            playerDotRect.sizeDelta = new Vector2(dotSize, dotSize);
            playerDotRect.anchoredPosition = Vector2.zero;
            playerDotImage = dotGO.AddComponent<Image>();
            playerDotSprite = CreateCircleSprite(config.playerDotRadius, out playerDotTexture);
            playerDotImage.sprite = playerDotSprite;
            playerDotImage.color = config.playerDotColor;
            playerDotImage.raycastTarget = false;
        }

        /// <summary>
        /// 设置 RectTransform 铺满父容器。
        /// </summary>
        private static void SetFullScreen(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 根据 Canvas 参考分辨率和地牢宽高比计算 Viewport 的位置和大小。
        ///
        /// 不使用 Screen.width/height 的原因：
        /// CanvasScaler(ScaleWithScreenSize) 将子物体的坐标空间映射到参考分辨率
        /// （如 1920×1200），而非实际屏幕像素。Screen.width 返回的是实际像素值，
        /// 两者不一致会导致 Viewport 尺寸偏小。应使用 CanvasScaler.referenceResolution
        /// 作为坐标基准。
        /// </summary>
        private void SetupViewportRect(RectTransform rt)
        {
            // 从父 Canvas 上的 CanvasScaler 获取参考分辨率
            var canvasScaler = rt.parent.GetComponent<CanvasScaler>();
            float canvasW = canvasScaler != null
                ? canvasScaler.referenceResolution.x
                : Screen.width;
            float canvasH = canvasScaler != null
                ? canvasScaler.referenceResolution.y
                : Screen.height;
            float padding = config.viewportPadding;

            float availW = canvasW - padding * 2f;
            float availH = canvasH - padding * 2f;
            float dungeonAspect = (float)dungeonSize.x / dungeonSize.y;

            float vpWidth, vpHeight;

            if (availW / availH > dungeonAspect)
            {
                vpHeight = availH;
                vpWidth = vpHeight * dungeonAspect;
            }
            else
            {
                vpWidth = availW;
                vpHeight = vpWidth / dungeonAspect;
            }

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(vpWidth, vpHeight);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 程序化生成一个圆形 Sprite 作为玩家标记。
        /// 用距离公式判定每个像素是否在圆内 → 白/透明二值。
        /// 通过 out 参数返回纹理引用，供调用方后续手动销毁。
        /// </summary>
        private static Sprite CreateCircleSprite(int radius, out Texture2D texture)
        {
            int size = radius * 2 + 1;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = radius;
            float radiusSq = radius * radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    bool inside = dx * dx + dy * dy <= radiusSq;
                    pixels[y * size + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(0, 0, 0, 0);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            texture = tex;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        #endregion

        #region 贴图与查找表初始化

        /// <summary>
        /// 创建贴图并全部填充为未探索颜色。
        /// filterMode = Point 确保拉伸时不模糊，保持像素风格。
        /// </summary>
        private void CreateMapTexture()
        {
            mapTexture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            texturePixels = new Color32[texWidth * texHeight];
            for (int i = 0; i < texturePixels.Length; i++)
            {
                texturePixels[i] = unexploredC32;
            }

            mapTexture.SetPixels32(texturePixels);
            mapTexture.Apply();
            mapRawImage.texture = mapTexture;
        }

        /// <summary>
        /// 构建地板格 → 房间索引的查找字典。
        ///
        /// 遍历每个房间的 GetFloorPositions()，将每个格子映射到房间在列表中的索引。
        /// 不在字典中的 AllFloorPositions = 走廊地板。
        ///
        /// 复杂度 O(总地板格数)，生成时执行一次。
        /// </summary>
        private void BuildFloorLookup()
        {
            floorToRoomIndex = new Dictionary<Vector2Int, int>();
            var rooms = dungeonGenerator.Rooms;

            for (int i = 0; i < rooms.Count; i++)
            {
                foreach (var pos in rooms[i].GetFloorPositions())
                {
                    floorToRoomIndex[pos] = i;
                }
            }
        }

        /// <summary>
        /// 将 config 中的 Color 预转换为 Color32，避免每帧反复转换。
        /// </summary>
        private void CacheColors32()
        {
            unexploredC32 = ToColor32(config.unexploredColor);
            corridorC32 = ToColor32(config.corridorColor);
            normalRoomC32 = ToColor32(config.normalRoomColor);
            bossRoomC32 = ToColor32(config.bossRoomColor);
            startRoomC32 = ToColor32(config.startRoomColor);
            treasureRoomC32 = ToColor32(config.treasureRoomColor);
        }

        private static Color32 ToColor32(Color c) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255),
            255);

        #endregion

        #region 地图开关 / 缩放 / 平移

        /// <summary>
        /// 切换地图开关状态。
        ///
        /// 打开时：
        /// - 禁用 PlayerController（防止 WASD 同时控制玩家和地图）
        /// - 复位缩放和平移状态到 1× 居中
        ///
        /// 关闭时：
        /// - 恢复 PlayerController
        /// </summary>
        private void ToggleMap()
        {
            isMapOpen = !isMapOpen;
            mapCanvas.enabled = isMapOpen;

            if (isMapOpen)
            {
                // Awake 时玩家可能尚未生成，打开地图时补一次查找
                if (playerController == null)
                    playerController = FindObjectOfType<PlayerController>();

                if (playerController != null)
                    playerController.enabled = false;

                isZoomedIn = false;
                panOffset = Vector2.zero;
                ApplyViewTransform();
            }
            else
            {
                if (playerController != null)
                    playerController.enabled = true;
            }
        }

        /// <summary>
        /// 在 1× 和 2× 之间切换，每次切换重置平移偏移。
        /// </summary>
        private void ToggleZoom()
        {
            isZoomedIn = !isZoomedIn;
            panOffset = Vector2.zero;
            ApplyViewTransform();
        }

        /// <summary>
        /// 处理 WASD（及方向键）平移输入。
        /// 使用 unscaledDeltaTime 保证即使游戏暂停也能响应。
        /// </summary>
        private void HandlePanInput()
        {
            Vector2 input = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    input.y -= 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  input.y += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  input.x += 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.x -= 1f;

            if (input.sqrMagnitude < 0.01f) return;

            panOffset += input.normalized * config.panSpeed * Time.unscaledDeltaTime;
            panOffset = ClampPanOffset(panOffset);
            mapRawImageRect.anchoredPosition = panOffset;
        }

        /// <summary>
        /// 限制平移范围。
        ///
        /// 2× 缩放时地图尺寸 = Viewport × 2。
        /// 最大允许偏移 = 地图边缘最多平移到 Viewport 边缘，
        /// 即 ± (地图半尺寸 - Viewport 半尺寸) = ± Viewport 半尺寸。
        /// </summary>
        private Vector2 ClampPanOffset(Vector2 offset)
        {
            float maxX = viewportRect.sizeDelta.x * 0.5f;
            float maxY = viewportRect.sizeDelta.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(offset.x, -maxX, maxX),
                Mathf.Clamp(offset.y, -maxY, maxY));
        }

        /// <summary>
        /// 应用缩放变换。
        ///
        /// 只修改 MapRawImage 的 localScale。
        /// PlayerDot 以 localScale 的倒数抵消地图的缩放，
        /// 保证圆点始终维持原始像素大小。
        ///
        /// 1× 时额外将 anchoredPosition 复位到零（居中）。
        /// </summary>
        private void ApplyViewTransform()
        {
            float scale = isZoomedIn ? 2f : 1f;
            mapRawImageRect.localScale = new Vector3(scale, scale, 1f);

            // PlayerDot 反向缩放，保持固定视觉大小
            if (playerDotRect != null)
            {
                playerDotRect.localScale = new Vector3(1f / scale, 1f / scale, 1f);
            }

            // 1× 时锁定居中
            if (!isZoomedIn)
            {
                mapRawImageRect.anchoredPosition = Vector2.zero;
            }
        }

        #endregion

        #region 迷雾逻辑

        /// <summary>
        /// 迷雾更新协程：每 config.fogUpdateInterval 秒执行一次。
        ///
        /// 逻辑：
        /// 1. 检测玩家是否进入新房间 → 若进入，瞬间揭示整个房间
        /// 2. 揭示玩家视野半径范围内的所有地板格（走廊渐进式显示）
        /// 3. 增量更新贴图脏像素
        /// 4. 更新玩家标记 UI 位置
        /// </summary>
        private IEnumerator FogUpdateCoroutine()
        {
            var wait = new WaitForSeconds(config.fogUpdateInterval);

            while (true)
            {
                yield return wait;

                // 玩家尚未生成则重试查找
                if (playerTransform == null)
                {
                    playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
                    if (playerTransform == null) continue;
                }

                if (dungeonGenerator == null || dungeonGenerator.Rooms.Count == 0)
                    continue;

                Vector2Int playerGridPos = WorldToGrid(playerTransform.position);

                // ---- 1. 房间进入检测 ----
                int roomIdx = FindRoomIndexAt(playerGridPos);
                if (roomIdx >= 0 && !visitedRoomIndices.Contains(roomIdx))
                {
                    visitedRoomIndices.Add(roomIdx);
                    RevealRoom(roomIdx);
                }

                // ---- 2. 视野半径揭示 ----
                RevealAround(playerGridPos, config.visionRadius);

                // ---- 3. 贴图提交 ----
                if (hasDirtyPixels)
                {
                    mapTexture.SetPixels32(texturePixels);
                    mapTexture.Apply();
                    hasDirtyPixels = false;
                }

                // ---- 4. 玩家标记位置 ----
                UpdatePlayerDotPosition();
            }
        }

        /// <summary>
        /// 瞬间揭示一个房间的全部地板格。
        /// </summary>
        private void RevealRoom(int roomIdx)
        {
            Room room = dungeonGenerator.Rooms[roomIdx];
            Color32 color = RoomIndexToColor(roomIdx);

            foreach (var pos in room.GetFloorPositions())
            {
                RevealTile(pos, color);
            }
        }

        /// <summary>
        /// 揭示玩家周围 radius 格范围内的地板格。
        ///
        /// 性能优化：只遍历 (2*radius+1)² 范围而非整张地图。
        /// 使用圆形判定（dx² + dy² <= radius²）让视野边缘更自然。
        /// </summary>
        private void RevealAround(Vector2Int center, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;

                    Vector2Int pos = center + new Vector2Int(dx, dy);

                    if (!dungeonGenerator.AllFloorPositions.Contains(pos))
                        continue;

                    if (revealedTiles.Contains(pos))
                        continue;

                    Color32 color = GetTileColor(pos);
                    RevealTile(pos, color);
                }
            }
        }

        /// <summary>
        /// 揭示单个地板格：标记为已探索 + 写入贴图对应的像素矩形区域。
        ///
        /// 一个世界格子通常对应贴图上的多像素（如 5×5），
        /// 必须填充整个区域而非单个像素，否则相邻格子会呈现为分离的孤立点。
        /// </summary>
        private void RevealTile(Vector2Int worldPos, Color32 color)
        {
            if (!revealedTiles.Add(worldPos)) return;

            // 计算该世界格在贴图中对应的像素矩形
            int pxStart = Mathf.FloorToInt((float)worldPos.x / dungeonSize.x * texWidth);
            int pyStart = Mathf.FloorToInt((float)worldPos.y / dungeonSize.y * texHeight);
            int pxEnd = Mathf.FloorToInt((float)(worldPos.x + 1) / dungeonSize.x * texWidth);
            int pyEnd = Mathf.FloorToInt((float)(worldPos.y + 1) / dungeonSize.y * texHeight);

            // Clamp 到贴图边界内
            pxStart = Mathf.Max(0, pxStart);
            pyStart = Mathf.Max(0, pyStart);
            pxEnd = Mathf.Min(texWidth, pxEnd);
            pyEnd = Mathf.Min(texHeight, pyEnd);

            for (int py = pyStart; py < pyEnd; py++)
            {
                for (int px = pxStart; px < pxEnd; px++)
                {
                    texturePixels[py * texWidth + px] = color;
                }
            }
            hasDirtyPixels = true;
        }

        /// <summary>
        /// 根据地板格的世界坐标查表获取对应颜色。
        /// 查 floorToRoomIndex 字典 O(1)，不在字典中即为走廊。
        /// </summary>
        private Color32 GetTileColor(Vector2Int worldPos)
        {
            if (floorToRoomIndex.TryGetValue(worldPos, out int roomIdx))
                return RoomIndexToColor(roomIdx);

            return corridorC32;
        }

        /// <summary>
        /// 房间索引 → 对应颜色（按房间类型）。
        /// </summary>
        private Color32 RoomIndexToColor(int roomIdx)
        {
            var room = dungeonGenerator.Rooms[roomIdx];
            return room.type switch
            {
                RoomType.Boss => bossRoomC32,
                RoomType.Start => startRoomC32,
                RoomType.Treasure => treasureRoomC32,
                _ => normalRoomC32,
            };
        }

        #endregion

        #region 玩家标记

        /// <summary>
        /// 玩家圆点闪烁协程：周期性切换 Image 的 alpha。
        /// 地图关闭时 Alpha 设为 0（不可见）。
        /// </summary>
        private IEnumerator PlayerDotBlinkCoroutine()
        {
            var wait = new WaitForSeconds(config.blinkInterval);

            while (true)
            {
                yield return wait;

                if (playerDotImage == null) continue;

                Color c = playerDotImage.color;
                c.a = isMapOpen ? (c.a > 0.5f ? 0.2f : 1f) : 0f;
                playerDotImage.color = c;
            }
        }

        /// <summary>
        /// 更新 PlayerDot 在 MapRawImage 中的本地位置。
        ///
        /// 坐标流程：世界坐标 → 贴图像素 → MapRawImage 本地坐标。
        ///
        /// PlayerDot 是 MapRawImage 的子物体，所以平移/缩放时
        /// Unity Transform 层级会自动处理相对位置，这里只需设置
        /// 正确的初始本地坐标。
        /// </summary>
        private void UpdatePlayerDotPosition()
        {
            if (playerDotRect == null || playerTransform == null) return;
            if (mapRawImageRect == null) return;

            Vector2Int gridPos = WorldToGrid(playerTransform.position);
            Vector2Int pixel = WorldToPixel(gridPos);
            Vector2 rawImageSize = mapRawImageRect.sizeDelta;

            // 像素位置 → MapRawImage 本地坐标（pivot 为中心，范围 ±size/2）
            float localX = ((float)pixel.x / texWidth - 0.5f) * rawImageSize.x;
            float localY = ((float)pixel.y / texHeight - 0.5f) * rawImageSize.y;

            playerDotRect.anchoredPosition = new Vector2(localX, localY);
        }

        #endregion

        #region 坐标转换工具

        /// <summary>
        /// 世界坐标（float）→ 网格坐标（int）。
        /// FloorToInt 保证像素对齐。
        /// </summary>
        private static Vector2Int WorldToGrid(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x),
                Mathf.FloorToInt(worldPos.y));
        }

        /// <summary>
        /// 世界网格坐标 → 贴图像素坐标。
        ///
        /// 世界原点在左下角，(0,0) → (0,0)。
        /// Texture2D.SetPixels 也是左下角为原点，
        /// 因此无需翻转 Y 轴。
        /// </summary>
        private Vector2Int WorldToPixel(Vector2Int worldPos)
        {
            int px = Mathf.FloorToInt((float)worldPos.x / dungeonSize.x * texWidth);
            int py = Mathf.FloorToInt((float)worldPos.y / dungeonSize.y * texHeight);
            return new Vector2Int(px, py);
        }

        /// <summary>
        /// 查找包含指定网格坐标的房间索引。
        /// 查 floorToRoomIndex 字典 O(1)。
        /// 不在任何房间内返回 -1。
        /// </summary>
        private int FindRoomIndexAt(Vector2Int gridPos)
        {
            if (floorToRoomIndex.TryGetValue(gridPos, out int roomIdx))
                return roomIdx;
            return -1;
        }

        #endregion
    }
}
