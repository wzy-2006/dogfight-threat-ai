using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>
    /// AI 控制器 —— 对应立项书要求的「多层分层 AI 架构」：
    ///   感知层  ThreatField + ThreatAssessment   → 战场态势量化
    ///   决策层  Decide()（按 reactionTime 刷新） → 选目标、切行为模式
    ///   执行层  AiBehaviors + Pathfinding        → 输出运动 / 开火意图
    ///
    /// 控制器不碰渲染，只产出 PlaneIntent，因此和玩家走的是同一条操控通道。
    /// </summary>
    public class AiController
    {
        public readonly PlaneState Self;
        public readonly AiProfile Profile;
        public readonly AiMemory Memory = new AiMemory();

        public AiMode Mode = AiMode.Patrol;
        /// <summary>当前交战目标 Id</summary>
        public int TargetId = -1;
        /// <summary>当前对自身的最大威胁值 0..1</summary>
        public float ThreatValue;
        public float Situation;

        /// <summary>模式切换历史（结算界面展示 AI 决策分布用）</summary>
        public readonly Dictionary<AiMode, float> ModeTime = new Dictionary<AiMode, float>();

        float _thinkTimer;
        float _aimNoise;
        float _triggerLatch;
        bool _missileHold;
        int _lastThreatTargetId = -1;

        public AiController(Simulation sim, PlaneState self, AiProfile profile)
        {
            Self = self;
            Profile = profile ?? sim.Config.FindAiProfile("veteran");
            Memory.Rng = new MathUtil.Rng((uint)(self.Id * 2654435761u + 1013904223u));
            _thinkTimer = Memory.Rng.NextFloat() * Profile.ReactionTime;   // 错开各机的决策帧
            AiBehaviors.RegisterBuiltIns();
        }

        public string DebugLine
        {
            get
            {
                return Self.Callsign + "[" + Profile.Label + "] " + Mode +
                       " 威胁" + ThreatValue.ToString("F2") +
                       " HP" + (int)(Self.HpRatio * 100f) + "%" +
                       " 速度" + (int)Self.Speed;
            }
        }

        /* ----------------------------------------------------------- 主入口 */

        public PlaneIntent Think(Simulation sim, float dt)
        {
            if (!Self.Alive) return PlaneIntent.None;

            Perceive(sim, dt);

            _thinkTimer += dt;
            if (_thinkTimer >= Profile.ReactionTime)
            {
                _thinkTimer = 0f;
                Decide(sim);
            }

            // 统计模式占时（供结算界面展示「AI 决策分布」）
            float acc;
            ModeTime.TryGetValue(Mode, out acc);
            ModeTime[Mode] = acc + dt;

            return Execute(sim, dt);
        }

        /* --------------------------------------------------------- 感知层 */

        void Perceive(Simulation sim, float dt)
        {
            // 被弹幕覆盖的程度（威胁评估的输入之一）
            _bulletThreat = 0f;
            for (int i = 0; i < sim.Projectiles.Count; i++)
            {
                ProjectileState pr = sim.Projectiles[i];
                if (!pr.Alive || pr.Team == Self.Team) continue;
                float d = Vec2.Distance(pr.Pos, Self.Pos);
                if (d < 260f)
                {
                    float v = 1f - d / 260f;
                    if (v > _bulletThreat) _bulletThreat = v;
                }
            }

            // 场景整体威胁（取最高的三个目标加权）
            Situation = ThreatAssessment.Situation(Self, sim.Planes, sim.Config.Threat, null);

            // 找威胁最高的目标
            float worst = 0f;
            int worstId = -1;
            for (int i = 0; i < sim.Planes.Count; i++)
            {
                PlaneState o = sim.Planes[i];
                if (!o.Alive || !Self.IsHostileTo(o)) continue;
                ThreatResult t = ThreatAssessment.Assess(Self, o, sim.Config.Threat, _bulletThreat, false);
                if (t.Value > worst) { worst = t.Value; worstId = o.Id; }
            }
            _lastThreatTargetId = worstId;
            ThreatValue = worst;

            // 被导弹锁定 → 威胁直接拉满
            for (int i = 0; i < sim.Projectiles.Count; i++)
            {
                ProjectileState pr = sim.Projectiles[i];
                if (pr.Alive && pr.Kind == WeaponKind.Missile && pr.TargetId == Self.Id)
                {
                    ThreatValue = System.Math.Max(ThreatValue, sim.Config.Threat.LockedThreatFloor);
                    break;
                }
            }
        }

        float _bulletThreat;

        /* --------------------------------------------------------- 决策层 */

        void Decide(Simulation sim)
        {
            ThreatConfig tc = sim.Config.Threat;

            // ① 选交战目标
            TargetId = AiBehaviors.SelectTarget(new BehaviorContext
            {
                Sim = sim, Field = sim.Threat, Self = Self, Profile = Profile,
            });
            if (TargetId != -1)
            {
                PlaneState t = sim.FindPlane(TargetId);
                if (t != null && t.Alive)
                {
                    Memory.LastKnown = t.Pos;
                    Memory.HasLastKnown = true;
                }
            }

            // ② 规避判定（带滞回，避免在阈值附近抖动）
            AiMode prev = Mode;
            AiMode next = prev;

            float enter = System.Math.Min(Profile.EvadeThreshold, tc.EvadeEnter + (1f - Profile.EvadeThreshold) * 0.5f);
            float exit = System.Math.Min(enter - 0.05f, tc.EvadeExit);

            if (prev == AiMode.Evade)
            {
                if (ThreatValue < exit && Self.HpRatio > 0.18f) next = PickOffensive(sim);
            }
            else if (ThreatValue > System.Math.Max(enter, tc.EvadeExit))
            {
                float bias = Self.HpRatio < 0.3f ? 0.08f : 0f;
                next = ThreatValue > enter - bias ? AiMode.Evade : PickOffensive(sim);
            }
            else
            {
                next = PickOffensive(sim);
            }

            if (next != prev)
            {
                Mode = next;
                Memory.EvadePath = null;
                Memory.EvadeTimer = 0f;
                Memory.HasWaypoint = false;
            }
        }

        AiMode PickOffensive(Simulation sim)
        {
            if (TargetId == -1) return Memory.HasLastKnown ? AiMode.Search : AiMode.Patrol;
            PlaneState t = sim.FindPlane(TargetId);
            if (t == null) return Memory.HasLastKnown ? AiMode.Search : AiMode.Patrol;
            float dist = Vec2.Distance(Self.Pos, t.Pos);
            return dist > Profile.FireRange * 1.4f ? AiMode.Pursue : AiMode.Attack;
        }

        /* --------------------------------------------------------- 执行层 */

        PlaneIntent Execute(Simulation sim, float dt)
        {
            AiBehaviors.BehaviorFunc fn = AiBehaviors.Get(Mode);
            var ctx = new BehaviorContext
            {
                Sim = sim,
                Field = sim.Threat,
                Self = Self,
                Profile = Profile,
                Dt = dt,
                TargetId = TargetId,
                ThreatValue = ThreatValue,
            };

            Desire desire = fn != null
                ? fn(ctx, Memory)
                : new Desire { Throttle = 0.6f, AimTargetId = -1, Note = Mode.ToString() };

            return ToIntent(sim, desire, dt);
        }

        PlaneIntent ToIntent(Simulation sim, Desire desire, float dt)
        {
            var intent = new PlaneIntent();
            PlaneState target = desire.AimTargetId != -1 ? sim.FindPlane(desire.AimTargetId) : null;

            // ---- 期望航向 ----
            float desiredHeading = Self.Angle;
            if (target != null && target.Alive && (Mode == AiMode.Attack || Mode == AiMode.Pursue))
            {
                // 瞄准：先算理想角，再叠加「人味」误差（平滑随机游走，而不是每帧乱跳）
                float baseAim = AiBehaviors.AimAngleWithLead(Self, target, Profile);
                float noise = Memory.Rng.Signed() * Profile.AimError;
                _aimNoise = MathUtil.Damp(_aimNoise, noise, Profile.AimSmooth * 0.8f, dt);
                desiredHeading = MathUtil.WrapAngle(baseAim + _aimNoise);
            }
            else if (desire.HasWaypoint)
            {
                desiredHeading = (desire.Waypoint - Self.Pos).Angle;
            }

            // ---- 转向：比例控制 + 死区 ----
            float err = MathUtil.AngleDelta(Self.Angle, desiredHeading);
            const float dead = 0.03f;
            float turn = 0f;
            if (System.Math.Abs(err) > dead) turn = MathUtil.Clamp(err / 0.55f, -1f, 1f);
            if (Mode == AiMode.Evade) turn = MathUtil.Clamp(turn * 1.35f, -1f, 1f);
            intent.Turn = turn;

            // ---- 油门 ----
            float throttle = MathUtil.Clamp(desire.Throttle, 0f, 1f);
            // 大角度转向时适当收油（真实飞行员也会这么做，且避免高速失位）
            if (System.Math.Abs(err) > 1.1f && Mode != AiMode.Evade) throttle *= 0.82f;
            // 失速保护
            if (Self.Speed < Self.EffectiveMinSpeed * 1.3f) throttle = System.Math.Max(throttle, 0.75f);
            intent.Throttle = throttle;

            // ---- 开火：做成「点射」，更像人，也省过热 ----
            bool fire = desire.Fire;
            if (fire)
            {
                WeaponRuntime cannon = Self.Weapon("cannon");
                if (cannon == null || cannon.Overheated) fire = false;
            }
            if (fire)
            {
                _triggerLatch += dt;
                float burst = 0.5f + Memory.Rng.NextFloat() * 0.35f;
                if (_triggerLatch > burst) { _triggerLatch = 0f; fire = false; }
            }
            else
            {
                _triggerLatch = 0f;
            }
            intent.Fire = fire;

            // ---- 导弹：需要持续照射一段时间来锁定，这里模拟 AI 的按住行为 ----
            bool missile = desire.Missile;
            WeaponRuntime mis = Self.Weapon("missile");
            if (missile)
            {
                if (mis == null || !mis.CanFire) missile = false;
                else _missileHold = true;
            }
            else
            {
                _missileHold = false;
            }
            intent.Missile = missile && _missileHold;
            intent.Brake = Mode == AiMode.Evade && throttle < 0.3f;

            return intent;
        }

        /// <summary>供 HUD / 调试显示：当前交战目标</summary>
        public int CurrentThreatTargetId { get { return _lastThreatTargetId; } }
    }
}
