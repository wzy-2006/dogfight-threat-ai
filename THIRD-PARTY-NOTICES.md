# 第三方素材声明（THIRD-PARTY-NOTICES）

本项目使用了以下第三方素材。**每一个第三方素材都必须在「素材台账」里登记**，
并在本节保留许可信息。发布（打包 / 上架 / 答辩材料）时不要删除本文件。

---

## 1. Kenney Pixel Shmup 1.1

| 项 | 值 |
|---|---|
| 名称 | Pixel Shmup (1.1) |
| 作者 | Kenney（<https://kenney.nl>） |
| 来源 | <https://kenney.nl/assets/pixel-shmup> |
| 创建日期 | 2021-11-01 |
| 许可 | **CC0 1.0 Universal**（公有领域贡献） |
| 许可原文 | [`art-source/third-party/kenney-pixel-shmup/source/License.txt`](art-source/third-party/kenney-pixel-shmup/source/License.txt) |
| 归档位置 | `art-source/third-party/kenney-pixel-shmup/` |
| 导入位置 | `src/client/DogfightThreatAI/Assets/ThirdParty/kenney-pixel-shmup/` |
| 用途 | 玩家机 / 敌机 / 地形块（第一阶段的占位与正式素材） |
| 登记台账 | [`docs/02-design/素材台账.md`](docs/02-design/素材台账.md) |
| 项目内登记 | `Assets/ThirdParty/kenney-pixel-shmup/LICENSE-Kenney-PixelShmup.txt` |

**CC0 意味着什么**：可以自由用于个人、教育、商业项目，可以修改、可以再分发，
**不强制署名**。本项目仍然保留署名与许可原文，理由是：

1. 结题报告与答辩材料里需要说明素材来源，这是学术规范；
2. 万一后续要换成付费素材，台账 + 声明已经建好了流程。

建议在游戏内或结题材料中保留一句 `Art by Kenney`。

### 内容与本次实际使用情况

| 目录 | 内容 | 规格 | 已导入 | 本次使用 |
|---|---|---|---|---|
| `Ships/` | 24 架飞机 | 32 × 32 | ✅ 24 个 | 玩家机 `ship_0000`；敌机 `ship_0001 / 0005 / 0009 / 0013` |
| `Tiles/` | 120 个地形块 | 16 × 16 | ✅ 120 个 | 海面 `42`、草地岛 8 向边缘、沙地岛 8 向边缘、点缀（树/房子） |
| `Tilemap/` | 图集（含 packed 版） | 图集 | ✅ 4 个 | 暂未使用，留给后续 Tilemap 方案 |
| `Preview.png` / `Sample.png` | 预览图 | — | ❌ 按要求未导入 | — |
| `Tilesheet *.txt` | 图集切分参数 | — | ❌ 按要求未导入 | — |

### 许可风险与替换计划

Pixel Shmup **不包含**子弹、爆炸序列帧、UI、字体与音频。这些本项目采用
**程序化生成**（爆炸复用图块 4~9、音效由代码合成、HUD 用 IMGUI 绘制），
因此没有引入额外的第三方素材与许可义务。

若后续从 Unity Asset Store 购入素材，请在此文件追加一节，并同步更新
[`docs/02-design/素材台账.md`](docs/02-design/素材台账.md)。

---

## 变更记录

| 日期 | 变更 | 说明 |
|---|---|---|
| 2026-10 | 建立本文件 | 登记 Kenney Pixel Shmup 1.1（CC0 1.0） |
