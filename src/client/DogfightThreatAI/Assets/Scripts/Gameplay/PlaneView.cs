using Dogfight.Core;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 战机视图：把 PlaneState 的仿真状态同步到 Transform + SpriteRenderer。
    /// 视图**只读**仿真状态，绝不回写 —— 这样 V2 接服务器权威同步时，
    /// 只要把 PlaneState 换成网络下发的快照，表现层不用动。
    /// </summary>
    public class PlaneView
    {
        public GameObject Go;
        public SpriteRenderer Renderer;
        public SpriteRenderer Trail;
        /// <summary>本架飞机的基色（玩家=白，敌机=偏红，占位图=机型色）</summary>
        public Color Tint = Color.white;

        readonly Transform _trailTransform;
        float _baseScale = 1f;

        public PlaneView(Transform parent, string name, int sortingOrder)
        {
            Go = new GameObject(name);
            Go.transform.SetParent(parent, false);
            Renderer = Go.AddComponent<SpriteRenderer>();
            Renderer.sortingOrder = sortingOrder;

            var trailGo = new GameObject("trail");
            trailGo.transform.SetParent(Go.transform, false);
            Trail = trailGo.AddComponent<SpriteRenderer>();
            Trail.sortingOrder = sortingOrder - 1;
            _trailTransform = trailGo.transform;
        }

        public void Bind(AircraftDef def, Sprite sprite)
        {
            Renderer.sprite = sprite;
            // 精灵本身是 32px @ PPU=32 → 1 世界单位；RenderScale 用来微调视觉大小
            _baseScale = def.RenderScale;
            Go.transform.localScale = Vector3.one * _baseScale;
            Go.name = def.Id;

            if (sprite != null)
            {
                // Kenney 的飞机精灵机头朝上（+Y），所以要把旋转偏移 -90°
                Trail.enabled = false;
            }
        }

        /// <summary>把仿真状态同步到 Transform</summary>
        public void Sync(PlaneState p, bool godMode)
        {
            if (!p.Alive)
            {
                Go.SetActive(false);
                return;
            }
            Go.SetActive(true);

            Vector2 world = DogfightConst.PxToWorld(p.Pos);
            Go.transform.position = new Vector3(world.x, world.y, 0f);

            // 精灵机头朝上（+Y），Unity 的 z 旋转 0° 表示朝 +X，因此减 90°
            float deg = p.Angle * Mathf.Rad2Deg - 90f;
            Go.transform.rotation = Quaternion.Euler(0f, 0f, deg);

            // 受击闪白；无敌模式常亮提示
            if (p.HitFlash > 0f) Renderer.color = Color.Lerp(Tint, Color.white, 0.75f);
            else if (godMode) Renderer.color = new Color(Tint.r, Tint.g, Mathf.Min(1f, Tint.b + 0.35f), 1f);
            else Renderer.color = Tint;
        }

        public void SetVisible(bool v)
        {
            Go.SetActive(v);
        }

        public void Dispose()
        {
            if (Go != null) Object.Destroy(Go);
        }
    }
}
