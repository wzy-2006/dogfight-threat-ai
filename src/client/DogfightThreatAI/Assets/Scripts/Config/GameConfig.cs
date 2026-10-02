using Dogfight.Core;
using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 游戏配置资源（ScriptableObject）。挂到 Assets/Resources/Dogfight/GameConfig.asset。
    ///
    /// 设计取舍：**不**把整个 DogfightConfig 丢给 Unity 序列化。
    /// 因为配置里有 Dictionary（弹匣容量、装填时间），Unity 序列化不了，
    /// 硬序列化会在 Inspector 里看不到、还会被静默重置。
    /// 所以这里只暴露真正需要手调的旋钮，其余全部来自 ConfigDefaults（唯一的数值来源）。
    /// 这样既满足立项书「玩家可手动调节 AI 算法参数」，又不会埋序列化坑。
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "空战/游戏配置", order = 0)]
    public class GameConfig : ScriptableObject
    {
        [Header("开局")]
        [Tooltip("默认 AI 档位：rookie / veteran / ace")]
        public string DefaultAiProfile = "veteran";

        [Tooltip("战场尺寸（px）。视图按 PPU=32 换算成世界单位")]
        public float WorldWidth = 3600f;
        public float WorldHeight = 2600f;

        [Header("AI 手感微调（全局倍率，会叠加到所选档位上）")]
        [Tooltip("反应时间倍率：<1 更灵敏，>1 更迟钝")]
        [Range(0.2f, 3f)] public float AiReactionScale = 1f;

        [Tooltip("额外瞄准误差（弧度）：调大 AI 更打不准")]
        [Range(0f, 0.5f)] public float AiAimErrorBonus = 0f;

        [Tooltip("开火克制倍率：<1 更爱乱开枪，>1 更沉稳")]
        [Range(0.1f, 2f)] public float AiTriggerScale = 1f;

        [Tooltip("规避阈值偏移：调小 AI 更早规避，调大更莽")]
        [Range(-0.3f, 0.3f)] public float AiEvadeThresholdOffset = 0f;

        [Header("玩家")]
        [Tooltip("玩家机体数量（命）")]
        [Range(1, 9)] public int PlayerLives = 3;

        [Tooltip("无敌模式，仅调试用")]
        public bool GodMode = false;

        [Header("画面")]
        [Tooltip("多层视差背景")]
        public bool EnableParallax = true;

        [Tooltip("运行时生成程序化地表（需要贴图勾选 Read/Write；失败会退化为纯色海面）")]
        public bool EnableGeneratedTerrain = true;

        /// <summary>按当前 Inspector 设置生成一份完整的仿真配置</summary>
        public DogfightConfig Build()
        {
            DogfightConfig c = ConfigDefaults.Create();

            c.World.Width = WorldWidth;
            c.World.Height = WorldHeight;
            c.Spawn.PlayerLives = PlayerLives;

            for (int i = 0; i < c.AiProfiles.Count; i++)
            {
                AiProfile p = c.AiProfiles[i];
                p.ReactionTime = Mathf.Clamp(p.ReactionTime * AiReactionScale, 0.02f, 2f);
                p.AimError = Mathf.Clamp(p.AimError + AiAimErrorBonus, 0f, 0.6f);
                p.TriggerDiscipline = Mathf.Clamp(p.TriggerDiscipline * AiTriggerScale, 0.05f, 1f);
                p.EvadeThreshold = Mathf.Clamp(p.EvadeThreshold + AiEvadeThresholdOffset, 0.1f, 0.95f);
            }

            return c;
        }
    }
}
