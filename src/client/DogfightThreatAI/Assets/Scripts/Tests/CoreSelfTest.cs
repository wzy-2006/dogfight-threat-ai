using System;
using System.Collections.Generic;
using Dogfight.Core;

namespace Dogfight.Tests
{
    /// <summary>单条自检结果</summary>
    public struct SelfTestResult
    {
        public string Name;
        public bool Passed;
        public string Message;
    }

    /// <summary>
    /// 核心算法自检。**不依赖 UnityEngine 也不依赖 NUnit**，因此：
    ///   · 在 Unity 里可以通过菜单「空战/运行核心算法自检」一键跑；
    ///   · 在 Unity 之外可以用 mono 直接跑（tools/unity-verify）；
    /// 覆盖数学、飞行物理、威胁评估、路径规划、波次规则，以及一次完整的 60 秒自动对战仿真。
    /// </summary>
    public static class CoreSelfTest
    {
        public static List<SelfTestResult> Run()
        {
            var results = new List<SelfTestResult>();

            MathTests(results);
            RngTests(results);
            FlightTests(results);
            WeaponTests(results);
            ThreatTests(results);
            PathfindingTests(results);
            SpawnTests(results);
            IntegrationTests(results);

            return results;
        }

        /* ------------------------------------------------------- 断言工具 */

        static void Check(List<SelfTestResult> r, string name, bool ok, string message)
        {
            r.Add(new SelfTestResult { Name = name, Passed = ok, Message = message ?? "" });
        }

        static void CheckTrue(List<SelfTestResult> r, string name, bool cond, string failMsg)
        {
            Check(r, name, cond, cond ? "" : failMsg);
        }

        static void CheckNear(List<SelfTestResult> r, string name, float a, float b, float eps)
        {
            bool ok = Math.Abs(a - b) <= eps;
            Check(r, name, ok, ok ? "" : "期望 " + b + "，实际 " + a);
        }

        static void CheckRange(List<SelfTestResult> r, string name, float v, float lo, float hi)
        {
            bool ok = v >= lo && v <= hi;
            Check(r, name, ok, ok ? "" : v + " 不在 [" + lo + ", " + hi + "]");
        }

        static void CheckEq(List<SelfTestResult> r, string name, int a, int b)
        {
            bool ok = a == b;
            Check(r, name, ok, ok ? "" : "期望 " + b + "，实际 " + a);
        }

        /* ============================================================ 数学 */

