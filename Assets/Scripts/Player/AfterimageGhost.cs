using System;
using System.Collections;
using UnityEngine;

namespace DungeonShooter.Player
{
    /// <summary>
    /// 单个残影组件。
    ///
    /// 职责：被池取出时拷贝玩家外观 → 原地固定 → Alpha 渐隐 → 归回池。
    /// 挂载在 AfterimageGhost 预制体上，配合 AfterimageEffect 池使用。
    ///
    /// 设计考量：
    /// - 不需要 Rigidbody/Collider，纯视觉对象
    /// - 通过回调通知池回收，不直接依赖 AfterimageEffect 类型
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class AfterimageGhost : MonoBehaviour
    {
        private SpriteRenderer spriteRenderer;
        private Coroutine fadeCoroutine;
        private Action<AfterimageGhost> onFadeComplete; // 淡出完毕后的回调（由池设置）

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// 由池调用，激活残影并开始淡出。
        /// </summary>
        /// <param name="sprite">拷贝的玩家精灵</param>
        /// <param name="position">世界坐标</param>
        /// <param name="flipX">是否水平翻转</param>
        /// <param name="color">起始颜色（含 Alpha）</param>
        /// <param name="lifetime">淡出持续时间（秒）</param>
        /// <param name="callback">淡出完毕后调用的回调，参数是自身</param>
        public void Show(Sprite sprite, Vector3 position, bool flipX, Color color,
            float lifetime, Action<AfterimageGhost> callback)
        {
            // 如果有正在进行的淡出协程，先停掉
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }

            onFadeComplete = callback;

            // 复制玩家外观
            spriteRenderer.sprite = sprite;
            spriteRenderer.flipX = flipX;
            spriteRenderer.color = color;

            // 放到指定位置（保持自身 Z 不变，残影通常和玩家同层或略靠后）
            Vector3 pos = position;
            pos.z = transform.position.z;
            transform.position = pos;

            gameObject.SetActive(true);

            // 开始淡出
            fadeCoroutine = StartCoroutine(FadeOut(lifetime));
        }

        /// <summary>
        /// 渐变淡出协程：Color.a 从起始值线性降到 0。
        /// </summary>
        private IEnumerator FadeOut(float lifetime)
        {
            float elapsed = 0f;
            Color color = spriteRenderer.color;
            float startAlpha = color.a;

            while (elapsed < lifetime)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / lifetime;
                color.a = Mathf.Lerp(startAlpha, 0f, t);
                spriteRenderer.color = color;
                yield return null;
            }

            // 确保完全透明
            color.a = 0f;
            spriteRenderer.color = color;

            // 停用自身
            gameObject.SetActive(false);

            // 通知池回收
            onFadeComplete?.Invoke(this);
            onFadeComplete = null;
        }
    }
}
