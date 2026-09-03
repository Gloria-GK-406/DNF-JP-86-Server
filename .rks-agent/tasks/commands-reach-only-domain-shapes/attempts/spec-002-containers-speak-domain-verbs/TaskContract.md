---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-4
  - SC-5
  - SC-8
  - SC-9
depends_on: []
---

# TaskContract: 内存容器改说领域话

## Context

### Source Request

来自根任务 `commands-reach-only-domain-shapes` 的第二个有界成果。根契约要求既非领域服务、
领域 store、实例三者之一，又暴露成可注入接缝的内存容器各自归位：**要么成为说领域话的领域
服务，要么拆成领域 store 与实例的组合**。本成果做前一半。

### Current Situation

基线 `70a3e54e`（spec-001 已接受）。

对着代码看，这批容器的实际状态比预想的好：`ExpertStoreRegistry`、`PvpLobbyRegistry`、
`SessionPresenceRegistry`、`SelectedSessionDirectory` **四个的字典都已经是私有字段**，
接口上暴露的也大多已经是领域动词（`Open` / `Close` / `HasOpenStore`、
`MarkReady` / `Offer` / `ReadyOn`、`Admit` / `Withdraw` / `InArea`）。

所以缺的不是私有化，是**名字仍在说容器话**。`*Registry` 与 `*Directory` 在这个仓库的后缀
分类法里属于「持久化与运行时注册表」那一行，与领域服务分属不同类别；spec-001 落地的注入面
判据据此拒绝它们。

其中 `ISelectedSessionDirectory` 是一处真问题，而不只是名字问题。它的接口是纯容器动词
（`RegisterReplacing` / `Find` / `TryRemoveExact` / `Current`），被 25 个文件引用。spec-001
为了让两个命令处理器通过判据，把它的三个动词逐字透传进了
`ISelectCharacterSessionEntryDomainService`——处理器做的还是同样三件容器事，只是隔了一层。
那份成果的结论里把这层壳明写为**必须在本成果收口**的负担，理由是：一条可以被改名壳满足的
白名单，守不住它声称守的东西。

`OnlineStateStore` 另有一处遗留的一词多义：它叫 `Store` 却不是 `IDomainStore`，而是
`Application` 内部的内存表。它不跨层暴露（只有三个门面和组合根碰它），但这个名字使「store」
在领域层仍有两种读法。

## Goal

暴露成可注入接缝的会话与在场内存容器，名字说的和它做的是同一件事：它们是领域服务。
`ISelectCharacterSessionEntryDomainService` 上那层为通过判据而加的透传壳随之消失——命令处理器
直接向拥有会话的领域服务提问，而不是隔着一层问同样三件容器事。

## Scope

### In

- `ISessionPresenceRegistry`、`IExpertStoreRegistry`、`IPvpLobbyRegistry`、
  `ISelectedSessionDirectory` 四个接缝改为领域服务的名字与类别
- 拆掉 `ISelectCharacterSessionEntryDomainService` 上那三个逐字透传的容器动词，
  两个命令处理器改为直接向会话领域服务提问
- `OnlineStateStore` 不再用 `Store` 这个名字，使领域层的 `store` 只剩一种读法

### Out

- `ISelectedSessionRuntimeDirectory` 与它持有的会话运行态聚合——那是下一个成果的核心
- `IEntryInventoryLeaseRegistry`。它带 `Version` 与 `IsDirty`，是 write-behind 缓存，
  与它缓存的行在同一批数据上形成两个 unit of work；根契约已显式把它记为推迟项
- 普通 socket 消息处理器的依赖面。判据只覆盖命令处理器，这条边界不在本成果变动
- 任何可观察行为的改变

## Constraints

- 不改变任何可观察行为
- 现有测试的期望值与断言不得为迁就实现而改写；类型改名带来的符号更新除外
- 改名逐类型进行。`IExpertStoreRegistry` 里的 `Store` 指玩家摆的摊，它的**语义**在改名后
  仍须能从名字读出
- 判据命令必须带 `PVF_ARCHIVE_PATH`

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C-1 | 四个接缝的名字说出它们是领域服务，且各自的内存容器不出现在接口上 | 四个接口的成员逐个列出；每个成员是领域动词而非容器操作 |
| C-2 | 命令处理器直接向会话领域服务提问；`ISelectCharacterSessionEntryDomainService` 上不再有为通过判据而存在的透传成员 | 该接口改动前后的成员对照；两个命令处理器的构造器 |
| C-3 | spec-001 的注入面判据在改动后仍然通过，且没有因为放宽规则或扩充豁免而通过 | 判据结果；判定谓词与豁免清单的 diff |
| C-4 | 领域层里 `store` 只剩一种读法 | 仓库中名字含 `Store` 的类型逐个列出，说明各自含义 |
| C-5 | 服务器行为逐值不变 | 全量判据通过且用例数与 spec-001 后的 2007 一致；`Server/tests` 的 diff 不含期望值或断言的改写（符号重命名除外） |
| C-6 | 仓库约定里关于这几个接缝类别的记载与代码一致 | 被改动的记载逐条核对 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 沿用根契约 `../../TaskContract.md` 的 `task` 行：所有者认可三批分工后给出「可以，先把任务契约和sdd搞起来把。multi-spec应该就行了。跑完了再和我说」 | 本成果范围内的实现、验证与一次已评审结果的本地提交；不含合并、推送、清理 |

## Assumptions

- 四个容器的字典**已经是私有字段**，接口上也已多为领域动词。本成果因此主要是名字与类别的
  变更，而非结构改造——除 `ISelectedSessionDirectory` 那层透传壳之外
- 基线自带一个偶发失败：`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`
  会抛已释放的 SQLite 句柄异常，单独复跑其所在项目即恢复全绿
