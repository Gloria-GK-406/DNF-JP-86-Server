# TASK-02 — 决斗契约、PVF 内容源与规则服务

## Task Unit Contract

- Revision: 1
- Outcome and references: `Abstractions/Pvp` 的模式枚举、能力表、频道权益记录、结算规则输入输出记录、两个内容源接口与两个规则服务接口；`Infrastructure/Content` 的 `pvp_ref.etc`、`pvpmapbasicdata.etc` 内容源；`Application/Pvp` 的两个规则服务与静态规则；对真实 A21 PVF 的规则测试与模式 switch 源码扫描测试。Plan T-001；Spec RQ-013（地图）、RQ-050–053、RQ-073，AC-050、AC-051、AC-070（模式扫描部分）。
- Owned boundary: 新文件 `Server/src/Dfo.Server.Abstractions/Pvp/*`、`Server/src/Dfo.Server.Infrastructure/Content/Pvf*Pvp*.cs`、`Server/src/Dfo.Server.Application/Pvp/*Rule*.cs`、`PvpRegistration.cs`（追加注册）、`InfrastructureModule.cs`（注册与预热两行）；测试 `Server/tests/Dfo.Server.Infrastructure.Tests/Content/`、`Server/tests/Dfo.Server.Application.Tests/Pvp/`；状态普查/写入口表如有新增单例需登记。不改动房间服务、持久化、协议。
- Required checks: 解决方案构建通过；新增测试通过（真实 PVF 用 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`）；Host.Tests 中 `ContentBoundaryArchitectureTests`、`RegistrationArchitectureTests`，Application.Tests 中 `GameplayStateCensusTests`、`CommandHandlerArchitectureTests` 通过。
- Dependencies: TASK-01
- Result review: required — 后续结算、房间、协议都消费这里的接口与数值；经验/段位读错会在实机前一直被下游当真，且 PVF 段落结构只能靠对真实档案的测试暴露。

## Detailed Plan

先用 `Tool/PvfProbe`（或测试里 `PvfDocument`）读出 A21 PVF 中 `etc/pvp_ref.etc` 与 `etc/pvpmapbasicdata.etc` 的真实段落与数值，把观察到的段名、形状和样例数值记入 `.rks-agent/tasks/pvp-duel-phase1/research/pvf-pvp.md`；接口按真实形状定，不按参照猜。参照：上游 `E:\Project\DNF\ServerS4A21\Server\DfoServer\Game\Pvp\PvpRecord.cs`（`PvpExperienceRules.Parse` 约 111 行）与 `PvpMapRules.cs`（约 30 行）只看读取了哪些段，不拷代码；70 档案 `Research/tw70-pvf-archive` 仅作段名旁证。

契约（`Abstractions/Pvp/`，命名可按现有风格微调，但语义固定）：
- `enum PvpBattleMode : byte { Individual = 1, Team = 2, Relay = 3, DeathMatchIndividual = 4, DeathMatchTeam = 5, Practice = 6 }`。
- `PvpBattleModeCapabilities`（照 `Abstractions/World/DungeonRunCapabilities.cs` 写法）：`Supported`、`TeamAssignment`（个人 / 交替两队）、`RequiresBalancedTeams`、`Terminal`（最后一人 / 最后一队）、`Settles`；`Of(mode)` 的 switch 列全部 6 个成员、无兜底；一期只有 1、2 的 `Supported=true`。
- `PvpChannelEntitlements(bool RecordsResult, bool AwardsGradeExperience, int DailyWinExperienceCap, int DailyLossExperienceCap)`；由 `IPvpSettlementRuleService.EntitlementsOf(byte loginEnvironment)` 唯一提供：8、13 不记录，24 记录且上限 50/10；其他环境抛异常。另加一个测试：频道分类（`ChannelEntryDomainService.KindOf` 视为 PvP 的环境）中的每个环境都在表里——用真实频道脚本或分类常量证明表覆盖。
- 结算规则记录：`PvpOutcome { Win, Loss, Draw }`；`PvpSettlementFacts`（角色当前段位、累计经验——可为空表示"取本段下限"、本局结果、是否逃离、本游戏日此前胜/负场数、权益行）；`PvpSettlementDecision`（新段位、新经验、经验变化、是否计入每日计数）。连胜不在规则服务里（存储侧简单计数）。
- `IPvpSettlementRuleService`：`EntitlementsOf`、`Decide(PvpSettlementFacts)`、`GradeOf(long experience)`、`LowerBound(byte grade)`/`UpperBound(byte grade)`（供 `0x0030` 段内上下界与迁移后空经验的取值）、`BattleTimeLimit(PvpBattleMode)`、`DateOnly GameDayOf(DateTimeOffset)`（委托 `IDailyResetRuleService`）。
- `IPvpRoomRuleService`：地图下标校验与随机选择（`[pvp map order]`，0=随机，越界拒绝），选择需显式随机种子参数以便测试。
- 内容源接口 `IPvpGradeDefinitionSource`、`IPvpMapDefinitionSource` 返回不可变目录记录；实现照 `Infrastructure/Content/PvfSaoTowerContentSource.cs`（`Lazy`、`IContentWarmup`）；必需段缺失时加载即抛（预热失败即启动失败）。
- 静态 `PvpGradeRules`：按 Spec 经验决策表实现 `Decide`（0 段首胜升 1 段下限；满级段胜不增、负照扣；第 51 胜起与第 11 负起经验 0；平局 0；逃离按负；经验下限 0；按累计经验重查段位，可跨多段）。

测试：
- Infrastructure：两个内容源对真实 PVF 的加载（段落数量与几处样例值写成断言，值来自 research/pvf-pvp.md 观察）。
- Application：`PvpGradeRules` 决策表每一行、跨多段、下限 0、满级不增仍扣、第 50/51 胜、第 10/11 负；权益表 8/13/24 与未知环境；时限来自 PVF；地图越界与随机。
- 源码扫描：照 `Application.Tests/World/DungeonRunKindTests.cs:64-100`，扫描 `Server/src` 中 switch 体含 `PvpBattleMode.` 且带 `_ =>` 或 `default:` 即失败，并附一个会失败的反例（扫描函数对构造的源码字符串返回违规）。

## Result

- Outcome: done
- Changed:
  - 契约 `Server/src/Dfo.Server.Abstractions/Pvp/PvpBattleModes.cs`（`PvpBattleMode`、`PvpTeamAssignment`、`PvpBattleTerminal`、`PvpBattleModeCapabilities.Of` 全 6 成员无兜底，仅 1、2 Supported/Settles）；`Pvp/PvpSettlement.cs`（`PvpChannelEntitlements`、`PvpOutcome`、`PvpSettlementFacts`、`PvpSettlementDecision`、`PvpGradeStep`、`PvpGradeCatalog`、`IPvpGradeDefinitionSource`、`IPvpSettlementRuleService`）；`Pvp/PvpMaps.cs`（`PvpMapCatalog`、`IPvpMapDefinitionSource`、`IPvpRoomRuleService`）。
  - 内容源 `Server/src/Dfo.Server.Infrastructure/Content/PvfPvpGradeDefinitionSource.cs`、`PvfPvpMapDefinitionSource.cs`（`Lazy` + `IContentWarmup`，`AtRoot()` 读根级段，缺段/形状不符即抛）；`InfrastructureModule.cs` 注册 2 行 + 预热 2 行。
  - 规则 `Server/src/Dfo.Server.Application/Pvp/PvpGradeRules.cs`（静态决策表）、`PvpSettlementRuleService.cs`（权益表 8/13→不记录，24→记录 50/10，其余抛；时限；`GameDayOf` 委托 `IDailyResetRuleService.PeriodDay`）、`PvpRoomRuleService.cs`（下标 0..Order.Count-1，0 用显式种子从 1..Count-1 抽）；`PvpRegistration.cs` 追加两行。
  - 研究记录 `.rks-agent/tasks/pvp-duel-phase1/research/pvf-pvp.md`。
  - 测试：`Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfPvpContentSourceTests.cs`（真实 PVF 数值 + 合成档案缺段拒收）；`Server/tests/Dfo.Server.Application.Tests/Pvp/PvpGradeRulesTests.cs`、`PvpSettlementRuleServiceTests.cs`、`PvpRoomRuleServiceTests.cs`、`PvpBattleModeTests.cs`（含 switch 源码扫描与反例）；`Server/tests/Dfo.Server.Host.Tests/ContentWarmupTests.cs` 登记两个新内容源为 Warmed（W2 普查）并在 `Participants` 名单加 `pvp-grade`、`pvp-map`（真实内容宿主的预热名单断言）。
- Why:
  - 满级段 = `[pvp experience loss]` 首值 20，并在加载时交叉校验 = 阈值行数−1 = `[pvp grade point]` 行数−1；段 20 上界取哨兵 9999999。
  - `Decide` 以经验为准重算当前段位，`Facts.Grade` 只在经验为空时用于取下限；逃离强制按负；`CountsTowardDaily` = 记录频道且非平局（第 51 胜/第 11 负仍计数，只是经验 0）。
  - 真实表每段胜经验 1000，从一致状态出发一局最多跨一段、碰不到下限 0，所以"跨多段"与"下限 0"用合成陡表证明，真实表覆盖其余每一行（含段 19→20、段 1→0、段 20→19）。
  - 地图 basic data 其余 6 列语义未证，不使用上游的"第 5 列=可用"过滤（A21 中 order 内全为 1，结果相同）。
- Checks（`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`）：
  - `dotnet build Server/86JP.Server.slnx`：已成功生成（无警告）。
  - Infrastructure.Tests `~PvfPvpContentSourceTests`：13 通过 0 跳过。
  - Application.Tests `~Dfo.Server.Application.Tests.Pvp`：40 通过 0 跳过。
  - Host.Tests `ContentBoundaryArchitectureTests|RegistrationArchitectureTests|RequiredDependencyArchitectureTests|ContentWarmupTests`：18 通过（`ContentWarmupTests.W2` 首跑因新内容源未登记失败，登记后通过）。
  - Host.Tests 全量：首跑 2 失败——`GmEventsHttpTests.Actual_A21_archive_…` 属基线失败；`GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs` 因预热名单缺新项失败，补登记后与 `ContentWarmupTests` 共 8 项重跑通过。
  - Application.Tests `GameplayStateCensusTests|CommandHandlerArchitectureTests|DependencyGraph|RequiredDependency`：66 通过；`DependencyGraphArchitectureTests|RegistryExistenceTests`：9 通过。
- Remaining:
  - 与 TASK 假设不符："预热失败即启动失败"不成立——`Dfo.Server.GameServer/ContentWarmup.cs` 的既定设计是预热失败只记 Warning、继续启动。现在缺段表现为预热报警 + 首次使用即抛。若要满足 Spec"PVF 缺失必需段时启动失败"，需后续单元加显式启动检查（可与"频道脚本出现未登记决斗环境即启动失败"同一处做），本单元未改 ContentWarmup。
  - 频道分类覆盖测试只覆盖按登录环境分类的路径（0..255 中被判为 PvP 的恰为 8/13/24）；`ChannelEntryOptions.FreeDuelChannelId`+`FreeDuelEnabled` 可把任意环境（含 null）的频道判为 FreeDuel，那时 `EntitlementsOf(environment)` 会抛。下游取权益时需决定这种配置的处理（启动校验拒绝或按频道身份取权益）。
  - `[pvp experience loss] 20 0` 另有"段 ≥20 输不扣"的可能读法，与 Spec 5b 相反；按 Spec 执行，已记入 research/pvf-pvp.md，实机可留意。

## Result Review

- Verdict: approved（独立快速审查，本地范围）。自跑：Application.Tests Pvp+普查+CMD 架构 123 项、Infrastructure `PvfPvpContentSourceTests` 13 项、Host 架构/预热 20 项全过。
- 处理：`ContentWarmupTests.cs` 换行恢复为 CRLF（只留 3 处语义改动）。
- 移交控制器：启动期校验（未登记决斗环境、PVF 缺段即启动失败）与 FreeDuel 按频道号配置的权益归属未被任何单元认领——由 Plan 修订指派。
