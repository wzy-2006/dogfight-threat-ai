using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>武器种类</summary>
    public enum WeaponKind { Bullet, Missile }

    /// <summary>AI 行为模式（对应立项书的巡逻 / 搜索 / 追击 / 攻击 / 规避）</summary>
    public enum AiMode { Patrol, Search, Pursue, Attack, Evade }

    /// <summary>对战状态机</summary>
    public enum MatchState { Ready, Playing, GameOver, Victory }

    /// <summary>阵营</summary>
    public enum Team { Player, Enemy, Neutral }

    /// <summary>
    /// 全部可调参数。**这里是唯一的数值来源**，仿真代码里不写死任何平衡性常量。
    /// 纯 C# 数据类：Unity 侧用 GameConfig(ScriptableObject) 包一层就能在 Inspector 里调。
    /// </summary>
    public class DogfightConfig
    {
        public WorldConfig World = new WorldConfig();
        public PhysicsConfig Physics = new PhysicsConfig();
        public ThreatConfig Threat = new ThreatConfig();
        public SpawnConfig Spawn = new SpawnConfig();
        public RuleConfig Rules = new RuleConfig();
        public FxConfig Fx = new FxConfig();

        public List<AircraftDef> Aircraft = new List<AircraftDef>();
        public List<WeaponDef> Weapons = new List<WeaponDef>();
        public List<AiProfile> AiProfiles = new List<AiProfile>();

        public AircraftDef FindAircraft(string id)
        {
            for (int i = 0; i < Aircraft.Count; i++) if (Aircraft[i].Id == id) return Aircraft[i];
            return null;
        }

        public WeaponDef FindWeapon(string id)
        {
            for (int i = 0; i < Weapons.Count; i++) if (Weapons[i].Id == id) return Weapons[i];
            return null;
        }

        public AiProfile FindAiProfile(string id)
        {
            for (int i = 0; i < AiProfiles.Count; i++) if (AiProfiles[i].Id == id) return AiProfiles[i];
            return AiProfiles.Count > 0 ? AiProfiles[0] : null;
        }
    }

    public class WorldConfig
    {
        public float Width = 3600f;
        public float Height = 2600f;
        /// <summary>软边界：越界后向内的回推力（px/s^2）</summary>
        public float SoftPush = 420f;
        /// <summary>软边界：越界后的额外阻尼</summary>
        public float SoftDamping = 1.6f;
        /// <summary>允许越界多远（画面上表现为快出界了）</summary>
        public float OutMargin = 260f;
    }

    public class PhysicsConfig
    {
        /// <summary>线性阻尼（空气阻力，低速时主要来源）</summary>
        public float DragLinear = 0.30f;
        /// <summary>二次阻尼（高速时主要来源，决定极速）</summary>
        public float DragQuadratic = 0.0030f;
        /// <summary>减速板额外线性阻尼</summary>
        public float BrakeExtraDrag = 1.1f;
        public float ThrottleUpRate = 1.35f;
        public float ThrottleDownRate = 1.9f;
        /// <summary>达到该速度才有满舵效</summary>
        public float MinTurnAuthoritySpeed = 150f;
        /// <summary>低速时的最低舵效比例</summary>
        public float TurnAuthorityFloor = 0.28f;
        /// <summary>转向阻尼（角速度回中）</summary>
        public float AngularDamping = 14f;
    }

    public class ThreatWeights
    {
        public float Distance = 0.34f;
        public float Aspect = 0.22f;
        public float Closing = 0.16f;
        public float Weapon = 0.18f;
        public float Health = 0.10f;
    }

    public class ThreatGridConfig
    {
        public float Cell = 100f;
        /// <summary>每秒衰减比例</summary>
        public float Decay = 0.55f;
        public float MaxValue = 12f;
        public float EnemyRadius = 420f;
        public float EnemyWeight = 1.0f;
        public float MissileRadius = 300f;
        public float MissileWeight = 3.2f;
        public float BulletRadius = 130f;
        public float BulletWeight = 0.55f;
        public float BoundaryRadius = 260f;
        public float BoundaryWeight = 0.8f;
    }

    public class ThreatConfig
    {
        public ThreatWeights Weights = new ThreatWeights();
        /// <summary>超过该距离威胁按 0 计</summary>
        public float Range = 1600f;
        public float MinRange = 60f;
        /// <summary>威胁高于该值进入规避</summary>
        public float EvadeEnter = 0.60f;
        /// <summary>威胁低于该值退出规避（滞回，避免抖动）</summary>
        public float EvadeExit = 0.42f;
        /// <summary>被导弹锁定时的威胁下限</summary>
        public float LockedThreatFloor = 0.92f;
        public ThreatGridConfig Grid = new ThreatGridConfig();
    }

    public class SpawnConfig
    {
        public float FirstDelay = 1.6f;
        public float BetweenWaves = 3.2f;
        public int MaxWaves = 20;
        public int MaxAlive = 14;
        public int BaseCount = 3;
        /// <summary>每波数量 = BaseCount + floor((wave-1) * Growth)</summary>
        public float Growth = 1.15f;
        public float EntryMargin = 260f;
        public float FormationSpacing = 130f;
        /// <summary>每波血量成长</summary>
        public float HpGrowth = 0.045f;
        public float DamageGrowth = 0.02f;
        public float SpeedGrowth = 0.012f;
        public float MaxHpScale = 2.0f;
        public int PlayerLives = 3;
        /// <summary>复活倒计时（游戏时间秒）</summary>
        public float RespawnDelay = 0.6f;

        public float DifficultyScale = 1f;
        public List<RosterEntry> Roster = new List<RosterEntry>();
    }

    public class RosterEntry
    {
        public string TypeId;
        public int FromWave;
        public float Weight;
        public int MaxAlive;

        public RosterEntry(string typeId, int fromWave, float weight, int maxAlive)
        {
            TypeId = typeId; FromWave = fromWave; Weight = weight; MaxAlive = maxAlive;
        }
    }

    public class RuleConfig
    {
        public bool FriendlyFire = false;
        public float RamDamage = 34f;
        public int ScorePerWaveClear = 250;
        public float ComboWindow = 2.5f;
        public float ComboBonusPerKill = 0.15f;
        public float WaveScorePerWave = 50f;
    }

    public class FxConfig
    {
        public int MaxParticles = 900;
    }

    /// <summary>机型定义</summary>
    public class AircraftDef
    {
        public string Id;
        public string Label;
        public Team DefaultTeam = Team.Enemy;
        /// <summary>工程内的精灵路径（相对 Assets/），Unity 侧用来取 Sprite</summary>
        public string SpritePath;
        public string FallbackColorHex = "#FF6B6B";
        public float RenderScale = 1.5f;
        /// <summary>圆形碰撞体半径</summary>
        public float Radius = 17f;
        public float MaxHp = 60f;
        public float MaxThrust = 950f;
        public float MaxSpeed = 470f;
        /// <summary>低于该速度视为失速（舵效骤降）</summary>
        public float MinSpeed = 65f;
        public float TurnRate = 3.0f;
        public List<string> Weapons = new List<string>();
        public Dictionary<string, float> Magazine = new Dictionary<string, float>();
        public Dictionary<string, float> Reload = new Dictionary<string, float>();
        public int Score = 150;
        public string TrailColorHex = "#FF9A6B";
        public float TrailSize = 2.4f;

        public float MagazineFor(string weaponId)
        {
            float v;
            if (Magazine.TryGetValue(weaponId, out v)) return v;
            return float.PositiveInfinity;
        }

        public float ReloadFor(string weaponId)
        {
            float v;
            if (Reload.TryGetValue(weaponId, out v)) return v;
            return 99f;
        }
    }

    /// <summary>武器定义</summary>
    public class WeaponDef
    {
        public string Id;
        public string Label;
        public WeaponKind Kind = WeaponKind.Bullet;
        public float Damage = 7f;
        /// <summary>出膛速度</summary>
        public float Speed = 1020f;
        public float Life = 1.15f;
        public float Cooldown = 0.095f;
        public int Count = 1;
        /// <summary>随机散布（弧度）</summary>
        public float Spread = 0.02f;
        /// <summary>齐射扇形张角（弧度）</summary>
        public float SpreadAngle = 0.055f;
        /// <summary>继承载机速度的比例</summary>
        public float InheritVelocity = 1.0f;
        public float Radius = 5f;
        /// <summary>从机头前方多少 px 生成</summary>
        public float Offset = 16f;
        public float Recoil = 40f;

        // 机炮过热
        public float HeatPerShot = 0.048f;
        public float HeatCool = 0.30f;
        public float OverheatLock = 1.0f;
        public float HeatRecover = 0.35f;

        // 导弹专有
        public float Accel = 780f;
        public float MaxSpeed = 900f;
        public float TurnRate = 2.75f;
        /// <summary>锁定所需时间</summary>
        public float LockTime = 1.05f;
        /// <summary>锁定锥半角</summary>
        public float LockCone = 0.62f;
        public float LockRange = 1250f;
        /// <summary>近炸引信半径</summary>
        public float ProximityFuse = 26f;
        /// <summary>离架后多久导引头解锁</summary>
        public float ArmTime = 0.10f;
    }

    /// <summary>AI 档位：靠算法质量区分，而不是靠数值压制</summary>
    public class AiProfile
    {
        public string Id;
        public string Label;
        public string Description;

        /// <summary>决策刷新间隔（秒）——反应速度</summary>
        public float ReactionTime = 0.24f;
        /// <summary>瞄准误差（弧度）</summary>
        public float AimError = 0.09f;
        public float AimSmooth = 5.0f;
        /// <summary>提前量计算准确度 0..1</summary>
        public float LeadAccuracy = 0.75f;
        /// <summary>威胁高于该值才规避</summary>
        public float EvadeThreshold = 0.60f;
        public float FireRange = 820f;
        public float FireCone = 0.24f;
        /// <summary>开火克制程度</summary>
        public float TriggerDiscipline = 0.75f;
        /// <summary>导弹使用水平 0..1</summary>
        public float MissileSkill = 0.55f;
        public float MissileMinRange = 320f;
        public float Aggression = 0.65f;
        public float MaxThrottle = 0.86f;
        /// <summary>规避路径规划器：steering / astar</summary>
        public string Planner = "astar";
        public float PatrolRadius = 900f;
        /// <summary>能量管理意识：是否主动保速、避免低速</summary>
        public float EnergyCare = 0.55f;
    }
}
