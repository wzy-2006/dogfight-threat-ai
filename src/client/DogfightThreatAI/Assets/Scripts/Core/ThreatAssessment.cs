using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>单目标威胁评估结果（带完整分解，便于调试与论文截图）</summary>
    public struct ThreatResult
    {
        /// <summary>综合威胁值 0..1</summary>
        public float Value;
        // 五个因子（均已归一化到 0..1）
        public float Distance;
        public float Aspect;
        public float Closing;
        public float Weapon;
        public float Health;
        /// <summary>距离（px），无目标时为 +inf</summary>
        public float Range;
        /// <summary>对应的目标 Id，-1 表示无</summary>
        public int TargetId;
    }

    /// <summary>
    /// 空战威胁态势评估（立项书核心算法之一）。
    ///
    /// 威胁 = 距离×w1 + 相对方位×w2 + 闭合率×w3 + 武器威胁×w4 + 我方状态×w5
    /// 五个因子全部归一化后加权求和，输出 0..1。
    /// </summary>
    public static class ThreatAssessment
    {
        public static ThreatResult Assess(PlaneState self, PlaneState other, ThreatConfig cfg, float bulletThreat, bool lockedByMe)
        {
            var r = new ThreatResult { TargetId = other != null ? other.Id : -1, Range = float.PositiveInfinity };
            if (self == null || other == null || !other.Alive || ReferenceEquals(self, other)) return r;

            ThreatWeights w = cfg.Weights;
            Vec2 d = other.Pos - self.Pos;
            float dist = d.Magnitude;
            r.Range = dist;

            // ① 距离：越近越危险
            r.Distance = 1f - MathUtil.Clamp01((dist - cfg.MinRange) / (cfg.Range - cfg.MinRange));

            // ② 相对方位：对方机头指向我方的程度
            float bearingToMe = (-d).Angle;
            float offNose = System.Math.Abs(MathUtil.AngleDelta(other.Angle, bearingToMe));
            r.Aspect = 1f - MathUtil.Clamp01((float)offNose / MathUtil.PI);

            // ③ 闭合率：距离正在快速缩小的程度
            Vec2 relV = other.Vel - self.Vel;
            float closingSpeed = -d.Dot(relV) / (dist > 1f ? dist : 1f);
            float maxClosing = System.Math.Max(other.EffectiveMaxSpeed, self.EffectiveMaxSpeed);
            if (maxClosing < 1f) maxClosing = 1f;
            r.Closing = MathUtil.Clamp01(closingSpeed / maxClosing * 1.6f);

            // ④ 武器威胁：对方是否已经能打到我 / 是否已经锁定
            float weapon = 0f;
            WeaponRuntime cannon = other.Weapon("cannon");
            if (cannon != null)
            {
                bool inRange = dist < 900f;
                bool inCone = System.Math.Abs(MathUtil.AngleDelta(other.Angle, bearingToMe)) < 0.35f;
                if (inRange && inCone) weapon = System.Math.Max(weapon, 0.7f);
            }
            WeaponRuntime missile = other.Weapon("missile");
            if (missile != null && missile.Ammo > 0f)
            {
                if (other.Lock.Locked && other.Lock.TargetId == self.Id) weapon = System.Math.Max(weapon, 1.0f);
                else if (dist < 1250f * 0.9f) weapon = System.Math.Max(weapon, 0.45f);
            }
            if (lockedByMe) weapon = System.Math.Max(weapon, 1.0f);
            r.Weapon = weapon;

            // ⑤ 我方状态：血越少、被弹越多，同一目标越致命
            float healthFactor = MathUtil.Clamp01((1f - self.HpRatio) * 0.7f + MathUtil.Clamp01(bulletThreat) * 0.6f);
            r.Health = healthFactor;

            r.Value = MathUtil.Clamp01(
                r.Distance * w.Distance +
                r.Aspect * w.Aspect +
                r.Closing * w.Closing +
                r.Weapon * w.Weapon +
                r.Health * w.Health);

            return r;
        }

        /// <summary>
        /// 战场整体威胁：取威胁最高的三个目标加权合并（主 100%、次 45%、第三 20%）。
        /// </summary>
        public static float Situation(PlaneState self, IList<PlaneState> planes, ThreatConfig cfg, List<ThreatResult> outThreats)
        {
            if (outThreats != null) outThreats.Clear();
            if (self == null || planes == null) return 0f;

            var list = new List<ThreatResult>();
            for (int i = 0; i < planes.Count; i++)
            {
                PlaneState o = planes[i];
                if (o == null || !o.Alive || !self.IsHostileTo(o)) continue;
                ThreatResult t = Assess(self, o, cfg, 0f, false);
                if (t.Value > 0f) list.Add(t);
            }
            list.Sort((a, b) => b.Value.CompareTo(a.Value));

            if (outThreats != null) outThreats.AddRange(list);

            float[] weights = { 1f, 0.45f, 0.2f };
            float agg = 0f;
            for (int i = 0; i < list.Count && i < weights.Length; i++) agg += list[i].Value * weights[i];
            return MathUtil.Clamp01(agg);
        }
    }
}
