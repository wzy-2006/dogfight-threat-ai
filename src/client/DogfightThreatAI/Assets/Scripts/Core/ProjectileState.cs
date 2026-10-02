namespace Dogfight.Core
{
    /// <summary>
    /// 弹丸 / 导弹状态（纯数据）。
    /// 位置用「上一帧 → 当前帧」的线段做扫掠判定，避免高速弹在低帧率下穿透目标。
    /// </summary>
    public class ProjectileState
    {
        public int Id;
        public int OwnerId;
        public Team Team = Team.Neutral;
        public WeaponKind Kind = WeaponKind.Bullet;
        public WeaponDef Def;

        public Vec2 Pos;
        public Vec2 PrevPos;
        public Vec2 Vel;
        public float Angle;

        public float Life = 1f;
        public float Age;
        public bool Alive = true;
        public float Radius = 5f;
        public float Damage = 7f;
        public float Speed = 1000f;

        /// <summary>导弹锁定的目标 Id（-1 表示无制导）</summary>
        public int TargetId = -1;
        public float ArmTime;
        /// <summary>尾迹事件限流计时</summary>
        public float TrailTimer;

        public bool IsArmed { get { return Age >= ArmTime; } }

        public bool CanHit(PlaneState target, bool friendlyFire)
        {
            if (target == null || !target.Alive) return false;
            if (target.Id == OwnerId) return false;
            if (friendlyFire) return true;
            if (Team == Team.Neutral || target.Team == Team.Neutral) return true;
            return Team != target.Team;
        }
    }
}
