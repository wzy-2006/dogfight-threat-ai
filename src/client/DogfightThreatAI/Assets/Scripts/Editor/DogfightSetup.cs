using System.Collections.Generic;
using System.Text;
using Dogfight.Tests;
using Dogfight.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dogfight.EditorTools
{
    /// <summary>
    /// 一键搭建工具。把「导入素材 → 生成台账 → 生成配置 → 建场景 → 挂脚本」这些
    /// 手工步骤全部自动化，避免出现「clone 下来跑不起来」。
    ///
    /// 菜单：Unity 顶部 → 空战
    /// </summary>
    public static class DogfightSetup
    {
        const string KenneyRoot = "Assets/ThirdParty/kenney-pixel-shmup";
        const string ResourceFolder = "Assets/Resources/Dogfight";
        const string BankPath = ResourceFolder + "/SpriteBank.asset";
        const string ConfigPath = ResourceFolder + "/GameConfig.asset";
        const string ScenePath = "Assets/Scenes/Dogfight.unity";

        const int ShipCount = 24;
        const int TileCount = 120;

        /* ------------------------------------------------------- 菜单入口 */

        [MenuItem("空战/① 生成精灵台账", false, 10)]
        public static void CreateSpriteBank()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(ResourceFolder);

            var bank = AssetDatabase.LoadAssetAtPath<SpriteBank>(BankPath);
            bool isNew = bank == null;
            if (isNew) bank = ScriptableObject.CreateInstance<SpriteBank>();

            int ships = 0, tiles = 0;
            for (int i = 0; i < ShipCount; i++)
            {
                string p = string.Format("{0}/Ships/ship_{1:0000}.png", KenneyRoot, i);
                Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (s != null) { bank.Ships[i] = s; ships++; }
            }
            for (int i = 0; i < TileCount; i++)
            {
                string p = string.Format("{0}/Tiles/tile_{1:0000}.png", KenneyRoot, i);
                Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (s != null) { bank.Tiles[i] = s; tiles++; }
            }

            if (isNew) AssetDatabase.CreateAsset(bank, BankPath);
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(string.Format("[空战] 精灵台账已生成：{0}（飞机 {1}/{2}，地形块 {3}/{4}）",
                BankPath, ships, ShipCount, tiles, TileCount));

            if (ships == 0)
            {
                Debug.LogError("[空战] 一个飞机精灵都没找到。请确认素材在 " + KenneyRoot +
                               "，并且已由 PixelArtPostprocessor 按 Sprite 导入。");
            }
            Selection.activeObject = bank;
        }

        [MenuItem("空战/② 生成游戏配置", false, 11)]
        public static void CreateGameConfig()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(ResourceFolder);

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<GameConfig>();
                AssetDatabase.CreateAsset(cfg, ConfigPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[空战] 游戏配置已生成：" + ConfigPath + "（可在 Inspector 里调 AI 参数）");
            }
            else
            {
                Debug.Log("[空战] 游戏配置已存在：" + ConfigPath);
            }
            Selection.activeObject = cfg;
        }

        [MenuItem("空战/③ 一键搭建（资源 + 场景）", false, 12)]
        public static void OneClickSetup()
        {
            CreateSpriteBank();
            CreateGameConfig();
            CreateScene();
            Debug.Log("[空战] 一键搭建完成。直接按 Play 即可开始。");
        }

        [MenuItem("空战/④ 打开游戏场景", false, 13)]
        public static void OpenScene()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogWarning("[空战] 还没有游戏场景，请先执行「③ 一键搭建」。");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("空战/运行核心算法自检", false, 30)]
        public static void RunSelfTest()
        {
            List<SelfTestResult> results = CoreSelfTest.Run();

            int pass = 0, fail = 0;
            var sb = new StringBuilder();
            sb.AppendLine("[空战] 核心算法自检");
            for (int i = 0; i < results.Count; i++)
            {
                SelfTestResult t = results[i];
                if (t.Passed) { pass++; }
                else
                {
                    fail++;
                    sb.AppendLine("  [失败] " + t.Name);
                    sb.AppendLine("         " + t.Message);
                }
            }
            sb.AppendLine(string.Format("  通过 {0} 项，失败 {1} 项，共 {2} 项", pass, fail, results.Count));

            if (fail == 0) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());
        }

        /* ------------------------------------------------------- 场景生成 */

        static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Dogfight");
            var director = root.AddComponent<GameDirector>();

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            var bank = AssetDatabase.LoadAssetAtPath<SpriteBank>(BankPath);
            if (cfg != null) director.Config = cfg;
            if (bank != null) director.Bank = bank;

            var hud = root.AddComponent<HudOverlay>();
            hud.Director = director;

            // 相机
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 14f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.05f, 0.09f, 1f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<AudioListener>();
            var follow = camGo.AddComponent<CameraFollow>();
            follow.OrthoSize = 14f;

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("[空战] 场景已生成：" + ScenePath);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