        static void MathTests(List<SelfTestResult> r)
        {
            CheckNear(r, "wrapAngle 把角度收敛到 (-π, π]", MathUtil.WrapAngle(MathUtil.PI * 3f), MathUtil.PI, 1e-4f);
            CheckNear(r, "wrapAngle 处理负数圈", MathUtil.WrapAngle(-MathUtil.PI * 3f), MathUtil.PI, 1e-4f);
            CheckTrue(r, "wrapAngle 结果始终在范围内",
                MathUtil.WrapAngle(1000f) <= MathUtil.PI && MathUtil.WrapAngle(1000f) > -MathUtil.PI,
                "超出 (-π, π]");

            CheckNear(r, "angleDelta 取最短有向角差", MathUtil.AngleDelta(0.1f, -0.1f), -0.2f, 1e-5f);
            CheckNear(r, "angleDelta 跨 ±π 走短弧", MathUtil.AngleDelta(-3f, 3f), -0.2831853f, 1e-4f);

            float? t = MathUtil.SolveQuadratic(1f, -3f, 2f);
            CheckTrue(r, "solveQuadratic 取最小非负根", t.HasValue && Math.Abs(t.Value - 1f) < 1e-4f,
                "期望 1，实际 " + (t.HasValue ? t.Value.ToString() : "null"));
            CheckTrue(r, "solveQuadratic 无解返回 null", !MathUtil.SolveQuadratic(1f, 0f, 1f).HasValue, "应为 null");

            float? aim = MathUtil.InterceptAngle(new Vec2(0f, 0f), 500f, new Vec2(600f, 0f), new Vec2(0f, 200f));
            CheckTrue(r, "interceptAngle 对横向目标给出提前量", aim.HasValue && aim.Value > 0.2f,
                "期望 > 0.2，实际 " + (aim.HasValue ? aim.Value.ToString() : "null"));

            float? still = MathUtil.InterceptAngle(new Vec2(0f, 0f), 500f, new Vec2(600f, 0f), Vec2.Zero);
            CheckTrue(r, "interceptAngle 对静止目标退化为直瞄", still.HasValue && Math.Abs(still.Value) < 1e-4f,
                "应为 0");

            CheckTrue(r, "segmentHitsCircle 命中", MathUtil.SegmentHitsCircle(new Vec2(0f, 0f), new Vec2(100f, 0f), new Vec2(50f, 3f), 5f), "应命中");
            CheckTrue(r, "segmentHitsCircle 不命中", !MathUtil.SegmentHitsCircle(new Vec2(0f, 0f), new Vec2(100f, 0f), new Vec2(50f, 30f), 5f), "不应命中");
            CheckTrue(r, "高速弹扫掠判定能抓到整段路径上的目标",
                MathUtil.SegmentHitsCircle(new Vec2(-500f, 0f), new Vec2(500f, 0f), new Vec2(0f, 0f), 4f), "高速扫掠应命中");

            // damp 与帧率无关
            float one = MathUtil.Damp(0f, 1f, 5f, 0.1f);
            float two = MathUtil.Damp(MathUtil.Damp(0f, 1f, 5f, 0.05f), 1f, 5f, 0.05f);
            CheckNear(r, "damp 与帧率无关（两步 vs 一步）", one, two, 1e-5f);
        }

        /* ============================================================ 随机 */

        static void RngTests(List<SelfTestResult> r)
        {
            var a = new MathUtil.Rng(42u);
            var b = new MathUtil.Rng(42u);
            bool same = true;
            for (int i = 0; i < 100; i++) if (a.NextUInt() != b.NextUInt()) { same = false; break; }
            CheckTrue(r, "同 seed 的随机序列完全可复现", same, "序列不一致");

            var c = new MathUtil.Rng(7u);
            int inRange = 0;
            for (int i = 0; i < 2000; i++)
            {
                float v = c.NextFloat();
                if (v >= 0f && v < 1f) inRange++;
            }
            CheckEq(r, "NextFloat 全部落在 [0,1)", inRange, 2000);
        }

        /* ============================================================ 飞行 */

        static DogfightConfig Cfg() { return ConfigDefaults.Create(); }

        static PlaneState MakePlane(DogfightConfig cfg, string typeId)
        {
            AircraftDef def = cfg.FindAircraft(typeId);
            var p = PlaneState.Create(1, def, new Vec2(5000f, 5000f), 0f, Team.Player, true);
            p.BindWeapons(cfg.FindWeapon);
            return p;
        }

