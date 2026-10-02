using UnityEngine;

namespace Dogfight.Unity
{
    /// <summary>
    /// 引导器。两种用法：
    ///   ① 什么都不用做 —— 打开任意场景按 Play，会自动建好相机 / 背景 / 战斗机（见 AutoCreate）；
    ///   ② 想要显式控制，就把本组件挂在一个空物体上，或执行菜单「空战 / ③ 一键搭建」。
    ///
    /// 之所以做自动引导：这是一个原型工程，最怕的是「别人 clone 下来打开发现跑不起来」。
    /// 自动建场景可以把「配置素材 / 挂脚本 / 拖引用」这些容易出错的步骤全部省掉。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("不做任何配置，直接开局")]
        public bool StartImmediately = true;

        [Tooltip("AI 档位：rookie / veteran / ace")]
        public string AiProfileId = "veteran";

        [Tooltip("固定随机种子（0 = 每局随机）")]
        public int Seed = 20260606;

        public static GameDirector Current { get; private set; }

        /// <summary>
        /// 全局开关：不想让任意场景自动开局，就把它设成 false。
        /// （放在静态字段上是因为 RuntimeInitializeOnLoadMethod 拿不到 Inspector 值。）
        /// </summary>
        public static bool AutoCreateInAnyScene = true;

        void Awake()
        {
            EnsureGame();
        }

        void EnsureGame()
        {
            if (Current != null) return;

            GameDirector director = FindObjectOfType<GameDirector>();
            if (director == null)
            {
                var go = new GameObject("Dogfight");
                director = go.AddComponent<GameDirector>();
            }

            director.AiProfileId = AiProfileId;
            director.Seed = Seed > 0 ? (uint)Seed : (uint)Random.Range(1, int.MaxValue);

            Current = director;

            if (StartImmediately && director.State != MatchState_Playing())
            {
                // GameDirector 在 Awake 里已经 Reset 过，这里只是确保状态正确
            }
        }

        static Dogfight.Core.MatchState MatchState_Playing()
        {
            return Dogfight.Core.MatchState.Playing;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (!AutoCreateInAnyScene) return;
            if (FindObjectOfType<GameBootstrap>() != null) return;   // 场景里已经手动放了
            if (FindObjectOfType<GameDirector>() != null) return;    // 已经有人在管

            var go = new GameObject("DogfightBootstrap");
            go.AddComponent<GameBootstrap>();
        }
    }
}
