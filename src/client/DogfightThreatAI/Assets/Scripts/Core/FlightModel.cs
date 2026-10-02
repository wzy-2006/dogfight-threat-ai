namespace Dogfight.Core
{
    /// <summary>
    /// 飞行动力学（对应立项书「飞机控制与武器发射」）：
    ///   油门加减速 → 推力沿机头方向
    ///   惯性滑行   → 速度是状态量，收油门不会立刻停下
    ///   转向阻尼   → 角速度受阻尼约束（松杆自动回中），舵效随速度变化
    ///   速度边界   → 夹在 [minSpeed, maxSpeed]，低于最小速度视为失速
    ///
    /// 纯静态函数：不依赖 UnityEngine，也不持有任何状态，因此可以逐帧单测。
    /// </summary>
    public static class FlightModel
    {
        public static void Integrate(PlaneState p, PlaneIntent intent, PhysicsConfig cfg, float dt)
        {
            if (!p.Alive) return;
            p.TimeAlive += dt;
            if (p.HitFlash > 0f) p.HitFlash = System.Math.Max(0f, p.HitFlash - dt);

            // ---- 油门（按配置的加减速速率逼近目标值） ----
            float want = MathUtil.Clamp01(intent.Throttle);
            float rate = want > p.Throttle ? cfg.ThrottleUpRate : cfg.ThrottleDownRate;
            float maxStep = rate * dt;
            float diff = want - p.Throttle;
            if (diff > maxStep) diff = maxStep;
            else if (diff < -maxStep) diff = -maxStep;
            p.Throttle = MathUtil.Clamp01(p.Throttle + diff);
            p.Braking = intent.Brake;

            float speed = p.Speed;

            // ---- 转向：舵效随速度变化 + 角速度阻尼 ----
            float authority = MathUtil.Clamp(
                speed / cfg.MinTurnAuthoritySpeed,
                cfg.TurnAuthorityFloor,
                1f);
            float targetAngVel = MathUtil.Clamp(intent.Turn, -1f, 1f) * p.Def.TurnRate * authority;
            p.AngVel = MathUtil.Damp(p.AngVel, targetAngVel, cfg.AngularDamping, dt);
            p.Angle = MathUtil.WrapAngle(p.Angle + p.AngVel * dt);

            // ---- 推力与阻力 ----
            float thrust = p.Throttle * p.Def.MaxThrust;
            Vec2 forward = Vec2.FromAngle(p.Angle);
            Vec2 accel = forward * thrust;

            float dragL = cfg.DragLinear + (p.Braking ? cfg.BrakeExtraDrag : 0f);
            Vec2 drag = p.Vel * dragL + p.Vel * (cfg.DragQuadratic * speed);
            accel -= drag;

            p.Vel += accel * dt;

            // ---- 速度边界 ----
            float sp = p.Vel.Magnitude;
            float maxSp = p.EffectiveMaxSpeed;
            if (sp > maxSp && sp > 1e-5f)
            {
                p.Vel = p.Vel * (maxSp / sp);
                sp = maxSp;
            }
            else
            {
                float minSp = p.EffectiveMinSpeed;
                if (sp < minSp && p.Throttle > 0.05f)
                {
                    // 推力不足时“托举”到最小可操控速度，避免飞机停在空中
                    float need = minSp - sp;
                    float k = MathUtil.Clamp01(need / (sp > 1f ? sp : 1f) * 3f);
                    p.Vel += forward * (minSp * 0.35f * dt * (0.4f + k));
                }
            }

            // ---- 位置积分 ----
            p.PrevPos = p.Pos;
            p.Pos += p.Vel * dt;
        }

        /// <summary>
        /// 战场边界（立项书：场景边界碰撞判定）。
        /// 采用软边界：越界后给一个向内的回推力 + 额外阻尼，并记录 OutOfBounds 供 HUD 告警。
        /// </summary>
        public static void ApplyBounds(PlaneState p, WorldConfig world, float dt)
        {
            bool left = p.Pos.X < 0f;
            bool right = p.Pos.X > world.Width;
            bool bottom = p.Pos.Y < 0f;
            bool top = p.Pos.Y > world.Height;
            p.OutOfBounds = left || right || bottom || top;
            if (!p.OutOfBounds) return;

            Vec2 push = Vec2.Zero;
            if (left) push.X += world.SoftPush;
            if (right) push.X -= world.SoftPush;
            if (bottom) push.Y += world.SoftPush;
            if (top) push.Y -= world.SoftPush;

            p.Vel += push * dt;
            p.Vel = p.Vel * (float)System.Math.Exp(-world.SoftDamping * dt);

            float pad = world.OutMargin;
            p.Pos = new Vec2(
                MathUtil.Clamp(p.Pos.X, -pad, world.Width + pad),
                MathUtil.Clamp(p.Pos.Y, -pad, world.Height + pad));
        }
    }
}
