using System;

namespace Dogfight.Core
{
    /// <summary>
    /// 数学工具。全部为静态纯函数，不依赖 UnityEngine。
    /// 角度一律用弧度，范围规范到 (-PI, PI]。
    /// </summary>
    public static class MathUtil
    {
        public const float PI = 3.14159265358979f;
        public const float TAU = 6.28318530717959f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;

        public static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static int ClampInt(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float LerpUnclamped(float a, float b, float t) { return a + (b - a) * t; }

        public static float InvLerp(float a, float b, float v)
        {
            if (Math.Abs(b - a) < 1e-9f) return 0f;
            return (v - a) / (b - a);
        }

        public static float SmoothStep(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>与帧率无关的指数趋近（比 Lerp 更稳）</summary>
        public static float Damp(float a, float b, float lambda, float dt)
        {
            return LerpUnclamped(a, b, 1f - (float)Math.Exp(-lambda * dt));
        }

        /// <summary>
        /// 把角度规范到 (-PI, PI]。
        ///
        /// 用 floor 归一再映射，并显式处理两个浮点边界：
        ///   · 计算结果落在 π 之上一点点（例如 -3π 归一后是 π+2.4e-7）→ 回绕后应仍视为 π
        ///   · 计算结果落在 -π 附近 → 统一表示成 π（两者等价，统一取值便于断言与调试）
        /// 早期版本用 (a+PI)%TAU + 精确等号判断，在浮点残差下会把 -3π 判成 -π。
        /// </summary>
        public static float WrapAngle(float a)
        {
            float k = (float)Math.Floor(a / TAU);
            a -= TAU * k;                      // 归一化到 [0, TAU)
            if (a >= TAU - 1e-5f) a = 0f;      // 极接近整圈，视为 0
            if (a > PI) a -= TAU;              // 映射到 (-PI, PI]
            if (a < -PI + 1e-5f) a = PI;       // -π 与 π 等价，统一成 π
            return a;
        }

        /// <summary>从 a 到 b 的最短有向角差</summary>
        public static float AngleDelta(float a, float b) { return WrapAngle(b - a); }

        /// <summary>朝目标角度旋转，限制最大角速度（弧度/秒）</summary>
        public static float RotateTowards(float current, float target, float maxRate, float dt)
        {
            float d = AngleDelta(current, target);
            float step = maxRate * dt;
            if (Math.Abs(d) <= step) return WrapAngle(target);
            return WrapAngle(current + Math.Sign(d) * step);
        }

        public static float AngleLerp(float a, float b, float t) { return WrapAngle(a + AngleDelta(a, b) * t); }

        /// <summary>点到线段的最短距离（用于高速弹的扫掠命中判定）</summary>
        public static float PointSegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float l2 = ab.SqrMagnitude;
            if (l2 < 1e-9f) return Vec2.Distance(p, a);
            float t = Clamp01(((p - a).Dot(ab)) / l2);
            return Vec2.Distance(p, a + ab * t);
        }

        /// <summary>线段是否与圆相交</summary>
        public static bool SegmentHitsCircle(Vec2 a, Vec2 b, Vec2 center, float radius)
        {
            return PointSegmentDistance(center, a, b) <= radius;
        }

        /// <summary>
        /// 二次方程的最小非负实根；无解返回 null。
        /// 用于「子弹能否命中匀速目标」的提前量解算。
        /// </summary>
        public static float? SolveQuadratic(float a, float b, float c)
        {
            if (Math.Abs(a) < 1e-9f)
            {
                if (Math.Abs(b) < 1e-9f) return null;
                float t0 = -c / b;
                return t0 >= 0f ? t0 : (float?)null;
            }
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return null;
            float s = (float)Math.Sqrt(disc);
            float t1 = (-b - s) / (2f * a);
            float t2 = (-b + s) / (2f * a);
            float lo = Math.Min(t1, t2);
            float hi = Math.Max(t1, t2);
            if (hi < 0f) return null;
            return lo >= 0f ? lo : hi;
        }

        /// <summary>
        /// 拦截解算：求从 from 以速度 speed 发射、命中以 targetVel 运动的目标所需的瞄准角。
        /// 无解（目标比弹还快且正在远离）时返回 null。
        /// </summary>
        public static float? InterceptAngle(Vec2 from, float speed, Vec2 targetPos, Vec2 targetVel)
        {
            Vec2 r = targetPos - from;
            float a = targetVel.SqrMagnitude - speed * speed;
            float b = 2f * r.Dot(targetVel);
            float c = r.SqrMagnitude;
            float? t = SolveQuadratic(a, b, c);
            if (!t.HasValue || t.Value <= 0f) return null;
            Vec2 aim = r + targetVel * t.Value;
            return (float)Math.Atan2(aim.Y, aim.X);
        }

        /// <summary>伪随机数（xorshift），同种子完全可复现</summary>
        public class Rng
        {
            uint _state;

            public Rng(uint seed) { _state = seed == 0u ? 1u : seed; }

            public uint NextUInt()
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x;
                return x;
            }

            /// <summary>[0,1)</summary>
            public float NextFloat() { return (NextUInt() & 0xFFFFFFu) / 16777216f; }

            public float Range(float lo, float hi) { return lo + NextFloat() * (hi - lo); }

            public int RangeInt(int lo, int hiExclusive)
            {
                if (hiExclusive <= lo) return lo;
                return lo + (int)(NextUInt() % (uint)(hiExclusive - lo));
            }

            public bool Chance(float p) { return NextFloat() < p; }

            /// <summary>近似正态分布，用于让 AI 的瞄准误差更自然</summary>
            public float Gaussian(float mean, float sd)
            {
                float s = (NextFloat() + NextFloat() + NextFloat() + NextFloat() + NextFloat() + NextFloat() - 3f) / 0.7071f;
                return mean + s * sd * 0.577f;
            }

            public float Signed() { return NextFloat() * 2f - 1f; }

            public T Pick<T>(System.Collections.Generic.IList<T> list)
            {
                if (list == null || list.Count == 0) return default(T);
                return list[RangeInt(0, list.Count)];
            }
        }
    }
}
