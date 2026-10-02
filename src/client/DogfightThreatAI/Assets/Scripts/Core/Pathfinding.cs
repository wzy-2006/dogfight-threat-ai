using System.Collections.Generic;

namespace Dogfight.Core
{
    /// <summary>规划器输入</summary>
    public struct PlannerContext
    {
        public ThreatField Field;
        public PlaneState Self;
        public Vec2 Goal;
        public bool HasGoal;
        public AiProfile Profile;
    }

    /// <summary>
    /// 战场网格上的路径规划（立项书要求）：
    ///   BFS → 目标搜索与区域巡逻
    ///   A*  → 追击路径与低风险规避路径规划
    ///   遗传算法 → V2，已预留 PlannerRegistry 注册位
    ///
    /// 所有规划器统一签名：输入上下文，输出世界坐标航路点列表（可为 null 表示无解）。
    /// </summary>
    public static class Pathfinding
    {
        public delegate List<Vec2> PlannerFunc(PlannerContext ctx);

        static readonly Dictionary<string, PlannerFunc> _registry = new Dictionary<string, PlannerFunc>();

        public static void Register(string name, PlannerFunc fn) { _registry[name] = fn; }

        public static PlannerFunc Get(string name)
        {
            PlannerFunc f;
            if (name != null && _registry.TryGetValue(name, out f)) return f;
            if (_registry.TryGetValue("steering", out f)) return f;
            return null;
        }

        public static IEnumerable<string> RegisteredNames { get { return _registry.Keys; } }

        // 八个方向（含对角线，对角线代价 sqrt2）
        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] Dcost = { 1f, 1f, 1f, 1f, 1.41421f, 1.41421f, 1.41421f, 1.41421f };

        /// <summary>格子通行代价：基础 1 + 威胁惩罚</summary>
        static float CellCost(ThreatField f, int cx, int cy, float threatPenalty)
        {
            float v = f.RawValues[f.Index(cx, cy)] / f.Config.MaxValue;
            return 1f + MathUtil.Clamp01(v) * threatPenalty;
        }

        static List<Vec2> Reconstruct(ThreatField f, int[] cameFrom, int startIndex, int endIndex)
        {
            var path = new List<Vec2>();
            int cur = endIndex;
            int guard = 0;
            while (cur != -1 && cur != startIndex && guard++ < 8192)
            {
                int cx = cur % f.Cols;
                int cy = cur / f.Cols;
                path.Add(f.CellCenter(cx, cy));
                cur = cameFrom[cur];
            }
            path.Reverse();
            return path;
        }

        /* --------------------------------------------------------- A* */

        /// <summary>A* 规划。threatPenalty 越大越倾向于绕开高威胁区域。</summary>
        public static List<Vec2> AStar(ThreatField field, Vec2 start, Vec2 goal, float threatPenalty, int maxNodes)
        {
            if (field == null) return null;
            if (threatPenalty <= 0f) threatPenalty = 3f;
            if (maxNodes <= 0) maxNodes = 4000;

            int scx, scy, gcx, gcy;
            field.CellOf(start, out scx, out scy);
            field.CellOf(goal, out gcx, out gcy);
            int startIndex = field.Index(scx, scy);
            int goalIndex = field.Index(gcx, gcy);

            var direct = new List<Vec2>();
            if (startIndex == goalIndex)
            {
                direct.Add(goal);
                return direct;
            }

            int total = field.CellCount;
            var gScore = new float[total];
            var cameFrom = new int[total];
            var closed = new bool[total];
            for (int i = 0; i < total; i++) { gScore[i] = float.PositiveInfinity; cameFrom[i] = -1; }

            var open = new MinHeap();
            gScore[startIndex] = 0f;
            open.Push(startIndex, Heuristic(scx, scy, gcx, gcy));

            int expanded = 0;
            while (open.Count > 0 && expanded < maxNodes)
            {
                int cur = open.Pop();
                if (cur == goalIndex)
                {
                    List<Vec2> cells = Reconstruct(field, cameFrom, startIndex, goalIndex);
                    var full = new List<Vec2>();
                    full.Add(start);
                    full.AddRange(cells);
                    return SmoothPath(field, full);
                }
                if (closed[cur]) continue;
                closed[cur] = true;
                expanded++;

                int cx = cur % field.Cols;
                int cy = cur / field.Cols;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k];
                    int ny = cy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= field.Cols || ny >= field.Rows) continue;
                    int ni = field.Index(nx, ny);
                    if (closed[ni]) continue;

                    // 不允许穿对角线缝隙
                    if (Dx[k] != 0 && Dy[k] != 0)
                    {
                        if (field.RawValues[field.Index(cx + Dx[k], cy)] / field.Config.MaxValue > 0.98f) continue;
                        if (field.RawValues[field.Index(cx, cy + Dy[k])] / field.Config.MaxValue > 0.98f) continue;
                    }