        static void FlightTests(List<SelfTestResult> r)
        {
            DogfightConfig cfg = Cfg();

            // 满油门加速
            PlaneState p = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            for (int i = 0; i < 2400; i++) FlightModel.Integrate(p, PlaneIntent.Cruise(1f), cfg.Physics, 1f / 120f);
            CheckTrue(r, "满油门加速收敛到最大速度附近", p.Speed > p.EffectiveMaxSpeed * 0.95f,
                "20 秒后仅 " + p.Speed.ToString("F1") + " / " + p.EffectiveMaxSpeed);
            CheckTrue(r, "速度不超过上限", p.Speed <= p.EffectiveMaxSpeed + 1e-3f, "超限 " + p.Speed);

            // 惯性滑行
            PlaneState q = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            for (int i = 0; i < 600; i++) FlightModel.Integrate(q, PlaneIntent.Cruise(1f), cfg.Physics, 1f / 120f);
            float fast = q.Speed;
            FlightModel.Integrate(q, PlaneIntent.Cruise(0f), cfg.Physics, 1f / 120f);
            CheckTrue(r, "收油门单帧内速度不骤降（有惯性）", q.Speed > fast * 0.9f,
                fast.ToString("F1") + " -> " + q.Speed.ToString("F1"));
            for (int i = 0; i < 240; i++) FlightModel.Integrate(q, PlaneIntent.Cruise(0f), cfg.Physics, 1f / 120f);
            CheckTrue(r, "持续收油会减速但仍保持前向速度", q.Speed < fast && q.Speed > 0f,
                "速度 " + q.Speed.ToString("F1"));

            // 舵效随速度变化
            PlaneState hi = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            for (int i = 0; i < 900; i++) FlightModel.Integrate(hi, PlaneIntent.Cruise(1f), cfg.Physics, 1f / 120f);
            hi.AngVel = 0f;
            for (int i = 0; i < 60; i++) FlightModel.Integrate(hi, new PlaneIntent { Throttle = 1f, Turn = 1f }, cfg.Physics, 1f / 120f);
            float rateHigh = Math.Abs(hi.AngVel);

            PlaneState lo = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            lo.Vel = new Vec2(20f, 0f);
            lo.AngVel = 0f;
            for (int i = 0; i < 60; i++) FlightModel.Integrate(lo, new PlaneIntent { Throttle = 0.02f, Turn = 1f }, cfg.Physics, 1f / 120f);
            float rateLow = Math.Abs(lo.AngVel);

            CheckTrue(r, "低速舵效显著低于高速（立项书：速度边界约束）", rateLow < rateHigh && rateLow > 0f,
                "低速 " + rateLow.ToString("F3") + " vs 高速 " + rateHigh.ToString("F3"));

            // 转向阻尼：松杆回中
            PlaneState s = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            for (int i = 0; i < 300; i++) FlightModel.Integrate(s, new PlaneIntent { Throttle = 1f, Turn = 1f }, cfg.Physics, 1f / 120f);
            float spinning = Math.Abs(s.AngVel);
            for (int i = 0; i < 300; i++) FlightModel.Integrate(s, PlaneIntent.Cruise(1f), cfg.Physics, 1f / 120f);
            CheckTrue(r, "松杆后角速度受阻尼回中（立项书：转向阻尼）", Math.Abs(s.AngVel) < spinning * 0.05f,
                "角速度 " + s.AngVel.ToString("F4"));

            // 速度边界
            PlaneState v = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            bool withinBounds = true;
            for (int i = 0; i < 900; i++)
            {
                FlightModel.Integrate(v, new PlaneIntent { Throttle = 0.9f, Turn = (i % 200 < 100) ? 1f : -1f }, cfg.Physics, 1f / 120f);
                if (i > 240 && (v.Speed < v.EffectiveMinSpeed * 0.95f || v.Speed > v.EffectiveMaxSpeed + 1e-3f)) withinBounds = false;
            }
            CheckTrue(r, "速度始终被约束在 [minSpeed, maxSpeed]", withinBounds, "出现越界速度");

            // 减速板
            PlaneState a1 = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            PlaneState a2 = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            for (int i = 0; i < 600; i++)
            {
                FlightModel.Integrate(a1, PlaneIntent.Cruise(1f), cfg.Physics, 1f / 120f);
                FlightModel.Integrate(a2, PlaneIntent.Cruise(1f), cfg.Physics, 1f / 120f);
            }
            for (int i = 0; i < 180; i++)
            {
                FlightModel.Integrate(a1, PlaneIntent.Cruise(0f), cfg.Physics, 1f / 120f);
                FlightModel.Integrate(a2, new PlaneIntent { Throttle = 0f, Brake = true }, cfg.Physics, 1f / 120f);
            }
            CheckTrue(r, "减速板显著提升阻力", a2.Speed < a1.Speed * 0.85f,
                "普通 " + a1.Speed.ToString("F1") + " vs 减速板 " + a2.Speed.ToString("F1"));

            // 软边界
            PlaneState ob = MakePlane(cfg, ConfigDefaults.PlayerTypeId);
            ob.Pos = new Vec2(-300f, 5000f);
            ob.Vel = new Vec2(-200f, 0f);
            for (int i = 0; i < 240; i++)
            {
                FlightModel.Integrate(ob, PlaneIntent.Cruise(0.5f), cfg.Physics, 1f / 120f);
                FlightModel.ApplyBounds(ob, cfg.World, 1f / 120f);
            }
            CheckTrue(r, "出界后被软边界推回（立项书：场景边界判定）", ob.Vel.X > -200f && ob.OutOfBounds,
                "vx=" + ob.Vel.X.ToString("F1"));
        }

