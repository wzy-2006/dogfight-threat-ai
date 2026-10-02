using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>仿真向视图层发出的表现事件（仿真本身不碰任何渲染资源）</summary>
    public enum SimEventType
    {
        Muzzle, HitSpark, Explosion, Trail, MissileTrail,
        PlaneSpawned, PlaneKilled, PlayerDied, WaveStart, WaveClear,
        MissileLaunched, Victory, Defeat
    }

    public struct SimEvent
    {
        public SimEventType Type;
        public Vec2 Pos;
        public float Angle;
        public float Power;
        public int PlaneId;
        public int Wave;
        public Team Team;
    }

    /// <summary>战局统计（结算界面用）</summary>
    public class MatchStats
    {
        public int Score;
        public int Kills;
        public int Deaths;
        public int ShotsFired;
        public int ShotsHit;
        public int MissilesFired;
        public int MissilesHit;
        public float PlayerDamageDealt;
        public float PlayerDamageTaken;
        public int Combo;
        public int BestCombo;
        public float Elapsed;

        public float Accuracy { get { return ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f; } }

        public void Reset()
        {
            Score = 0; Kills = 0; Deaths = 0; ShotsFired = 0; ShotsHit = 0;
            MissilesFired = 0; MissilesHit = 0;
            PlayerDamageDealt = 0f; PlayerDamageTaken = 0f;
            Combo = 0; BestCombo = 0; Elapsed = 0f;
        }
    }

    /// <summary>
    /// 战斗仿真内核。**整个游戏逻辑都在这里**，不引用 UnityEngine，
    /// 因此可以在 Unity 之外逐帧跑、做回归测试，也便于 V2 把这套逻辑直接搬到逻辑服务器上
    /// 做服务器权威计算（客户端只负责表现与插值）。
    /// </summary>
    public class Simulation
    {
        public readonly DogfightConfig Config;
        public readonly MatchStats Stats = new MatchStats();

        public readonly List<PlaneState> Planes = new List<PlaneState>();
        public readonly List<ProjectileState> Projectiles = new List<ProjectileState>();
        public readonly List<SimEvent> Events = new List<SimEvent>();

        public ThreatField Threat { get; private set; }
        public PlaneState Player { get; private set; }

        public MatchState State = MatchState.Ready;
        public float Time;
        public int Wave = 1;
        public int Lives;
        public float RespawnTimer;
        public float WaveTimer;
        public bool WaveActive;
        public string AiProfileId = "veteran";

        /// <summary>玩家这一帧的操控意图，由输入层每帧写入</summary>
        public PlaneIntent PlayerIntent;

        readonly MathUtil.Rng _rng;
        MathUtil.Rng _rngRef;
        int _nextPlaneId = 1;
        int _nextProjectileId = 1;
        AiProfile _profile;

        /// <summary>本局使用的随机源；Reset 时可按新种子替换，保证可复现</summary>
        MathUtil.Rng Rng { get { return _rngRef ?? _rng; } }

        public AiProfile Profile { get { return _profile; } }
        public int EnemyCount { get { int n = 0; for (int i = 0; i < Planes.Count; i++) if (Planes[i].Alive && !Planes[i].IsPlayer) n++; return n; } }
        public int AliveCount { get { int n = 0; for (int i = 0; i < Planes.Count; i++) if (Planes[i].Alive) n++; return n; } }

        public Simulation(DogfightConfig config, uint seed)
        {
            Config = config;
            _rng = new MathUtil.Rng(seed);
            Pathfinding.RegisterBuiltIns();
            Threat = new ThreatField(config.World.Width, config.World.Height, config.Threat.Grid);
        }

        /* ------------------------------------------------------- 生命周期 */

        public void Reset(string aiProfileId, uint seed)
        {
            AiProfileId = aiProfileId;
            _profile = Config.FindAiProfile(aiProfileId);
            Config.Spawn.DifficultyScale = ConfigDefaults.DifficultyScaleFor(aiProfileId);

            var rng = new MathUtil.Rng(seed);
            _rngRef = rng;

            Planes.Clear();
            Projectiles.Clear();
            Events.Clear();
            Stats.Reset();

            Time = 0f;
            Wave = 1;
            Lives = Config.Spawn.PlayerLives;
            RespawnTimer = 0f;
            WaveTimer = Config.Spawn.FirstDelay;
            WaveActive = false;
            State = MatchState.Playing;
            PlayerIntent = PlaneIntent.None;

            Threat.Clear();

            AircraftDef pdef = Config.FindAircraft(ConfigDefaults.PlayerTypeId);
            Player = CreatePlane(pdef, new Vec2(Config.World.Width * 0.5f, Config.World.Height * 0.5f), MathUtil.PI / 2f, Team.Player, true);
            Player.BindWeapons(Config.FindWeapon);
        }

        PlaneState CreatePlane(AircraftDef def, Vec2 pos, float angle, Team team, bool isPlayer)
        {
            var p = PlaneState.Create(_nextPlaneId++, def, pos, angle, team, isPlayer);
            p.BindWeapons(Config.FindWeapon);
            Planes.Add(p);
            Events.Add(new SimEvent { Type = SimEventType.PlaneSpawned, Pos = pos, PlaneId = p.Id, Team = team });
            return p;
        }

        /* ----------------------------------------------------------- 主循环 */

        public void Step(float dt)
        {
            if (State != MatchState.Playing)
            {
                return;
            }

            Time += dt;
            Stats.Elapsed += dt;

            // 1) 威胁场：立项书要求每帧量化战场
            Threat.Refresh(Planes, Projectiles, dt);

            // 2) 波次
            StepWaves(dt);
            if (State != MatchState.Playing) return;

            // 3) 实体更新
            for (int i = 0; i < Planes.Count; i++)
            {
                PlaneState p = Planes[i];
                if (!p.Alive) continue;
                PlaneIntent intent = p.IsPlayer
                    ? PlayerIntent
                    : (p.Ai != null ? p.Ai.Think(this, dt) : PlaneIntent.Cruise(0.6f));
                FlightModel.Integrate(p, intent, Config.Physics, dt);
                UpdateWeapons(p, intent, dt);
                FlightModel.ApplyBounds(p, Config.World, dt);
                UpdateTrailEmission(p, dt);
            }

            for (int i = 0; i < Projectiles.Count; i++)
            {
                ProjectileState pr = Projectiles[i];
                if (pr.Alive) UpdateProjectile(pr, dt);
            }

            // 4) 碰撞
            ResolveProjectileHits();
            ResolveRamCollisions();

            // 5) 清理与复活
            for (int i = Projectiles.Count - 1; i >= 0; i--) if (!Projectiles[i].Alive) Projectiles.RemoveAt(i);
            for (int i = Planes.Count - 1; i >= 0; i--) if (!Planes[i].Alive && !Planes[i].IsPlayer) Planes.RemoveAt(i);
            StepRespawn(dt);

            // 6) 连杀计时
            if (Stats.Combo > 0)
            {
                _comboTimer -= dt;
                if (_comboTimer <= 0f) Stats.Combo = 0;
            }
        }

        float _comboTimer;

        /* ----------------------------------------------------------- 武器 */

        void UpdateWeapons(PlaneState p, PlaneIntent intent, float dt)
        {
            foreach (var kv in p.Weapons)
            {
                WeaponRuntime w = kv.Value;
                if (w.Cooldown > 0f) w.Cooldown -= dt;

                if (w.Def.Kind == WeaponKind.Bullet)
                {
                    if (w.Heat > 0f) w.Heat = System.Math.Max(0f, w.Heat - w.Def.HeatCool * dt);
                    if (w.Overheated && w.Heat <= w.Def.HeatRecover) w.Overheated = false;
                }

                if (!w.IsInfinite && w.Ammo <= 0f)
                {
                    w.ReloadTimer += dt;
                    if (w.ReloadTimer >= p.Def.ReloadFor(w.Id))
                    {
                        w.Ammo = System.Math.Min(w.MaxAmmo, w.Ammo + 1f);
                        w.ReloadTimer = 0f;
                    }
                }
            }

            if (intent.Fire) TryFireBullet(p);
            UpdateMissileLock(p, intent, dt);
        }

        void TryFireBullet(PlaneState p)
        {
            WeaponRuntime w = p.Weapon("cannon");
            if (w == null || w.Overheated) return;
            if (!FireWeapon(p, w, -1)) return;

            w.Heat += w.Def.HeatPerShot;
            if (w.Heat >= w.Def.OverheatLock)
            {
                w.Overheated = true;
                w.Heat = w.Def.OverheatLock;
            }
        }

        bool FireWeapon(PlaneState p, WeaponRuntime w, int targetId)
        {
            if (w == null || !w.CanFire) return false;

            w.Cooldown = w.Def.Cooldown;
            if (!w.IsInfinite) w.Ammo -= 1f;
            w.ShotsFired++;

            if (p.IsPlayer)
            {
                Stats.ShotsFired += w.Def.Count;
                if (w.Def.Kind == WeaponKind.Missile) Stats.MissilesFired++;
            }

            WeaponDef d = w.Def;
            Vec2 forward = Vec2.FromAngle(p.Angle);
            Vec2 muzzle = p.Pos + forward * d.Offset;

            for (int i = 0; i < d.Count; i++)
            {
                float fan = d.Count > 1 ? ((float)i / (d.Count - 1) - 0.5f) * d.SpreadAngle : 0f;
                float jitter = Rng.Signed() * d.Spread;
                float ang = p.Angle + fan + jitter;

                var pr = new ProjectileState
                {
                    Id = _nextProjectileId++,
                    OwnerId = p.Id,
                    Team = p.Team,
                    Kind = d.Kind,
                    Def = d,
                    Pos = muzzle,
                    PrevPos = muzzle,
                    Angle = ang,
                    Life = d.Life,
                    Radius = d.Radius,
                    Damage = d.Damage * p.DamageScale,
                    Speed = d.Speed,
                    TargetId = targetId,
                    ArmTime = d.Kind == WeaponKind.Missile ? d.ArmTime : 0f,
                };

                pr.Vel = Vec2.FromAngle(ang) * d.Speed;
                if (d.Kind == WeaponKind.Bullet && d.InheritVelocity > 0f)
                {
                    pr.Vel += p.Vel * d.InheritVelocity;
                    pr.Speed = pr.Vel.Magnitude;
                }

                Projectiles.Add(pr);
            }

            // 后坐
            p.Vel -= forward * d.Recoil;

            if (d.Kind == WeaponKind.Missile)
            {
                Events.Add(new SimEvent { Type = SimEventType.MissileLaunched, Pos = muzzle, Angle = p.Angle, PlaneId = p.Id, Team = p.Team });
            }
            else
            {
                Events.Add(new SimEvent { Type = SimEventType.Muzzle, Pos = muzzle, Angle = p.Angle, PlaneId = p.Id, Team = p.Team });
            }
            return true;
        }

        /// <summary>导弹锁定：按住导弹键在锥内累积进度，完成后发射</summary>
        void UpdateMissileLock(PlaneState p, PlaneIntent intent, float dt)
        {
            WeaponRuntime w = p.Weapon("missile");
            if (w == null) return;
            WeaponDef d = w.Def;

            bool canShoot = w.CanFire;
            if (intent.Missile && canShoot)
            {
                int cand = FindLockCandidate(p, d);
                if (cand == p.Lock.TargetId && cand != -1)
                {
                    float prog = p.Lock.Progress + dt / d.LockTime;
                    if (prog >= 1f)
                    {
                        p.Lock.Progress = 0f;
                        p.Lock.Locked = false;
                        int t = p.Lock.TargetId;
                        p.Lock.TargetId = -1;
                        FireWeapon(p, w, t);
                    }
                    else
                    {
                        p.Lock.Progress = prog;
                        p.Lock.Locked = false;
                    }
                }
                else
                {
                    p.Lock.TargetId = cand;
                    p.Lock.Progress = cand != -1 ? dt / d.LockTime : 0f;
                    p.Lock.Locked = false;
                }
            }
            else
            {
                // 松手时若已有部分锁定 → 无制导发射（发射后不管）
                if (!intent.Missile && p.Lock.Progress > 0.22f && canShoot)
                {
                    FireWeapon(p, w, -1);
                }
                p.Lock.Progress = 0f;
                p.Lock.Locked = false;
                p.Lock.TargetId = -1;
            }
        }

        /// <summary>在锁定锥内挑一个「最好打」的目标</summary>
        int FindLockCandidate(PlaneState p, WeaponDef d)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < Planes.Count; i++)
            {
                PlaneState o = Planes[i];
                if (!o.Alive || !p.IsHostileTo(o)) continue;
                float dist = Vec2.Distance(p.Pos, o.Pos);
                if (dist > d.LockRange) continue;
                float ang = (o.Pos - p.Pos).Angle;
                float off = System.Math.Abs(MathUtil.AngleDelta(p.Angle, ang));
                if (off > d.LockCone) continue;
                float score = (1f - off / d.LockCone) * 2f + (1f - dist / d.LockRange) + (1f - o.HpRatio) * 0.6f;
                if (score > bestScore) { bestScore = score; best = o.Id; }
            }
            return best;
        }

        /* --------------------------------------------------------- 弹丸 */

        void UpdateProjectile(ProjectileState pr, float dt)
        {
            pr.PrevPos = pr.Pos;
            pr.Age += dt;
            if (pr.Age >= pr.Life)
            {
                Expire(pr);
                return;
            }

            if (pr.Kind == WeaponKind.Missile)
            {
                WeaponDef d = pr.Def;
                if (pr.IsArmed && pr.TargetId != -1)
                {
                    PlaneState t = FindPlane(pr.TargetId);
                    if (t != null && t.Alive)
                    {
                        float want = (t.Pos - pr.Pos).Angle;
                        pr.Angle = MathUtil.RotateTowards(pr.Angle, want, d.TurnRate, dt);
                    }
                    else
                    {
                        pr.TargetId = -1;
                    }
                }
                pr.Speed = System.Math.Min(d.MaxSpeed, pr.Speed + d.Accel * dt);
                pr.Vel = Vec2.FromAngle(pr.Angle) * pr.Speed;

                // 尾迹限流：按固定间隔发事件，避免每物理步都刷
                pr.TrailTimer += dt;
                if (pr.TrailTimer >= 0.016f)
                {
                    pr.TrailTimer = 0f;
                    Events.Add(new SimEvent { Type = SimEventType.MissileTrail, Pos = pr.Pos, Angle = pr.Angle });
                }
            }

            pr.Pos += pr.Vel * dt;

            float margin = 200f;
            if (pr.Pos.X < -margin || pr.Pos.Y < -margin ||
                pr.Pos.X > Config.World.Width + margin || pr.Pos.Y > Config.World.Height + margin)
            {
                pr.Alive = false;
            }
        }

        void Expire(ProjectileState pr)
        {
            pr.Alive = false;
            if (pr.Kind == WeaponKind.Missile)
            {
                Events.Add(new SimEvent { Type = SimEventType.Explosion, Pos = pr.Pos, Power = 0.6f, Team = pr.Team });
            }
        }

        public PlaneState FindPlane(int id)
        {
            for (int i = 0; i < Planes.Count; i++) if (Planes[i].Id == id) return Planes[i];
            return null;
        }

        /* --------------------------------------------------------- 碰撞 */

        void ResolveProjectileHits()
        {
            bool ff = Config.Rules.FriendlyFire;
            for (int i = 0; i < Projectiles.Count; i++)
            {
                ProjectileState pr = Projectiles[i];
                if (!pr.Alive) continue;

                // 近炸引信（导弹）
                if (pr.Kind == WeaponKind.Missile)
                {
                    float fuse = pr.Def.ProximityFuse + pr.Radius;
                    PlaneState prox = null;
                    for (int j = 0; j < Planes.Count; j++)
                    {
                        PlaneState t = Planes[j];
                        if (!pr.CanHit(t, ff)) continue;
                        if (Vec2.Distance(pr.Pos, t.Pos) <= fuse + t.Radius) { prox = t; break; }
                    }
                    if (prox != null) { ApplyHit(pr, prox); continue; }
                }

                // 扫掠判定：用上一帧到当前帧的线段与目标圆求交
                PlaneState hit = null;
                for (int j = 0; j < Planes.Count; j++)
                {
                    PlaneState t = Planes[j];
                    if (!pr.CanHit(t, ff)) continue;
                    if (MathUtil.SegmentHitsCircle(pr.PrevPos, pr.Pos, t.Pos, t.Radius + pr.Radius)) { hit = t; break; }
                }
                if (hit != null) { ApplyHit(pr, hit); continue; }

                // 出界
                if (pr.Pos.X < 0f || pr.Pos.Y < 0f || pr.Pos.X > Config.World.Width || pr.Pos.Y > Config.World.Height)
                {
                    pr.Alive = false;
                }
            }
        }

        void ApplyHit(ProjectileState pr, PlaneState target)
        {
            pr.Alive = false;
            PlaneState owner = FindPlane(pr.OwnerId);

            float dealt = Damage(target, pr.Damage, owner, pr.Kind == WeaponKind.Missile ? "missile" : "cannon");

            Events.Add(new SimEvent { Type = SimEventType.HitSpark, Pos = pr.Pos, Angle = pr.Vel.Angle + MathUtil.PI, Team = pr.Team });

            WeaponRuntime w = owner != null ? owner.Weapon(pr.Def.Id) : null;
            if (w != null) w.Hits++;
            if (owner != null && owner.IsPlayer)
            {
                Stats.ShotsHit++;
                if (pr.Kind == WeaponKind.Missile) Stats.MissilesHit++;
            }
            if (target.IsPlayer) Stats.PlayerDamageTaken += dealt;
        }

        float Damage(PlaneState target, float amount, PlaneState source, string cause)
        {
            if (target == null || !target.Alive || amount <= 0f) return 0f;
            float before = target.Hp;
            target.Hp = System.Math.Max(0f, target.Hp - amount);
            float dealt = before - target.Hp;
            target.HitFlash = 0.12f;

            if (source != null && source.IsPlayer) Stats.PlayerDamageDealt += dealt;
            if (target.Hp <= 0f) Kill(target, source, cause);
            return dealt;
        }

        void Kill(PlaneState victim, PlaneState killer, string cause)
        {
            if (!victim.Alive) return;
            victim.Alive = false;
            Stats.Deaths += victim.IsPlayer ? 1 : 0;

            Events.Add(new SimEvent
            {
                Type = victim.IsPlayer ? SimEventType.PlayerDied : SimEventType.PlaneKilled,
                Pos = victim.Pos,
                Power = victim.Radius > 20f ? 2.0f : 1.35f,
                PlaneId = victim.Id,
                Team = victim.Team,
            });

            if (killer != null && killer != victim && killer.IsHostileTo(victim))
            {
                killer.Kills++;
                killer.DamageDealt += victim.MaxHp;
                if (killer.IsPlayer)
                {
                    Stats.Kills++;
                    Stats.Combo++;
                    _comboTimer = Config.Rules.ComboWindow;
                    if (Stats.Combo > Stats.BestCombo) Stats.BestCombo = Stats.Combo;
                    float comboMul = 1f + (Stats.Combo - 1) * Config.Rules.ComboBonusPerKill;
                    float baseScore = victim.Def.Score;
                    Stats.Score += (int)(baseScore * comboMul * (cause == "ram" ? 0.6f : 1f));
                }
            }

            if (victim.IsPlayer) OnPlayerDeath();
        }

        void ResolveRamCollisions()
        {
            for (int i = 0; i < Planes.Count; i++)
            {
                PlaneState a = Planes[i];
                if (!a.Alive) continue;
                for (int j = i + 1; j < Planes.Count; j++)
                {
                    PlaneState b = Planes[j];
                    if (!b.Alive) continue;
                    if (!a.IsHostileTo(b)) continue;
                    if (!Vec2.CirclesOverlap(a.Pos, a.Radius, b.Pos, b.Radius)) continue;

                    float dmg = Config.Rules.RamDamage;
                    Damage(a, dmg, b, "ram");
                    Damage(b, dmg, a, "ram");

                    float ang = (b.Pos - a.Pos).Angle;
                    Vec2 push = Vec2.FromAngle(ang) * 160f;
                    a.Vel -= push;
                    b.Vel += push;
                    Events.Add(new SimEvent { Type = SimEventType.HitSpark, Pos = (a.Pos + b.Pos) * 0.5f, Angle = ang, Team = a.Team });
                }
            }
        }

        /* --------------------------------------------------------- 波次 */

        void StepWaves(float dt)
        {
            SpawnConfig s = Config.Spawn;
            if (WaveActive)
            {
                if (EnemyCount == 0)
                {
                    Stats.Score += Config.Rules.ScorePerWaveClear + (int)(Wave * Config.Rules.WaveScorePerWave);
                    Events.Add(new SimEvent { Type = SimEventType.WaveClear, Wave = Wave, Pos = new Vec2(Config.World.Width * 0.5f, Config.World.Height * 0.5f) });
                    Wave++;
                    if (Wave > s.MaxWaves)
                    {
                        State = MatchState.Victory;
                        Events.Add(new SimEvent { Type = SimEventType.Victory, Wave = Wave - 1 });
                        return;
                    }
                    WaveActive = false;
                    WaveTimer = s.BetweenWaves;
                }
                return;
            }

            WaveTimer -= dt;
            if (WaveTimer > 0f) return;

            Events.Add(new SimEvent { Type = SimEventType.WaveStart, Wave = Wave, Pos = new Vec2(Config.World.Width * 0.5f, Config.World.Height * 0.5f) });
            SpawnWave(Wave);
            WaveActive = true;
        }

        public int CountForWave(int wave)
        {
            SpawnConfig s = Config.Spawn;
            int n = s.BaseCount + (int)System.Math.Floor((wave - 1) * s.Growth);
            n = (int)System.Math.Round(n * s.DifficultyScale);
            return n < 1 ? 1 : n;
        }

        void SpawnWave(int wave)
        {
            SpawnConfig s = Config.Spawn;
            int count = CountForWave(wave);

            // 按波次解锁机型 + 权重抽取 + 场上上限
            var pool = new List<RosterEntry>();
            for (int i = 0; i < s.Roster.Count; i++)
            {
                RosterEntry r = s.Roster[i];
                if (wave < r.FromWave) continue;
                int aliveOfType = 0;
                for (int k = 0; k < Planes.Count; k++)
                {
                    PlaneState p = Planes[k];
                    if (p.Alive && !p.IsPlayer && p.Def.Id == r.TypeId) aliveOfType++;
                }
                if (aliveOfType >= r.MaxAlive) continue;
                pool.Add(r);
            }
            if (pool.Count == 0) pool.Add(s.Roster[0]);

            float totalWeight = 0f;
            for (int i = 0; i < pool.Count; i++) totalWeight += pool[i].Weight;

            // 阵型：随机一边进入
            int side = Rng.RangeInt(0, 4);
            float m = s.EntryMargin;
            float W = Config.World.Width, H = Config.World.Height;
            Vec2 basePos;
            float entryAngle;
            switch (side)
            {
                case 0: basePos = new Vec2(Rng.Range(0f, W), -m); entryAngle = MathUtil.PI / 2f; break;
                case 1: basePos = new Vec2(W + m, Rng.Range(0f, H)); entryAngle = MathUtil.PI; break;
                case 2: basePos = new Vec2(Rng.Range(0f, W), H + m); entryAngle = -MathUtil.PI / 2f; break;
                default: basePos = new Vec2(-m, Rng.Range(0f, H)); entryAngle = 0f; break;
            }
            Vec2 perp = Vec2.FromAngle(entryAngle + MathUtil.PI / 2f);
            int formation = Rng.RangeInt(0, 3);

            float hpScale = System.Math.Min(s.MaxHpScale, 1f + (wave - 1) * s.HpGrowth);
            float dmgScale = 1f + (wave - 1) * s.DamageGrowth;
            float spdScale = 1f + (wave - 1) * s.SpeedGrowth;

            for (int i = 0; i < count; i++)
            {
                if (EnemyCount >= s.MaxAlive) break;

                // 权重抽取
                float r = Rng.NextFloat() * totalWeight;
                RosterEntry chosen = pool[pool.Count - 1];
                for (int k = 0; k < pool.Count; k++)
                {
                    r -= pool[k].Weight;
                    if (r <= 0f) { chosen = pool[k]; break; }
                }

                Vec2 pos;
                if (formation == 0)
                {
                    pos = basePos + perp * ((i - (count - 1) * 0.5f) * s.FormationSpacing);
                }
                else if (formation == 1)
                {
                    int row = i / 2;
                    float sideSign = (i % 2 == 0) ? 1f : -1f;
                    pos = basePos + perp * (sideSign * row * s.FormationSpacing * 0.8f)
                                 - Vec2.FromAngle(entryAngle) * (row * s.FormationSpacing * 0.5f);
                }
                else
                {
                    pos = basePos + new Vec2(Rng.Range(-260f, 260f), Rng.Range(-260f, 260f));
                }

                AircraftDef def = Config.FindAircraft(chosen.TypeId);
                if (def == null) continue;

                PlaneState p = CreatePlane(def, pos, entryAngle, Team.Enemy, false);
                p.SpeedScale = spdScale;
                p.DamageScale = dmgScale;
                p.MaxHp = (float)System.Math.Round(def.MaxHp * hpScale);
                p.Hp = p.MaxHp;
                p.Vel = Vec2.FromAngle(entryAngle) * (def.MaxSpeed * 0.55f);
                p.Ai = new AiController(this, p, _profile);
            }
        }

        /* --------------------------------------------------------- 复活 */

        void OnPlayerDeath()
        {
            Lives--;
            if (Lives > 0)
            {
                RespawnTimer = Config.Spawn.RespawnDelay;
            }
            else
            {
                State = MatchState.GameOver;
                Events.Add(new SimEvent { Type = SimEventType.Defeat, Pos = Player != null ? Player.Pos : Vec2.Zero });
            }
        }

        void StepRespawn(float dt)
        {
            if (RespawnTimer <= 0f) return;
            RespawnTimer -= dt;
            if (RespawnTimer > 0f) return;
            if (Player == null || State != MatchState.Playing) return;
            Player.Revive(new Vec2(Config.World.Width * 0.5f, Config.World.Height * 0.5f), MathUtil.PI / 2f);
            Events.Add(new SimEvent { Type = SimEventType.PlaneSpawned, Pos = Player.Pos, PlaneId = Player.Id, Team = Team.Player });
        }

        /* --------------------------------------------------------- 表现 */

        void UpdateTrailEmission(PlaneState p, float dt)
        {
            if (p.Throttle > 0.12f)
            {
                p.TrailTimer += dt * 26f * (0.35f + p.Throttle);
                while (p.TrailTimer >= 1f)
                {
                    p.TrailTimer -= 1f;
                    Vec2 back = p.Pos - Vec2.FromAngle(p.Angle) * (p.Radius * 0.9f);
                    Events.Add(new SimEvent { Type = SimEventType.Trail, Pos = back, Angle = p.Angle, PlaneId = p.Id, Team = p.Team });
                }
            }
            if (p.HpRatio < 0.45f)
            {
                p.SmokeTimer += dt * (p.HpRatio < 0.2f ? 22f : 11f);
                while (p.SmokeTimer >= 1f)
                {
                    p.SmokeTimer -= 1f;
                    Vec2 back = p.Pos - Vec2.FromAngle(p.Angle) * (p.Radius * 0.6f);
                    Events.Add(new SimEvent { Type = SimEventType.Explosion, Pos = back, Power = 0.25f, Team = p.Team });
                }
            }
        }

        /// <summary>取出并清空本帧事件（视图层消费）</summary>
        public void DrainEvents(List<SimEvent> into)
        {
            if (into == null) return;
            into.AddRange(Events);
            Events.Clear();
        }
    }
}
