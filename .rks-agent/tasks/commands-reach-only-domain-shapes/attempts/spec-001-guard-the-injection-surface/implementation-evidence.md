# 实现证据：守住注入面

实现阶段选用会话待办控制七个任务；依赖关系已由 `plan.md` 编码，完整变更评审由 Profile 的
review 阶段独立执行。本文件累积各任务的证据，供 review 与 verification 取用。

## T-001：三个没有消费方的接缝与只写不读的角色副本不再存在

### 做了什么

| 退役对象 | 处置 |
|---|---|
| `IActiveCharacterAccessDomainService` + `IActiveCharacterWriteScope` | 接口与实现文件整体删除，注册删除 |
| `IAccountCargoGoldService` | 接口与实现文件整体删除，注册删除 |
| `IEquipmentEligibilityCharacterReader` | 接口删除；`CharacterDomainService` 不再声明它，其实现成员删除；`EquipmentEligibilityCharacterFacts` 记录随之退役 |
| `IActiveCharacterDomainService` | 收窄到仍有调用方的两个成员 |
| `OnlineStateStore` | 删除 `Publish(AccountId,…)`、`GetRequiredCharacter`、`ReadCharacter`、`ReadCharacters`、`TryApplyCharacters` |

### 计划没有预料到的一件事（执行期发现）

计划把这项当作「删三个接缝加它们的测试」。实际比这深一层：退役访问接缝之后，
`OnlineStateStore` 的 `byCharacter` 反向索引与注册项里那份完整 `ActiveCharacterState`
**双双失去全部读取方**——它们唯一的读者正是被退役的那个接缝。

于是本任务的范围据实扩大到把注册项收敛成它真正被读到的部分：

```csharp
private sealed record ActiveCharacterRegistration(
    OnlineUserId OnlineUserId,
    SelectedSessionIdentity? Session);
```

`byCharacter` 整个退役。发布方法随之从 `Publish(SelectedSessionIdentity, ActiveCharacterState)`
收窄为 `Publish(SelectedSessionIdentity)`——会话本身就带着角色标识，原先那道
「传入的角色与会话不符」的校验因此从「运行时检查」变成「结构上不可能」。

**这没有削弱可观察性。** 「某账号正在玩哪个角色」有一条生产在用的幸存路径：
`Publish` 内部 `owner.BindCharacter(session.CharacterId)` 写的是在线用户快照的
`CurrentCharacterId`，而该快照由 `GetRequiredUser` / `TryGetByConnection` 返回，两者生产在用。
六处原先经已死路径观察该事实的测试断言，改为经这条活路径观察。

### 断言强度：变异验证

改动测试断言有削弱风险，因此对改后的判据做了变异验证——把生产代码逐一改坏，确认仍被抓住
（三处变异均已还原，`git diff -- Server/src` 无残留）：

| 注入的缺陷 | 被判据抓住的用例数 |
|---|---|
| `TryDisconnect` 不调用 `RemoveActive` | 1 |
| `ClearExact` 不调用 `UnbindCharacter` | 4 |
| `Publish` 不调用 `BindCharacter` | 6 |

### 验证

| 检查 | 结果 |
|---|---|
| CK-001 构建 | 0 警告 0 错误 |
| CK-002 全量判据 | 通过 1994 / 跳过 2 / 失败 0 |
| CK-003 用例数对账 | 基线通过 2015，现 1994，**差 21**；被删三个测试文件的用例数为 17 + 3 + 1 = **21**，逐条吻合，无未交代的减少 |
| CK-004 注册项字段读出点 | `OnlineUserId` 由 `TryDisconnect`、`ClearExact` 读；`Session` 由 `ClearExact` 读。无只写不读的字段 |

`OnlineStateStore` 现存的全部公开成员逐个复核，均有存活调用方。

### 留给后续的（本 Spec 范围外，如实记下以免被静默丢弃）

`Server/tests/Dfo.Server.Application.Tests/Support/EntryServices.cs:34` 声明了
`activeCharacters` 参数，但第 46-63 行的构造调用**并没有把它传下去**。这在基线
`9c0de6c6` 上即如此，不是本次造成的。后果是 `CharacterEntryInventoryTests` 里那批
`PublishCount` / `GetRequired` 断言在结构上恒真——那条路径根本不可能发布。本次按原样保留
并补强（把原先隐含在替身覆写行为里的 `PublishCount == 0` 显式写出），但它们目前是无效断言。
接线上去还是删掉这层替身，属于测试自身的缺陷修复，与本 Spec 的注入面无关。

## T-002 / T-003：让名字说出类别

两项同为纯改名，风险面与判据一致，合并执行。