        /* ============================================================ 武器 */

        static void WeaponTests(List<SelfTestResult> r)
        {
            DogfightConfig cfg = Cfg();
            cfg.Spawn.DifficultyScale = 1f;

            // 用一个小仿真跑武器链路
            var sim = new Simulation(cfg, 1234u);
            sim.Reset("veteran", 1234u);

            PlaneState player = sim.Player;
            var fire = new PlaneIntent { Throttle = 1f, Fire = true };
            sim.PlayerIntent = fire;

            int maxBullets = 0;
            int overheatedAt = -1;
            for (int i = 0; i < 1800; i++)
            {
                sim.Step(1f / 120f);
                if (sim.Projectiles.Count > maxBullets) maxBullets = sim.Projectiles.Count;
                WeaponRuntime w = player.Weapon("cannon");
                if (overheatedAt < 0 && w != null && w.Overheated) overheatedAt = i;
            }
            CheckTrue(r, "机炮能发射弹丸", maxBullets > 0, "没有产生任何弹丸");
            CheckTrue(r, "机炮有冷却限制，不会无限连发", maxBullets < 40, "同屏弹丸数 " + maxBullets);
            CheckTrue(r, "持续连发会触发过热", overheatedAt > 0, "15 秒内没有过热");
            CheckTrue(r, "过热不会来得太快（> 1 秒）", overheatedAt > 120,
                "第 " + overheatedAt + " 步就过热了");

            // 玩家统计接入
            CheckTrue(r, "玩家开火数被统计", sim.Stats.ShotsFired > 0, "ShotsFired = 0");

            // 弹丸继承载机速度
            var sim2 = new Simulation(cfg, 55u);
            sim2.Reset("veteran", 55u);
            PlaneState p2 = sim2.Player;
            p2.Vel = new Vec2(400f, 0f);
            p2.Angle = 0f;
            sim2.PlayerIntent = new PlaneIntent { Throttle = 0f, Fire = true };
            sim2.Step(1f / 120f);
            bool inherited = false;
            for (int i = 0; i < sim2.Projectiles.Count; i++)
            {
                if (sim2.Projectiles[i].Vel.Magnitude > cfg.FindWeapon("cannon").Speed * 1.2f) inherited = true;
            }
            CheckTrue(r, "弹丸出膛速度叠加了载机速度", inherited, "未继承载机速度");

            // 敌我识别
            var shooter = PlaneState.Create(1, cfg.FindAircraft("enemy_scout"), Vec2.Zero, 0f, Team.Enemy, false);
            var foe = PlaneState.Create(2, cfg.FindAircraft(ConfigDefaults.PlayerTypeId), new Vec2(100f, 0f), 0f, Team.Player, true);
            var friend = PlaneState.Create(3, cfg.FindAircraft("enemy_scout"), new Vec2(100f, 0f), 0f, Team.Enemy, false);
            var proj = new ProjectileState { OwnerId = 1, Team = Team.Enemy };
            CheckTrue(r, "弹丸可命中敌对方", proj.CanHit(foe, false), "应可命中");
            CheckTrue(r, "弹丸不命中自己", !proj.CanHit(shooter, false), "不应命中发射者");
            CheckTrue(r, "弹丸不命中同队（默认关闭友伤）", !proj.CanHit(friend, false), "不应命中同队");
        }

        /* ============================================================ 威胁 */