                    float step = Dcost[k] * CellCost(field, nx, ny, threatPenalty);
                    float tentative = gScore[cur] + step;
                    if (tentative < gScore[ni])
                    {
                        gScore[ni] = tentative;
                        cameFrom[ni] = cur;
                        open.Push(ni, tentative + Heuristic(nx, ny, gcx, gcy));
                    }
                }
            }
            return null;
        }

        static float Heuristic(int ax, int ay, int bx, int by)
        {
            float dx = ax - bx, dy = ay - by;
            return (float)System.Math.Sqrt(dx * dx + dy * dy);
        }

        /* --------------------------------------------------------- BFS */

        public delegate bool CellPredicate(int cx, int cy, int index);

        /// <summary>BFS：找到最近的满足条件的格子并返回路径（用于搜索未侦察空域 / 区域巡逻）</summary>
        public static List<Vec2> BfsNearest(ThreatField field, Vec2 start, CellPredicate predicate, float blockThreshold, int maxNodes)
        {
            if (field == null || predicate == null) return null;
            if (maxNodes <= 0) maxNodes = 6000;
            if (blockThreshold <= 0f) blockThreshold = 0.85f;

            int scx, scy;
            field.CellOf(start, out scx, out scy);
            int startIndex = field.Index(scx, scy);
            int total = field.CellCount;

            var cameFrom = new int[total];
            var seen = new bool[total];
            var queue = new int[total];
            for (int i = 0; i < total; i++) cameFrom[i] = -1;

            int head = 0, tail = 0;
            queue[tail++] = startIndex;
            seen[startIndex] = true;
            int expanded = 0;

            while (head < tail && expanded < maxNodes)
            {
                int cur = queue[head++];
                expanded++;
                int ccx = cur % field.Cols;
                int ccy = cur / field.Cols;

                if (cur != startIndex && predicate(ccx, ccy, cur))
                {
                    return Reconstruct(field, cameFrom, startIndex, cur);
                }

                for (int k = 0; k < 8; k++)
                {
                    int nx = ccx + Dx[k];
                    int ny = ccy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= field.Cols || ny >= field.Rows) continue;
                    int ni = field.Index(nx, ny);
                    if (seen[ni]) continue;
                    if (field.RawValues[ni] / field.Config.MaxValue > blockThreshold) continue;
                    seen[ni] = true;
                    cameFrom[ni] = cur;
                    queue[tail++] = ni;
                }
            }
            return null;
        }

        /* ------------------------------------------------------ 工具 */

        /// <summary>航路点抽稀：去掉近似共线的中间点，减轻转向抖动</summary>
        public static List<Vec2> SmoothPath(ThreatField field, List<Vec2> pts)
        {
            if (pts == null) return null;
            if (pts.Count <= 2)
            {
                var simple = new List<Vec2>();
                for (int i = 1; i < pts.Count; i++) simple.Add(pts[i]);
                return simple;
            }

            var outPts = new List<Vec2>();
            outPts.Add(pts[pts.Count - 1]);
            int anchor = pts.Count - 1;
            for (int k = 0; k < pts.Count - 1; k++)
            {
                if (LineOfSight(field, pts[k], pts[anchor], 0.8f))
                {
                    outPts.Add(pts[k]);
                    anchor = k;
                }
            }
            outPts.Reverse();
            return outPts;
        }

        /// <summary>两点之间是否「低威胁直线可达」</summary>
        public static bool LineOfSight(ThreatField field, Vec2 a, Vec2 b, float threshold)
        {
            if (threshold <= 0f) threshold = 0.8f;
            float dist = Vec2.Distance(a, b);
            int steps = (int)(dist / (field.Cell * 0.6f));
            if (steps < 2) steps = 2;
            for (int i = 1; i < steps; i++)
            {
                float t = (float)i / steps;
                Vec2 p = a + (b - a) * t;
                if (field.Sample(p) / field.Config.MaxValue > threshold) return false;
            }
            return true;
        }

        /* -------------------------------------------- 内置规划器实现 */

        /// <summary>直接转向：不寻路，朝目标点飞（新兵档用的默认规划器）</summary>
        public static List<Vec2> PlanSteering(PlannerContext ctx)
        {
            if (!ctx.HasGoal) return null;
            var list = new List<Vec2>(1);
            list.Add(ctx.Goal);
            return list;
        }

        /// <summary>A* 规划器</summary>
        public static List<Vec2> PlanAStar(PlannerContext ctx)
        {
            if (!ctx.HasGoal || ctx.Field == null || ctx.Self == null) return null;
            return AStar(ctx.Field, ctx.Self.Pos, ctx.Goal, 3.5f, 4000);
        }

        /// <summary>BFS 规划器：找最近的未侦察格子</summary>
        public static List<Vec2> PlanBfs(PlannerContext ctx)
        {
            if (ctx.Field == null || ctx.Self == null) return null;
            return BfsNearest(ctx.Field, ctx.Self.Pos,
                delegate (int cx, int cy, int index) { return ctx.Field.RawScanned[index] < 0.25f; },
                0.80f, 6000);
        }

        /// <summary>注册内置规划器。V2 接入遗传算法只需 Pathfinding.Register("ga", fn)。</summary>
        public static void RegisterBuiltIns()
        {
            Register("steering", PlanSteering);
            Register("astar", PlanAStar);
            Register("bfs", PlanBfs);
        }
    }

    /// <summary>最小二叉堆（A* 的开放列表）</summary>
    internal class MinHeap
    {
        struct Node { public int Index; public float F; }
        Node[] _a = new Node[256];
        int _count;

        public int Count { get { return _count; } }

        public void Push(int index, float f)
        {
            if (_count >= _a.Length) System.Array.Resize(ref _a, _a.Length * 2);
            int i = _count++;
            _a[i].Index = index;
            _a[i].F = f;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_a[parent].F <= _a[i].F) break;
                Node tmp = _a[parent]; _a[parent] = _a[i]; _a[i] = tmp;
                i = parent;
            }
        }

        public int Pop()
        {
            int top = _a[0].Index;
            _count--;
            if (_count > 0)
            {
                _a[0] = _a[_count];
                int i = 0;
                for (;;)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < _count && _a[l].F < _a[m].F) m = l;
                    if (r < _count && _a[r].F < _a[m].F) m = r;
                    if (m == i) break;
                    Node tmp = _a[m]; _a[m] = _a[i]; _a[i] = tmp;
                    i = m;
                }
            }
            return top;
        }
    }
}
