using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>一个行为产生的「意图描述」：控制器负责把它变成具体的转向与开火指令</summary>
    public struct Desire
    {
        public bool HasWaypoint;
        public Vec2 Waypoint;
        /// <summary>要对准的目标 Id（-1 表示不对准任何目标）</summary>
        public int AimTargetId;
        public float Throttle;
        public bool Fire;
        public bool Missile;
        public string Note;
    }

    /// <summary>行为上下文</summary>
    public struct BehaviorContext
    {
        public Simulation Sim;
        public ThreatField Field;
        public PlaneState Self;
        public AiProfile Profile;
        public float Dt;
        /// <summary>当前交战目标 Id（-1 表示无）</summary>
        public int TargetId;
        public float ThreatValue;
    }

    /// <summary>
    /// 五种空战行为（立项书：巡逻 / 搜索 / 追击 / 攻击 / 规避）。
    /// 通过 BehaviorRegistry 注册，V2 想加「编队协同」「诱饵」直接 Register 即可。
    /// </summary>
    public static class AiBehaviors
    {
        public delegate Desire BehaviorFunc(BehaviorContext ctx, AiMemory mem);

        static readonly Dictionary<AiMode, BehaviorFunc> _registry = new Dictionary<AiMode, BehaviorFunc>();

        public static void Register(AiMode mode, BehaviorFunc fn) { _registry[mode] = fn; }

        public static BehaviorFunc Get(AiMode mode)
        {
            BehaviorFunc f;
            return _registry.TryGetValue(mode, out f) ? f : null;
        }

        public static void RegisterBuiltIns()
        {
            Register(AiMode.Patrol, Patrol);
            Register(AiMode.Search, Search);
            Register(AiMode.Pursue, Pursue);
            Register(AiMode.Attack, Attack);
            Register(AiMode.Evade, Evade);
        }

        /* ------------------------------------------------------- 工具 */

        /// <summary>在世界内取一个随机巡逻点，偏向低威胁方向</summary>
        public static Vec2 PickPatrolPoint(BehaviorContext ctx, AiMemory mem)
        {
            PlaneState self = ctx.Self;
            float R = ctx.Profile.PatrolRadius;
            float bestScore = float.NegativeInfinity;
            Vec2 best = new Vec2(ctx.Sim.Config.World.Width * 0.5f, ctx.Sim.Config.World.Height * 0.5f);

            for (int i = 0; i < 14; i++)
            {
                float a = mem.Rand() * MathUtil.TAU;
                float r = R * (0.45f + mem.Rand() * 0.55f);
                var p = new Vec2(
                    MathUtil.Clamp(self.Pos.X + (float)System.Math.Cos(a) * r, 200f, ctx.Sim.Config.World.Width - 200f),
                    MathUtil.Clamp(self.Pos.Y + (float)System.Math.Sin(a) * r, 200f, ctx.Sim.Config.World.Height - 200f));
                float t = ctx.Field.Normalized(p);
                float step = MathUtil.Clamp01(1f - Vec2.Distance(p, self.Pos) / R);
                float score = -t * 2f + step;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        /// <summary>找一个「威胁最低且远离边界」的规避目标点（沿威胁梯度下降）</summary>
        public static Vec2 FindSafePoint(BehaviorContext ctx, float minR, float maxR)
        {
            PlaneState self = ctx.Self;
            Vec2 grad = ctx.Field.Gradient(self.Pos, ctx.Field.Cell * 0.5f);
            float gMag = grad.Magnitude;
            if (gMag < 1e-6f) gMag = 1f;
            Vec2 gDir = grad / gMag;

            float bestScore = float.NegativeInfinity;
            Vec2 best = self.Pos;
            const int samples = 20;
            for (int i = 0; i < samples; i++)
            {
                float a = (float)i / samples * MathUtil.TAU;
                float r = minR + (maxR - minR) * (((i * 7919) % 13) / 13f);
                var p = new Vec2(
                    MathUtil.Clamp(self.Pos.X + (float)System.Math.Cos(a) * r, 150f, ctx.Sim.Config.World.Width - 150f),
                    MathUtil.Clamp(self.Pos.Y + (float)System.Math.Sin(a) * r, 150f, ctx.Sim.Config.World.Height - 150f));
                float t = ctx.Field.Normalized(p);
                // 越靠近「威胁下降方向」越好
                float downhill = -((float)System.Math.Cos(a) * gDir.X + (float)System.Math.Sin(a) * gDir.Y);
                float edge = System.Math.Min(System.Math.Min(p.X, p.Y),
                    System.Math.Min(ctx.Sim.Config.World.Width - p.X, ctx.Sim.Config.World.Height - p.Y)) / 600f;
                float score = -t * 3.0f + downhill * 1.2f + MathUtil.Clamp01(edge) * 0.8f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        /// <summary>选目标：威胁 × 易击杀性</summary>
        public static int SelectTarget(BehaviorContext ctx)
        {
            Simulation sim = ctx.Sim;
            PlaneState self = ctx.Self;
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < sim.Planes.Count; i++)
            {
                PlaneState p = sim.Planes[i];
                if (!p.Alive || !self.IsHostileTo(p)) continue;
                ThreatResult t = ThreatAssessment.Assess(self, p, sim.Config.Threat, 0f, false);
                float killability = 1f - p.HpRatio;
                float score = t.Value * 0.55f + killability * 0.30f + MathUtil.Clamp01(1f - t.Range / 1400f) * 0.35f;
                if (score > bestScore) { bestScore = score; best = p.Id; }
            }
            return best;
        }

        /* ------------------------------------------------------- 行为 */

        /// <summary>PATROL 巡逻：沿低威胁航路点巡航，保持能量</summary>
        public static Desire Patrol(BehaviorContext ctx, AiMemory mem)
        {
            if (!mem.HasWaypoint || Vec2.Distance(ctx.Self.Pos, mem.Waypoint) < 140f)
            {
                mem.Waypoint = PickPatrolPoint(ctx, mem);
                mem.HasWaypoint = true;
            }
            return new Desire
            {
                HasWaypoint = true,
                Waypoint = mem.Waypoint,
                AimTargetId = -1,
                Throttle = MathUtil.Clamp(ctx.Profile.MaxThrottle * 0.72f, 0.4f, 1f),
                Note = "巡逻",
            };
        }

        /// <summary>SEARCH 搜索：优先飞向最后已知位置，否则用 BFS 找最近的未侦察空域</summary>
        public static Desire Search(BehaviorContext ctx, AiMemory mem)
        {
            var d = new Desire { AimTargetId = -1, Note = "搜索" };

            if (mem.HasLastKnown)
            {
                Pathfinding.PlannerFunc planner = Pathfinding.Get(ctx.Profile.Planner);
                if (planner != null)
                {
                    var pctx = new PlannerContext
                    {
                        Field = ctx.Field, Self = ctx.Self, Goal = mem.LastKnown, HasGoal = true, Profile = ctx.Profile,
                    };
                    List<Vec2> path = planner(pctx);
                    if (path != null && path.Count > 0)
                    {
                        d.HasWaypoint = true;
                        d.Waypoint = path[0];
                    }
                }
                if (!d.HasWaypoint)
                {
                    d.HasWaypoint = true;
                    d.Waypoint = mem.LastKnown;
                }
            }
            else
            {
                List<Vec2> path = Pathfinding.BfsNearest(ctx.Field, ctx.Self.Pos,
                    delegate (int cx, int cy, int index) { return ctx.Field.RawScanned[index] < 0.25f; }, 0.80f, 6000);
                if (path != null && path.Count > 0)
                {
                    d.HasWaypoint = true;
                    d.Waypoint = path[0];
                }
                else
                {
                    d.HasWaypoint = true;
                    d.Waypoint = PickPatrolPoint(ctx, mem);
                }
            }

            ctx.Field.MarkScanned(ctx.Self.Pos, 340f);
            d.Throttle = MathUtil.Clamp(ctx.Profile.MaxThrottle * 0.85f, 0.45f, 1f);
            return d;
        }

        /// <summary>PURSUE 追击：按提前量追，接近时收油门避免冲过头</summary>
        public static Desire Pursue(BehaviorContext ctx, AiMemory mem)
        {
            PlaneState target = ctx.Sim.FindPlane(ctx.TargetId);
            if (target == null) return Attack(ctx, mem);
            return PursueTarget(ctx, target);
        }

        public static Desire PursueTarget(BehaviorContext ctx, PlaneState target)
        {
            PlaneState self = ctx.Self;
            float dist = Vec2.Distance(self.Pos, target.Pos);
            float ideal = MathUtil.Clamp(dist / 620f, 0.35f, 1f);
            float throttle = MathUtil.Clamp(ctx.Profile.MaxThrottle * ideal, 0.35f, ctx.Profile.MaxThrottle);
            if (ctx.Profile.EnergyCare > 0.5f && self.Speed < self.EffectiveMaxSpeed * 0.55f && dist > 500f)
            {
                throttle = System.Math.Min(ctx.Profile.MaxThrottle, throttle + 0.25f);
            }
            return new Desire
            {
                HasWaypoint = true,
                Waypoint = target.Pos,
                AimTargetId = target.Id,
                Throttle = throttle,
                Note = "追击",
            };
        }

        /// <summary>ATTACK 攻击：进入射程与包线后瞄准开火，满足条件放导弹</summary>
        public static Desire Attack(BehaviorContext ctx, AiMemory mem)
        {
            PlaneState target = ctx.Sim.FindPlane(ctx.TargetId);
            if (target == null) return Pursue(ctx, mem);

            PlaneState self = ctx.Self;
            AiProfile prof = ctx.Profile;
            float dist = Vec2.Distance(self.Pos, target.Pos);

            // 瞄准角由控制器统一合成（含误差），这里只判断「是否已经对准」
            float aim = AimAngleWithLead(self, target, prof);
            float off = System.Math.Abs(MathUtil.AngleDelta(self.Angle, aim));

            bool aligned = off < prof.FireCone;
            float fireChance = prof.TriggerDiscipline * (1f - off / System.Math.Max(prof.FireCone, 0.001f));
            bool fire = aligned && dist < prof.FireRange && mem.Rand() < (0.35f + fireChance * 0.9f);

            WeaponRuntime mis = self.Weapon("missile");
            bool missile = false;
            if (mis != null && mis.CanFire)
            {
                WeaponDef md = mis.Def;
                bool inCone = off < md.LockCone * 0.9f;
                bool inRange = dist > prof.MissileMinRange && dist < md.LockRange;
                missile = inCone && inRange && mem.Rand() < (0.2f + prof.MissileSkill * 0.8f);
            }

            float idealT = MathUtil.Clamp(dist / 560f, 0.4f, 1f);
            return new Desire
            {
                HasWaypoint = true,
                Waypoint = target.Pos,
                AimTargetId = target.Id,
                Throttle = MathUtil.Clamp(prof.MaxThrottle * idealT, 0.4f, prof.MaxThrottle),
                Fire = fire,
                Missile = missile,
                Note = "攻击",
            };
        }

        /// <summary>EVADE 规避：沿威胁梯度下降找安全点，用规划器飞出低风险航线</summary>
        public static Desire Evade(BehaviorContext ctx, AiMemory mem)
        {
            mem.EvadeTimer += ctx.Dt;

            bool needReplan = mem.EvadePath == null || mem.EvadePath.Count == 0 || mem.EvadeTimer > 0.55f;
            if (needReplan)
            {
                mem.EvadeTimer = 0f;
                Vec2 goal = FindSafePoint(ctx, 380f, 820f);
                Pathfinding.PlannerFunc planner = Pathfinding.Get(ctx.Profile.Planner);
                List<Vec2> path = null;
                if (planner != null && ctx.Profile.Planner != "steering")
                {
                    var pctx = new PlannerContext
                    {
                        Field = ctx.Field, Self = ctx.Self, Goal = goal, HasGoal = true, Profile = ctx.Profile,
                    };
                    path = planner(pctx);
                }
                if (path == null || path.Count == 0)
                {
                    path = new List<Vec2>(1);
                    path.Add(goal);
                }
                // 规划出的第一个点若仍在高威胁里，直接换一条
                if (path.Count > 0 && ctx.Field.Normalized(path[0]) > 0.75f)
                {
                    path = new List<Vec2>(1);
                    path.Add(FindSafePoint(ctx, 300f, 700f));
                }
                mem.EvadePath = path;
            }

            Vec2 wp = mem.EvadePath.Count > 0 ? mem.EvadePath[0] : ctx.Self.Pos;
            if (mem.EvadePath.Count > 0 && Vec2.Distance(ctx.Self.Pos, wp) < 110f)
            {
                mem.EvadePath.RemoveAt(0);
                wp = mem.EvadePath.Count > 0 ? mem.EvadePath[0] : PickPatrolPoint(ctx, mem);
            }

            return new Desire
            {
                HasWaypoint = true,
                Waypoint = wp,
                AimTargetId = -1,
                Throttle = 1f,
                // 高技巧 AI 会在规避中反手打一发导弹
                Missile = ctx.Profile.MissileSkill > 0.7f && mem.Rand() < 0.02f,
                Note = "规避",
            };
        }

        /// <summary>按 profile.leadAccuracy 在「直瞄」与「完美提前量」之间插值</summary>
        public static float AimAngleWithLead(PlaneState self, PlaneState target, AiProfile prof)
        {
            float direct = (target.Pos - self.Pos).Angle;
            WeaponRuntime cannon = self.Weapon("cannon");
            float muzzleSpeed = cannon != null ? cannon.Def.Speed : 1000f;
            float? perfect = MathUtil.InterceptAngle(self.Pos, muzzleSpeed, target.Pos, target.Vel);
            if (!perfect.HasValue) return direct;
            return MathUtil.WrapAngle(direct + MathUtil.AngleDelta(direct, perfect.Value) * prof.LeadAccuracy);
        }
    }

    /// <summary>AI 的每架飞机私有记忆</summary>
    public class AiMemory
    {
        public bool HasWaypoint;
        public Vec2 Waypoint;
        public List<Vec2> EvadePath;
        public float EvadeTimer;

        public bool HasLastKnown;
        public Vec2 LastKnown;

        /// <summary>行为内部使用的随机源（与仿真主随机流分开，避免影响可复现性）</summary>
        public MathUtil.Rng Rng;

        public float Rand() { return Rng != null ? Rng.NextFloat() : 0.5f; }
    }
}
