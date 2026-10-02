using System;
using Dogfight.Core;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 程序化地表参数。图块编号是**实测**出来的：
    /// Kenney 这套图块为草地岛与沙地岛各提供了完整的 8 向边缘砖，
    /// 所以直接按「水在哪一侧」选砖即可，不需要靠旋转去凑。
    /// </summary>
    [Serializable]
    public class TerrainSettings
    {
        public int TextureSize = 1024;
        public int TileSize = 16;
        public int Seed = 20260606;

        [Tooltip("纯浅海（实测 #DFF6F5，100% 单色）")]
        public int SeaTile = 42;

        // 草地岛
        public int GrassSolid = 50, GrassN = 38, GrassS = 62, GrassW = 49, GrassE = 51;
        public int GrassNW = 37, GrassNE = 39, GrassSW = 61, GrassSE = 63;

        // 沙地岛
        public int DesertSolid = 56, DesertN = 44, DesertS = 68, DesertW = 55, DesertE = 57;
        public int DesertNW = 43, DesertNE = 45, DesertSW = 67, DesertSE = 69;

        [Tooltip("草地上的点缀（树 / 房子）。四角都是草绿，可直接叠放")]
        public int[] GrassProps = { 36, 48, 72, 84 };
        [Tooltip("沙地上的点缀（松树）。四角都是棕土")]
        public int[] DesertProps = { 54, 66 };

        [Range(0f, 0.2f)] public float PropChance = 0.030f;
        public int Islands = 8;
        public float IslandMinRadius = 5f;
        public float IslandMaxRadius = 13f;
        public int DesertPatches = 5;
        public int SmoothPasses = 2;
    }

    /// <summary>
    /// 运行时把 Kenney 图块拼成一张**无缝**地表贴图（对应立项书「多层视差空战场景」）。
    ///
    /// 无缝性：岛屿掩码、边缘判定、点缀位置全部按 N 取模环绕，
    /// 因此产出的贴图可以直接 wrapMode = Repeat 无限平铺。
    ///
    /// 全部失败路径都会返回 null，由调用方退化成纯色海面 —— 宁可少一层景观，也不能崩。
    /// </summary>
    public static class TerrainTextureFactory
    {
        public static Texture2D BuildTerrain(SpriteBank bank, TerrainSettings s)
        {
            if (bank == null || s == null) return null;

            int S = s.TextureSize;
            int TS = s.TileSize;
            int N = S / TS;
            if (N <= 0) return null;

            try
            {
                var land = new bool[N * N];
                var sandy = new bool[N * N];
                var rng = new MathUtil.Rng((uint)s.Seed);

                // --- 1. 岛屿：几块圆盘叠出不规则形状 ---
                for (int i = 0; i < s.Islands; i++)
                {
                    float cx = rng.Range(0f, N), cy = rng.Range(0f, N);
                    float baseR = rng.Range(s.IslandMinRadius, s.IslandMaxRadius);
                    int blobs = rng.RangeInt(2, 5);
                    for (int b = 0; b < blobs; b++)
                    {
                        float ox = cx + rng.Range(-baseR * 0.7f, baseR * 0.7f);
                        float oy = cy + rng.Range(-baseR * 0.7f, baseR * 0.7f);
                        float r = baseR * rng.Range(0.55f, 1.0f);
                        StampCircle(land, N, ox, oy, r);
                    }
                }
                for (int p = 0; p < s.SmoothPasses; p++) land = Smooth(land, N);

                // --- 2. 沙地斑块（只落在陆地上） ---
                for (int i = 0; i < s.DesertPatches; i++)
                {
                    float cx = rng.Range(0f, N), cy = rng.Range(0f, N);
                    float r = rng.Range(3.5f, 8f);
                    StampCircleMasked(sandy, land, N, cx, cy, r);
                }
                for (int p = 0; p < s.SmoothPasses; p++)
                {
                    sandy = Smooth(sandy, N);
                    for (int i = 0; i < sandy.Length; i++) if (!land[i]) sandy[i] = false;
                }

                // --- 3. 铺砖 ---
                Color32[] px = new Color32[S * S];
                Color32[] seaPx = bank.ReadTilePixels(s.SeaTile);
                if (seaPx == null) return null;   // 贴图不可读，交给调用方退化

                if (seaPx.Length < TS * TS) return null;

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int tileIndex;
                        if (!At(land, N, x, y))
                        {
                            tileIndex = s.SeaTile;
                        }
                        else
                        {
                            bool desert = At(sandy, N, x, y);
                            bool wU = !At(land, N, x, y + 1);
                            bool wD = !At(land, N, x, y - 1);
                            bool wL = !At(land, N, x - 1, y);
                            bool wR = !At(land, N, x + 1, y);

                            if (wU && wL) tileIndex = desert ? s.DesertNW : s.GrassNW;
                            else if (wU && wR) tileIndex = desert ? s.DesertNE : s.GrassNE;
                            else if (wD && wR) tileIndex = desert ? s.DesertSE : s.GrassSE;
                            else if (wD && wL) tileIndex = desert ? s.DesertSW : s.GrassSW;
                            else if (wU) tileIndex = desert ? s.DesertN : s.GrassN;
                            else if (wR) tileIndex = desert ? s.DesertE : s.GrassE;
                            else if (wD) tileIndex = desert ? s.DesertS : s.GrassS;
                            else if (wL) tileIndex = desert ? s.DesertW : s.GrassW;
                            else tileIndex = desert ? s.DesertSolid : s.GrassSolid;
                        }

                        Color32[] src = bank.ReadTilePixels(tileIndex);
                        if (src == null || src.Length < TS * TS) src = seaPx;
                        Blit(px, S, x * TS, y * TS, TS, src);
                    }
                }

                // --- 4. 点缀（只放在实心格上，避免盖住沙滩边缘） ---
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        if (!At(land, N, x, y)) continue;
                        if (!At(land, N, x, y + 1) || !At(land, N, x, y - 1) ||
                            !At(land, N, x - 1, y) || !At(land, N, x + 1, y)) continue;
                        if (!rng.Chance(s.PropChance)) continue;

                        int[] pool = At(sandy, N, x, y) ? s.DesertProps : s.GrassProps;
                        if (pool == null || pool.Length == 0) continue;
                        Color32[] src = bank.ReadTilePixels(pool[rng.RangeInt(0, pool.Length)]);
                        if (src == null || src.Length < TS * TS) continue;
                        Blit(px, S, x * TS, y * TS, TS, src);
                    }
                }

                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                tex.name = "DogfightTerrain";
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.SetPixels32(px);
                tex.Apply(false, false);
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[空战] 程序化地表生成失败，退化为纯色背景：" + e.Message);
                return null;
            }
        }

        /// <summary>云层贴图：柔和程序化云朵，按 ±S 偏移重复绘制保证无缝</summary>
        public static Texture2D BuildClouds(int size, int count, int seed, Color color)
        {
            try
            {
                var rng = new MathUtil.Rng((uint)seed);
                var px = new Color32[size * size];
                Color32 clear = new Color32(0, 0, 0, 0);
                for (int i = 0; i < px.Length; i++) px[i] = clear;

                for (int i = 0; i < count; i++)
                {
                    float cx = rng.Range(0f, size), cy = rng.Range(0f, size);
                    // 云团别太大：一整张贴图铺满屏幕时，半径超过 0.15 就会连成一片雾
                    float r = rng.Range(size * 0.05f, size * 0.15f);
                    int puffs = rng.RangeInt(5, 11);
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            for (int p = 0; p < puffs; p++)
                            {
                                float a = rng.Range(0f, MathUtil.TAU);
                                float d = rng.Range(0f, r * 0.75f);
                                float px0 = cx + (float)Math.Cos(a) * d + ox * size;
                                float py0 = cy + (float)Math.Sin(a) * d + oy * size;
                                float pr = r * rng.Range(0.35f, 0.70f);
                                BlendDisc(px, size, px0, py0, pr, color);
                            }
                        }
                    }
                }

                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.name = "DogfightClouds";
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.SetPixels32(px);
                tex.Apply(false, false);
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[空战] 云层生成失败：" + e.Message);
                return null;
            }
        }

        /* ---------------------------------------------------------- 工具 */

        static bool At(bool[] mask, int n, int x, int y)
        {
            int wx = ((x % n) + n) % n;
            int wy = ((y % n) + n) % n;
            return mask[wy * n + wx];
        }

        static void StampCircle(bool[] mask, int n, float cx, float cy, float r)
        {
            int R = (int)Math.Ceiling(r);
            for (int dy = -R; dy <= R; dy++)
            {
                for (int dx = -R; dx <= R; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int wx = ((int)Math.Floor(cx + dx) % n + n) % n;
                    int wy = ((int)Math.Floor(cy + dy) % n + n) % n;
                    mask[wy * n + wx] = true;
                }
            }
        }

        static void StampCircleMasked(bool[] mask, bool[] limit, int n, float cx, float cy, float r)
        {
            int R = (int)Math.Ceiling(r);
            for (int dy = -R; dy <= R; dy++)
            {
                for (int dx = -R; dx <= R; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int wx = ((int)Math.Floor(cx + dx) % n + n) % n;
                    int wy = ((int)Math.Floor(cy + dy) % n + n) % n;
                    if (limit[wy * n + wx]) mask[wy * n + wx] = true;
                }
            }
        }

        /// <summary>去掉孤立点与 1 格宽的地峡，填掉小洞，让岛形更自然</summary>
        static bool[] Smooth(bool[] mask, int n)
        {
            var outMask = new bool[mask.Length];
            Array.Copy(mask, outMask, mask.Length);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (At(mask, n, x + dx, y + dy)) count++;
                        }
                    }
                    bool v = At(mask, n, x, y);
                    if (v && count <= 3) outMask[y * n + x] = false;
                    else if (!v && count >= 6) outMask[y * n + x] = true;
                }
            }
            return outMask;
        }

        static void Blit(Color32[] dst, int dstW, int ox, int oy, int size, Color32[] src)
        {
            for (int y = 0; y < size; y++)
            {
                int dRow = (oy + y) * dstW + ox;
                int sRow = y * size;
                for (int x = 0; x < size; x++) dst[dRow + x] = src[sRow + x];
            }
        }

        /// <summary>
        /// 把一团柔和的圆斑叠加到 RGBA 缓冲上（标准 source-over）。
        ///
        /// ⚠️ 这里必须把颜色**除以 outA** 再写回。
        /// Texture2D 用的 Color32 是**非预乘**的 straight alpha：RGB 表示"完全不含透明度"的颜色。
        /// 早期版本算了 outA 却忘了除，等于把预乘颜色写进了非预乘字段，
        /// 结果云雾边缘整体偏暗发灰，看上去像一层脏雾（截图里海面上的灰白麻点就是它）。
        /// </summary>
        static void BlendDisc(Color32[] px, int size, float cx, float cy, float r, Color color)
        {
            int R = (int)Math.Ceiling(r);
            for (int dy = -R; dy <= R; dy++)
            {
                for (int dx = -R; dx <= R; dx++)
                {
                    float d2 = dx * dx + dy * dy;
                    if (d2 > r * r) continue;
                    float t = 1f - (float)Math.Sqrt(d2) / r;   // 中心 1 → 边缘 0
                    float a = t * t * color.a;
                    if (a <= 0.002f) continue;
                    int x = (int)Math.Floor(cx + dx);
                    int y = (int)Math.Floor(cy + dy);
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int i = y * size + x;
                    Color32 c = px[i];
                    float na = c.a / 255f;
                    float outA = a + na * (1f - a);
                    if (outA <= 1e-5f) continue;

                    float inv = 1f / outA;                     // ← 关键：还原成非预乘颜色
                    px[i] = new Color32(
                        (byte)Mathf.Clamp((color.r * 255f * a + c.r * na * (1f - a)) * inv, 0f, 255f),
                        (byte)Mathf.Clamp((color.g * 255f * a + c.g * na * (1f - a)) * inv, 0f, 255f),
                        (byte)Mathf.Clamp((color.b * 255f * a + c.b * na * (1f - a)) * inv, 0f, 255f),
                        (byte)Mathf.Clamp(outA * 255f, 0f, 255f));
                }
            }
        }
    }
}