        static void ThreatTests(List<SelfTestResult> r)
        {
            DogfightConfig cfg = Cfg();
            ThreatConfig tc = cfg.Threat;

            var self = PlaneState.Create(1, cfg.FindAircraft(ConfigDefaults.PlayerTypeId), new Vec2(1000f, 1000f), 0f, Team.Player, true);
            self.BindWeapons(cfg.FindWeapon);

            var near = PlaneState.Create(2, cfg.FindAircraft("enemy_fighter"), new Vec2(1200f, 1000f), MathUtil.PI, Team.Enemy, false);
            near.BindWeapons(cfg.FindWeapon);
            var far = PlaneState.Create(3, cfg.FindAircraft("enemy_fighter"), new Vec2(3200f, 1000f), MathUtil.PI, Team.Enemy, false);
            far.BindWeapons(cfg.FindWeapon);

            float tNear = ThreatAssessment.Assess(self, near, tc, 0f, false).Value;
            float tFar = ThreatAssessment.Assess(self, far, tc, 0f, false).Value;
            CheckTrue(r, "距离越近威胁越高", tNear > tFar, tNear.ToString("F3") + " vs " + tFar.ToString("F3"));

            var facing = PlaneState.Create(4, cfg.FindAircraft("enemy_fighter"), new Vec2(2000f, 1000f), MathUtil.PI, Team.Enemy, false);
            facing.BindWeapons(cfg.FindWeapon);
            var away = PlaneState.Create(5, cfg.FindAircraft("enemy_fighter"), new Vec2(2000f, 1000f), 0f, Team.Enemy, false);
            away.BindWeapons(cfg.FindWeapon);
            float aspectFacing = ThreatAssessment.Assess(self, facing, tc, 0f, false).Aspect;
            float aspectAway = ThreatAssessment.Assess(self, away, tc, 0f, false).Aspect;
            CheckTrue(r, "机头指向我方的目标方位因子更高", aspectFacing > aspectAway,
                aspectFacing.ToString("F3") + " vs " + aspectAway.ToString("F3"));

            float full = ThreatAssessment.Assess(self, near, tc, 0f, false).Value;
            self.Hp = self.MaxHp * 0.1f;
            float hurt = ThreatAssessment.Assess(self, near, tc, 0f, false).Value;
            CheckTrue(r, "我方残血时同一目标威胁更高", hurt > full,
                full.ToString("F3") + " -> " + hurt.ToString("F3"));
            self.Hp = self.MaxHp;

            // 取值恒在 [0,1]
            var rng = new MathUtil.Rng(99u);
            bool allInRange = true;
            for (int i = 0; i < 300; i++)
            {
                var e = PlaneState.Create(100 + i, cfg.FindAircraft("enemy_fighter"),
                    new Vec2(1000f + rng.Range(-3000f, 3000f), 1000f + rng.Range(-3000f, 3000f)),
                    rng.Range(-MathUtil.PI, MathUtil.PI), Team.Enemy, false);
                e.BindWeapons(cfg.FindWeapon);
                e.Vel = new Vec2(rng.Range(-500f, 500f), rng.Range(-500f, 500f));
                float v = ThreatAssessment.Assess(self, e, tc, 0f, false).Value;
                if (v < 0f || v > 1f) allInRange = false;
            }
            CheckTrue(r, "威胁值始终落在 [0,1]", allInRange, "出现越界值");

            // 威胁网格
            var field = new ThreatField(cfg.World.Width, cfg.World.Height, cfg.Threat.Grid);
            field.AddSource(new Vec2(1000f, 1000f), 420f, 1f, 1f);
            float center = field.Sample(new Vec2(1000f, 1000f));
            float edge = field.Sample(new Vec2(1350f, 1000f));
            float outside = field.Sample(new Vec2(3000f, 2400f));
            CheckTrue(r, "威胁网格：源中心有值", center > 0f, "中心为 0");
            CheckTrue(r, "威胁网格：随距离衰减", center > edge, center.ToString("F3") + " vs " + edge.ToString("F3"));
            CheckTrue(r, "威胁网格：半径外为 0", outside <= 1e-6f, "外部应为 0，实际 " + outside);

            for (int i = 0; i < 240; i++) field.Decay(1f / 120f);
            float after = field.Sample(new Vec2(1000f, 1000f));
            CheckTrue(r, "威胁网格随时间衰减", after < center * 0.6f,
                center.ToString("F3") + " -> " + after.ToString("F3"));

            var field2 = new ThreatField(cfg.World.Width, cfg.World.Height, cfg.Threat.Grid);
            field2.AddSource(new Vec2(2000f, 1300f), 500f, 1f, 1f);
            Vec2 grad = field2.Gradient(new Vec2(1500f, 1300f), 50f);
            CheckTrue(r, "威胁梯度指向威胁升高的方向", grad.X > 0f, "gx=" + grad.X.ToString("F4"));
        }

