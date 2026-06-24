namespace DungeonShooter.Dungeon
{
    /// <summary>
    /// 房间类型枚举。
    /// 后续刷怪、宝箱放置等系统会根据此类型决定每个房间的内容。
    /// </summary>
    public enum RoomType
    {
        /// <summary>普通战斗房间（默认类型）</summary>
        Normal,

        /// <summary>宝箱房间</summary>
        Treasure,

        /// <summary>Boss 房间</summary>
        Boss,

        /// <summary>玩家出生房间</summary>
        Start
    }
}
