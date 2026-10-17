---
kind: rks-verification
decision: pass
---

# Completion Evidence: 婚姻业务收为单一实体，阶段由记录直接表达

## 结论范围

本记录覆盖 TaskContract revision 3 的 SC-1～SC-7，验证对象是 worktree `task/marriage-domain-aggregate` 相对基线 `5a0cbed0` 的全部未提交改动。验证时 `git status --short` 共 77 行：33 个修改、8 个删除、36 个新增，它们构成提交前的完整结果。

实机双端回归按契约属于 Out，由用户执行，本记录不对它做任何宣称。

## 新鲜检查（2026-10-17，控制者执行）

- `dotnet build Server/86JP.Server.slnx`：0 个警告，0 个错误。
- `dotnet test Server/86JP.Server.slnx --no-build --logger "console;verbosity=detailed"`：退出码 0，8 个项目都显示「测试运行成功」，失败 0。日志中 `ObjectDisposedException` 出现 0 次。

  | 项目 | 总数 | 通过 | 跳过 |
  |---|---:|---:|---:|
  | Application | 4009 | 3583 | 426 |
  | Protocol | 1588 | 1528 | 60 |
  | Persistence | 470 | 470 | 0 |
  | Infrastructure | 933 | 596 | 337 |
  | Host | 159 | 149 | 10 |
  | Networking | 92 | 92 | 0 |
  | HttpApi | 50 | 50 | 0 |
  | Pvf | 80 | 71 | 9 |

  跳过的都是 Skippable 用例，原因是本机缺 PVF 或客户端档案，与本次改动无关。日志位于会话 scratchpad 的 `mda-verification-full.log`。
- 搜索命令都在 worktree 根目录执行，范围为 `Server/src`，排除 bin 和 obj：
  - 旧的 8 个实现类名（不含 `IMarriageQuestDomainService`）：0 处；
  - 其余 7 个旧接口名：0 处；
  - `IsReservedAsync`、`.MarriedAsync(`：0 处；
  - 剩下的 `IsMarriedAsync(Guid)` 是 Query 接口方法，实现读的是关系阶段（`MarriageDomainService.Query.cs:68`）；
  - `MarriageWeddingSlice.cs:219` 读的是本次推进结果的 phase，不是从存储推导关系阶段。
- `Dfo.Server.Application.csproj` 相对基线没有改动，没有新增对 Protocol 的引用；文件里唯一一处 Protocol 字样是原有的 `InternalsVisibleTo Dfo.Server.Protocol.Tests`。

## 各成功条件

| SC | 结论 | 决定性证据 |
|---|---|---|
| SC-1 单一实体与按调用方划分的接口 | 通过 | 上面的搜索结果为 0；`MarriageDomainService` 实现 4 个接口，DI 解析出的是同一个实例（`MarriageArchitectureTests` 及 TASK-003 中的同一实例用例，全量测试通过）；调用方的注入清单见 TASK-003 Result |
| SC-2 阶段由记录直接存储，不再推导 | 通过 | 推导路径的搜索结果为 0；阶段规则的穷举测试和 03D0 投影的穷举测试（`MarriageProposalCodecTests.Wire_state_projects_each_stage`）通过；I3、I4 用例通过（`MarriageRelationLedgerTests`） |
| SC-3 组件互不依赖、账本是唯一写入者、CAS | 通过 | `MarriageArchitectureTests` 正例和反例都通过；同 revision 二次提交被拒，以及子记录迁移后关系 CAS 失败时回滚，相关用例通过 |
| SC-4 回填正确、幂等、旧数据完好 | 通过 | `MarriageRelationMigrationTests` 通过（11 行数据覆盖回填表每一种情形，异常计数为 2，重复打开结果不变，integrity 为 ok，旧表内容不变）；真实运行库两份副本的迁移结果为 Married 1、Closed 1，异常 0，原 92 张表不变，见 `evidence/TASK-005-db-copy-migration.txt`；心意点双写用例通过 |
| SC-5 除刻意修正外行为不变 | 通过 | 表征测试基线见 `evidence/TASK-001-baseline.txt`，断言只在批准的位置改动过（独立审查 round 1 逐项核对，见 `reviews/change-review.md`）；全量测试通过，codec 期望字节没有改 |
| SC-6 刻意修正生效 | 通过 | `MarriagePairCorrectionTests` 4 条通过，修正前后的实测对照见 TASK-004 修正清单 B1-1～B1-5 |
| SC-7 构建、全量测试、文档、回归清单 | 通过 | 构建和测试结果见上；设计稿落地状态、`Document/MarriageAggregateRegression-20261017.md`、`Document/MarriageWeddingFlow-20261016.md` 的注记、`protocol/` 三处注记都已存在，独立审查 round 1 和 round 2 核对过引用 |

## 审查

`reviews/change-review.md`：round 1 为 `approved`，round 2（审查后修正）为 `approved`，都没有阻塞项。非阻塞观察 N2 和 R2-1 作为残余记录保留。