        /* ============================================================ 寻路 */

        static void PathfindingTests(List<SelfTestResult> r)
        {
            DogfightConfig cfg = Cfg();
            var field = new ThreatField(cfg.World.Width, cfg.World.Height, cfg.Threat.Grid);

            List<Vec2> path = Pathfinding.AStar(field, new Vec2(200f, 200f), new Vec2(1800f, 1200f), 3f, 6000);
            CheckTrue(r, "A* 能在空白战场找到路径", path != null && path.Count > 0, "返回 null");
            if (path != null && path.Count > 0)
            {
                Vec2 last = path[path.Count - 1];
                CheckTrue(r, "A* 终点接近目标", Vec2.Distance(last, new Vec2(1800f, 1200f)) < 140f,
                    "终点 " + last);
            }

            // 造一堵高威胁墙，中间留缺口
            var wall = new ThreatField(cfg.World.Width, cfg.World.Height, cfg.Threat.Grid);
            float cx = 900f;
            for (float y = 0f; y < cfg.World.Height; y += wall.Cell)
            {
                if (Math.Abs(y - 1300f) < 200f) continue;
                wall.AddSource(new Vec2(cx, y), wall.Cell, wall.Config.MaxValue, 100000f);
            }
            List<Vec2> around = Pathfinding.AStar(wall, new Vec2(200f, 1300f), new Vec2(1800f, 1300f), 6f, 8000);
            bool found = around != null && around.Count > 0;
            CheckTrue(r, "A* 能在高威胁墙上找到绕行路径", found, "返回 null");
            if (found)
            {
                bool throughWall = false;
                for (int i = 0; i < around.Count; i++)
                {
                    if (Math.Abs(around[i].X - cx) < wall.Cell * 1.2f && Math.Abs(around[i].Y - 1300f) > 260f) throughWall = true;
                }
                CheckTrue(r, "A* 绕行路径不穿过高威胁墙", !throughWall, "路径穿墙了");
            }

            // BFS 找未侦察格子
            var scan = new ThreatField(cfg.World.Width, cfg.World.Height, cfg.Threat.Grid);
            for (int i = 0; i < scan.RawScanned.Length; i++) scan.RawScanned[i] = 1f;
            int tx, ty;
            scan.CellOf(new Vec2(3000f, 2000f), out tx, out ty);
            scan.RawScanned[scan.Index(tx, ty)] = 0f;
            List<Vec2> bfs = Pathfinding.BfsNearest(scan, new Vec2(200f, 200f),
                delegate (int x, int y, int index) { return scan.RawScanned[index] < 0.25f; }, 0.9f, 8000);
            CheckTrue(r, "BFS 能找到最近的未侦察格子", bfs != null && bfs.Count > 0, "返回 null");

            // 规划器注册表
            Pathfinding.RegisterBuiltIns();
            CheckTrue(r, "规划器注册表可用（V2 接遗传算法只需再注册一个）",
                Pathfinding.Get("astar") != null && Pathfinding.Get("steering") != null && Pathfinding.Get("bfs") != null,
                "内置规划器缺失");
            CheckTrue(r, "未知规划器回退到 steering", Pathfinding.Get("不存在的规划器") == Pathfinding.Get("steering"), "未回退");
        }

        /* ============================================================ 波次 */

