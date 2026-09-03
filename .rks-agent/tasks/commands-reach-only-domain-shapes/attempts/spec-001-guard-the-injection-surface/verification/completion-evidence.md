---
kind: rks-verification
decision: pass
---

# 完成验证：守住注入面

## 被评估的主张

`spec-001-guard-the-injection-surface` 的八条成功条件（C-1..C-8）全部满足：命令处理器的每一个
构造器依赖都能仅凭类型归类、判据守着这条且违反会红、领域 store 可由名字辨认、三个零消费方
接缝与只写不读的角色副本不复存在、服务器行为逐值不变、仓库约定说出了这套词汇表。

基线 `9c0de6c6`，工作区 `commands-reach-only-domain-shapes`，**变更尚未提交**。

## 新鲜判据

```
dotnet build Server/86JP.Server.slnx --nologo
  → 0 个警告，0 个错误

PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
  → 通过 2007 / 跳过 2 / 失败 0
    488 + 52 + 60 + 255 + 6 + 221 + 925 = 2007
```

那 2 个跳过是「缺少归档时应报错」的用例——它们跳过正说明 `PVF_ARCHIVE_PATH` 生效、读档案的
判据没有被静默略过。

## 条件到证据的映射

| ID | 条件 | 证据 | 判定 |
|---|---|---|---|
| C-1 | 每个命令处理器的每个构造器参数都属于领域服务、领域 store、实例工厂，或具名豁免且写明理由 | `CommandHandlerArchitectureTests.ProductionCommandHandlers_OnlyTakeDomainShapes` 通过，报告零违规。独立评审者另建外部反射探针直接加载 `Dfo.Server.Protocol.dll` 复核：**27 个处理器、55 个参数、0 违规**，54 个是 `Abstractions` 里的 `*DomainService`/`*DomainStore`，唯一非领域形状是 `SelectCardCommandHandler(TimeProvider clock)`，命中豁免。`EveryExemptionCarriesItsReason` 断言每项豁免带理由 | 满足 |
| C-2 | 没有命令处理器注入行网关或持久化接缝 | 同上判据；`ICharacterTable`、`ISelectedSessionPositionPersistence`、`ISelectedSessionDirectory` 均不在任何命令处理器构造器上。`Protocol` 中这三者尚有 25 处引用，全部在同文件共置的 socket 消息 handler 上——那是 Spec 明列的 Non-Goal | 满足 |
| C-3 | 归类由类型本身可判定，不依赖人工维护的类型名单 | `IsDomainShape` 是一个对任意 `Type` 求值的谓词：接口 + 声明在 `Dfo.Server.Abstractions` + 后缀 `DomainService`/`DomainStore`。两个条件缺一不可（只看后缀，`Application`/`Protocol` 里的类型可自称接缝；只看程序集，接缝层同时声明的行网关与注册表会被放行）。具名豁免清单是唯一例外，当前只有一项 | 满足 |
| C-4 | 违反白名单会让判据变红 | `ATypeOutsideTheVocabularyIsRefused` 把行网关、持久化接缝、运行时容器、命令总线与一个普通 BCL 类型逐个喂给谓词，断言全被拒。`TheSweepFindsTheCommandHandlers` 断言反射遍历非空，防止查询失配后每条检查退化成对空序列的断言。独立评审者另做三次生产代码变异，逐次被新用例抓住 | 满足 |
| C-5 | 领域 store 可由名字辨认；摆摊语义仍能从名字读出 | 仓库中名字含 `Store` 的类型逐个列出并分类：17 个领域 store 已改为 `*DomainStore` 名族；`IExpertStoreRegistry` 一族（摆摊）一字未动；`OnlineStateStore`（内存容器）、`ChunkStore`（PVF 内部）、`Stored*`（被存的数据）各自含义不变。改名逐类型进行，未做字符串替换 | 满足 |
| C-6 | 三个零消费方接缝及只写不读的角色状态副本不复存在 | 全仓 grep：`IActiveCharacterAccessDomainService`、`IActiveCharacterWriteScope`、`IAccountCargoGoldService`、`IEquipmentEligibilityCharacterReader`、`EquipmentEligibilityCharacterFacts` 均 **0 处**。`ActiveCharacterRegistration` 收敛为 `(OnlineUserId, Session)`，两个字段各有存活读出点（`TryDisconnect`、`ClearExact`）；`byCharacter` 反向索引随之退役。独立评审者确认无半删 | 满足 |
| C-7 | 服务器行为逐值不变 | 全量判据通过。用例数逐文件对账：仅 5 个文件变化——`ActiveCharacterAccessTests` −17、`AccountCargoGoldServiceTests` −3、`EquipmentEligibilityCharacterFactsTests` −1、`CommandHandlerArchitectureTests` +11、`CharacterEntryInventoryTests` +2，其余 30 个被改测试文件用例数**逐个不变**。2015 − 21 + 11 + 2 = **2007**，闭合。两次纯改名各自做了「对删除行套用改名映射后与新增行逐行比对」的自证（153 对 153、93 对 93，逐字符对消）。唯一的次序变化（出生点落库从租约准备之后移到之前）经证明不可观察：同属一个 `Transactional` 命令，且 `PrepareLeaseAsync` 既不读也不写角色位置 | 满足 |
| C-8 | 仓库约定说出这套词汇表并指明判据；本成果触及的文档冲突不复存在 | `AGENTS.md` 四处改动：新增「命令处理器只够得到领域形状」一节（两种形状、实例与快照为何不在表上、禁用清单、豁免、守它的判据及其按类型判定的理由）；后缀表 `*Store` → `*DomainStore` 并给注册表族补上「不得注入命令处理器」；五个内部服务「不跨层暴露」的错误记载据实改写并留下教训；「分层测试不存在」的自陈改为指向新判据并声明其只覆盖命令处理器。独立评审者把 `AGENTS.md` 里全部 140 个反引号标识符逐个在 `Server/` 下反查，只有 1 个不存在（`PersistOnTeardownAsync`），而它在基线上已失效，非本次造成 | 满足 |

