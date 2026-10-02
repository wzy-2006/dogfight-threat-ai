using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>
    /// 战场威胁网格（立项书的「网格化战场建模」）。
    ///
    /// 把战场切成 Cell×Cell 的格子，每帧把敌机、来袭弹丸、导弹与边界风险
    /// 按半径衰减累加进去，形成一张实时的「危险度地图」；
    /// 提供双线性采样与梯度，供 AI 的规避与寻路使用。
    ///
    /// 纯 C#，可在 Unity 之外独立测试。
    /// </summary>
    public class ThreatField
    {
        public readonly int Cols;
        public readonly int Rows;
        public readonly float Cell;
        public readonly float WorldWidth;
        public readonly float WorldHeight;

        readonly float[] _values;
        readonly float[] _scanned;
        readonly ThreatGridConfig _cfg;

        public ThreatGridConfig Config { get { return _cfg; } }
        public float[] RawValues { get { return _values; } }
        public float[] RawScanned { get { return _scanned; } }
        public int CellCount { get { return Cols * Rows; } }

        public ThreatField(float worldWidth, float worldHeight, ThreatGridConfig cfg)
        {
            _cfg = cfg;
            Cell = cfg.Cell > 1f ? cfg.Cell : 100f;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            Cols = (int)(worldWidth / Cell) + 1;
            Rows = (int)(worldHeight / Cell) + 1;
            _values = new float[Cols * Rows];
            _scanned = new float[Cols * Rows];
        }

        public int Index(int cx, int cy) { return cy * Cols + cx; }

        public void CellOf(Vec2 p, out int cx, out int cy)
        {
            cx = MathUtil.ClampInt((int)(p.X / Cell), 0, Cols - 1);
            cy = MathUtil.ClampInt((int)(p.Y / Cell), 0, Rows - 1);
        }

        public Vec2 CellCenter(int cx, int cy)
        {
            return new Vec2((cx + 0.5f) * Cell, (cy + 0.5f) * Cell);
        }

        public void Clear()
        {
            System.Array.Clear(_values, 0, _values.Length);
            System.Array.Clear(_scanned, 0, _scanned.Length);
        }

        /// <summary>每帧衰减</summary>
        public void Decay(float dt)
        {
            float k = (float)System.Math.Exp(-_cfg.Decay * dt);
            for (int i = 0; i < _values.Length; i++) _values[i] *= k;
            float ks = (float)System.Math.Exp(-0.12f * dt);
            for (int i = 0; i < _scanned.Length; i++) _scanned[i] *= ks;
        }

        /// <summary>累加一个威胁源（只遍历半径内的格子，保持 O(面积)）</summary>
        public void AddSource(Vec2 p, float radius, float weight, float dt)
        {
            if (radius <= 0f || weight <= 0f) return;
            float r2 = radius * radius;
            int minCx = MathUtil.ClampInt((int)((p.X - radius) / Cell), 0, Cols - 1);
            int maxCx = MathUtil.ClampInt((int)((p.X + radius) / Cell), 0, Cols - 1);
            int minCy = MathUtil.ClampInt((int)((p.Y - radius) / Cell), 0, Rows - 1);
            int maxCy = MathUtil.ClampInt((int)((p.Y + radius) / Cell), 0, Rows - 1);

            float scale = weight * dt * 60f;
            float maxV = _cfg.MaxValue;
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    Vec2 c = CellCenter(cx, cy);
                    float dx = c.X - p.X, dy = c.Y - p.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;
                    float falloff = 1f - (float)System.Math.Sqrt(d2) / radius;
                    int i = Index(cx, cy);
                    float v = _values[i] + scale * falloff;
                    _values[i] = v > maxV ? maxV : v;
                }
            }
        }

        /// <summary>
        /// 由世界状态重建本帧威胁。
        /// 立项书要求「每帧依据敌机位置、弹幕密度和导弹追踪状态计算综合威胁值」。
        /// </summary>
        public void Refresh(IList<PlaneState> planes, IList<ProjectileState> projectiles, float dt)
        {
            Decay(dt);

            if (planes != null)
            {
                for (int i = 0; i < planes.Count; i++)
                {
                    PlaneState p = planes[i];
                    if (p == null || !p.Alive || p.IsPlayer || p.Team == Team.Player) continue;
                    AddSource(p.Pos, _cfg.EnemyRadius, _cfg.EnemyWeight, dt);
                }
            }

            if (projectiles != null)
            {
                for (int i = 0; i < projectiles.Count; i++)
                {
                    ProjectileState pr = projectiles[i];
                    if (pr == null || !pr.Alive) continue;
                    if (pr.Kind == WeaponKind.Missile) AddSource(pr.Pos, _cfg.MissileRadius, _cfg.MissileWeight, dt);
                    else AddSource(pr.Pos, _cfg.BulletRadius, _cfg.BulletWeight, dt);
                }
            }

            // 边界风险：贴边等于失去机动空间
            float bw = _cfg.BoundaryWeight, br = _cfg.BoundaryRadius;
            for (float x = 0f; x <= WorldWidth; x += br)
            {
                AddSource(new Vec2(x, 0f), br, bw, dt);
                AddSource(new Vec2(x, WorldHeight), br, bw, dt);
            }
            for (float y = 0f; y <= WorldHeight; y += br)
            {
                AddSource(new Vec2(0f, y), br, bw, dt);
                AddSource(new Vec2(WorldWidth, y), br, bw, dt);
            }
        }

        /// <summary>世界坐标处威胁（双线性采样，比最近邻平滑，避免 AI 抖动）</summary>
        public float Sample(Vec2 p)
        {
            float fx = p.X / Cell - 0.5f;
            float fy = p.Y / Cell - 0.5f;
            int x0 = (int)System.Math.Floor(fx);
            int y0 = (int)System.Math.Floor(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            float a = GetClamped(x0, y0) * (1f - tx) + GetClamped(x0 + 1, y0) * tx;
            float b = GetClamped(x0, y0 + 1) * (1f - tx) + GetClamped(x0 + 1, y0 + 1) * tx;
            return a * (1f - ty) + b * ty;
        }

        float GetClamped(int cx, int cy)
        {
            return _values[Index(MathUtil.ClampInt(cx, 0, Cols - 1), MathUtil.ClampInt(cy, 0, Rows - 1))];
        }

        /// <summary>归一化威胁 0..1</summary>
        public float Normalized(Vec2 p)
        {
            return MathUtil.Clamp01(Sample(p) / _cfg.MaxValue);
        }

        /// <summary>威胁梯度：AI 规避时沿梯度下降方向飞</summary>
        public Vec2 Gradient(Vec2 p, float h)
        {
            if (h <= 0f) h = Cell * 0.5f;
            float gx = (Sample(new Vec2(p.X + h, p.Y)) - Sample(new Vec2(p.X - h, p.Y))) / (2f * h);
            float gy = (Sample(new Vec2(p.X, p.Y + h)) - Sample(new Vec2(p.X, p.Y - h))) / (2f * h);
            return new Vec2(gx, gy);
        }

        /// <summary>标记侦察覆盖（BFS 区域巡逻用）</summary>
        public void MarkScanned(Vec2 p, float radius)
        {
            int cx0, cy0;
            CellOf(p, out cx0, out cy0);
            int r = (int)(radius / Cell) + 1;
            for (int cy = cy0 - r; cy <= cy0 + r; cy++)
            {
                for (int cx = cx0 - r; cx <= cx0 + r; cx++)
                {
                    if (cx < 0 || cy < 0 || cx >= Cols || cy >= Rows) continue;
                    int i = Index(cx, cy);
                    float v = _scanned[i] + 0.05f;
                    _scanned[i] = v > 1f ? 1f : v;
                }
            }
        }
    }
}
