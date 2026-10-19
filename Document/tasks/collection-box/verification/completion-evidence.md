---
kind: rks-verification
decision: pass
---

# 常驻收集箱完成验证

日期：2026-10-19。目标为TaskContract revision 2的SC-1至SC-7软件交付；不声明原生客户端验收、真实服部署或实际PVF替换。

- 基线：`bf283b868cef249ca731e5555b64d8d80202a556`
- 规划接管检查点：`7ca2d8a7fb77248d954aed98e68df6a14fdaeec4`
- 固定实现：`024acb76ed46cf6b35687ac836721af4b64caef6`
- 独立完整审核：[change-review.md](../reviews/change-review.md)，`approved`，绑定上述实现和契约revision 2，无未解决阻断。
- 运行目录：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/collection-box`
- 真实PVF：`E:/Project/DNF/DNF-JP-86-Server/artifacts/wedding-ticket-pvf-fix-20261018/Script.server.pvf`，SHA-256 `BCF99E4E4CC691D43DD89373519EF67D632B83FB0BDAD560454E101415768D63`；仅只读使用，未修改。

## 条件与决定性证据

| 条件 | 结果与证据 |
|---|---|
| SC-1：动态系列、空位和错误定义 | 当前三系列在`Real_catalog_piles_round_trip_without_reconstructing_any_item_fields`同一路径往返；合成第四系列的`New_series_and_selection_use_the_same_persistence_without_cross_character_leakage`及目录校验覆盖扩展、洞、重复和容量限制。最终真实PVF应用回归通过。 |
| SC-2：原物、原子性和拒绝 | 三套完整快照往返、满包无邮件、期限与锁拒绝、转移后选择写故障回滚均在真实SQLite中检查；持久层防止账号拥有241或同格合堆两件。测试检查数据库事实及实例字段，不仅检查返回值。 |
| SC-3：持久选择、登录和隔离 | 应用合成系列测试跨事务和角色读取，协议`Entry_appends_all_real_series_and_restores_persisted_selection_after_fixed_compatibility_frames`观察选角完整帧，永久快照测试确认先全箱、后仅选中箱激活。 |
| SC-4：协议、永久和0381 | `CollectionBoxCodecTests`检查严格1/8/5/8字节、保留字节、ACK、8+4n03EB和两个固定0381；dispatcher覆盖放入顺序、永久延期不消费及错误码映射。证据为ClientStatic和自动检查，不是A21实帧证明。 |
| SC-5：提交重放与隐藏隔离 | 提交后丢失投递再请求只恢复当前事实，不二次取物；选择重放只发箱状态，不带库存或旧ACK；替换物理连接不接收原ACK；普通库存排除241但数据库原行保留。 |
| SC-6：兼容和升级 | `Conflicting_definitions_and_legacy_rows_preserve_assets_and_do_not_invent_instances`检查旧itemId行与定义冲突，原物保留／不制造实例。交付说明记录旧二进制不能直接读新库及成套恢复代价。 |
| SC-7：构建、回归、独立审核和实机步骤 | 构建、固定实现的最终全套回归与下表补充检查通过；完整审核approved，交付说明含实机步骤。所有软件交付条件证据满足。 |

## 命令与结果

构建／需重建的项目使用 `-m:1 -nodeReuse:false /p:BuildInParallel=false /p:NuGetAudit=false --nologo`（记为B）；`NuGetAudit=false`仅本次CLI参数，网络审计受限时不改仓库策略。已经构建的测试使用 `--no-build --no-restore -m:1 -nodeReuse:false --nologo`（记为N）。测试均串行执行，使用可正常运行vstest本地通信的执行环境。

真实PVF行在该进程设置`PVF_ARCHIVE_PATH`为上述档案；无PVF行先清空该环境变量。各行命令前缀均为`dotnet`，项目路径完整列出，不存在名称过滤隐瞒失败。

| 命令（相对运行目录） | 环境 | Exit | 结果 | 原始日志 |
|---|---|---|---|---|
| `test Server/86JP.Server.slnx N` | 真实PVF | 0 | 7581通过、0失败、2条件跳过；PVF80、Application4154、Host165、HttpApi50、Infrastructure939/2skip、Networking94、Persistence475、Protocol1624 | [final solution](logs/collection-solution-accepted.log) |
| `test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj B` | 真实PVF | 0 | 4154通过，0失败，0跳过 | [application](logs/collection-application-accepted.log) |
| `test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj B` | 真实PVF | 0 | 1624通过，0失败，0跳过 | [protocol](logs/collection-protocol-accepted.log) |
| `build Server/86JP.Server.slnx B` | 真实PVF | 0 | 0警告、0错误 | [build](logs/collection-solution-build-accepted.log) |
| `test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj N --filter FullyQualifiedName~CollectionBox` | 无PVF | 0 | 13通过，4资源用例跳过，0失败 | [collection no-PVF](logs/collection-final-no-pvf-focused.log) |
| `test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj N --filter FullyQualifiedName~A_missing_archive` | 无PVF | 0 | 2通过，0失败，覆盖有PVF时跳过的两个缺失档案检查 | [missing archive](logs/collection-missing-archive-accepted.log) |
| `test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj N` | 无PVF | 0 | 1559通过、60资源跳过，0失败 | [protocol no-PVF](logs/collection-protocol-no-pvf-accepted.log) |

补充的中期修复回归：持久层全量475通过、exit0（[日志](logs/collection-persistence-final.log)）；无PVF应用全量3632通过、433资源跳过、exit0（[日志](logs/collection-application-no-pvf-final.log)）。这两份日志保留各自当时的覆盖边界；最终实现以本表最新检查及最终解决方案检查共同判断。无PVF的资源用例跳过不当作通过；对应资源行为由真实PVF路径覆盖。不同资源条件会影响theory数据展开数量，不能将不同运行总数简单相减当作遗漏数。

## RED与修复证据

- 隐藏容器泄入普通库存：新增真实读取测试先exit1，预期1行实际2行（[RED](logs/collection-red-unrestricted.log)）；白名单修复后同过滤重建exit0、1通过，隐藏资产原行仍在（[GREEN](logs/collection-green.log)）。该测试观察外部行为和持久事实，构成TDD保护。
- 原ACK按复用连接ID落到替换连接：行为测试先exit1、物理连接引用不符（[RED](logs/collection-ack-red.log)）；改为原物理连接同批发送后，收集箱无PVF回归7通过/4跳过、exit0（[GREEN](logs/collection-ack-green.log)），最终应用套件另含拒绝前替换连接检查。
- 首轮解决方案为exit1（[历史日志](logs/collection-solution-regression.log)）：新增选择表后Schema清单漏加，以及持久层当轮旧产物仍允许隐藏格合堆2件；修正后持久层475通过。后续应用回归曾为4151通过/1失败，选择重放实际9帧而非3（[历史日志](logs/collection-application-review-final.log)）；修复空库存足迹记录后最新4154通过。这些红灯均不作为最终通过证据。
- 首次NuGet网络审计失败和沙盒vstest挂起属于启动失败，不计为行为RED；其余新增用例是实现后回归，不宣称全部为TDD。

## 实机与运行边界

[实现与实机验收说明](../../../CollectionBox-Implementation-20261019.md)提供三套存取、重登、面板单箱效果、满包、锁／期限、延期及双向抓包步骤。错误提示含义、严格时序及原生加成实际生效尚待用户实机确认；当前保持`CurrentImplementation + ClientStatic`，未新增`WireCaptured`或`ClientVerified`。

未修改真实玩家库、实际PVF或客户端二进制；未部署、重启、发布、远端推送或合并。上述实机结果属于明确排除的软件交付之外，不通过自动检查代替。

## Repository Evidence and Decision

最终检查时HEAD为固定实现`024acb76ed46cf6b35687ac836721af4b64caef6`，其parent为规划接管检查点`7ca2d8a7fb77248d954aed98e68df6a14fdaeec4`。产品、测试、协议和交付说明相对该HEAD无差异；源分支`task/next-round-20261019`仍指向基线。审核精确绑定同一实现，`git diff --check`无输出。测试日志在本任务目录内原样归档并逐份核对SHA-256与原始日志相同。作者文档使用标准diff whitespace检查；原始日志保留Windows CRLF及工具末尾空行，单次检查对日志启用cr-at-eol并禁用blank-at-eof，不修改仓库配置或改写原始输出。

决定为`pass`：SC-1至SC-7的软件交付条件均有当前证据。最终解决方案exit0关闭了首轮exit1；两个环境跳过案例另在无PVF条件下exit0。后续seal仅提交审核、此证据与spec-result，不改变已验证产品。收尾保持任务分支及工作区，合并或清理等待用户另行选择。
