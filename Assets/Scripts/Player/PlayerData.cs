using UnityEngine;

namespace DungeonShooter.Player
{
    /// <summary>
    /// ScriptableObject：玩家角色的可配置参数。
    /// 与 DungeonData 保持相同的设计模式，数据驱动，方便调试和扩展。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerData", menuName = "Dungeon/Player Data")]
    public class PlayerData : ScriptableObject
    {
        [Header("移动")]
        [Tooltip("玩家正常移动速度（单位/秒）")]
        [Range(1f, 20f)]
        public float moveSpeed = 5f;

        [Header("冲刺")]
        [Tooltip("冲刺速度（单位/秒），应明显大于移动速度")]
        [Range(5f, 50f)]
        public float dashSpeed = 20f;

        [Tooltip("冲刺持续时间（秒）")]
        [Range(0.05f, 0.5f)]
        public float dashDuration = 0.15f;

        [Tooltip("冲刺冷却时间（秒）")]
        [Range(0.5f, 5f)]
        public float dashCooldown = 1.5f;

        [Tooltip("冲刺结束后额外无敌时间（秒），防止冲刺结束瞬间受伤")]
        [Range(0f, 0.5f)]
        public float invincibilityPadding = 0.05f;
    }
}
