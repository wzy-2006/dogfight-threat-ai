using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>一帧的操控意图。玩家输入与 AI 决策都产出这个东西，飞机本身不关心来源。</summary>
    public struct PlaneIntent
    {
        /// <summary>目标油门 0..1</summary>
        public float Throttle;
        /// <summary>转向指令 -1..1</summary>
        public float Turn;
        public bool Fire;
        public bool Missile;
        public bool Brake;

        public static readonly PlaneIntent None = new PlaneIntent { Throttle = 0f, Turn = 0f };

        public static PlaneIntent Cruise(float throttle)
        {
            return new PlaneIntent { Throttle = throttle };
        }
    }

    /// <summary>单个武器的运行时状态</summary>
    public class WeaponRuntime
    {
        public string Id;
        public WeaponDef Def;
        public float Cooldown;
        public float Ammo;
        public float MaxAmmo;
        public float ReloadTimer;
        public float Heat;
        public bool Overheated;
        public int ShotsFired;
        public int Hits;

        public bool IsInfinite { get { return float.IsPositiveInfinity(MaxAmmo); } }
        public bool CanFire { get { return Cooldown <= 0f && Ammo > 0f; } }
    }

    /// <summary>导弹锁定状态</summary>
    public struct LockState
    {
        public int TargetId;
        public float Progress;
        public bool Locked;
    }

    /// <summary>
    /// 战机运行时状态。**纯数据 + 纯逻辑**，不引用 UnityEngine，
    /// 因此可以在 Unity 之外被完整地仿真与测试（也为 V2 的服务器权威计算留好了路）。
    /// </summary>
    public class PlaneState
    {
        public int Id;
        public AircraftDef Def;
        public Team Team = Team.Enemy;
        public bool IsPlayer;
        public string Callsign = "";

        public Vec2 Pos;
        public Vec2 PrevPos;
        public Vec2 Vel;
        /// <summary>机头朝向（弧度）</summary>
        public float Angle = MathUtil.PI / 2f;
        public float AngVel;

        public float Throttle = 0.65f;
        public bool Braking;

        public float MaxHp = 60f;
        public float Hp = 60f;
        public bool Alive = true;
        public float Radius = 16f;

        /// <summary>波次成长倍率</summary>
        public float SpeedScale = 1f;
        public float DamageScale = 1f;

        public readonly Dictionary<string, WeaponRuntime> Weapons = new Dictionary<string, WeaponRuntime>();
        public LockState Lock;

        public float HitFlash;
        public bool OutOfBounds;
        public float TimeAlive;
        public float TrailTimer;
        public float SmokeTimer;

        public int Kills;
        public float DamageDealt;

        /// <summary>由 Simulation 注入的武器查表委托（避免 PlaneState 直接依赖 DogfightConfig 实例）</summary>
        System.Func<string, WeaponDef> _weaponLookup;

        /// <summary>AI 控制器；玩家机为 null</summary>
        public AiController Ai;

        public float Speed { get { return Vel.Magnitude; } }
        public float EffectiveMaxSpeed { get { return Def.MaxSpeed * SpeedScale; } }
        public float EffectiveMinSpeed { get { return Def.MinSpeed * SpeedScale; } }
        public float HpRatio { get { return MaxHp <= 0f ? 0f : MathUtil.Clamp01(Hp / MaxHp); } }
        public bool IsStalling { get { return Speed < EffectiveMinSpeed * 1.05f; } }
        public bool IsHostileTo(PlaneState other)
        {
            if (other == null || other == this) return false;
            if (Team == Team.Neutral || other.Team == Team.Neutral) return true;
            return Team != other.Team;
        }

        public static PlaneState Create(int id, AircraftDef def, Vec2 pos, float angle, Team team, bool isPlayer)
        {
            var p = new PlaneState
            {
                Id = id,
                Def = def,
                Team = team,
                IsPlayer = isPlayer,
                Pos = pos,
                PrevPos = pos,
                Angle = angle,
                Radius = def.Radius,
                MaxHp = def.MaxHp,
                Hp = def.MaxHp,
                Callsign = isPlayer ? "玩家" : def.Label,
            };
            p.ResetWeapons();
            return p;
        }

        public void ResetWeapons()
        {
            Weapons.Clear();
            if (_weaponLookup == null) return;   // 还没绑定查表函数，等 BindWeapons 后再建
            for (int i = 0; i < Def.Weapons.Count; i++)
            {
                string wid = Def.Weapons[i];
                WeaponDef wdef = _weaponLookup(wid);
                if (wdef == null) continue;
                float mag = Def.MagazineFor(wid);
                Weapons[wid] = new WeaponRuntime
                {
                    Id = wid,
                    Def = wdef,
                    Ammo = mag,
                    MaxAmmo = mag,
                };
            }
        }

        /// <summary>
        /// 注入武器查表函数**并立刻建立武器表**。
        /// 注意顺序：PlaneState.Create 时还拿不到 WeaponDef，所以必须由这里补建，
        /// 否则整架飞机一把武器都不会有（这个坑踩过一次，已由自检覆盖）。
        /// </summary>
        public void BindWeapons(System.Func<string, WeaponDef> lookup)
        {
            _weaponLookup = lookup;
            ResetWeapons();
        }

        public WeaponRuntime Weapon(string id)
        {
            WeaponRuntime w;
            return Weapons.TryGetValue(id, out w) ? w : null;
        }

        /// <summary>复活 / 重置</summary>
        public void Revive(Vec2 pos, float angle)
        {
            Pos = pos;
            PrevPos = pos;
            Vel = Vec2.Zero;
            Angle = angle;
            AngVel = 0f;
            Hp = MaxHp;
            Alive = true;
            Throttle = 0.65f;
            Braking = false;
            HitFlash = 0f;
            Lock = new LockState();
            for (int i = 0; i < Def.Weapons.Count; i++)
            {
                var w = Weapon(Def.Weapons[i]);
                if (w == null) continue;
                w.Cooldown = 0f;
                w.Heat = 0f;
                w.Overheated = false;
                w.ReloadTimer = 0f;
                w.Ammo = w.MaxAmmo;
            }
        }
    }
}
