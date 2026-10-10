# 镜像阿拉德候选图组装修复

> 2026-10-10：此文保留历史调查/测试过程；四图组装已修复并经用户实测反馈通过，临时轮换与免扣次数入口已退役。最新验收范围见 `Document/MirrorAradCloseout-20261010.md`。

2026-10-10；关联 [issue #143](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/143)。用户授权：继续处理此前镜像阿拉德三张地图的组装问题。复查同时覆盖重复难度字段清理后暴露的幽暗密林深处，实际范围为四张图。

## 根因与修复

| 地下城 | 原因 | 修复 |
| --- | --- | --- |
| 4 幽暗密林深处 | DGN 显式绑定的房间被错误套用随机池的完整门型匹配。例如 `(1,0)` 声明 `JJ`，MAP 415/413 声明 `II II`，但允许本次从 Down 进入。 | 显式候选只检查入口掩码；随机池仍要求完整 pattern 与入口在同一对声明中匹配。 |
| 17 悬空城 | MAP 4602 的 `[greed] KK II` 位于 `[tile]`、怪物等区块之后；原读取器遇到 `[tile]` 就停止，误用缺省 `All/All`。 | 使用现有 `PvfDocument.AtRoot()` 读取全文根级元数据，排除嵌套字段。 |
| 23 炼狱 | 同样漏读后半段门型。例如 MAP 3408 实际具有 `[greed] JJ BB`，并非缺少 `JJ` 随机池素材。 | 同上；不修改该 DGN 或补造 MAP 门型。 |
| 60 热血八番街 | 普通 maze 4 的五条 MAP 绑定落在 `AA` 空格上。 | 离线 PVF 工具只删除这五条绑定；保留格子、路线、起点、Boss 点及另外四个任务迷宫。 |

此前“悬空城、炼狱缺门型素材”的判断被完整 MAP token 读取证据否定。修复没有引入任意 MAP 兜底；显式候选全部入口不兼容时仍拒绝，不借用随机池；深渊替换检查保持原样。

## 证据边界

- **70Reference / Confirmed**：`CBattle_Field::get_candidator_map` (`0x08308214`) 的普通及 Boss 显式列表调用 `CMap::CheckEntrance` (`0x0834ECAA`)，后者只检查 pair.second 的入口位；随机列表则调用 `CDungeon::ChooseRandomMap` (`0x0834BF56`)，按完整 pattern 分桶。`CDataManager::load_map_files` (`0x0835FE02`) 对真正缺少 greed 的随机注册使用 `15/15`；本次没有把缺省改成通配。
- **Content / Confirmed**：当前 PVF 完整 token 中 MAP 4602 的 greed 为 `KK II`，MAP 3408 为 `JJ BB`，均在 tile 之后。五条 Odesa 绑定的坐标对应 `AA`。
- **CurrentImplementation**：全文根级读取、显式入口检查及候选包的实际布局扫描通过。
- **ClientVerified**：本次修复尚未部署，未验证实机入场、清房换房、Boss 通关。退图虚弱提示的实机验收属于前一轮修复，不作为本次组装修复的验收证据。

70 ELF：`E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r`；SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。完整反汇编保留在本地 `artifacts/mirror-layout-repair-20261010/70-*.asm.txt`。

## PVF 候选包

- 输入：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA-256 `D9723FC9A1BEDEEB1BC2EFBF16CA65E3E5537A58E3D911008C73240EA719BF37`。
- 输出：`E:/Project/DNF/DNF-JP-86-Server/artifacts/mirror-layout-repair-20261010/Script.fixed.pvf`，SHA-256 `87599470F3F387B14DEAC355177DDF49B8FE70E9A8D521B3E496786CB2384714`。
- 唯一修改文件：`dungeon/Village/CartelArad/Odesa.dgn`，maze 4 删除 `map 3 0 11105`、`map 1 1 11101`、`map 1 2 11106`、`map 2 3 11108`、`map 6 3 11112`，合计 20 token。
- 712,010 个文件中 712,009 个目录项未变，原 5,770 个 body chunk、名称及字符串池字节保持不变；新增一个 chunk。重新打开档案后核对目标脚本。
- 工具固定校验输入 SHA、迷宫数量、完整普通网格、绑定形态及五个精确目标，拒绝原地修改或覆盖输出。输入不同必须重新审计。

在工作树根目录复现生成，第二个路径必须尚不存在：

```powershell
dotnet run --project Tool/PvfMirrorLayoutPatch/PvfMirrorLayoutPatch.csproj -- <source.pvf> <new-output.pvf>
```

## 验证

- 元数据回归先红后绿：两个真实 MAP 和一个嵌套隔离样本，修复前 3 失败；修复后该类 5 项全过，无跳过。
- 地下城结构与镜像入场回归 56 项通过，无跳过，包括幽暗密林深处、悬空城、炼狱所有普通起点/Boss 组合。
- 当前镜像候选 50 张定义均解析成功；每张 seed 0–15，共 800 次正常布局预检通过（difficulty 1，空活动/已完成任务，Interactive）。
- 额外枚举全部普通 maze × start × boss，共 124 组合，通过预检；在每个组合内检查实际可作为首次进入的 1,779 条有向边，全部有合法 MAP。判定首次进入时，从起点搜索 source 的路径必须避开 target，且不穿过 Boss 终点；已经生成房间的反向重访不冒充首次生成。
- 后者是有限候选与图拓扑检查，不证明怪物脚本、演出或实机全流程。
- 同目录 `layout.json`、`exhaustive.json` 保留扫描结果；完整诊断程序、输入/输出及日志在仓库本地 `artifacts/mirror-layout-repair-20261010/`。

测试命令（先设置 `PVF_ARCHIVE_PATH` 为待验收 PVF）：

```powershell
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~PvfDungeonMapDefinitionSourceTests
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~DungeonStructure|FullyQualifiedName~MirrorDungeonEntryTests'
```

本次不改协议布局、不改在线数据库、不切换服务/PVF。后续部署需同时核对服务端及实际游玩客户端 PVF；issue #143 仍需实机验收后收尾。