### 领域 store 家族（T-002）

`IStoreSession<,,>` → `IDomainStoreSession<,,>`；三个 store 接口、三个会话接口、三个 Sqlite
实现及其会话实现全部改为 `*DomainStore` / `*DomainStoreSession` 名族，文件名以 `git mv` 随之，
历史保留。执行中额外发现并一并处置的词根衍生类型六个（两个测试替身、两个替身会话、两个
`<Type>Tests` 镜像），另加一处 `.csproj` 的链接编译引用。

`IDomainStore` 与 `IDomainStore<out TSession>` 本身名字已经正确，未动。

### 两个形状正确但名字没说出类别的接缝（T-003）

`ICharacterExperienceGrant` → `ICharacterExperienceDomainService`，
`IExperienceBonusService` → `IExperienceBonusDomainService`，实现类与其 `<Type>Tests` 随之。
同文件内的 `ExperienceAward` 等数据记录未动——它们是记录不是服务。

### 反向核对：仓库中每一个名字含 `Store` 的类型（CK-007）

| 类别 | 类型 | 处置 |
|---|---|---|
| 领域 store | 上述 17 个 | 已改名，可由 `DomainStore` 后缀辨认 |
| 领域 store 的基类型 | `IDomainStore`、`IDomainStore<out TSession>` | 名字已正确，未动 |
| **摆摊（专家商店）** | `IExpertStoreRegistry`、`ExpertStoreRegistry`、`StoreOwner`、`ExpertStoreEntryState`、`ExpertStoreClosedMessage`、`HasOpenStore`、`EncodeExpertStoreClose`、`ExpertStoreClosed`、`PrivateStore` | **一字未动**。这里的 store 指玩家摆的摊 |
| 内存容器 | `OnlineStateStore` | 未动。它的一词多义归后续处理 |
| PVF 内部 | `ChunkStore` | 未动，与领域层无关 |
| 被存的数据记录 | `StoredCreature`、`StoredItem`、`StoredActiveQuest`、`StoredClearedQuest`、`StoredTrackedQuest`、`StoredCarryLimitOnly` | 未动。它们是被存的东西，不是 store |
| 与 store 相关但本身不是 store | `AmbientStoreAccess`（取用器）、`StoreBackedItemContainer`（实例形状）、`ISqliteStoreFacet`（Sqlite 参与者的内部切面） | 未动 |

三类含义彼此可仅凭名字区分，摆摊语义完好。`ISqliteStoreFacet` 的名字同样没说出类别，
但它是 `Persistence` 内部类型、不跨层，记为下一轮词汇收敛的候选而非本 Spec 的缺口。

### 改名未夹带行为改动的证明

把本任务独有文件的 diff 取出，对删除行套用同一套改名映射后与新增行逐行比对：
**153 行对 153 行完全相等**。即除标识符之外一行未动——签名、成员集合、断言、期望值都没变。

另外临时以 `-p:GenerateDocumentationFile=true` 做了一次只读校验（本仓平时不开该开关，
编译器因此不校验 `<see cref>`）：四处涉及改名的 cref 全部解析成功。报出的四个 CS1574
在改名前就存在，与本次无关，未顺手修改。

### 验证

| 检查 | 结果 |
|---|---|
| CK-005 / CK-008 构建 | 0 警告 0 错误 |
| CK-006 / CK-009 全量判据 | 通过 1994 / 跳过 2 / 失败 0，与 T-001 后完全一致 |
| CK-007 名称含义对照 | 见上表 |

## T-004：五个跨层使用的内部服务住进接缝层

五个接口移入 `Dfo.Server.Abstractions` 并补 `DomainService` 后缀，实现留在 `Application` 原处
只改声明的接口名。四个支撑 record 移入 `Dfo.Server.Data`。

**执行中的最小必要扩展**：`SelectedSessionCleanupNotificationPlan` 的字段类型
（`SelectedSessionCleanupNoticeKind`、`SelectedSessionCleanupNotice`、
`SelectedSessionCleanupNotificationTarget`）本身也住在 `Application`，不一并搬走 `Data` 就编译
不过。三者都是纯数据、只依赖 `NetworkConnectionId`，按最小必要范围一并搬入，明示于此。
接口签名的其余依赖本来就在 `Data`，没有别的东西需要搬。

**未夹带行为改动的证明**：对本次 diff 施加「五个接口改名」映射后，排除 `using` / `namespace` /
空行，**实质删除行 93 与实质新增行 93 逐字符完全对消**，两侧各自独有的行均为 0。

