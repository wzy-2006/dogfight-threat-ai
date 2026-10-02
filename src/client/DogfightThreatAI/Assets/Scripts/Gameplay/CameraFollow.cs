using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 相机跟随 + 速度前瞻 + 屏幕抖动。
    ///
    /// 刻意不依赖 Rigidbody2D：目标位置与速度都由 GameDirector 从仿真状态写入，
    /// 这样相机只看「逻辑状态」，不掺 Unity 物理，行为可预测。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Tooltip("跟随平滑系数，越大跟得越紧")]
        public float FollowLerp = 6f;

        [Tooltip("按速度方向前瞻比例")]
        public float LookAhead = 0.30f;

        [Tooltip("前瞻距离上限（世界单位）")]
        public float LookAheadMax = 7f;

        [Tooltip("抖动衰减")]
        public float ShakeDecay = 7f;

        /// <summary>目标位置（世界单位），由 GameDirector 每帧写入</summary>
        public Vector3 TargetPosition;

        /// <summary>目标速度（世界单位/秒），用于前瞻</summary>
        public Vector2 TargetVelocity;

        [Tooltip("正交相机半高（世界单位）")]
        public float OrthoSize = 14f;

        Camera _cam;
        float _shake;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam != null)
            {
                _cam.orthographic = true;
                _cam.orthographicSize = OrthoSize;
            }
        }

        public void SnapTo(Vector3 p)
        {
            TargetPosition = p;
            transform.position = new Vector3(p.x, p.y, transform.position.z);
        }

        public void Shake(float amount)
        {
            _shake = Mathf.Min(_shake + amount, 40f);
        }

        public void LateUpdate()
        {
            Vector3 want = TargetPosition;
            if (TargetVelocity.sqrMagnitude > 0.0001f)
            {
                float la = Mathf.Clamp(TargetVelocity.magnitude * LookAhead, 0f, LookAheadMax);
                Vector2 dir = TargetVelocity.normalized * la;
                want.x += dir.x;
                want.y += dir.y;
            }

            Vector3 cur = transform.position;
            Vector3 next = Vector3.Lerp(cur, new Vector3(want.x, want.y, cur.z),
                1f - Mathf.Exp(-FollowLerp * Time.deltaTime));

            _shake = Mathf.Lerp(_shake, 0f, 1f - Mathf.Exp(-ShakeDecay * Time.deltaTime));
            if (_shake > 0.02f)
            {
                next.x += Random.Range(-1f, 1f) * _shake * 0.01f;
                next.y += Random.Range(-1f, 1f) * _shake * 0.01f;
            }

            transform.position = next;
        }
    }
}
