using System;
using Dogfight.Core;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// HUD + 多层雷达。用 IMGUI（OnGUI）绘制。
    ///
    /// 为什么用 IMGUI 而不是 uGUI/TMP：这是第一阶段原型，IMGUI 不需要任何字体资源、
    /// Canvas、EventSystem 或 TMP Essentials 导入步骤，**不会因为素材没配好而白屏**。
    /// 代价是表现力一般；等进第二阶段换成 uGUI + TMP 时，这里的数据接口（Sim）不用变。
    ///
    /// 关于中文：IMGUI 用的是内置动态字体，Windows 上通常会回退到系统字体渲染中文。
    /// 万一你的环境显示成方块，把 UseChineseLabels 取消勾选即可切到英文标签。
    /// </summary>
    public class HudOverlay : MonoBehaviour
    {
        public GameDirector Director;

        [Tooltip("关掉后 HUD 用英文标签（内置字体没有中文字形时用）")]
        public bool UseChineseLabels = true;

        [Tooltip("显示雷达")]
        public bool ShowRadar = true;

        [Tooltip("显示『威胁网格』调试视图：把 AI 眼里的危险度画出来")]
        public bool ShowThreatGrid = false;

        Texture2D _white;
        Texture2D _circle;
        GUIStyle _label;
        GUIStyle _labelRight;
        GUIStyle _big;
        GUIStyle _small;
        bool _stylesReady;

        float _fps;

        string T(string zh, string en) { return UseChineseLabels ? zh : en; }

        void Awake()
        {
            _white = MakeSolid(new Color(1f, 1f, 1f, 1f));
            _circle = MakeCircle(128, 3f);
        }

        void Update()
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _fps = Mathf.Lerp(_fps, 1f / dt, 0.1f);
        }

        /* ------------------------------------------------------- 素材生成 */

        static Texture2D MakeSolid(Color c)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        static Texture2D MakeCircle(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float r = size * 0.5f - thickness;
            float rIn = r - thickness;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - size * 0.5f + 0.5f;
                    float dy = y - size * 0.5f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= r ? (d >= rIn ? 1f : 0f) : 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        void EnsureStyles()
        {
            if (_stylesReady) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperLeft };
            _label.normal.textColor = new Color(0.88f, 0.95f, 1f);
            _labelRight = new GUIStyle(_label) { alignment = TextAnchor.UpperRight };
            _big = new GUIStyle(_label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(_label) { fontSize = 11 };
            _stylesReady = true;
        }

        /* ----------------------------------------------------------- 绘制 */

        void OnGUI()
        {
            if (Director == null || Director.Sim == null) return;
            EnsureStyles();

            Simulation sim = Director.Sim;
            PlaneState p = sim.Player;
            float w = Screen.width, h = Screen.height;

            DrawPlayerPanel(sim, p, 22f, h - 138f);
            DrawSituationPanel(sim, 22f, 22f);
            DrawScorePanel(sim, w - 240f, 22f);

            if (p != null && p.Alive)
            {
                if (ShowRadar) DrawRadar(sim, p, w - 130f, h - 130f, 96f);
                DrawThreatBar(Director.PlayerThreatLevel(), w - 226f, h - 30f, 192f, 9f);
            }

            DrawAlerts(sim, p, w, h);
            DrawCenterMark(w, h);
            DrawFooter(w, h);

            if (ShowThreatGrid) DrawThreatGridOverlay(sim, p);
            if (sim.State != MatchState.Playing) DrawEndPanel(sim, w, h);
        }

        void Rect_(float x, float y, float w, float h, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), _white);
            GUI.color = old;
        }

        void Bar(float x, float y, float w, float h, float ratio, Color fill)
        {
            Rect_(x, y, w, h, new Color(1f, 1f, 1f, 0.10f));
            Rect_(x, y, w * Mathf.Clamp01(ratio), h, fill);
            Rect_(x, y, w, 1f, new Color(1f, 1f, 1f, 0.22f));
            Rect_(x, y + h - 1f, w, 1f, new Color(0f, 0f, 0f, 0.25f));
        }

        void Panel(float x, float y, float w, float h)
        {
            Rect_(x, y, w, h, new Color(0.02f, 0.07f, 0.12f, 0.62f));
            Rect_(x, y, w, 1f, new Color(0.45f, 0.82f, 1f, 0.30f));
        }

        /* ------------------------------------------------------------ 面板 */

        void DrawPlayerPanel(Simulation sim, PlaneState p, float x, float y)
        {
            const float w = 300f, h = 112f;
            Panel(x, y, w, h);
            if (p == null) return;

            GUI.Label(new Rect(x + 12, y + 6, w - 24, 20), p.Def.Label, _label);
            GUI.Label(new Rect(x + 12, y + 6, w - 24, 20), T("剩余机体 ", "LIVES ") + sim.Lives, _labelRight);

            GUI.Label(new Rect(x + 12, y + 30, 36, 18), T("结构", "HP"), _small);
            float hp = p.HpRatio;
            Color hpColor = hp > 0.55f ? new Color(0.29f, 0.87f, 0.50f)
                          : hp > 0.25f ? new Color(0.98f, 0.75f, 0.14f)
                                       : new Color(0.94f, 0.27f, 0.27f);
            Bar(x + 52, y + 30, w - 130, 12, hp, hpColor);
            GUI.Label(new Rect(x + w - 76, y + 27, 64, 18), Mathf.CeilToInt(p.Hp) + "/" + (int)p.MaxHp, _labelRight);

            GUI.Label(new Rect(x + 12, y + 52, 40, 18), T("油门", "THR"), _small);
            Bar(x + 52, y + 52, w - 130, 12, p.Throttle, new Color(0.22f, 0.74f, 0.97f));
            GUI.Label(new Rect(x + w - 76, y + 49, 64, 18), Mathf.RoundToInt(p.Throttle * 100f) + "%", _labelRight);

            GUI.Label(new Rect(x + 12, y + 74, 40, 18), T("空速", "SPD"), _small);
            bool stall = p.IsStalling;
            Bar(x + 52, y + 74, w - 130, 12, p.Speed / Mathf.Max(1f, p.EffectiveMaxSpeed),
                stall ? new Color(0.94f, 0.27f, 0.27f) : new Color(0.63f, 0.90f, 1f));
            GUI.Label(new Rect(x + w - 76, y + 71, 64, 18),
                Mathf.RoundToInt(p.Speed) + (stall ? T(" 失速", " STALL") : ""), _labelRight);

            WeaponRuntime cannon = p.Weapon("cannon");
            if (cannon != null)
            {
                GUI.Label(new Rect(x + 12, y + 94, 40, 18), T("机炮", "GUN"), _small);
                float heat = Mathf.Clamp01(cannon.Heat / Mathf.Max(0.001f, cannon.Def.OverheatLock));
                Bar(x + 52, y + 94, 90, 8, heat, cannon.Overheated ? new Color(0.94f, 0.27f, 0.27f) : new Color(1f, 0.82f, 0.40f));
                if (cannon.Overheated) GUI.Label(new Rect(x + 146, y + 91, 50, 18), T("过热", "HOT"), _small);
            }

            WeaponRuntime mis = p.Weapon("missile");
            if (mis != null)
            {
                GUI.Label(new Rect(x + 196, y + 92, 40, 18), T("导弹", "AAM"), _small);
                string ammo = mis.IsInfinite ? "∞" : Mathf.FloorToInt(mis.Ammo).ToString();
                Color c = mis.CanFire ? new Color(0.49f, 1f, 0.75f) : new Color(0.62f, 0.72f, 0.80f);
                var st = new GUIStyle(_label) { fontSize = 15 };
                st.normal.textColor = c;
                GUI.Label(new Rect(x + 232, y + 88, 40, 22), ammo, st);
                if (!mis.CanFire) GUI.Label(new Rect(x + 258, y + 92, 44, 18), "-" + mis.Cooldown.ToString("F1"), _small);
            }

            if (p.Lock.Progress > 0f && !p.Lock.Locked)
            {
                GUI.Label(new Rect(x, y - 20, w, 18), T("导弹锁定中 ", "LOCKING ") +
                    Mathf.RoundToInt(p.Lock.Progress * 100f) + "%", _label);
            }
            else if (p.Lock.Locked)
            {
                var st = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
                st.normal.textColor = new Color(1f, 0.35f, 0.35f);
                GUI.Label(new Rect(x, y - 20, w, 18), T("已锁定", "LOCKED"), st);
            }
        }

        void DrawSituationPanel(Simulation sim, float x, float y)
        {
            const float w = 236f, h = 84f;
            Panel(x, y, w, h);

            GUI.Label(new Rect(x + 12, y + 6, w - 24, 20),
                T("第 " + sim.Wave + " / " + sim.Config.Spawn.MaxWaves + " 波",
                  "WAVE " + sim.Wave + " / " + sim.Config.Spawn.MaxWaves), _label);

            int alive = sim.EnemyCount;
            var st = new GUIStyle(_label);
            st.normal.textColor = alive > 0 ? new Color(0.98f, 0.82f, 0.35f) : new Color(0.49f, 1f, 0.75f);
            GUI.Label(new Rect(x + 12, y + 28, w - 24, 20),
                alive > 0 ? T("敌机 " + alive, "ENEMY " + alive) : T("空域净空", "CLEAR"), st);

            float prog = sim.WaveActive ? 1f
                : 1f - Mathf.Clamp01(sim.WaveTimer / Mathf.Max(0.01f, sim.Config.Spawn.BetweenWaves));
            Bar(x + 12, y + 54, w - 24, 6, prog,
                sim.WaveActive ? new Color(0.22f, 0.74f, 0.97f) : new Color(0.49f, 1f, 0.75f));

            GUI.Label(new Rect(x + 12, y + 62, w - 24, 18),
                sim.WaveActive ? T("交战中", "ENGAGED")
                               : T("下一波 " + Mathf.Max(0f, sim.WaveTimer).ToString("F1") + "s",
                                   "NEXT " + Mathf.Max(0f, sim.WaveTimer).ToString("F1") + "s"), _small);
        }

        void DrawScorePanel(Simulation sim, float x, float y)
        {
            const float w = 218f, h = 84f;
            Panel(x, y, w, h);

            GUI.Label(new Rect(x + 12, y + 6, 100, 18), T("得分", "SCORE"), _small);
            GUI.Label(new Rect(x + 12, y + 20, w - 24, 26), sim.Stats.Score.ToString("D6"), _big);

            GUI.Label(new Rect(x + 12, y + 50, w - 24, 18),
                T("击落 " + sim.Stats.Kills, "KILLS " + sim.Stats.Kills), _label);

            if (sim.Stats.Combo > 1)
            {
                var st = new GUIStyle(_label);
                st.normal.textColor = new Color(0.98f, 0.82f, 0.35f);
                GUI.Label(new Rect(x + 90, y + 50, 120, 18), T("连杀 ×" + sim.Stats.Combo, "COMBO x" + sim.Stats.Combo), st);
            }

            GUI.Label(new Rect(x + 12, y + 66, w - 24, 18),
                Mathf.RoundToInt(_fps) + " FPS   " + (Director.Paused ? T("已暂停", "PAUSED") : ""), _small);
        }

        void DrawThreatBar(float level, float x, float y, float w, float h)
        {
            Rect_(x, y, w, h, new Color(0.02f, 0.07f, 0.12f, 0.75f));
            Color c = Color.Lerp(new Color(0.29f, 0.87f, 0.50f), new Color(0.94f, 0.27f, 0.27f), level);
            Rect_(x, y, w * Mathf.Clamp01(level), h, c);
            GUI.Label(new Rect(x, y - 17, w, 16), T("威胁 " + Mathf.RoundToInt(level * 100f) + "%",
                "THREAT " + Mathf.RoundToInt(level * 100f) + "%"), _small);
        }

        /* ------------------------------------------------------------ 雷达 */

        void DrawRadar(Simulation sim, PlaneState p, float cx, float cy, float r)
        {
            // 底板 + 三档区间环
            Rect_(cx - r, cy - r, r * 2f, r * 2f, new Color(0.02f, 0.07f, 0.12f, 0.72f));

            float[] ranges = { 600f, 1400f, 2400f };
            float maxRange = ranges[ranges.Length - 1];
            float scale = r / maxRange;

            Color old = GUI.color;
            GUI.color = new Color(0.47f, 0.82f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2f, r * 2f), _circle);
            for (int i = 0; i < ranges.Length - 1; i++)
            {
                float rr = ranges[i] * scale;
                GUI.DrawTexture(new Rect(cx - rr, cy - rr, rr * 2f, rr * 2f), _circle);
            }
            GUI.color = old;

            // 十字
            Rect_(cx - r, cy, r * 2f, 1f, new Color(0.47f, 0.82f, 1f, 0.20f));
            Rect_(cx, cy - r, 1f, r * 2f, new Color(0.47f, 0.82f, 1f, 0.20f));

            // 扫描线
            float sweep = (Time.time * 1.5f) % MathUtil.TAU;
            float sx = cx + Mathf.Cos(sweep) * r;
            float sy = cy + Mathf.Sin(sweep) * r;
            DrawLine(cx, cy, sx, sy, new Color(0.55f, 1f, 0.82f, 0.55f), 1.5f);

            // 威胁目标
            for (int i = 0; i < sim.Planes.Count; i++)
            {
                PlaneState e = sim.Planes[i];
                if (!e.Alive || e == p) continue;
                float dx = (e.Pos.X - p.Pos.X) * scale;
                float dy = (e.Pos.Y - p.Pos.Y) * scale;
                if (dx * dx + dy * dy > r * r) continue;

                float threat = p.IsHostileTo(e)
                    ? ThreatAssessment.Assess(p, e, sim.Config.Threat, 0f, false).Value : 0f;
                Color c = !p.IsHostileTo(e) ? new Color(0.37f, 0.88f, 1f)
                    : threat > sim.Config.Threat.EvadeEnter ? new Color(1f, 0.18f, 0.18f)
                                                            : new Color(1f, 0.42f, 0.42f);
                float s = 5f + threat * 3f;
                Rect_(cx + dx - s * 0.5f, cy + dy - s * 0.5f, s, s, c);

                // 朝向短线
                DrawLine(cx + dx, cy + dy,
                    cx + dx + Mathf.Cos(e.Angle) * s * 1.8f,
                    cy + dy + Mathf.Sin(e.Angle) * s * 1.8f, c, 1f);
            }

            // 来袭导弹
            for (int i = 0; i < sim.Projectiles.Count; i++)
            {
                ProjectileState pr = sim.Projectiles[i];
                if (!pr.Alive || pr.Kind != WeaponKind.Missile) continue;
                float dx = (pr.Pos.X - p.Pos.X) * scale;
                float dy = (pr.Pos.Y - p.Pos.Y) * scale;
                if (dx * dx + dy * dy > r * r) continue;
                bool hostile = pr.Team != p.Team;
                Rect_(cx + dx - 3f, cy + dy - 3f, 6f, 6f,
                    hostile ? new Color(1f, 0.62f, 0.11f) : new Color(0.49f, 1f, 0.75f));
            }

            // 玩家（中心）
            Rect_(cx - 3f, cy - 3f, 6f, 6f, Color.white);

            GUI.Label(new Rect(cx - r, cy + r + 4f, r * 2f, 16f),
                ShowThreatGrid ? T("雷达 · F2 威胁网格开", "RADAR · THREAT GRID ON") : T("雷达", "RADAR"), _small);
        }

        void DrawLine(float x0, float y0, float x1, float y1, Color c, float width)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(x0, y0));
            Rect_(x0, y0 - width * 0.5f, len, width, c);
            GUI.matrix = saved;
        }

        /* -------------------------------------------------------- 调试层 */

        void DrawThreatGridOverlay(Simulation sim, PlaneState p)
        {
            if (sim.Threat == null || Director == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            ThreatField f = sim.Threat;
            float maxV = f.Config.MaxValue;
            float cell = f.Cell;

            for (int cy = 0; cy < f.Rows; cy++)
            {
                for (int cx = 0; cx < f.Cols; cx++)
                {
                    float v = f.RawValues[f.Index(cx, cy)];
                    if (v < 0.05f) continue;
                    float k = Mathf.Clamp01(v / maxV);

                    Vector3 wc = DogfightConst.PxToWorld(new Vec2((cx + 0.5f) * cell, (cy + 0.5f) * cell));
                    Vector3 sp = cam.WorldToScreenPoint(new Vector3(wc.x, wc.y, 0f));
                    if (sp.z < 0f) continue;

                    float size = cell / DogfightConst.PixelsPerUnit * (Screen.height / (cam.orthographicSize * 2f));
                    float sx = sp.x - size * 0.5f;
                    float sy = Screen.height - sp.y - size * 0.5f;
                    if (sx < -size || sy < -size || sx > Screen.width || sy > Screen.height) continue;

                    Rect_(sx, sy, size, size, new Color(1f, 0.3f + 0.5f * (1f - k), 0.25f, 0.06f + 0.30f * k));
                }
            }
        }

        /* ------------------------------------------------------------ 提示 */

        void DrawAlerts(Simulation sim, PlaneState p, float w, float h)
        {
            if (p == null) return;

            bool locked = false;
            for (int i = 0; i < sim.Projectiles.Count; i++)
            {
                ProjectileState pr = sim.Projectiles[i];
                if (pr.Alive && pr.Kind == WeaponKind.Missile && pr.TargetId == p.Id) { locked = true; break; }
            }

            if (locked)
            {
                var st = new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                st.normal.textColor = new Color(1f, 0.2f, 0.2f, 0.5f + 0.45f * Mathf.Sin(Time.time * 12f));
                GUI.Label(new Rect(0f, h * 0.16f, w, 30f), T("⚠ 导弹来袭", "MISSILE INCOMING"), st);
            }

            if (p.OutOfBounds)
            {
                var st = new GUIStyle(_label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                st.normal.textColor = new Color(1f, 0.74f, 0.24f, 0.6f + 0.3f * Mathf.Sin(Time.time * 9f));
                GUI.Label(new Rect(0f, h * 0.10f, w, 28f), T("◀ 脱离战场区域 · 立即返航 ▶", "OUT OF COMBAT ZONE"), st);
            }

            if (p.HpRatio < 0.4f && p.Alive)
            {
                float k = 1f - p.HpRatio / 0.4f;
                float pulse = 0.3f + 0.25f * Mathf.Sin(Time.time * 8f);
                Rect_(0f, 0f, w, 4f, new Color(1f, 0.1f, 0.1f, k * pulse));
                Rect_(0f, h - 4f, w, 4f, new Color(1f, 0.1f, 0.1f, k * pulse));
                Rect_(0f, 0f, 4f, h, new Color(1f, 0.1f, 0.1f, k * pulse));
                Rect_(w - 4f, 0f, 4f, h, new Color(1f, 0.1f, 0.1f, k * pulse));
            }
        }

        void DrawCenterMark(float w, float h)
        {
            float cx = w * 0.5f, cy = h * 0.5f;
            Color c = new Color(0.71f, 0.94f, 1f, 0.5f);
            Rect_(cx - 22f, cy, 10f, 1f, c);
            Rect_(cx + 12f, cy, 10f, 1f, c);
            Rect_(cx, cy - 22f, 1f, 10f, c);
            Rect_(cx, cy + 12f, 1f, 10f, c);
        }

        void DrawFooter(float w, float h)
        {
            var st = new GUIStyle(_small);
            st.normal.textColor = new Color(0.75f, 0.88f, 0.96f, 0.6f);
            GUI.Label(new Rect(22f, h - 20f, w - 44f, 18f),
                T("W/S 油门 · A/D 转向 · 空格 机炮 · Shift 导弹 · Ctrl 减速板 · V 切换操控 · P 暂停 · R 重开 · F1 无敌 · F2 威胁网格 · F3 云层",
                  "W/S throttle · A/D turn · Space gun · Shift missile · Ctrl brake · V scheme · P pause · R restart · F1 god · F2 threat grid · F3 clouds"), st);
        }

        void DrawEndPanel(Simulation sim, float w, float h)
        {
            float pw = 520f, ph = 300f;
            float x = (w - pw) * 0.5f, y = (h - ph) * 0.5f;
            Rect_(x, y, pw, ph, new Color(0.03f, 0.09f, 0.15f, 0.94f));
            Rect_(x, y, pw, 2f, new Color(0.47f, 0.82f, 1f, 0.6f));

            bool win = sim.State == MatchState.Victory;
            var title = new GUIStyle(_big) { fontSize = 30, alignment = TextAnchor.UpperCenter };
            title.normal.textColor = win ? new Color(0.49f, 1f, 0.75f) : new Color(1f, 0.48f, 0.48f);
            GUI.Label(new Rect(x, y + 24f, pw, 40f),
                win ? T("空域肃清 · 任务完成", "MISSION COMPLETE") : T("战机被击落", "SHOT DOWN"), title);

            var sub = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter };
            sub.normal.textColor = new Color(0.80f, 0.90f, 0.97f);
            GUI.Label(new Rect(x, y + 68f, pw, 22f),
                sim.Config.FindAiProfile(Director.AiProfileId).Label + T(" 档 · 第 ", " · WAVE ") + sim.Wave + T(" 波", ""), sub);

            string[,] rows = new string[,]
            {
                { T("最终得分", "SCORE"), sim.Stats.Score.ToString() },
                { T("击落敌机", "KILLS"), sim.Stats.Kills.ToString() },
                { T("命中率", "ACCURACY"), (sim.Stats.Accuracy * 100f).ToString("F1") + "%" },
                { T("最高连杀", "BEST COMBO"), "x" + sim.Stats.BestCombo },
                { T("承受伤害", "DAMAGE TAKEN"), Mathf.RoundToInt(sim.Stats.PlayerDamageTaken).ToString() },
                { T("存活时间", "TIME"), Mathf.FloorToInt(sim.Stats.Elapsed / 60f) + "m " +
                                        Mathf.FloorToInt(sim.Stats.Elapsed % 60f) + "s" },
            };

            for (int i = 0; i < rows.GetLength(0); i++)
            {
                float ry = y + 104f + i * 26f;
                GUI.Label(new Rect(x + 70f, ry, 200f, 22f), rows[i, 0], _label);
                GUI.Label(new Rect(x + pw - 270f, ry, 200f, 22f), rows[i, 1], _labelRight);
            }

            var hint = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter };
            hint.normal.textColor = new Color(0.49f, 1f, 0.75f, 0.6f + 0.4f * Mathf.Sin(Time.time * 4f));
            GUI.Label(new Rect(x, y + ph - 44f, pw, 22f), T("按 R 再来一局", "PRESS R TO RESTART"), hint);
        }
    }
}