| 检查 | 结果 |
|---|---|
| CK-010 构建 | 0 警告 0 错误 |
| CK-011 全量判据 | 通过 1994 / 跳过 2 / 失败 0 |
| CK-012 `Application` 中是否仍有被 `Protocol` 注入的接口 | 无。`Protocol` 现在引用 `Application` 只剩三类：命令总线机制（38 处）、异常类型 `SelectedSessionCleanupFailure`（3 处）、两个并发闸具体类（2 处，注入点是消息 handler 与连接观察者，**不是命令处理器**，不在本 Spec 口径内） |

## T-005：命令处理器不再够到 store 以下与容器

三处，全部把用法收进拥有那件事的领域服务：

| 处 | 改法 |
|---|---|
| 教学升级处理器注入行网关 | `ICharacterExperienceDomainService` 新增 `TrySetProgressAsync`——该实现本就持有行网关，等级与经验的写入归它是恰当归属而非搬家。处理器对「行未写入」的拒绝语义原样保留 |
| 准备处理器注入持久化接缝 | 出生点的解析、写入会话结果、按频道策略落库三步折进 `ISelectCharacterPreparationDomainService.PlaceAtSpawnAsync`。它们本就是一件事，拆成三步意味着调用方持有存储接缝、且能只做其中两步 |
| 准备与补偿处理器注入会话容器 | 查找、替换登记、精确释放收进 `ISelectCharacterSessionEntryDomainService`（该实现本就持有容器） |

**CK-015 次序安全性**：折叠把落库从「租约准备之后」移到「之前」。
`SelectCharacterEntryInventoryDomainService.PrepareLeaseAsync` 全文只读 `session`，返回
`result with { CharacterInventory = reloaded }`，**既不读也不写角色位置**；两处副作用同属一个
`CommandExecutionKind.Transactional` 命令，一起收口。故次序变化不可观察。
另有独立佐证：生命周期用例的 ledger 断言带顺序，而 `PrepareLeaseAsync` 在替身里不写 ledger，
`["prepare", "position", "static", "inventory", ...]` 序列在折叠前后完全一致。

| 检查 | 结果 |
|---|---|
| CK-013 构建 | 0 警告 0 错误 |
| CK-014 全量判据 | 通过 1994 / 跳过 2 / 失败 0 |
| CK-021 命令处理器上的会话容器 | `ISelectedSessionDirectory` 不再出现在任何命令处理器构造器上 |

## T-006：判据守住注入面

判定规则：**参数类型是接口 + 声明在 `Dfo.Server.Abstractions` + 名字以 `DomainService` 或
`DomainStore` 结尾**，否则须命中具名豁免清单。两个条件缺一不可——只看后缀，`Application` 或
`Protocol` 里的类型可以自称接缝；只看程序集，接缝层同时声明的行网关与注册表会被放行。

Spec 里的「交出实例的工厂」不需要单独一类：`ICharacterDomainService.Get(id)` 之类本身就是领域
服务，实例是它们交出来的返回值。三类因此收敛为两类加豁免。

具名豁免清单目前只有 `TimeProvider`，理由与它写在一起，并有一条用例断言每项豁免都带理由。

| 检查 | 结果 |
|---|---|
| CK-016 判据 | 新增 10 条用例全部通过；`ProductionCommandHandlers_OnlyTakeDomainShapes` 报告**零违规** |
| CK-017 全部构造器参数枚举 | 27 个处理器（其中一个零依赖）的每一个参数都是 `I*DomainService`；唯一的非领域形状是 `SelectCardCommandHandler` 的 `TimeProvider clock`，命中豁免 |
| CK-018 判定不依赖人工类型名单 | 判定是一个对任意 `Type` 求值的谓词；豁免清单是唯一的具名例外 |

**可证伪性（RQ-005）**：新增 `ATypeOutsideTheVocabularyIsRefused`，把行网关
（`ICharacterTable`）、持久化接缝（`ISelectedSessionPositionPersistence`）、运行时容器
（`ISelectedSessionDirectory`）、命令总线与一个普通 BCL 类型逐个喂给判定谓词，断言全被拒。
这五个正是这个仓库的注入面实际出过问题的方式。若无这条，一个什么都放行的规则会和一个没有
违规的仓库看起来一模一样。

## T-007：仓库约定说出这套词汇表

`AGENTS.md` 四处改动：

1. 分层一节后新增「命令处理器只够得到领域形状」：两种可注入形状的表、实例与快照为何不在表上、
   哪些一律不许注入、具名豁免，以及守它的那条判据与「它按类型判定而不是按名单」的理由。
2. 后缀表：`*Store` 一行改为 `*DomainStore` 并写明它是可注入的两种形状之一；
   `*Repository` / `*Registry` / `*Directory` / `*Persistence` 一行补上**不得注入命令处理器**。
