using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonShooter.Dungeon
{
    /// <summary>
    /// 地牢生成器主控制器。
    /// 挂载在场景 GameObject 上，驱动完整的地牢生成流程。
    ///
    /// 算法流程：放置房间 → 分离重叠 → 筛除无效 → 分配类型
    ///         → MST 连接 → 生成走廊 → Tilemap 渲染
    /// </summary>
    public class DungeonGenerator : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("地牢生成参数（ScriptableObject）")]
        public DungeonData dungeonData;

        [Header("Tilemap 引用")]
        [Tooltip("地板层 Tilemap")]
        public Tilemap floorTilemap;
        [Tooltip("墙壁层 Tilemap")]
        public Tilemap wallTilemap;

        [Header("Tile 资源")]
        [Tooltip("地板 Tile（拖入你的地板 Tile 资产）")]
        public TileBase floorTile;
        [Tooltip("墙壁 Tile（拖入你的墙壁 Tile 资产）")]
        public TileBase wallTile;

        [Header("调试")]
        [Tooltip("启用后每次生成输出详细日志")]
        public bool verboseLogging = false;

        // ---- 内部状态 ----
        private List<Room> rooms = new List<Room>();
        private List<(int from, int to)> connections = new List<(int, int)>();
        private HashSet<Vector2Int> allFloorPositions = new HashSet<Vector2Int>();
        private HashSet<Vector2Int> allWallPositions = new HashSet<Vector2Int>();
        private System.Random rng;

        /// <summary>生成的房间列表（只读），供其他系统使用</summary>
        public IReadOnlyList<Room> Rooms => rooms;
        /// <summary>所有地板瓦片坐标（只读）</summary>
        public IReadOnlyCollection<Vector2Int> AllFloorPositions => allFloorPositions;

        #region 公共入口

        /// <summary>
        /// 生成地牢（右键菜单入口，使用随机种子）。
        /// </summary>
        [ContextMenu("Generate Dungeon")]
        public void GenerateDungeon()
        {
            Generate(-1);
        }

        /// <summary>
        /// 生成地牢。
        /// </summary>
        /// <param name="seed">随机种子，-1 表示使用系统时间种子</param>
        public void Generate(int seed = -1)
        {
            if (seed < 0)
                seed = Environment.TickCount;

            rng = new System.Random(seed);
            Log($"开始生成地牢，种子: {seed}");

            Clear();
            PlaceRooms();
            SeparateRooms();
            RemoveInvalidRooms();

            if (rooms.Count == 0)
            {
                Debug.LogError("[DungeonGenerator] 所有房间都被筛除了，请检查参数！");
                return;
            }

            AssignRoomTypes();
            BuildConnections();
            BuildCorridors();
            RenderToTilemap();

            Log($"生成完成！共 {rooms.Count} 个房间, {connections.Count} 条走廊");
        }

        /// <summary>
        /// 清除地牢（清空 Tilemap 和所有数据）
        /// </summary>
        [ContextMenu("Clear Dungeon")]
        public void Clear()
        {
            rooms.Clear();
            connections.Clear();
            allFloorPositions.Clear();
            allWallPositions.Clear();

            if (floorTilemap != null)
                floorTilemap.ClearAllTiles();
            if (wallTilemap != null)
                wallTilemap.ClearAllTiles();
        }

        #endregion

        #region 步骤①：随机放置房间

        private void PlaceRooms()
        {
            if (dungeonData == null)
            {
                Debug.LogError("[DungeonGenerator] DungeonData 未赋值！");
                return;
            }

            if (dungeonData.roomTemplates == null || dungeonData.roomTemplates.Count == 0)
            {
                Debug.LogError("[DungeonGenerator] 房间模板池为空！");
                return;
            }

            Vector2Int dSize = dungeonData.DungeonSize;

            for (int i = 0; i < dungeonData.roomCount; i++)
            {
                // 加权随机选取模板
                RoomTemplate template = PickWeightedTemplate();
                Vector2Int size = new Vector2Int(template.width, template.height);

                // 确保房间不会超出地牢边界
                int maxX = dSize.x - size.x - 1;
                int maxY = dSize.y - size.y - 1;
                if (maxX <= 1 || maxY <= 1) continue;

                Vector2Int pos = new Vector2Int(
                    rng.Next(1, maxX + 1),  // +1 因为 Next(max) 是不包含的
                    rng.Next(1, maxY + 1)
                );

                rooms.Add(new Room(pos, size, template));
            }

            Log($"放置了 {rooms.Count} 个房间");
        }

        /// <summary>
        /// 按权重随机从模板池中选取一个模板。
        /// 权重越大，被选中的概率越高。
        /// </summary>
        private RoomTemplate PickWeightedTemplate()
        {
            var templates = dungeonData.roomTemplates;
            float totalWeight = 0f;
            foreach (var t in templates) totalWeight += t.weight;

            float roll = (float)rng.NextDouble() * totalWeight;
            float cumulative = 0f;
            foreach (var t in templates)
            {
                cumulative += t.weight;
                if (roll <= cumulative) return t;
            }
            return templates[templates.Count - 1];
        }

        #endregion

        #region 步骤②：分离重叠房间

        private void SeparateRooms()
        {
            Vector2Int dSize = dungeonData.DungeonSize;

            for (int iter = 0; iter < dungeonData.separationIterations; iter++)
            {
                bool moved = false;

                for (int i = 0; i < rooms.Count; i++)
                {
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        Room a = rooms[i];
                        Room b = rooms[j];

                        if (!a.Overlaps(b, padding: 1)) continue;

                        moved = true;
                        Vector2Int push = CalcSeparation(a, b);

                        a.position += new Vector2Int(
                            Mathf.CeilToInt(push.x / 2f),
                            Mathf.CeilToInt(push.y / 2f));
                        b.position += new Vector2Int(
                            -Mathf.FloorToInt(push.x / 2f),
                            -Mathf.FloorToInt(push.y / 2f));

                        ClampToBounds(a, dSize);
                        ClampToBounds(b, dSize);
                    }
                }

                if (!moved) break; // 没有重叠了，提前退出
            }

            Log("分离完成");
        }

        /// <summary>
        /// 计算两个重叠房间的分离方向与距离。
        /// 选择重叠较小的轴推开，让房间位移最小。
        /// </summary>
        private Vector2Int CalcSeparation(Room a, Room b)
        {
            RectInt ra = a.Bounds, rb = b.Bounds;
            int overlapX = Mathf.Min(ra.xMax, rb.xMax) - Mathf.Max(ra.xMin, rb.xMin);
            int overlapY = Mathf.Min(ra.yMax, rb.yMax) - Mathf.Max(ra.yMin, rb.yMin);

            if (Mathf.Abs(overlapX) <= Mathf.Abs(overlapY))
            {
                int sign = ra.center.x < rb.center.x ? -1 : 1;
                return new Vector2Int(sign * (overlapX + 1), 0);
            }
            else
            {
                int sign = ra.center.y < rb.center.y ? -1 : 1;
                return new Vector2Int(0, sign * (overlapY + 1));
            }
        }

        private void ClampToBounds(Room room, Vector2Int dSize)
        {
            room.position = new Vector2Int(
                Mathf.Clamp(room.position.x, 1, Mathf.Max(1, dSize.x - room.size.x - 1)),
                Mathf.Clamp(room.position.y, 1, Mathf.Max(1, dSize.y - room.size.y - 1))
            );
        }

        #endregion

        #region 步骤③：筛除无效房间

        private void RemoveInvalidRooms()
        {
            Vector2Int dSize = dungeonData.DungeonSize;

            rooms.RemoveAll(room =>
            {
                // 超出边界的移除
                if (room.position.x < 0 || room.position.y < 0
                    || room.position.x + room.size.x > dSize.x
                    || room.position.y + room.size.y > dSize.y)
                {
                    Log($"移除房间 ({room.position.x},{room.position.y}): 超出边界");
                    return true;
                }

                // 过度重叠的移除（与其他房间重叠面积 > 自身 50%）
                int roomArea = room.size.x * room.size.y;
                foreach (var other in rooms)
                {
                    if (other == room) continue;
                    if (!room.Overlaps(other, padding: 0)) continue;

                    RectInt overlap = GetOverlap(room.Bounds, other.Bounds);
                    if (overlap.width * overlap.height > roomArea * 0.5f)
                    {
                        Log($"移除房间 ({room.position.x},{room.position.y}): 过度重叠");
                        return true;
                    }
                }

                return false;
            });

            Log($"筛除后剩余 {rooms.Count} 个房间");
        }

        private RectInt GetOverlap(RectInt a, RectInt b)
        {
            int xMin = Mathf.Max(a.xMin, b.xMin);
            int yMin = Mathf.Max(a.yMin, b.yMin);
            int xMax = Mathf.Min(a.xMax, b.xMax);
            int yMax = Mathf.Min(a.yMax, b.yMax);
            if (xMin < xMax && yMin < yMax)
                return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
            return new RectInt(0, 0, 0, 0);
        }

        #endregion

        #region 步骤④：分配房间类型

        private void AssignRoomTypes()
        {
            if (rooms.Count == 0) return;

            Vector2Int dungeonCenter = dungeonData.DungeonSize / 2;

            // 按与地牢中心的距离排序
            var sorted = rooms
                .Select((room, index) => (room, index, dist: Vector2Int.Distance(room.Center, dungeonCenter)))
                .OrderBy(x => x.dist)
                .ToList();

            // Boss 房：模板允许 Boss 类型且最靠近中心的房间
            var bossCandidate = sorted.FirstOrDefault(x => IsAllowed(x.room, RoomType.Boss));
            if (bossCandidate != default)
            {
                bossCandidate.room.type = RoomType.Boss;
                Log($"Boss 房: #{bossCandidate.index} (距中心 {bossCandidate.dist:F0})");
            }
            else
            {
                sorted[0].room.type = RoomType.Boss;
                Log($"Boss 房（回退）: #{sorted[0].index}");
            }

            // 出生房：模板允许 Start 类型且离 Boss 房最远的房间
            var bossRoom = rooms.FirstOrDefault(r => r.type == RoomType.Boss);
            if (bossRoom != null)
            {
                var startCandidate = sorted
                    .Where(x => x.room.type == RoomType.Normal && IsAllowed(x.room, RoomType.Start))
                    .OrderByDescending(x => Vector2Int.Distance(x.room.Center, bossRoom.Center))
                    .FirstOrDefault();

                if (startCandidate != default)
                {
                    startCandidate.room.type = RoomType.Start;
                    Log($"出生房: #{startCandidate.index} (距Boss {Vector2Int.Distance(startCandidate.room.Center, bossRoom.Center):F0})");
                }
                else
                {
                    var fallback = sorted.Last(x => x.room.type == RoomType.Normal);
                    fallback.room.type = RoomType.Start;
                    Log($"出生房（回退）: #{fallback.index}");
                }
            }

            // 宝箱房：从模板允许 Treasure 的 Normal 房间中随机选取
            int treasureCount = dungeonData.TreasureRoomCount(rooms.Count);
            var treasureCandidates = sorted
                .Where(x => x.room.type == RoomType.Normal && IsAllowed(x.room, RoomType.Treasure))
                .OrderBy(x => rng.Next())
                .Take(treasureCount);

            foreach (var item in treasureCandidates)
            {
                item.room.type = RoomType.Treasure;
                Log($"宝箱房: #{item.index}");
            }

            Log("房间类型分配完成");
        }

        /// <summary>
        /// 检查房间的模板是否允许指定的房间类型。
        /// 如果模板没有配置 allowedTypes，默认允许所有类型。
        /// </summary>
        private bool IsAllowed(Room room, RoomType type)
        {
            if (room.template == null) return true;
            if (room.template.allowedTypes == null || room.template.allowedTypes.Count == 0)
                return true;
            return room.template.allowedTypes.Contains(type);
        }

        #endregion

        #region 步骤⑤：连接房间（并查集 + 最小生成树）

        private void BuildConnections()
        {
            if (rooms.Count < 2) return;

            int n = rooms.Count;

            // 构建所有可能的房间对，按距离排序
            var allEdges = new List<(int from, int to, float dist)>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float d = Vector2Int.Distance(rooms[i].Center, rooms[j].Center);
                    allEdges.Add((i, j, d));
                }
            }
            allEdges.Sort((a, b) => a.dist.CompareTo(b.dist));

            // 并查集初始化：每个房间是自己的根
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            var mstEdges = new List<(int, int)>();
            var spareEdges = new List<(int, int)>();

            // Kruskal 算法：遍历最短边，连接不同集合的房间
            foreach (var (from, to, dist) in allEdges)
            {
                if (Find(from) != Find(to))
                {
                    Union(from, to);
                    mstEdges.Add((from, to));
                }
                else
                {
                    spareEdges.Add((from, to));
                }
            }

            // 额外加一些边产生环路（让地图探索更有趣）
            int extraCount = Mathf.FloorToInt(spareEdges.Count * dungeonData.extraEdgeChance);
            var shuffledSpare = spareEdges.OrderBy(x => rng.Next()).ToList();

            connections.AddRange(mstEdges);
            for (int i = 0; i < extraCount; i++)
                connections.Add(shuffledSpare[i]);

            Log($"连接: {mstEdges.Count} 条主干 + {extraCount} 条额外");

            // ---- 并查集局部函数 ----
            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]]; // 路径压缩
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[rb] = ra;
            }
        }

        #endregion

        #region 步骤⑥：生成走廊

        private void BuildCorridors()
        {
            // 先收集所有房间的地板格子
            foreach (var room in rooms)
            {
                foreach (var pos in room.GetFloorPositions())
                {
                    allFloorPositions.Add(pos);
                }
            }

            // 为每条连接生成 L 形走廊
            foreach (var (fromIdx, toIdx) in connections)
            {
                var corridorTiles = CorridorGenerator.GenerateCorridor(
                    rooms[fromIdx].Center,
                    rooms[toIdx].Center,
                    rng
                );

                foreach (var tile in corridorTiles)
                {
                    allFloorPositions.Add(tile);
                }
            }

            Log($"地板总格数: {allFloorPositions.Count}");
        }

        #endregion

        #region 步骤⑦：渲染到 Tilemap

        private void RenderToTilemap()
        {
            if (floorTilemap == null || wallTilemap == null)
            {
                Debug.LogError("[DungeonGenerator] Tilemap 引用未赋值！");
                return;
            }
            if (floorTile == null || wallTile == null)
            {
                Debug.LogError("[DungeonGenerator] Tile 资源未赋值！");
                return;
            }

            floorTilemap.ClearAllTiles();
            wallTilemap.ClearAllTiles();

            // 确定墙壁位置：地板格的四邻域中，非地板的格子就是墙壁
            allWallPositions.Clear();
            foreach (var floorPos in allFloorPositions)
            {
                foreach (var dir in EightDirections)
                {
                    Vector2Int neighbor = floorPos + dir;
                    if (!allFloorPositions.Contains(neighbor))
                    {
                        allWallPositions.Add(neighbor);
                    }
                }
            }

            // 批量写入 Tilemap
            PaintTiles(floorTilemap, allFloorPositions, floorTile);
            PaintTiles(wallTilemap, allWallPositions, wallTile);

            Log($"渲染: {allFloorPositions.Count} 地板, {allWallPositions.Count} 墙壁");
        }

        /// <summary>
        /// 批量将 HashSet 中的位置写入 Tilemap。
        /// 使用 SetTilesBlock 一次性提交一个矩形区域，比逐个 SetTile 高效很多。
        /// </summary>
        private void PaintTiles(Tilemap tilemap, HashSet<Vector2Int> positions, TileBase tile)
        {
            if (positions.Count == 0) return;

            // 计算包围盒
            int minX = positions.Min(p => p.x);
            int maxX = positions.Max(p => p.x);
            int minY = positions.Min(p => p.y);
            int maxY = positions.Max(p => p.y);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;

            // 构造 TileBase 数组（大部分是 null，只在 positions 中的位置放入 tile）
            TileBase[] tileArray = new TileBase[width * height];
            foreach (var pos in positions)
            {
                int localX = pos.x - minX;
                int localY = pos.y - minY;
                tileArray[localY * width + localX] = tile;
            }

            // 一次性写入整个区域
            tilemap.SetTilesBlock(
                new BoundsInt(minX, minY, 0, width, height, 1),
                tileArray
            );
        }

        #endregion

        #region 工具

        private static readonly Vector2Int[] EightDirections =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1, 1), new Vector2Int(1, -1),     // 右上、右下
            new Vector2Int(-1, 1), new Vector2Int(-1, -1),   // 左上、左下
        };

        private void Log(string message)
        {
            if (verboseLogging)
                Debug.Log($"[DungeonGenerator] {message}");
        }

        #endregion

        #region Gizmos 调试绘制

