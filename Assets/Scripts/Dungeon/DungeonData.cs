using System.Collections.Generic;
using UnityEngine;

namespace DungeonShooter.Dungeon
{
    /// <summary>
    /// ScriptableObject：地牢生成的可配置参数。
    /// 数据与 DungeonGenerator 算法逻辑分离，
    /// 可以创建多个 .asset 文件来配置不同风格的地牢。
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonData", menuName = "Dungeon/Dungeon Data")]
    public class DungeonData : ScriptableObject
    {
        [Header("地牢尺寸")]
        [Tooltip("地牢总宽度（瓦片数）")]
        [Min(20)]
        public int dungeonWidth = 80;

        [Tooltip("地牢总高度（瓦片数）")]
        [Min(20)]
        public int dungeonHeight = 60;

        [Header("房间生成")]
        [Tooltip("初始放置的房间数量")]
        [Range(5, 50)]
        public int roomCount = 15;

        [Tooltip("房间模板池，生成时随机选取")]
        public List<RoomTemplate> roomTemplates = new List<RoomTemplate>();

        [Header("分离参数")]
        [Tooltip("重叠房间分离的迭代次数，越大房间越分散但越耗时")]
        [Range(1, 50)]
        public int separationIterations = 15;

        [Header("连接参数")]
        [Tooltip("额外连接边的比例，产生环路让地图更有探索感")]
        [Range(0f, 1f)]
        public float extraEdgeChance = 0.15f;

        [Header("房间类型分配")]
        [Tooltip("宝箱房占房间总数的比例")]
        [Range(0f, 1f)]
        public float treasureRoomRatio = 0.15f;

        /// <summary>便捷获取地牢尺寸</summary>
        public Vector2Int DungeonSize => new Vector2Int(dungeonWidth, dungeonHeight);

        /// <summary>根据总房间数计算宝箱房数量，至少 1 个</summary>
        public int TreasureRoomCount(int totalRooms)
        {
            return Mathf.Max(1, Mathf.RoundToInt(totalRooms * treasureRoomRatio));
        }
    }
}
