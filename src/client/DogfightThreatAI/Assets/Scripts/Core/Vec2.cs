using System;

namespace Dogfight.Core
{
    /// <summary>
    /// 二维向量。纯 C#（不依赖 UnityEngine），因此这一层可以在 Unity 之外单独编译与测试，
    /// 也便于 V2 把同一套逻辑搬到逻辑服务器上做权威计算。
    /// 坐标系约定：x 向右，y 向上，角度单位为弧度、逆时针为正（与 Unity 2D 一致）。
    /// </summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator -(Vec2 a) { return new Vec2(-a.X, -a.Y); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.X * s, a.Y * s); }
        public static Vec2 operator *(float s, Vec2 a) { return new Vec2(a.X * s, a.Y * s); }
        public static Vec2 operator /(Vec2 a, float s) { return new Vec2(a.X / s, a.Y / s); }

        public float SqrMagnitude { get { return X * X + Y * Y; } }
        public float Magnitude { get { return (float)Math.Sqrt(X * X + Y * Y); } }

        public Vec2 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? new Vec2(X / m, Y / m) : Zero;
            }
        }

        public float Dot(Vec2 o) { return X * o.X + Y * o.Y; }

        /// <summary>把向量旋转指定角度（逆时针为正）</summary>
        public Vec2 Rotated(float radians)
        {
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        public static Vec2 FromAngle(float radians)
        {
            return new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians));
        }

        public float Angle { get { return (float)Math.Atan2(Y, X); } }

        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Magnitude; }
        public static float SqrDistance(Vec2 a, Vec2 b) { return (a - b).SqrMagnitude; }

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) { return a + (b - a) * MathUtil.Clamp01(t); }

        public static Vec2 MoveTowards(Vec2 a, Vec2 b, float maxDelta)
        {
            Vec2 d = b - a;
            float m = d.Magnitude;
            if (m <= maxDelta || m < 1e-6f) return b;
            return a + d / m * maxDelta;
        }

        /// <summary>圆-圆是否相交</summary>
        public static bool CirclesOverlap(Vec2 a, float ra, Vec2 b, float rb)
        {
            float r = ra + rb;
            return SqrDistance(a, b) <= r * r;
        }

        public bool Equals(Vec2 o) { return X == o.X && Y == o.Y; }
        public override bool Equals(object o) { return o is Vec2 && Equals((Vec2)o); }
        public override int GetHashCode() { return X.GetHashCode() * 397 ^ Y.GetHashCode(); }
        public override string ToString() { return "(" + X.ToString("F1") + ", " + Y.ToString("F1") + ")"; }
    }
}