#if UNITY_EDITOR
        /// <summary>
        /// 在 Scene 视图中绘制房间范围和连接线（仅选中 DungeonGenerator 时可见）。
        /// 帮助调试生成效果，不影响运行性能。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (rooms == null || rooms.Count == 0) return;

            // 绘制房间
            foreach (var room in rooms)
            {
                Color color = room.type switch
                {
                    RoomType.Boss => Color.red,
                    RoomType.Start => Color.green,
                    RoomType.Treasure => Color.yellow,
                    _ => new Color(0.3f, 0.6f, 1f),
                };

                Gizmos.color = color;
                Vector3 center = new Vector3(room.Center.x, room.Center.y, 0);
                Vector3 size = new Vector3(room.size.x, room.size.y, 0);
                Gizmos.DrawWireCube(center, size);
            }

            // 绘制连接线
            if (connections != null)
            {
                Gizmos.color = Color.white;
                foreach (var (from, to) in connections)
                {
                    if (from < rooms.Count && to < rooms.Count)
                    {
                        Gizmos.DrawLine(
                            new Vector3(rooms[from].Center.x, rooms[from].Center.y, 0),
                            new Vector3(rooms[to].Center.x, rooms[to].Center.y, 0)
                        );
                    }
                }
            }
        }
#endif

        #endregion
    }
}

