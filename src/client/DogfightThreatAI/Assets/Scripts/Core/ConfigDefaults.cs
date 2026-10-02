using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>
    /// 默认配置。全部数值集中在这里，方便对照调节；
    /// Unity 侧可以创建一个 GameConfig(ScriptableObject) 覆盖其中任意字段。
    /// </summary>
    public static class ConfigDefaults
    {
        public const string PlayerTypeId = "player_falcon";

        public static DogfightConfig Create()
        {
            var c = new DogfightConfig();

            /* ------------------------------------------------------- 机型 */
            c.Aircraft.Add(new AircraftDef
            {
                Id = PlayerTypeId,
                Label = "游隼 F-1",
                DefaultTeam = Team.Player,
                SpritePath = "ThirdParty/kenney-pixel-shmup/Ships/ship_0000.png",
                FallbackColorHex = "#4FC3F7",
                RenderScale = 1.5f,
                Radius = 17f,
                MaxHp = 100f,
                MaxThrust = 980f,
                MaxSpeed = 520f,
                MinSpeed = 70f,
                TurnRate = 3.45f,
                Weapons = new List<string> { "cannon", "missile" },
                Magazine = new Dictionary<string, float> { { "cannon", float.PositiveInfinity }, { "missile", 6f } },
                Reload = new Dictionary<string, float> { { "missile", 4.0f } },
                TrailColorHex = "#7FD4FF",
                TrailSize = 2.6f,
                Score = 0,
            });

            c.Aircraft.Add(new AircraftDef
            {
                Id = "enemy_scout",
                Label = "侦察机",
                SpritePath = "ThirdParty/kenney-pixel-shmup/Ships/ship_0001.png",
                FallbackColorHex = "#EF5350",
                RenderScale = 1.35f,
                Radius = 15f,
                MaxHp = 38f,
                MaxThrust = 900f,
                MaxSpeed = 430f,
                MinSpeed = 60f,
                TurnRate = 2.85f,
                Weapons = new List<string> { "cannon" },
                Magazine = new Dictionary<string, float> { { "cannon", float.PositiveInfinity } },
                TrailColorHex = "#FF9A6B",
                TrailSize = 2.2f,
                Score = 100,
            });

            c.Aircraft.Add(new AircraftDef
            {
                Id = "enemy_fighter",
                Label = "战斗机",
                SpritePath = "ThirdParty/kenney-pixel-shmup/Ships/ship_0005.png",
                FallbackColorHex = "#EF5350",
                RenderScale = 1.5f,
                Radius = 17f,
                MaxHp = 62f,
                MaxThrust = 950f,
                MaxSpeed = 470f,
                MinSpeed = 65f,
                TurnRate = 3.05f,
                Weapons = new List<string> { "cannon" },
                Magazine = new Dictionary<string, float> { { "cannon", float.PositiveInfinity } },
                TrailColorHex = "#FF8A5C",
                TrailSize = 2.4f,
                Score = 180,
            });

            c.Aircraft.Add(new AircraftDef
            {
                Id = "enemy_missiler",
                Label = "导弹机",
                SpritePath = "ThirdParty/kenney-pixel-shmup/Ships/ship_0009.png",
                FallbackColorHex = "#EF5350",
                RenderScale = 1.45f,
                Radius = 16f,
                MaxHp = 52f,
                MaxThrust = 860f,
                MaxSpeed = 440f,
                MinSpeed = 60f,
                TurnRate = 2.95f,
                Weapons = new List<string> { "cannon", "missile" },
                Magazine = new Dictionary<string, float> { { "cannon", float.PositiveInfinity }, { "missile", 3f } },
                Reload = new Dictionary<string, float> { { "missile", 7.0f } },
                TrailColorHex = "#FFB36B",
                TrailSize = 2.3f,
                Score = 240,
            });

            c.Aircraft.Add(new AircraftDef
            {
                Id = "enemy_hunter",
                Label = "重型截击机",
                SpritePath = "ThirdParty/kenney-pixel-shmup/Ships/ship_0013.png",
                FallbackColorHex = "#EF5350",
                RenderScale = 1.7f,
                Radius = 22f,
                MaxHp = 130f,
                MaxThrust = 780f,
                MaxSpeed = 400f,
                MinSpeed = 55f,
                TurnRate = 2.25f,
                Weapons = new List<string> { "cannon", "missile" },
                Magazine = new Dictionary<string, float> { { "cannon", float.PositiveInfinity }, { "missile", 4f } },
                Reload = new Dictionary<string, float> { { "missile", 6.0f } },
                TrailColorHex = "#FF7A4C",
                TrailSize = 3.0f,
                Score = 420,
            });

            /* ------------------------------------------------------- 武器 */
            c.Weapons.Add(new WeaponDef
            {
                Id = "cannon",
                Label = "机炮",
                Kind = WeaponKind.Bullet,
                Damage = 7f,
                Speed = 1020f,
                Life = 1.15f,
                Cooldown = 0.095f,
                Count = 1,
                Spread = 0.020f,
                SpreadAngle = 0.055f,
                InheritVelocity = 1.0f,
                Radius = 5f,
                Offset = 16f,
                Recoil = 40f,
                HeatPerShot = 0.048f,
                HeatCool = 0.30f,
                OverheatLock = 1.0f,
                HeatRecover = 0.35f,
            });

            c.Weapons.Add(new WeaponDef
            {
                Id = "missile",
                Label = "空空导弹",
                Kind = WeaponKind.Missile,
                Damage = 38f,
                Speed = 300f,
                Life = 5.0f,
                Cooldown = 3.0f,
                Accel = 780f,
                MaxSpeed = 900f,
                TurnRate = 2.75f,
                LockTime = 1.05f,
                LockCone = 0.62f,
                LockRange = 1250f,
                ProximityFuse = 26f,
                Radius = 7f,
                Offset = 20f,
                ArmTime = 0.10f,
            });

            /* ---------------------------------------------------- AI 档位 */
            c.AiProfiles.Add(new AiProfile
            {
                Id = "rookie", Label = "新兵",
                Description = "反应迟缓、直线追击、很少规避",
                ReactionTime = 0.50f, AimError = 0.20f, AimSmooth = 3.0f, LeadAccuracy = 0.35f,
                EvadeThreshold = 0.68f, FireRange = 700f, FireCone = 0.30f, TriggerDiscipline = 0.45f,
                MissileSkill = 0.15f, MissileMinRange = 260f, Aggression = 0.45f, MaxThrottle = 0.72f,
                Planner = "steering", PatrolRadius = 700f, EnergyCare = 0.20f,
            });
            c.AiProfiles.Add(new AiProfile
            {
                Id = "veteran", Label = "老兵",
                Description = "反应正常、会算提前量、会脱离",
                ReactionTime = 0.24f, AimError = 0.09f, AimSmooth = 5.0f, LeadAccuracy = 0.75f,
                EvadeThreshold = 0.60f, FireRange = 820f, FireCone = 0.24f, TriggerDiscipline = 0.75f,
                MissileSkill = 0.55f, MissileMinRange = 320f, Aggression = 0.65f, MaxThrottle = 0.86f,
                Planner = "astar", PatrolRadius = 900f, EnergyCare = 0.55f,
            });
            c.AiProfiles.Add(new AiProfile
            {
                Id = "ace", Label = "王牌",
                Description = "预判走位、用威胁场规划规避、导弹压制",
                ReactionTime = 0.11f, AimError = 0.032f, AimSmooth = 7.5f, LeadAccuracy = 0.95f,
                EvadeThreshold = 0.52f, FireRange = 900f, FireCone = 0.20f, TriggerDiscipline = 0.92f,
                MissileSkill = 0.95f, MissileMinRange = 380f, Aggression = 0.82f, MaxThrottle = 1.0f,
                Planner = "astar", PatrolRadius = 1100f, EnergyCare = 0.90f,
            });

            /* ------------------------------------------------------ 波次 */
            c.Spawn.MaxWaves = 20;
            c.Spawn.PlayerLives = 3;
            c.Spawn.Roster.Add(new RosterEntry("enemy_scout", 1, 3.0f, 6));
            c.Spawn.Roster.Add(new RosterEntry("enemy_fighter", 2, 3.0f, 6));
            c.Spawn.Roster.Add(new RosterEntry("enemy_missiler", 4, 1.6f, 3));
            c.Spawn.Roster.Add(new RosterEntry("enemy_hunter", 7, 1.0f, 2));

            return c;
        }

        /// <summary>难度系数（按波次强度整体缩放）</summary>
        public static float DifficultyScaleFor(string profileId)
        {
            if (profileId == "rookie") return 0.85f;
            if (profileId == "ace") return 1.25f;
            return 1.0f;
        }
    }
}