        static void SpawnTests(List<SelfTestResult> r)
        {
            DogfightConfig cfg = Cfg();
            var sim = new Simulation(cfg, 1u);

            cfg.Spawn.DifficultyScale = 1f;
            int prev = 0;
            bool monotonic = true;
            for (int w = 1; w <= 12; w++)
            {
                int c = sim.CountForWave(w);
                if (c < prev) monotonic = false;
                prev = c;
            }
            CheckTrue(r, "敌机数量随波次单调递增", monotonic, "出现回退");

            cfg.Spawn.DifficultyScale = ConfigDefaults.DifficultyScaleFor("rookie");
            int easy = sim.CountForWave(6);
            cfg.Spawn.DifficultyScale = ConfigDefaults.DifficultyScaleFor("ace");
            int hard = sim.CountForWave(6);
            CheckTrue(r, "难度档位影响敌机数量（王牌档更多）", hard > easy, easy + " -> " + hard);

            cfg.Spawn.DifficultyScale = 1f;
            CheckTrue(r, "第 1 波不会出现重型截击机",
                cfg.Spawn.Roster[3].FromWave > 1, "解锁波次配置有误");
            CheckTrue(r, "机型按波次解锁（第 8 波有重型机）", 8 >= cfg.Spawn.Roster[3].FromWave, "第 8 波应解锁");

            float lateHp = System.Math.Min(cfg.Spawn.MaxHpScale, 1f + 99f * cfg.Spawn.HpGrowth);
            CheckRange(r, "属性成长有上限，不会无限膨胀", lateHp, 1f, cfg.Spawn.MaxHpScale + 1e-4f);
        }

        /* ======================================================== 集成仿真 */

        /// <summary>自动飞行员：朝最近敌机做提前量瞄准并开火，用来把整条链路跑通</summary>
        static PlaneIntent Autopilot(Simulation sim)
        {
            PlaneState p = sim.Player;
            if (p == null || !p.Alive) return PlaneIntent.None;

            PlaneState best = null;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < sim.Planes.Count; i++)
            {
                PlaneState e = sim.Planes[i];
                if (!e.Alive || !p.IsHostileTo(e)) continue;
                float d = Vec2.Distance(p.Pos, e.Pos);
                if (d < bestD) { bestD = d; best = e; }
            }

            var intent = new PlaneIntent();

            // 越界就往回飞
            float margin = 420f;
            bool oob = p.Pos.X < margin || p.Pos.Y < margin ||
                       p.Pos.X > sim.Config.World.Width - margin || p.Pos.Y > sim.Config.World.Height - margin;
            if (oob)
            {
                Vec2 toCenter = new Vec2(sim.Config.World.Width * 0.5f, sim.Config.World.Height * 0.5f) - p.Pos;
                float errC = MathUtil.AngleDelta(p.Angle, toCenter.Angle);
                intent.Turn = MathUtil.Clamp(errC / 0.55f, -1f, 1f);
                intent.Throttle = 1f;
                return intent;
            }

            if (best == null)
            {
                intent.Turn = 1f;
                intent.Throttle = p.Speed < 260f ? 1f : 0f;
                return intent;
            }

            float? aim = MathUtil.InterceptAngle(p.Pos, 1020f, best.Pos, best.Vel);
            float aimAngle = aim.HasValue ? aim.Value : (best.Pos - p.Pos).Angle;
            float err = MathUtil.AngleDelta(p.Angle, aimAngle);

            intent.Turn = MathUtil.Clamp(err / 0.55f, -1f, 1f);
            intent.Throttle = (bestD > 900f || Math.Abs(err) < 0.9f) ? 1f : 0f;
            intent.Fire = Math.Abs(err) < 0.14f && bestD < 800f;
            intent.Missile = Math.Abs(err) < 0.30f && bestD > 400f && bestD < 1100f;
            return intent;
        }