## 独立评审

`reviews/change-review.md`，verdict `approved`（第一轮 `changes_required` 的两条阻断已整改并
经第二轮复核关闭）。评审者未采信执行者结论：自建反射探针、自行三次变异、自行逐文件重算对账，
并逐字符比对两轮 `git diff -- Server/src` 证明整改**零生产代码改动**。

## 范围核验

Spec 明列的 Non-Goals 逐条确认未被触碰：三个注册表（`ISessionPresenceRegistry` /
`IExpertStoreRegistry` / `IPvpLobbyRegistry`）零改动；会话运行态聚合与 `ICharacter` 收编零改动
（`ISelectedSessionRuntimeDirectory`、`AddGoldAsync`、`ProtocolMirror` 在新增行中 0 命中）；
除四个被点名的文件外无其他 `Protocol` 文件被改。

## 留给后续成果的（非阻断，不得被静默丢弃）

- **白名单可以被一个「改名壳」满足**。`ISelectCharacterSessionEntryDomainService` 现在有三个
  逐字透传容器动词的成员（`Find` / `RegisterReplacing` / `ReleaseExact`），处理器做的还是同样
  三件容器事、只隔了一层。容器既没私有化也没改说领域话——那正是后续「内存容器转领域服务」
  成果的范围，请在那里收口。
- `Server/tests/.../Support/EntryServices.cs:34` 声明 `activeCharacters` 却没传下去，导致
  `CharacterEntryInventoryTests` 里一批断言结构上恒真。基线即如此。
- `OnlineUserExecutionGate` / `SelectedSessionTransitionGate` 两个 `Application` 具体类被跨层
  注入，注入点是消息 handler 与连接观察者，不是命令处理器。
- `AGENTS.md:212` 的 `PersistOnTeardownAsync` 在基线上即已失效。
- `Docs/Refactor/TableOwnershipRuling.md:1262/1264` 是「怎么复核」的路径指引而非叙述，照着走
  会扑空。该文件自陈是 `9c535a9` 的存档，本轮按历史记载处理未改。
- `Protocol` 有两处已死的 `using Dfo.Server.Application.Inventory`；`ISqliteStoreFacet` 的名字
  同样没说出类别（`Persistence` 内部类型，不跨层）。
