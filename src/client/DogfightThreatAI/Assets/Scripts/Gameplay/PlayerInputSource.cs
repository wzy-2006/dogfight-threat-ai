using Dogfight.Core;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 玩家输入 → PlaneIntent。用旧版 Input Manager（ProjectSettings 里
    /// activeInputHandler = 0），键位与 docs/04-manuals/用户手册.md 保持一致。
    ///
    /// 这里只产出意图，不直接改飞机状态；AI 走的是同一条通道。
    /// </summary>
    public class PlayerInputSource : MonoBehaviour
    {
        [Header("键位（旧版 Input Manager）")]
        public KeyCode ThrottleUp = KeyCode.W;
        public KeyCode ThrottleDown = KeyCode.S;
        public KeyCode TurnLeft = KeyCode.A;
        public KeyCode TurnRight = KeyCode.D;
        public KeyCode Fire = KeyCode.Space;
        public KeyCode Missile = KeyCode.LeftShift;
        public KeyCode Brake = KeyCode.LeftControl;

        [Header("操控方案")]
        [Tooltip("勾选后 W/A/S/D 直接给方向（街机），否则是油门+舵面的物理飞行")]
        public bool ArcadeScheme = false;

        [Tooltip("是否同时接受方向键")]
        public bool UseArrowKeys = true;

        /// <summary>目标油门（0..1），在物理飞行方案下缓慢变化</summary>
        public float ThrottleTarget = 0.7f;

        /// <summary>玩家是否仍然接管（结算/暂停时为 false）</summary>
        public bool ControlEnabled = true;

        public PlaneIntent Build(DogfightConfig cfg, float dt)
        {
            var intent = new PlaneIntent();

            if (!ControlEnabled)
            {
                intent.Throttle = ThrottleTarget;
                return intent;
            }

            bool up = Input.GetKey(ThrottleUp) || (UseArrowKeys && Input.GetKey(KeyCode.UpArrow));
            bool down = Input.GetKey(ThrottleDown) || (UseArrowKeys && Input.GetKey(KeyCode.DownArrow));
            bool left = Input.GetKey(TurnLeft) || (UseArrowKeys && Input.GetKey(KeyCode.LeftArrow));
            bool right = Input.GetKey(TurnRight) || (UseArrowKeys && Input.GetKey(KeyCode.RightArrow));

            if (ArcadeScheme)
            {
                float tx = (right ? 1f : 0f) - (left ? 1f : 0f);
                float ty = (up ? 1f : 0f) - (down ? 1f : 0f);

                // 街机方案：WASD 直接给航向，推满油门
                float want = Mathf.Atan2(ty, tx);
                float err = MathUtil.AngleDelta(PlayerAngleProbe, want);
                intent.Turn = Mathf.Clamp(err / 0.35f, -1f, 1f);
                intent.Throttle = (Mathf.Abs(tx) > 0.01f || Mathf.Abs(ty) > 0.01f) ? 1f : 0f;
                intent.Fire = Input.GetKey(Fire) || Input.GetMouseButton(0);
                intent.Missile = Input.GetKey(Missile) || Input.GetMouseButton(1);
                intent.Brake = Input.GetKey(Brake);
                return intent;
            }

            // 物理飞行方案：W/S 改油门目标，A/D 压杆
            float rate = up ? cfg.Physics.ThrottleUpRate : cfg.Physics.ThrottleDownRate;
            ThrottleTarget = Mathf.Clamp01(ThrottleTarget + ((up ? 1f : 0f) - (down ? 1f : 0f)) * rate * dt);

            intent.Throttle = ThrottleTarget;
            intent.Turn = (right ? 1f : 0f) - (left ? 1f : 0f);
            intent.Fire = Input.GetKey(Fire) || Input.GetMouseButton(0);
            intent.Missile = Input.GetKey(Missile) || Input.GetMouseButton(1);
            intent.Brake = Input.GetKey(Brake);
            return intent;
        }

        /// <summary>街机方案需要一个当前朝向来做比例控制，由 GameDirector 每帧写入</summary>
        public float PlayerAngleProbe;

        public void ResetThrottle(float value) { ThrottleTarget = value; }
    }
}
