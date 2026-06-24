using System.Collections.Generic;
using UnityEngine;

namespace DungeonShooter.Dungeon
{
    /// <summary>
    /// ScriptableObject：定义房间的形状模板。
    /// 每个模板是一个宽×高的网格，标记每格是否是地板。
    /// 生成地牢时从模板池中随机选取。
    /// </summary>
    [CreateAssetMenu(fileName = "RoomTemplate", menuName = "Dungeon/Room Template")]
    public class RoomTemplate : ScriptableObject
    {
        [Header("基础信息")]
        [Tooltip("模板名称")]
        public string templateName = "新模板";

        [Tooltip("房间宽度（瓦片数），最小 3")]
        [Min(3)]
        public int width = 5;

        [Tooltip("房间高度（瓦片数），最小 3")]
        [Min(3)]
        public int height = 5;

        [Header("瓦片网格")]
        [Tooltip("0 = 空白, 1 = 地板。编辑器内可视编辑。")]
        [HideInInspector]
        public int[] grid;

        // 跟踪上一次的宽高，用于宽高变化时正确保留旧数据
        [SerializeField, HideInInspector]
        private int previousWidth;
        [SerializeField, HideInInspector]
        private int previousHeight;

        [Header("生成参数")]
        [Tooltip("该模板允许生成的房间类型")]
        public List<RoomType> allowedTypes = new List<RoomType> { RoomType.Normal };

        [Tooltip("随机选取权重，越大越容易被选中")]
        [Range(0.1f, 10f)]
        public float weight = 1f;

        // ---- 网格访问方法 ----

        /// <summary>
        /// 获取指定坐标的瓦片值。越界返回 0（空白）。
        /// </summary>
        public int GetCell(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return 0;
            if (grid == null || grid.Length != width * height)
                return 1; // 数组未初始化时默认为地板
            return grid[y * width + x];
        }

        /// <summary>
        /// 设置指定坐标的瓦片值。
        /// </summary>
        public void SetCell(int x, int y, int value)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;
            EnsureGrid();
            grid[y * width + x] = value;
        }

        /// <summary>
        /// 确保 grid 数组存在且长度匹配当前宽高。
        /// 使用 previousWidth/previousHeight 记录旧尺寸，非正方形网格也能正确保留数据。
        /// </summary>
        public void EnsureGrid()
        {
            int requiredLength = width * height;
            if (grid == null || grid.Length != requiredLength)
            {
                int[] oldGrid = grid;
                grid = new int[requiredLength];

                if (oldGrid != null)
                {
                    // 使用记录的旧宽高，非正方形网格也能正确复制
                    int oldW = previousWidth > 0 ? previousWidth : 1;
                    int oldH = previousHeight > 0 ? previousHeight : 1;
                    int copyW = Mathf.Min(width, oldW);
                    int copyH = Mathf.Min(height, oldH);
                    for (int y = 0; y < copyH; y++)
                    {
                        for (int x = 0; x < copyW; x++)
                        {
                            int oldIdx = y * oldW + x;
                            int newIdx = y * width + x;
                            if (oldIdx < oldGrid.Length)
                                grid[newIdx] = oldGrid[oldIdx];
                        }
                    }
                }

                // 更新记录
                previousWidth = width;
                previousHeight = height;
            }
        }

        /// <summary>
        /// 将整个网格填充为指定值。
        /// </summary>
        public void FillGrid(int value)
        {
            EnsureGrid();
            for (int i = 0; i < grid.Length; i++)
                grid[i] = value;
        }

        // ---- 快捷操作（右键菜单）----

        /// <summary>
        /// 生成默认矩形：外圈空白（留给墙壁），内部全地板。
        /// </summary>
        [ContextMenu("生成默认矩形")]
        public void GenerateDefaultRectangle()
        {
            EnsureGrid();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = (x == 0 || y == 0 || x == width - 1 || y == height - 1);
                    grid[y * width + x] = isBorder ? 0 : 1;
                }
            }
        }

        /// <summary>
        /// 全部填充为地板（实心矩形）。
        /// </summary>
        [ContextMenu("全部填充为地板")]
        public void FillAllFloor()
        {
            FillGrid(1);
        }

        // ---- Unity 生命周期 ----

        private void OnEnable()
        {
            // 第一次创建资产时自动生成默认矩形形状
            if (grid == null || grid.Length == 0)
            {
                GenerateDefaultRectangle();
            }
        }

        private void OnValidate()
        {
            // Inspector 中修改宽高时自动调整数组大小
            if (grid != null && grid.Length != width * height)
            {
                EnsureGrid();
            }
        }
    }
}
