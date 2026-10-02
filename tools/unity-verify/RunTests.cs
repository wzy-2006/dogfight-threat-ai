using System;
using System.Collections.Generic;
using Dogfight.Tests;

/// <summary>
/// 在 Unity 之外运行核心算法自检（编译 Core + CoreSelfTest 后用 mono 执行）。
/// 这样算法层可以在没有 Unity 的环境里跑回归，也能接进 CI。
///   tools/unity-verify/run-self-test.ps1
/// </summary>
public static class RunTests
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine();
        Console.WriteLine("  空战 · 核心算法自检（Unity 之外运行）");
        Console.WriteLine("  ============================================");

        List<SelfTestResult> results = CoreSelfTest.Run();

        int pass = 0, fail = 0;
        for (int i = 0; i < results.Count; i++)
        {
            SelfTestResult t = results[i];
            if (t.Passed)
            {
                pass++;
                Console.WriteLine("  [PASS] " + t.Name);
            }
            else
            {
                fail++;
                Console.WriteLine("  [FAIL] " + t.Name);
                Console.WriteLine("         " + t.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine("  --------------------------------------------");

        // AI 行为分布（说明「威胁驱动动态切换」确实在工作）
        if (args.Length > 0 && args[0] == "--modes")
        {
            Dictionary<string, float> modes = CoreSelfTest.RunAiModeSurvey(60);
            Console.WriteLine("  敌方 AI 行为时间占比（60 秒）：");
            foreach (KeyValuePair<string, float> kv in modes)
            {
                Console.WriteLine(string.Format("    {0,-10} {1,6:F1}s", kv.Key, kv.Value));
            }
            Console.WriteLine();
        }

        Console.WriteLine("  通过 " + pass + " 项，失败 " + fail + " 项，共 " + results.Count + " 项");
        Console.WriteLine("  ============================================");
        Console.WriteLine();

        return fail == 0 ? 0 : 1;
    }
}
