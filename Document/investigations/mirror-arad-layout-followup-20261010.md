# 镜像阿拉德：候选地下城的布局预检失败

> 2026-10-10：此文保留历史调查/测试过程；四图组装已修复并经用户实测反馈通过，临时轮换与免扣次数入口已退役。最新验收范围见 `Document/MirrorAradCloseout-20261010.md`。

GitHub：[#143](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/143)，关联 #70。此项单独跟踪，不包含指定难度字段重复的 PVF 清理。

## 当前证据

- 代码：`4aaedfc129652c5c808698cea7f8c08067b922bc`，分支 `task/monster-declaration-parser`。
- 已部署程序集：`0.2.3+quota-direct.20261010.r1`。
- PVF SHA-256：`03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543`。
- 从 `etc/crackofdimensionlist.etc` 取得 50 个候选图，使用部署版本的 `PvfDungeonDefinitionSource`、`PvfDungeonMapDefinitionSource`、`DungeonStructureRuleService` 检查普通迷宫。
- 输入：难度 1，无活动/已完成任务，Interactive，种子 0～15。以下三张全部 16 次失败；不是实机逐图验证，也不能据此断言根因全部在 PVF。

## 失败项

| 地下城 | 文件 | 预检错误 |
|---|---|---|
| 17 悬空城 | `dungeon/Act2/FloatingCastle.dgn` | maze 0，cell (0,7) 无兼容入口 Down 的地图；该格显式绑定 4602 |
| 23 炼狱 | `dungeon/Act3/Darkhell.dgn` | maze 0，cell (4,5) 无兼容入口 Left 的地图，或 cell (0,0) 无兼容入口 Right 的地图 |
| 60 热血八番街 | `dungeon/Village/CartelArad/Odesa.dgn` | 普通 maze 4 的 (3,0)、(1,1)、(1,2)、(2,3)、(6,3) 在 greed 中为 AA 空格，却有 map specification 绑定 |

清理 11 张镜像图重复难度声明后，修正版 PVF SHA-256 为 `D9723FC9A1BEDEEB1BC2EFBF16CA65E3E5537A58E3D911008C73240EA719BF37`。50 张全部通过定义解析；每图种子 0～15 的布局检查有 46 张通过、上述三张仍失败，并暴露一张此前被字段错误遮挡的布局问题：

| 地下城 | 文件 | 预检错误 |
|---|---|---|
| 4 幽暗密林深处 | `dungeon/Act1/MirkWoodDeep.dgn` | maze 0，cell (1,0) 无兼容入口 Down 的地图；修正版所有 16 个抽样种子均失败 |

当前行为：入场布局预检抛出配置异常，由入场领域服务转为 `ConfigurationInvalid`，镜像请求返回 CMD `0x02B6` 拒绝包。上述为 CurrentImplementation，不代表原生客户端或 70 服务端已确认。

## 后续处理

- 对照原生/上游的 DGN、MAP 门方向和候选选择，明确每张图是素材错误还是当前兼容性判据过严。
- 悬空城检查 4602 的门方向、可进入方向与 DGN 竖向路径；炼狱检查随机房间池的归属、门型与入口条件。
- 热血八番街核对上述五条是否为无效残留；不要直接补造可走格或擅自改变路线。
- 保留严格校验，按证据修正内容或具体规则，不增加全局任意地图回退。

## 验收

- [ ] 四张图的根因与证据分别明确。
- [ ] 普通迷宫的起点/终点候选和实际可达入口均能解析出兼容 MAP。
- [ ] 重新检查全部 50 个镜像候选；有限种子抽检与穷举候选覆盖分开记录。
- [ ] 实机完成入场、跨房间和通关；拒绝入场不扣次数。

本地原始证据：`artifacts/pvf-dungeon-difficulty-audit-20261010/audit.layout.json`、`audit.parser.json`、`fixed-audit.layout.json`、`fixed-audit.parser.json`。这些是诊断产物，不随 GitHub issue 自动上传；上文已摘录复现条件与完整错误要点。
