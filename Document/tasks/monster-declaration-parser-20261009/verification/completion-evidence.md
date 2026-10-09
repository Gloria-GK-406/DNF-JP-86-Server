---
kind: rks-verification
decision: pass
---

# 完成证据：怪物声明统一解析

验证对象：task/monster-declaration-parser，基线 8e8192b94f5acc3ab7a471e27ed197d08ae8c7fd 上本任务的最终差异。结论对应 TaskContract SC-1 至 SC-4；实机验收仍交由用户，不作为本次自动化通过的推论。

| 条件 | 结果与证据 |
|---|---|
| SC-1 | 当前权威 PVF map.lst 的 10,293 MAP 全部扫描；普通怪物、APC、可选字段、对象子模板与附属/条件声明盘点见 ../survey.md。70 与上游证据明确分层。 |
| SC-2 | Infrastructure 49 项通过，包括实际 PVF 全量结构测试、22036 五条怪物记录、21314 三十对象模板、合法空/虚拟对象头、变长精英特征、非法记录诊断。 |
| SC-3 | Application 177、Protocol 295 项通过；覆盖 dummy 不计经验/掉落/击杀与清房、team 和 faction 分离、波次不随 Boss suppression 删除、父子编号及普通/APC/深渊投影。独立审查 ../reviews/change-review.md 为 approved。 |
| SC-4 | 2026-10-09 23:11:55 +08:00 核验 PID 32956、六端口归属、两个 HTTP ready、四个程序集版本/哈希一致。当前数据库和配置沿用，停服后备份 db/wal/shm，旧产物保留。 |

## 测试和发布身份

聚焦回归共 **521 passed / 0 failed / 0 skipped**，不是宣称全解决方案测试。Release 构建、真实 PVF 用例已运行；共享 .NET 输出串行构建。

- 实际 PVF SHA-256：03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543；发布后重新读取文件哈希与调查一致。
- 运行版本：0.2.3+monster-declarations.20261009.f2fa6e3e。
- 源快照 SHA-256：F2FA6E3EBAD95D122682677DA3E23E620672FB6943465CD335B8392F9F8080F2；发布后逐项复核清单文件哈希一致。
- 部署证据根目录：E:/Project/DNF/DNF-JP-86-Server/artifacts/monster-declarations-20261009/。
- 测试原始结果：tests/infrastructure.trx、tests/application.trx、tests/protocol.trx。
- 发布源清单：source-manifest.json；部署凭据 deployment.json；stdout.log、stderr.log（验证时 stderr 空）。
- 运行路径：上述根目录下 runtime/Dfo.Server.Host.exe；端口 7001、10010、10011、10100、18080、18081 均归属 PID 32956。
- 数据备份：database-before-20261009-231127/；回滚运行目录：E:/Project/DNF/DNF-JP-86-Server/artifacts/ordinary-maze-preflight-20261009/runtime/。

原固定解析器在实 PVF 22036 抛异常，21314 只有一个 APC、缺少三十子模板；先运行两例失败回归，再实现并完成最终通过。新源未改客户端、PVF 或数据库结构。

复现命令：设置测试支持的实际 PVF 环境变量后，分别运行 `dotnet test Server/tests/<项目>/<项目>.csproj -c Release --nologo --disable-build-servers -m:1 -p:NuGetAudit=false --filter "<下列过滤器>" --logger "trx;LogFileName=<名称>.trx" --results-directory <上述证据目录>/tests`。实际 PVF 环境变量名称以 `Server/tests/Support/PvfAttributes.cs` 与测试文件中的要求为准；必须确认实际 PVF 用例未跳过。

| 项目 | 过滤器 |
|---|---|
| Dfo.Server.Infrastructure.Tests | PvfMapActorDeclarationParserTests\|PvfDungeonActorDeclarationRegressionTests\|PvfDungeonRoomActorSourceTests\|PvfDungeonRoomActorRankTests\|PvfDungeonRoomDropSourceTests |
| Dfo.Server.Application.Tests | DungeonCellFactoryTests\|DungeonRunDomainServiceTests\|DungeonVisitDomainServiceTests\|DungeonStructureRuleServiceTests\|HellDungeonEntryTests\|CycleQuestFlowTests\|CycleDungeonContentTests |
| Dfo.Server.Protocol.Tests | DungeonEntry\|DungeonSelection\|CycleDungeonEntry\|MapMove\|MonsterDeath\|DungeonClear |

## 实机边界

待用户测试：追击歼灭战 Boss 房入场、根特防御战 Boss/波次/清房、普通与主动深渊房间。条件、blood/ultimate/attacked 等声明保留而未实现新触发引擎；10,293 MAP 语法覆盖不等于全地下城机关行为已实现。

## 仓库处置

本任务工作区保留；最终结果只作一次本地提交，提交 ID 由 Git 输出及外部 deployment.json 记录。原 task/achievement-state-events 保持 8e8192b9，四个既有脏文档未修改；main 保持 d0427ce3。Windows CRLF 使用 git -c core.whitespace=cr-at-eol diff --check 验证。
