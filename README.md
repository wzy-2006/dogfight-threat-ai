# 空战 · 基于实时威胁评估与动态决策的飞机对战游戏

> 湖北经济学院 · 信息工程学院 · 大学生创新训练计划项目
> Unity 2D + C#。第一阶段（V1）：可移动、可射击的 2D 空战原型 + 实时威胁评估 AI。

```
文档   docs/                       需求 / 设计 / 测试 / 手册 / 报告
客户端 src/client/DogfightThreatAI/  Unity 工程（本阶段主战场）
网关服 src/server/GatewayServer/     第二阶段
逻辑服 src/server/LogicServer/       第二阶段
协议   src/shared/Protocol/          第二阶段
素材   art-source/                   原始归档（含第三方素材与 License）
验证   tools/unity-verify/           不开 Unity 也能编译验证 + 跑算法自检
```

---

## 一、快速开始

### 1. 打开工程

用 Unity 打开 `src/client/DogfightThreatAI/`。

- 工程记录的版本是 **Unity 2022.3.62f3c1**；
- 本机实测可用 **团结引擎（Tuanjie）2022.3.62t16**，它是 Unity 中国的官方分支，API 兼容；
- 用其他 2022.3.x 版本通常也能直接打开。

### 2. 一键搭建（只用点一次）

Unity 顶部菜单 → **空战 → ③ 一键搭建（资源 + 场景）**

它会自动：
1. 按 `docs/02-design/美术规范.md` 的约定把 Kenney 素材配好导入设置（PPU=32、Point 过滤、Read/Write）；
2. 生成精灵台账 `Assets/Resources/Dogfight/SpriteBank.asset`；
3. 生成可调参的配置资源 `Assets/Resources/Dogfight/GameConfig.asset`；
4. 建好场景 `Assets/Scenes/Dogfight.unity`（相机 + 战斗机 + HUD）。

然后**按 Play** 即可开打。

> 其实**不做这一步也能跑**：`GameBootstrap` 会在任意场景自动建好一切，
> 素材没配好时会退化成程序化占位图。一键搭建只是让画面和配置到位。

### 3. 操作

| 按键 | 作用 |
|---|---|
| `W` / `S`（或 ↑↓） | 加大 / 收油门 |
| `A` `D`（或 ←→） | 左右压杆转向 |
| `空格` / 鼠标左键 | 机炮（有冷却与过热） |
| `Shift` / 鼠标右键 | 导弹：**按住**锁定，锁定完成自动发射 |
| `Ctrl` | 减速板 |
| `V` | 切换操控方案：物理飞行 / 街机 |
| `P` / `Esc` | 暂停 |
| `R` | 重开一局 |
| `F1` | 无敌（调试） |
| `F2` | 显示「威胁网格」——直接看到 AI 眼里的危险度地图 |

详细玩法见 [`docs/04-manuals/用户手册.md`](docs/04-manuals/用户手册.md)。

---

## 二、不开 Unity 也能验证（重要）

这是本工程特意做的一件事：**算法层与渲染层彻底分离**，
所以不需要启动编辑器就能确认代码没写坏。

```powershell
# 交互式
tools\unity-verify\验证.bat

# 或命令行
powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity-verify\verify-all.ps1
```

它做三件事：

1. **按 Unity 的程序集划分编译三遍**（用编辑器自带的 Roslyn，C# 9 + 真实的
   `UnityEngine.dll` / `UnityEditor.dll`）：
   - `Dogfight.Core` —— 纯 C#，**不传任何引擎引用**（等价于 asmdef 的 `noEngineReferences`）
   - `Assembly-CSharp` —— Gameplay / View / Config / Tests
   - `Assembly-CSharp-Editor` —— 编辑器工具
2. **跑 67 项核心算法自检**（用编辑器自带的 mono 执行）：

   | 分类 | 覆盖内容 |
   |---|---|
   | 数学 | 角度归一化、最短弧、拦截解算、扫掠几何、帧率无关插值 |
   | 飞行 | 加速收敛与上限、惯性滑行、低速舵效、转向阻尼回中、速度边界、减速板、软边界 |
   | 武器 | 冷却、过热与恢复、出膛叠加载机速度、敌我识别 |
   | 威胁评估 | 距离/方位/血量三个单调性、取值恒在 [0,1]、威胁网格衰减与梯度 |
   | 路径规划 | A\* 寻路、A\* 绕开高威胁墙、BFS 找未侦察格子、规划器注册表回退 |
   | 波次 | 数量单调递增、难度系数、机型按波解锁、成长上限 |
   | 集成仿真 | **60 秒完整自动对战**：刷怪 → 开火 → 命中 → 击落 → 计分 → 清波 → 推进波次，三档 AI 各跑一遍 |

3. 全部通过才返回 0，可直接接进 CI。

### 这套验证抓到过什么

不是摆设。开发过程中它抓到了 4 个真 bug：

| bug | 后果 | 怎么发现的 |
|---|---|---|
| `PlaneState.Create` 在绑定武器查表**之前**就建武器表 | 整架飞机**一把武器都没有**，战斗变成纯撞机 | 自检「机炮能发射弹丸」失败 |
| 游戏总控的属性叫 `Input`，遮蔽了 `UnityEngine.Input` | 所有 `Input.GetKeyDown` 编译不过 | 编译验证报 CS1061 |
| `WrapAngle` 用精确等号判断 ±π | `wrapAngle(-3π)` 返回 −π | 自检「wrapAngle 处理负数圈」失败 |
| 字段名 `UseGeneratedTerrainTexture` 与使用处 `EnableGeneratedTerrainTexture` 不一致 | 编译不过 | 编译验证报 CS0103 |

---