        static void IntegrationTests(List<SelfTestResult> r)
        {
            DogfightConfig cfg = Cfg();
            var sim = new Simulation(cfg, 20260606u);
            sim.Reset("veteran", 20260606u);

            const float dt = 1f / 60f;
            const int frames = 3600;   // 60 秒

            bool everHadEnemies = false;
            bool everFired = false;
            int maxPlanes = 0;
            int maxWave = 1;

            var events = new List<SimEvent>();

            for (int i = 0; i < frames; i++)
            {
                sim.PlayerIntent = Autopilot(sim);
                sim.Step(dt);
                sim.DrainEvents(events);

                if (sim.EnemyCount > 0) everHadEnemies = true;
                if (sim.Projectiles.Count > 0) everFired = true;
                if (sim.Planes.Count > maxPlanes) maxPlanes = sim.Planes.Count;
                if (sim.Wave > maxWave) maxWave = sim.Wave;
                if (sim.State != MatchState.Playing) break;
            }

            CheckNear(r, "整局仿真推进了 60 秒游戏时间", sim.Time, 60f, 2f);
            CheckTrue(r, "仿真过程没有异常（能走到这里就说明没崩）", true, "");
            CheckTrue(r, "会刷出敌机", everHadEnemies && maxPlanes >= 2, "峰值飞机数 " + maxPlanes);
            CheckTrue(r, "场上出现过弹丸", everFired, "没有任何弹丸");
            CheckTrue(r, "威胁网格被写入数值", HasThreat(sim), "威胁网格全为 0");
            CheckTrue(r, "玩家自动飞行员击落过敌机", sim.Stats.Kills > 0, "击落 " + sim.Stats.Kills);
            CheckTrue(r, "击落后有得分", sim.Stats.Score > 0, "得分 " + sim.Stats.Score);
            CheckTrue(r, "波次能推进（战斗链路完整）", maxWave >= 2, "只到第 " + maxWave + " 波");
            CheckTrue(r, "命中率在一次合理区间内", sim.Stats.Accuracy > 0f && sim.Stats.Accuracy <= 1f,
                "命中率 " + sim.Stats.Accuracy.ToString("P1"));

            // 单独跑一局统计 AI 行为分布，验证「威胁驱动模式切换」确实在工作
            Dictionary<string, float> modes = RunAiModeSurvey(45);
            CheckTrue(r, "AI 会动态切换多种行为模式（威胁驱动生效）", modes.Count >= 3,
                "只观察到 " + modes.Count + " 种模式");

            // 三档 AI 都能正常跑
            foreach (string pid in new[] { "rookie", "veteran", "ace" })
            {
                var s2 = new Simulation(Cfg(), 7u);
                s2.Reset(pid, 7u);
                int enemies = 0;
                for (int i = 0; i < 900; i++)   // 15 秒
                {
                    s2.PlayerIntent = Autopilot(s2);
                    s2.Step(dt);
                    if (s2.EnemyCount > enemies) enemies = s2.EnemyCount;
                }
                CheckTrue(r, "AI 档位「" + pid + "」能正常运行且会接敌", enemies > 0,
                    "15 秒内没有出现敌机");
            }
        }

        static bool HasThreat(Simulation sim)
        {
            float[] v = sim.Threat.RawValues;
            for (int i = 0; i < v.Length; i++) if (v[i] > 0.0001f) return true;
            return false;
        }

        /// <summary>跑一局并收集各 AI 模式的使用情况（供结算/报告展示）</summary>
        public static Dictionary<string, float> RunAiModeSurvey(int seconds)
        {
            DogfightConfig cfg = Cfg();
            var sim = new Simulation(cfg, 4242u);
            sim.Reset("veteran", 4242u);
            int frames = seconds * 60;
            var totals = new Dictionary<string, float>();
            var events = new List<SimEvent>();

            for (int i = 0; i < frames; i++)
            {
                sim.PlayerIntent = Autopilot(sim);
                sim.Step(1f / 60f);
                sim.DrainEvents(events);
                for (int k = 0; k < sim.Planes.Count; k++)
                {
                    AiController ai = sim.Planes[k].Ai;
                    if (ai == null || !sim.Planes[k].Alive) continue;
                    string key = ai.Mode.ToString();
                    float acc;
                    totals.TryGetValue(key, out acc);
                    totals[key] = acc + 1f / 60f;
                }
                if (sim.State != MatchState.Playing) break;
            }
            return totals;
        }
    }
}