3. 五个内部服务的记载据实改写。原文称它们「不跨层暴露」，而 `Protocol` 的命令处理器一直在跨层
   注入——记载与代码冲突。改为如实说明它们现在住在接缝层，并留下一句教训：一句「不跨层」的
   记载不会阻止跨层，只会让下一个读它的人以为没有跨层。
4. 「分层问题看项目引用图，别找分层测试（不存在）」改为指向新落地的判据，并明确它**只覆盖
   命令处理器**；普通消息 handler 的能力清单仍然只写在文档里，没有判据守着。

| 检查 | 结果 |
|---|---|
| CK-019 记载与代码逐条核对 | 四处改动均对应已完成的代码事实 |
| CK-020 全量判据 | 通过 2004 / 跳过 2 / 失败 0 |

## 实现阶段收口

最终全量：**通过 2004 / 跳过 2 / 失败 0**（52 + 488 + 60 + 255 + 6 + 221 + 922）。

用例数对账：基线 2015 − 随死件移除 21 + 白名单新增 10 = **2004**，精确吻合。

## 留给后续的（本 Spec 范围外）

- `Server/tests/.../Support/EntryServices.cs` 声明了 `activeCharacters` 参数却没有传下去，
  导致 `CharacterEntryInventoryTests` 里一批断言结构上恒真。基线即如此。
- `OnlineUserExecutionGate` / `SelectedSessionTransitionGate` 是 `Application` 的两个具体类被
  跨层注入。注入点是消息 handler 与连接观察者，**不是命令处理器**，不在本 Spec 口径内。
- `Protocol` 有两处已死的 `using Dfo.Server.Application.Inventory`，基线即如此。
- `ISqliteStoreFacet` 的名字同样没说出类别。它是 `Persistence` 内部类型、不跨层，
  记为下一轮词汇收敛的候选。

## 评审整改（第一轮 `changes_required` 的两条阻断）

### BF-1：`AGENTS.md` 留下四个已删除的类型名

`AGENTS.md:194-198` 的载体协议一段仍写着 `IStoreSession<TPlace,TKey,TValue>`、
`IItemStoreSession`、`ICharacterStoreSession`、`IQuestStoreSession`，四者全仓零命中——这是本次
改名**新造出来**的文档与代码矛盾，正是 RQ-008 要根除的那一类。四处改为改名后的名字。

改完复扫：`AGENTS.md` 中已删除或改名的类型名命中数全部为 0。

顺带扫了 `Docs/`，另有三处提到旧名，**都不改**，因为它们是带日期的历史记载而不是现行约定：
`Docs/ProtocolScaffoldDissonances.md:224` 位于「2026-08-27 已收口」的条目内，
`Docs/Refactor/TableOwnershipRuling.md` 是一次已完成裁决的存档。改它们等于篡改当时的记载。

### BF-2：出生点落位/落库搬家后生产侧零覆盖

评审的判断成立且严重：这段逻辑原先写在命令处理器里、被生命周期用例真实驱动；折叠进
`SelectCharacterPreparationService.PlaceAtSpawnAsync` 之后，生命周期用例调的是替身，而替身把
生产逻辑逐行抄了一遍。那三条位置断言从此守的是复制品。**这是本次重构造成的覆盖回退。**

补一条直接驱动真实服务的用例（`CharacterEntryInventoryTests`，`[Theory]` 两个分支）：用
`EntryServices.Create(..., positions:)` 装配真实的准备服务，取得真实的入场结果，翻转
`World.Channel.PersistsPosition` 后调 `PlaceAtSpawnAsync`，断言角色位置被盖成出生点、且只在
频道保存位置时才落库。

**变异验证**：把生产实现里的 `if (placed.World.Channel.PersistsPosition)` 改成 `if (true)`，
新用例立刻 **1 失败 / 1 通过**；还原后恢复全绿。即它确实在守生产代码，不是恒真断言。

### 一并采纳的非阻断建议

判据新增 `TheSweepFindsTheCommandHandlers`：断言反射遍历**非空**。没有它，一次不再匹配的反射
查询（契约改名、程序集移动）会让底下每一条检查都退化成「对空序列的断言」，而空序列里没有违规。

### 整改后判据

**通过 2007 / 跳过 2 / 失败 0**（52 + 488 + 60 + 255 + 6 + 221 + 925）。

用例数对账：基线 2015 − 随死件移除 21 + 白名单新增 10 + 整改新增 3 = **2007**，精确吻合。
（整改新增 3 = `PlaceAtSpawn` 两个分支 + 遍历非空一条。）