## 三、架构：为什么把逻辑全放进纯 C#

```
src/client/DogfightThreatAI/Assets/Scripts/
├── Core/          ★ 纯 C#，零 UnityEngine 依赖（asmdef 的 noEngineReferences 强制）
│   ├── Vec2 / MathUtil / DogfightConfig / ConfigDefaults
│   ├── FlightModel        惯性滑行、转向阻尼、速度边界
│   ├── PlaneState         战机状态（血、武器、锁定）
│   ├── ProjectileState    弹丸 / 导弹
│   ├── ThreatAssessment   ★ 五因子威胁评估
│   ├── ThreatField        ★ 战场威胁网格
│   ├── Pathfinding        ★ A* / BFS + 规划器注册表
│   ├── AiBehaviors        巡逻 / 搜索 / 追击 / 攻击 / 规避
│   ├── AiController       分层 AI：感知 → 决策 → 执行
│   └── Simulation         ★ 整个战斗仿真（碰撞、伤害、波次、计分、事件）
├── Gameplay/      MonoBehaviour：只做「喂输入 / 收事件 / 画出来」
├── View/          精灵台账、程序化地表、HUD、雷达
├── Config/        GameConfig（ScriptableObject，可调 AI 参数）
├── Tests/         核心算法自检（可直接在 Unity 菜单里跑）
└── Editor/        一键搭建 + 像素素材导入设置
```

**为什么这么分**：

1. **可测**：Core 能在 Unity 之外逐帧跑。上面那 67 项就是这么来的。
2. **为第二阶段铺路**：立项书要求「逻辑服务器集中管理，服务器权威 + 客户端插值」。
   仿真与渲染已经彻底隔离，到时候把 `Simulation` 搬到逻辑服、客户端只渲染下发的
   状态快照即可，表现层几乎不用改。
3. **可移植**：以后想换引擎或做无头压测，Core 一行不用动。

**约定**：仿真内部统一用**像素**做单位，只在视图层除以 **PPU=32** 换算成世界单位。
详见 [`docs/02-design/美术规范.md`](docs/02-design/美术规范.md)。

---

## 四、本阶段实现范围

对照立项书：

| 立项书要求 | 状态 |
|---|---|
| Unity 2D 工程 / 战机精灵导入 / 基本飞行操控验证 | ✅ |
| 多层视差空战场景 | ✅ 3 层，地表与云层程序化生成且无缝平铺 |
| 惯性滑行 / 转向阻尼 / 速度边界约束 | ✅ |
| 武器发射（子弹 / 导弹）与飞行轨迹 | ✅ 含出膛叠加机速、导引头、近炸引信 |
| 碰撞检测（飞机 / 子弹 / 导弹 / 敌机 / 场景边界） | ✅ 扫掠检测 + 撞机 + 软边界 |
| 多层级雷达信息显示 | ✅ 近 / 中 / 远三环 |
| 实时威胁指标计算 | ✅ 五因子加权，带完整分解 |
| 网格化战场威胁建模 | ✅ 实时威胁网格 + 双线性采样 + 梯度 |
| 巡逻 / 搜索 / 追击 / 攻击 / 规避动态切换 | ✅ 带滞回的状态机 |
| BFS 目标搜索与区域巡逻 | ✅ |
| A\* 追击与低风险规避路径规划 | ✅ |
| 遗传算法优化 | ⬜ 仅预留 `Pathfinding.Register("ga", fn)` 注册位 |
| 三档难度 AI（靠算法质量而非数值压制） | ✅ |
| 音效 | ⬜ Unity 版未做 |
| 客户端—网关—逻辑服务器三层架构 | ⬜ 第二阶段 |
| 房间 / 观战 / 文字交流 | ⬜ 第二阶段 |
| 轨迹保存与复盘 | ⬜ 第二阶段 |

文档：

| 文档 | 位置 |
|---|---|
| 需求分析 | [`docs/01-requirements/需求分析.md`](docs/01-requirements/需求分析.md) |
| 系统设计 | [`docs/02-design/系统设计.md`](docs/02-design/系统设计.md) |
| 美术规范 | [`docs/02-design/美术规范.md`](docs/02-design/美术规范.md) |
| 素材台账 | [`docs/02-design/素材台账.md`](docs/02-design/素材台账.md) |
| 用户手册 | [`docs/04-manuals/用户手册.md`](docs/04-manuals/用户手册.md) |
| 第三方素材声明 | [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) |

---

## 五、协作约定

开工前请读：

- 代码风格 → [`.editorconfig`](.editorconfig)（Allman、4 空格、LF、using System 优先、私有字段 `_` 前缀）
- 换行符与 Unity 智能合并 → [`.gitattributes`](.gitattributes)
- 提交前自查 → [`.github/PULL_REQUEST_TEMPLATE.md`](.github/PULL_REQUEST_TEMPLATE.md)

**特别提醒**：

- **切换 / 合并分支前先关 Unity**；
- `.meta` 文件必须与素材在**同一次提交**（否则 GUID 会变，场景引用全断）；
- 不要提交 `Library/` `Temp/` `Logs/` `UserSettings/`；
- 改 `ProjectSettings/`（Layers / Tags / 物理 / 画质 / Packages）**必须在群里同步**；
- 本工程**故意不改 TagManager**：所有前后关系用 `sortingOrder` 控制（见美术规范第 5 节），
  就是为了避免「改 ProjectSettings 需要全员同步」这件事。

---

## 变更记录

| 日期 | 变更 | 说明 |
|---|---|---|
| 2026-10 | 第一阶段 V1 | Unity 客户端：飞行 / 武器 / 威胁评估 AI / 视差背景 / 雷达 HUD；Core 层 67 项自检 |
