using UnityEngine;

namespace DungeonShooter.UI
{
    /// <summary>
    /// 小地图系统可配置参数（ScriptableObject）。
    ///
    /// 数据驱动：修改 Inspector 中的值即可调整小地图外观，
    /// 无需重新编译代码。支持创建多份配置用于不同地牢主题。
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Minimap Config", fileName = "MinimapConfig")]
    public class MinimapConfig : ScriptableObject
    {
        [Header("贴图分辨率")]
        [Tooltip("小地图贴图宽度（像素）")]
        [Range(100, 1024)]
        public int textureWidth = 400;

        [Tooltip("小地图贴图高度（像素）")]
        [Range(100, 1024)]
        public int textureHeight = 300;

        [Header("颜色（未探索 / 已探索）")]
        [Tooltip("未探索区域颜色（默认近乎全黑）")]
        public Color unexploredColor = new Color(0.05f, 0.05f, 0.08f, 1f);

        [Tooltip("走廊地板颜色")]
        public Color corridorColor = new Color(0.25f, 0.25f, 0.32f, 1f);

        [Tooltip("普通房间颜色")]
        public Color normalRoomColor = new Color(0.35f, 0.35f, 0.42f, 1f);

        [Tooltip("Boss 房间颜色")]
        public Color bossRoomColor = new Color(0.82f, 0.18f, 0.18f, 1f);

        [Tooltip("出生房间颜色")]
        public Color startRoomColor = new Color(0.18f, 0.72f, 0.28f, 1f);

        [Tooltip("宝箱房间颜色")]
        public Color treasureRoomColor = new Color(0.88f, 0.78f, 0.15f, 1f);

        [Header("迷雾")]
        [Tooltip("玩家视野半径（格子数），走廊中逐步照亮此范围内的地板")]
        [Range(2, 12)]
        public int visionRadius = 5;

        [Tooltip("迷雾更新间隔（秒），值越小更新越频繁")]
        [Range(0.05f, 0.5f)]
        public float fogUpdateInterval = 0.1f;

        [Header("玩家标记")]
        [Tooltip("玩家圆点颜色")]
        public Color playerDotColor = Color.white;

        [Tooltip("玩家圆点半径（像素）")]
        [Range(2, 10)]
        public int playerDotRadius = 5;

        [Tooltip("玩家圆点闪烁间隔（秒）")]
        [Range(0.1f, 1f)]
        public float blinkInterval = 0.3f;

        [Header("操作")]
        [Tooltip("开关地图的按键")]
        public KeyCode toggleKey = KeyCode.M;

        [Tooltip("缩放地图的按键")]
        public KeyCode zoomKey = KeyCode.Tab;

        [Tooltip("WASD 平移速度（像素/秒）")]
        [Range(100f, 1000f)]
        public float panSpeed = 400f;

        [Header("地图 UI 布局")]
        [Tooltip("地图面板距离屏幕四边的 padding（像素）")]
        [Range(5f, 200f)]
        public float viewportPadding = 60f;

        [Tooltip("地图背景遮罩的不透明度")]
        [Range(0.5f, 1f)]
        public float backgroundAlpha = 0.85f;
    }
}
