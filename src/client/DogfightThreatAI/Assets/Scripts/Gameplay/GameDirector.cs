using System.Collections.Generic;
using Dogfight.Core;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 战斗总控：驱动 Simulation、同步视图、把仿真事件翻译成表现。
    ///
    /// 分工刻意划得很干净：
    ///   Simulation（纯 C#）负责一切规则与状态；
    ///   GameDirector（MonoBehaviour）只负责「喂输入 / 收事件 / 画出来」。
    /// 这样 V2 把 Simulation 换成「服务器下发的快照」时，表现层几乎不用改。
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        [Header("资源")]
        public GameConfig Config;
        public SpriteBank Bank;

        [Tooltip("HUD。留空会自动在同一个物体上找 / 建一个")]
        public HudOverlay Hud;

        [Header("运行参数")]
        public string AiProfileId = "veteran";
        public uint Seed = 20260606u;
        public bool GodMode = false;

        [Header("背景")]
        public bool EnableParallax = true;

        [Tooltip("运行时程序化生成地表（需要贴图勾选 Read/Write；失败会退化为纯色海面）")]
        public bool EnableGeneratedTerrain = true;

        [Tooltip("云层开关。觉得背景有雾感 / 太花就把它关掉（运行时按 F3 也能切）")]
        public bool EnableClouds = true;

        [Tooltip("云层整体透明度。0 = 完全看不见")]
        [Range(0f, 1f)] public float CloudOpacity = 0.42f;

        [Tooltip("云层贴图分辨率")]
        public int CloudTextureSize = 512;

        [Tooltip("一张云层贴图里画几团云（越少越干净）")]
        [Range(2, 24)] public int CloudCount = 9;

        [Tooltip("单团云的最大 alpha")]
        [Range(0f, 1f)] public float CloudMaxAlpha = 0.35f;

        [Tooltip("程序化地表参数（岛数、图块编号、平滑次数等）")]
        public TerrainSettings Terrain = new TerrainSettings();

        [Header("仿真")]
        [Tooltip("固定仿真步长（秒）。与渲染帧率解耦，保证不同机器上战斗结果一致")]
        public float SimStep = 1f / 120f;

        [Tooltip("单帧最多补几步，防止卡顿后爆炸")]
        public int MaxStepsPerFrame = 6;

        public bool Paused;

        public Simulation Sim { get; private set; }
        public MatchState State { get { return Sim != null ? Sim.State : MatchState.Ready; } }

        /// <summary>
        /// 玩家输入源。**刻意不叫 Input** —— 叫 Input 会在 GameDirector 内部遮蔽
        /// UnityEngine.Input，导致所有 Input.GetKeyDown 编译不过（踩过一次）。
        /// </summary>
        public PlayerInputSource InputSource { get { return _input; } }

        Camera _camera;
        CameraFollow _cameraFollow;
        ParallaxBackground _background;
        PlayerInputSource _input;

        readonly Dictionary<int, PlaneView> _planeViews = new Dictionary<int, PlaneView>();
        SpritePool _bulletPool;
        SpritePool _missilePool;
        SpritePool _fxPool;
        readonly List<GameObject> _planeViewRoots = new List<GameObject>();
        readonly HashSet<GameObject> _keep = new HashSet<GameObject>();

        // 特效实例
        class Fx
        {
            public GameObject Go;
            public SpriteRenderer Sr;
            public float Age;
            public float Life;
            public int[] Frames;
            public float FrameTime;
            public float Scale;
        }
        readonly List<Fx> _fx = new List<Fx>();

        readonly List<SimEvent> _events = new List<SimEvent>();
        float _accumulator;
        int _lastWave;

        Sprite _bulletSprite;
        Sprite _missileSprite;
        int[] _explosionFrames;
        int[] _smokeFrames;

        void Awake()
        {
            EnsureAssets();
            BuildCamera();
            BuildPools();
            BuildSimulation();
            BuildBackground();
            EnsureHud();

            if (_input == null) _input = gameObject.AddComponent<PlayerInputSource>();
        }

        void Start()
        {
            // 场景里已经存在 HUD 时（一键搭建生成的场景），同一帧内 Hud 可能还没被赋值，
            // 这里再兜一次，保证 F2 之类依赖 Hud 的按键一定能用。
            EnsureHud();
        }

        void EnsureHud()
        {
            if (Hud == null) Hud = GetComponent<HudOverlay>();
            if (Hud == null) Hud = gameObject.AddComponent<HudOverlay>();
            Hud.Director = this;
        }

        void EnsureAssets()
        {
            if (Bank == null)
            {
                Bank = Resources.Load<SpriteBank>("Dogfight/SpriteBank");
                if (Bank == null)
                {
                    Debug.LogWarning("[空战] 没找到 SpriteBank（Assets/Resources/Dogfight/SpriteBank.asset）。" +
                                     "请先执行菜单「空战 / ① 生成精灵台账」。当前会使用程序化占位图，游戏仍可运行。");
                    Bank = ScriptableObject.CreateInstance<SpriteBank>();
                }
            }
            if (Config == null) Config = Resources.Load<GameConfig>("Dogfight/GameConfig");
        }

        void BuildCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                _camera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.02f, 0.05f, 0.09f, 1f);
            _camera.transform.position = new Vector3(0f, 0f, -10f);

            _cameraFollow = _camera.GetComponent<CameraFollow>();
            if (_cameraFollow == null) _cameraFollow = _camera.gameObject.AddComponent<CameraFollow>();
            _cameraFollow.OrthoSize = Mathf.Max(6f, (_camera.orthographicSize = 14f));
        }

        void BuildPools()
        {
            if (_bulletPool == null) _bulletPool = new SpritePool(transform, "Bullet", DogfightConst.Order.Projectile);
            if (_missilePool == null) _missilePool = new SpritePool(transform, "Missile", DogfightConst.Order.Projectile);
            if (_fxPool == null) _fxPool = new SpritePool(transform, "Fx", DogfightConst.Order.PlaneFx);
        }

        void BuildSimulation()
        {
            DogfightConfig cfg = Config != null ? Config.Build() : ConfigDefaults.Create();
            Sim = new Simulation(cfg, Seed);
            RestartInternal(cfg);

            _bulletSprite = Bank.ForPath("ThirdParty/kenney-pixel-shmup/Tiles/tile_0000.png");
            if (_bulletSprite == null) _bulletSprite = Bank.Fallback("bullet", new Color(1f, 0.88f, 0.4f));
            _missileSprite = Bank.ForPath("ThirdParty/kenney-pixel-shmup/Tiles/tile_0003.png");
            if (_missileSprite == null) _missileSprite = Bank.Fallback("missile", new Color(1f, 0.72f, 0.2f));

            _explosionFrames = new[] { 4, 5, 6, 7, 8, 9 };
            _smokeFrames = new[] { 8, 9 };
        }

        // 贴图生成比较贵（地表要写 100 万像素），缓存下来，
        // 这样 F3 开关云层时可以只重建图层而不重新生成贴图。
        Texture2D _terrainTex;
        Texture2D _cloudTex;

        void BuildBackground()
        {
            if (!EnableParallax) return;
            if (_background == null) _background = gameObject.AddComponent<ParallaxBackground>();

            if (_terrainTex == null && EnableGeneratedTerrain)
            {
                _terrainTex = TerrainTextureFactory.BuildTerrain(Bank, Terrain);
                if (_terrainTex == null)
                {
                    Debug.LogWarning("[空战] 程序化地表不可用（多半是贴图没勾 Read/Write），退化为纯色海面。");
                }
            }

            bool wantClouds = EnableClouds && CloudOpacity > 0.001f && CloudCount > 0;
            if (wantClouds && _cloudTex == null)
            {
                _cloudTex = TerrainTextureFactory.BuildClouds(
                    CloudTextureSize, CloudCount, 991, new Color(1f, 1f, 1f, CloudMaxAlpha));
            }

            // ParallaxBackground.Build 内部会先清空旧图层，所以可以反复调用
            _background.Build(_camera, Bank.GetTile(42), _terrainTex,
                wantClouds ? _cloudTex : null, CloudOpacity);
        }

        /* ------------------------------------------------------- 生命周期 */

        public void Restart(string profileId, uint seed)
        {
            AiProfileId = profileId;
            Seed = seed;
            DogfightConfig cfg = Config != null ? Config.Build() : ConfigDefaults.Create();
            Sim = new Simulation(cfg, seed);
            RestartInternal(cfg);
            ClearViews();
        }

        void RestartInternal(DogfightConfig cfg)
        {
            Sim.Reset(AiProfileId, Seed);
            _lastWave = Sim.Wave;
            _accumulator = 0f;
        }

        void ClearViews()
        {
            foreach (KeyValuePair<int, PlaneView> kv in _planeViews)
            {
                if (kv.Value.Go != null) Destroy(kv.Value.Go);
            }
            _planeViews.Clear();
            _planeViewRoots.Clear();
            if (_bulletPool != null) _bulletPool.RecycleAll();
            if (_missilePool != null) _missilePool.RecycleAll();
            if (_fxPool != null) _fxPool.RecycleAll();
            _fx.Clear();
        }

        /* ----------------------------------------------------------- 主循环 */

        void Update()
        {
            if (Sim == null) return;

            HandleHotkeys();

            if (!Paused && Sim.State == MatchState.Playing)
            {
                // 固定步长推进：与渲染帧率解耦
                _accumulator += Mathf.Min(Time.deltaTime, 0.25f);
                int steps = 0;
                while (_accumulator >= SimStep && steps < MaxStepsPerFrame)
                {
                    if (_input != null)
                    {
                        _input.PlayerAngleProbe = Sim.Player != null ? Sim.Player.Angle : 0f;
                        Sim.PlayerIntent = _input.Build(Sim.Config, SimStep);
                    }
                    Sim.Step(SimStep);
                    _accumulator -= SimStep;
                    steps++;
                }
                if (steps >= MaxStepsPerFrame) _accumulator = 0f;
            }

            SyncPlanes();
            SyncProjectiles();
            DrainEvents();
            UpdateFx(Time.deltaTime);
            SyncCamera();

            if (Sim.Wave != _lastWave) _lastWave = Sim.Wave;
        }

        void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape)) Paused = !Paused;
            if (Input.GetKeyDown(KeyCode.R)) Restart(AiProfileId, (uint)Random.Range(1, int.MaxValue));
            if (Input.GetKeyDown(KeyCode.V) && _input != null) _input.ArcadeScheme = !_input.ArcadeScheme;
            if (Input.GetKeyDown(KeyCode.F1)) GodMode = !GodMode;

            // F2 威胁网格：立项书「实时威胁评估」最直观的演示视图
            if (Input.GetKeyDown(KeyCode.F2))
            {
                EnsureHud();
                if (Hud != null) Hud.ShowThreatGrid = !Hud.ShowThreatGrid;
            }

            // F3 云层开关：觉得背景有雾感/太花就按它
            if (Input.GetKeyDown(KeyCode.F3))
            {
                EnableClouds = !EnableClouds;
                BuildBackground();   // 贴图有缓存，这里只是重建图层，开销很小
            }
        }

        void SyncCamera()
        {
            if (_cameraFollow == null) return;
            PlaneState p = Sim.Player;
            if (p != null && p.Alive)
            {
                Vector2 w = DogfightConst.PxToWorld(p.Pos);
                _cameraFollow.TargetPosition = new Vector3(w.x, w.y, 0f);
                _cameraFollow.TargetVelocity = DogfightConst.PxToWorld(p.Vel);
            }
        }

        void SyncPlanes()
        {
            for (int i = 0; i < Sim.Planes.Count; i++)
            {
                PlaneState p = Sim.Planes[i];
                PlaneView view;
                if (!_planeViews.TryGetValue(p.Id, out view))
                {
                    Sprite sprite = Bank.ForPath(p.Def.SpritePath);
                    Color tint = Color.white;
                    if (sprite == null)
                    {
                        ColorUtility.TryParseHtmlString(p.Def.FallbackColorHex, out tint);
                        sprite = Bank.Fallback(p.Def.Id, tint);
                    }
                    else if (p.Team == Team.Enemy)
                    {
                        // 同款机型区分敌我：敌机统一染红
                        tint = Bank.EnemyTint;
                    }

                    view = new PlaneView(transform, p.IsPlayer ? "Player" : ("Enemy_" + p.Id),
                        DogfightConst.Order.Plane);
                    view.Bind(p.Def, sprite);
                    view.Tint = tint;
                    _planeViews[p.Id] = view;
                }

                view.Sync(p, GodMode && p.IsPlayer);
            }
        }

        void SyncProjectiles()
        {
            _keep.Clear();

            for (int i = 0; i < Sim.Projectiles.Count; i++)
            {
                ProjectileState pr = Sim.Projectiles[i];
                if (!pr.Alive) continue;

                bool isMissile = pr.Kind == WeaponKind.Missile;
                SpritePool pool = isMissile ? _missilePool : _bulletPool;
                GameObject go;
                SpriteRenderer sr = pool.Rent(out go);
                _keep.Add(go);

                Vector2 w = DogfightConst.PxToWorld(pr.Pos);
                go.transform.position = new Vector3(w.x, w.y, 0f);
                float deg = pr.Angle * Mathf.Rad2Deg - 90f;
                go.transform.rotation = Quaternion.Euler(0f, 0f, deg);

                sr.sprite = isMissile ? _missileSprite : _bulletSprite;
                sr.color = pr.Team == Team.Player ? Color.white : new Color(1f, 0.7f, 0.7f, 1f);
                float scale = pr.Def != null ? Mathf.Max(0.6f, pr.Def.Radius / 5f) : 1f;
                go.transform.localScale = Vector3.one * scale;
            }

            _bulletPool.RecycleUnmarked(_keep);
            _missilePool.RecycleUnmarked(_keep);
        }

        /* --------------------------------------------------------- 表现 */

        void DrainEvents()
        {
            if (Sim.Events.Count == 0) return;
            _events.Clear();
            Sim.DrainEvents(_events);

            for (int i = 0; i < _events.Count; i++)
            {
                SimEvent e = _events[i];
                switch (e.Type)
                {
                    case SimEventType.Explosion:
                        SpawnFx(e.Pos, _explosionFrames, 0.055f, 2.4f * Mathf.Max(0.4f, e.Power), 0.33f);
                        if (e.Power > 1f && _cameraFollow != null) _cameraFollow.Shake(8f * e.Power);
                        break;

                    case SimEventType.HitSpark:
                        SpawnFx(e.Pos, _smokeFrames, 0.05f, 1.2f, 0.10f);
                        break;

                    case SimEventType.PlaneKilled:
                        SpawnFx(e.Pos, _explosionFrames, 0.055f, 2.6f, 0.33f);
                        if (_cameraFollow != null) _cameraFollow.Shake(GodMode ? 0f : 5f);
                        break;

                    case SimEventType.PlayerDied:
                        SpawnFx(e.Pos, _explosionFrames, 0.055f, 3.2f, 0.33f);
                        if (_cameraFollow != null) _cameraFollow.Shake(16f);
                        break;

                    case SimEventType.MissileTrail:
                        SpawnFx(e.Pos, _smokeFrames, 0.09f, 1.4f, 0.18f);
                        break;

                    case SimEventType.Trail:
                        SpawnFx(e.Pos, _smokeFrames, 0.09f, 0.8f, 0.12f);
                        break;
                }
            }
        }

        void SpawnFx(Vec2 pos, int[] frames, float frameTime, float scale, float life)
        {
            if (frames == null || frames.Length == 0) return;

            GameObject go;
            SpriteRenderer sr = _fxPool.Rent(out go);
            Vector2 w = DogfightConst.PxToWorld(pos);
            go.transform.position = new Vector3(w.x, w.y, 0f);
            go.transform.localScale = Vector3.one * scale;

            Sprite first = Bank.GetTile(frames[0]);
            if (first == null) first = Bank.Fallback("fx" + frames[0], new Color(1f, 0.7f, 0.3f));
            sr.sprite = first;
            sr.color = Color.white;

            _fx.Add(new Fx
            {
                Go = go,
                Sr = sr,
                Age = 0f,
                Life = life,
                Frames = frames,
                FrameTime = frameTime,
                Scale = scale,
            });
        }

        void UpdateFx(float dt)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                Fx f = _fx[i];
                f.Age += dt;
                if (f.Age >= f.Life || f.Go == null)
                {
                    if (f.Go != null) f.Go.SetActive(false);
                    _fx.RemoveAt(i);
                    continue;
                }

                int frame = Mathf.Clamp((int)(f.Age / f.FrameTime), 0, f.Frames.Length - 1);
                Sprite s = Bank.GetTile(f.Frames[frame]);
                if (s != null) f.Sr.sprite = s;

                float k = f.Age / f.Life;
                f.Sr.color = new Color(1f, 1f, 1f, 1f - k);
                f.Go.transform.localScale = Vector3.one * f.Scale * (1f + k * 0.6f);
            }
        }

        /// <summary>供 HUD 读取：当前玩家承受的最大威胁（0..1）</summary>
        public float PlayerThreatLevel()
        {
            if (Sim == null || Sim.Player == null || !Sim.Player.Alive) return 0f;
            float worst = 0f;
            for (int i = 0; i < Sim.Planes.Count; i++)
            {
                PlaneState o = Sim.Planes[i];
                if (!o.Alive || !Sim.Player.IsHostileTo(o)) continue;
                ThreatResult t = ThreatAssessment.Assess(Sim.Player, o, Sim.Config.Threat, 0f, false);
                if (t.Value > worst) worst = t.Value;
            }
            return worst;
        }
    }
}
