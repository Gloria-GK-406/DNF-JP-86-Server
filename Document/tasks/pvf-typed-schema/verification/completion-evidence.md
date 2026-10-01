---
kind: rks-verification
decision: pass
---
# PVF 全量迁移完成验证

## 验证结论

TaskContract revision 1 的全量固定字段迁移、类型与形状保护、合法档案兼容、分层和完整自动验收通过。2026-10-01，第四轮独立批准之后，对同一冻结源码执行 Release 10 项最终检查；全部 exit 0。结果汇总为 [final-summary.json](final-summary.json)，逐命令及起止时间为 [final-checks.json](final-checks.json)。

本记录与 spec-result、完成游标一起作为唯一 accepted-result 提交的候选内容；完成状态仅在该提交成功且仓库后置检查通过时生效。新提交身份及 clean/parent/source-HEAD 的实际证明由控制器在接受后返回，不向包含本记录的提交回填自己的 SHA。

## 成功条件

| 条件 | 决定性证据 |
|---|---|
| SC-1 / AC-001 | 当前 [覆盖清单](../content-coverage.md)：53 个内容主题、476 个具名声明，另有字段族与 Probe 固定 Name。check-field-access.py PASS，检查1020个源码文件；第四轮独立审查核对完整固定/动态边界。 |
| SC-2 / AC-002、003 | 核心80项、内容739项均通过，包括缺失与坏型区分、截断、范围、非有限数、复杂行、根层/嵌套、合法重复投影、每次声明验证，以及技能fixed-level与coupon多参数生产入口回归。晚期好值不能掩盖早期坏值。 |
| SC-3 / AC-004 | 多档案不同字符串偏移共享同一声明的核心回归通过。现场项目引用检查：Dfo.Pvf无项目引用/游戏命名空间；Application仅Abstractions/Data；Infrastructure引用Pvf/Abstractions/Data。无跨档案静态offset。 |
| SC-4 / AC-005 | 下表8项目完整Release运行5198通过/0失败/2既有条件跳过。真实PVF只读。Application/Data/effect没有本次源码改动。真实副本、任务、技能、商城、装备等兼容回归通过。 |
| SC-5 / AC-006 | [当前独立报告](../reviews/change-review.md) approved；源码指纹与审查目标一致；审查后10项新鲜检查全通过，git diff --check PASS；完整产品/测试/契约/历史与最终证据由同一接受操作提交。控制器仅在一次提交及clean/source不变证明成功后封存完成。 |

## 最终 Release 结果

| 项目 | 通过 | 失败 | 跳过 | 原始证据 |
|---|---:|---:|---:|---|
| Dfo.Pvf | 80 | 0 | 0 | [TRX](final-Dfo.Pvf.trx) |
| Infrastructure | 739 | 0 | 2 | [TRX](final-Dfo.Server.Infrastructure.trx) |
| Application | 2599 | 0 | 0 | [TRX](final-Dfo.Server.Application.trx) |
| Persistence | 392 | 0 | 0 | [TRX](final-Dfo.Server.Persistence.trx) |
| Protocol | 1226 | 0 | 0 | [TRX](final-Dfo.Server.Protocol.trx) |
| Networking | 86 | 0 | 0 | [TRX](final-Dfo.Server.Networking.trx) |
| HttpApi | 50 | 0 | 0 | [TRX](final-Dfo.Server.HttpApi.trx) |
| Host | 26 | 0 | 0 | [TRX](final-Dfo.Server.Host.trx) |
| 合计 | 5198 | 0 | 2 | [汇总](final-summary.json) |

计数来自每条 UnitTestResult 的 outcome；Infrastructure Counters.notExecuted 为0，不能仅使用Counters而遗漏实际2条NotExecuted。跳过为 Town/Quest 的 `A_missing_archive_*`，真实档案已提供，该条件不适用。基线Infrastructure同为2项跳过；没有新增失败或其他跳过。

最终命令串行执行：`dotnet build Server/86JP.Server.slnx -c Release --no-restore`、上述8项目分别 `dotnet test ... -c Release --no-build --no-restore`、`dotnet build Tool/PvfProbe/PvfProbe.csproj -c Release --no-restore`。服务端与Probe构建均0警告/0错误，完整日志位于本目录 `final-*.log`。复跑脚本为 [run-final-checks.ps1](run-final-checks.ps1)。

## 目标身份与边界

- 接受前任务HEAD：`828e769600122f23db2b244e8cfc44ef400d0ffe`；分支 `task/solo-play-roadmap`。
- 源checkout HEAD：`10ccbad248495abc80dc0ce8df86b3d552808e86`，本任务不修改源分支。
- 72个改动源码/项目文件的规范指纹：`b57df3617b80064a915bf10ee15e8ffb2df0400f599bb26c723b10ea78c38003`。Python sorted POSIX相对路径、每行path加竖线及小写文件SHA256、LF及末尾LF、UTF8再SHA256；与第四轮报告一致。
- `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；档案SHA256现场复核为 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`。
- 固定字段迁移完整；运行时外部Probe查询、内容枚举值和未知scope算法不属于固定字段。Tokens声明的专业解析责任保留，由主题解析器校验完整形状，不宣称Tokens自动完成所有校验。
- [实现记录](../implementation.md)和本目录历史TRX如实保留失败。首轮最终Release失败证据在 [attempt-1-release](attempt-1-release)，不会用本轮成功改写历史。
- 本次不建立ClientVerified结论；客户端/PVF/数据库/服务未变更。按契约保留任务分支与工作区，不部署、推送、合并或清理。

暂存后的完整检查使用 `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --cached --check`，明确接受 .editorconfig 规定的 CRLF。默认暂存check曾把新文件CRLF误判为尾随空白；额外发现的execution YAML末尾空行已移除，复核必须通过。此格式检查调整不改源码字节或复审目标。
