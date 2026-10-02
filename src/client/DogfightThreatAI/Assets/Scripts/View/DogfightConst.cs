using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 全局常量。**PPU 必须与导入设置一致**（见 docs/02-design/美术规范.md）。
    /// 仿真内部统一用像素做单位，只在视图层除以 PPU 换算成世界单位，
    /// 这样 config 里的数值（速度 520 px/s 等）读起来才直观。
    /// </summary>
    public static class DogfightConst
    {
        /// <summary>每世界单位多少像素。32×32 的飞机正好 1×1 世界单位。</summary>
        public const float PixelsPerUnit = 32f;

        /// <summary>像素坐标 → 世界坐标</summary>
        public static Vector3 PxToWorld(float x, float y, float z)
        {
            return new Vector3(x / PixelsPerUnit, y / PixelsPerUnit, z);
        }

        public static Vector2 PxToWorld(Dogfight.Core.Vec2 v)
        {
            return new Vector2(v.X / PixelsPerUnit, v.Y / PixelsPerUnit);
        }

        /// <summary>
        /// 排序层：整个游戏只用 Default 层，靠 sortingOrder 控制前后关系，
        /// 避免依赖 ProjectSettings 里的自定义 Layer（那需要改 ProjectSettings 并全员同步）。
        /// </summary>
        public static class Order
        {
            public const int Terrain = -200;
            public const int Clouds = -100;
            public const int Projectile = 0;
            public const int Plane = 10;
            public const int PlaneFx = 20;
        }
    }
}
