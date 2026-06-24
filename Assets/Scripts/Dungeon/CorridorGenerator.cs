using System.Collections.Generic;
using UnityEngine;

namespace DungeonShooter.Dungeon
{
    /// <summary>
    /// 静态工具类：在房间之间生成 L 形走廊路径。
    /// 纯数学计算，无状态，不依赖 Unity 生命周期。
    /// </summary>
    public static class CorridorGenerator
    {
        /// <summary>
        /// 生成从 from 到 to 的 L 形走廊。
        /// </summary>
        /// <param name="from">起始房间中心坐标</param>
        /// <param name="to">目标房间中心坐标</param>
        /// <param name="rng">随机数生成器，用于决定拐角方向</param>
        /// <returns>走廊路径包含的所有瓦片坐标（HashSet 去重）</returns>
        public static HashSet<Vector2Int> GenerateCorridor(
            Vector2Int from, Vector2Int to, System.Random rng)
        {
            var tiles = new HashSet<Vector2Int>();

            // 随机选择：先水平再垂直，还是先垂直再水平
            bool horizontalFirst = rng.Next(2) == 0;

            Vector2Int corner;
            if (horizontalFirst)
            {
                // 先水平再垂直：A → corner(拐角) → B
                corner = new Vector2Int(to.x, from.y);
                DrawHorizontalLine(tiles, from.x, corner.x, from.y);
                DrawVerticalLine(tiles, corner.y, to.y, to.x);
            }
            else
            {
                // 先垂直再水平：A → corner(拐角) → B
                corner = new Vector2Int(from.x, to.y);
                DrawVerticalLine(tiles, from.y, corner.y, from.x);
                DrawHorizontalLine(tiles, corner.x, to.x, to.y);
            }

            return tiles;
        }

        /// <summary>在指定 y 上从 x1 到 x2 画一条水平线</summary>
        private static void DrawHorizontalLine(
            HashSet<Vector2Int> tiles, int x1, int x2, int y)
        {
            int min = Mathf.Min(x1, x2);
            int max = Mathf.Max(x1, x2);
            for (int x = min; x <= max; x++)
                tiles.Add(new Vector2Int(x, y));
        }

        /// <summary>在指定 x 上从 y1 到 y2 画一条垂直线</summary>
        private static void DrawVerticalLine(
            HashSet<Vector2Int> tiles, int y1, int y2, int x)
        {
            int min = Mathf.Min(y1, y2);
            int max = Mathf.Max(y1, y2);
            for (int y = min; y <= max; y++)
                tiles.Add(new Vector2Int(x, y));
        }
    }
}
