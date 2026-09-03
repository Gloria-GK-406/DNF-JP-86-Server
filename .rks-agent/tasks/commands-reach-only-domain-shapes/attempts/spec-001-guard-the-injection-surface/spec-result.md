---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 守住注入面

## Delivered

- 命令处理器的每一个构造器依赖都能仅凭类型说清自己是什么：接口 + 声明在
  `Dfo.Server.Abstractions` + 后缀 `DomainService` 或 `DomainStore`，否则须命中一张具名豁免
  清单（当前只有 `TimeProvider`，理由与它写在一起）。27 个处理器、55 个参数、零违规。
- 这条约束由判据守着，而不是靠人的记性。判据按类型判定而非按人工名单，并自带证明它会拒绝的
  用例，以及一条防止反射失配后退化成恒真的遍历非空断言。
- 三个真正的领域 store 与其会话类型改名为 `*DomainStore` / `*DomainStoreSession`；「摆摊」
  与「内存容器」两种同名不同义的东西一字未动，含义仍可从名字读出。
- 两处硬违规与一处落在批次之间的漏项消除：教学升级的落库归给已持有行网关的经验领域服务；
  出生点的解析、盖章与落库折进准备用领域服务；会话的注册与释放收进会话入场领域服务。
- 三个零消费方接缝退役。连带发现并处置：在线表注册项里那份**只写不读**的完整角色状态，
  以及随之失去全部读取方的角色到账号反向索引。
- `AGENTS.md` 记下这套词汇表与守它的判据，并订正了一处与代码冲突的记载——它曾声称五个
  `Application` 内部服务「不跨层暴露」，而协议层的命令处理器一直在跨层注入它们。

## Success Evidence

| Condition | Evidence |
|---|---|
| C-1 | `verification/completion-evidence.md`；`CommandHandlerArchitectureTests` 零违规，独立评审者以外部反射探针复核得同一结论 |
| C-2 | 同上；三个禁用类型均不在任何命令处理器构造器上 |
| C-3 | `IsDomainShape` 是对任意 `Type` 求值的谓词，程序集与后缀两个条件缺一不可；豁免清单为唯一例外 |
| C-4 | `ATypeOutsideTheVocabularyIsRefused` 与 `TheSweepFindsTheCommandHandlers`；独立评审者另做三次生产代码变异，逐次被抓 |
| C-5 | 仓库中名字含 `Store` 的类型逐个分类对照，见 `implementation-evidence.md` |
| C-6 | 五个标识符全仓 0 处；注册项两个保留字段各有存活读出点 |
| C-7 | 通过 2007 / 跳过 2 / 失败 0；逐文件用例数对账 2015 − 21 + 11 + 2 = 2007 闭合；两次纯改名各自逐行对消自证 |
| C-8 | `AGENTS.md` 四处改动；评审者把其中 140 个反引号标识符逐个反查，唯一失效的一个在基线上即已失效 |

## Repository Result

- Changed paths: 81 项，全部在 `Server/` 与 `AGENTS.md` 之下，外加未跟踪的
  `.rks-agent/tasks/commands-reach-only-domain-shapes/` 产物目录。改动文件无 CRLF 污染。
- Baseline or accepted result: 基线 `9c0de6c689964b5e791b5adb33cf6bc9bcb9b4ed`
- Parent rollback required: no

## Successor Guidance

- **白名单可以被一个「改名壳」满足。** `ISelectCharacterSessionEntryDomainService` 现在有三个
  逐字透传容器动词的成员（`Find` / `RegisterReplacing` / `ReleaseExact`）。命令处理器做的还是
  同样三件容器事，只是隔了一层；容器既没私有化也没改说领域话。这是本成果为消除注入面违规
  付出的代价，**请在「内存容器转领域服务」那个成果里收口**，不要因为白名单已经绿了就当它
  已解决。
- 判据只覆盖**命令处理器**。普通 socket 消息 handler 的能力清单仍然只写在 `AGENTS.md` 里、
  没有判据守着，而它已经漂移到有八个 handler 直接调用领域写操作。
- `Server/tests/Dfo.Server.Application.Tests/Support/EntryServices.cs:34` 声明了
  `activeCharacters` 参数却没有传给被构造的服务，`CharacterEntryInventoryTests` 里因此有一批
  断言结构上恒真。基线即如此，本成果按原样保留并补强，未修。
- `OnlineUserExecutionGate` 与 `SelectedSessionTransitionGate` 是 `Application` 的两个具体类
  被跨层注入。注入点是消息 handler 与连接观察者，不是命令处理器，因此不在本成果口径内。
- `ISqliteStoreFacet` 的名字同样没说出类别（`Persistence` 内部类型、不跨层），
  `AGENTS.md:212` 的 `PersistOnTeardownAsync` 在基线上即已失效，
  `Docs/Refactor/TableOwnershipRuling.md:1262/1264` 的复核路径指引照着走会扑空。三者都不在
  本成果范围，记下以免被遗忘。
- 基线自带一个偶发失败：`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`
  会抛 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，单独复跑其所在项目即绿。它不是回归。

## Artifacts

- `./TaskContract.md`、`./routing-decision.md`、`./sdd-execution.yaml`
- `./spec.md`、`./plan.md`（T-005 含一处执行期计划修订）
- `./implementation-evidence.md`
- `./reviews/change-review.md`（verdict `approved`，两轮）
- `./verification/completion-evidence.md`（decision `pass`）
