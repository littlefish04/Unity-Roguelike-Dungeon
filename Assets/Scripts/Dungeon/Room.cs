using System.Collections.Generic;
using UnityEngine;

namespace DungeonShooter.Dungeon
{
    /// <summary>
    /// 运行时房间数据。
    /// 不继承 MonoBehaviour，是纯数据容器（POCO），
    /// 方便生成阶段用 new Room(...) 大量创建，无需 Instantiate。
    /// </summary>
    [System.Serializable]
    public class Room
    {
        /// <summary>房间左下角在地牢坐标系中的位置</summary>
        public Vector2Int position;

        /// <summary>房间尺寸（宽、高瓦片数）</summary>
        public Vector2Int size;

        /// <summary>房间类型</summary>
        public RoomType type;

        /// <summary>创建此房间所使用的模板引用（只读，来自 ScriptableObject）</summary>
        public RoomTemplate template;

        /// <summary>房间中心坐标（只读计算属性）</summary>
        public Vector2Int Center => position + size / 2;

        /// <summary>房间矩形范围（只读计算属性）</summary>
        public RectInt Bounds => new RectInt(position, size);

        public Room(Vector2Int position, Vector2Int size, RoomTemplate template = null)
        {
            this.position = position;
            this.size = size;
            this.template = template;
            this.type = RoomType.Normal;
        }

        /// <summary>
        /// 检查此房间是否与另一个房间重叠。
        /// </summary>
        /// <param name="other">另一个房间</param>
        /// <param name="padding">额外间距（瓦片数），默认 1 格</param>
        public bool Overlaps(Room other, int padding = 1)
        {
            // 在自己的矩形外围加上 padding，用于检测需要间隔的情况
            RectInt padded = new RectInt(
                position.x - padding,
                position.y - padding,
                size.x + padding * 2,
                size.y + padding * 2
            );
            return padded.Overlaps(other.Bounds);
        }

        /// <summary>
        /// 获取此房间内所有"地板"格子的地牢世界坐标。
        /// 这是连接走廊和渲染 Tilemap 的关键数据。
        /// </summary>
        public List<Vector2Int> GetFloorPositions()
        {
            var positions = new List<Vector2Int>();

            if (template != null && template.grid != null
                && template.grid.Length == size.x * size.y)
            {
                // 有模板时，读取模板网格中标记为 1（地板）的位置
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        if (template.GetCell(x, y) == 1)
                        {
                            positions.Add(new Vector2Int(
                                position.x + x,
                                position.y + y
                            ));
                        }
                    }
                }
            }
            else
            {
                // 没有模板时的回退逻辑：整个矩形都是地板
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        positions.Add(new Vector2Int(
                            position.x + x,
                            position.y + y
                        ));
                    }
                }
            }

            return positions;
        }
    }
}
