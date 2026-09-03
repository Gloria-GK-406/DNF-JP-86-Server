---
kind: rks-verification
decision: pass
---

# 完成验证：内存容器改说领域话

## 被评估的主张

`spec-002-containers-speak-domain-verbs` 的六条成功条件全部满足：四个会话与在场接缝的名字与
成员说的和它做的是同一件事、spec-001 为通过判据而留下的透传壳已拆除、注入面判据仍然通过且
不是靠放宽规则通过、领域层 `store` 只剩一种读法、行为逐值不变、仓库约定与代码一致。

基线 `70a3e54e`，工作区 `commands-reach-only-domain-shapes`，**变更尚未提交**。

## 新鲜判据

```
dotnet build Server/86JP.Server.slnx --nologo        → 0 个警告，0 个错误
PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo
  → 通过 2007 / 跳过 2 / 失败 0
    52 + 488 + 60 + 255 + 6 + 221 + 925 = 2007
```

与基线 `70a3e54e` 的 2007 逐项目相同——本成果是行为保持的重构，且删除 `Current()` 时只删了它
自己那一条断言（见 C-5）。

## 条件到证据的映射

| ID | 条件 | 证据 | 判定 |
|---|---|---|---|
| C-1 | 四个接缝的名字说出它们是领域服务，成员是领域动词，容器不出现在接口上 | 四个接口的成员面已逐个取出核对：`ISessionPresenceDomainService`（`Admit`/`Withdraw`/`WithdrawExact`/`Find`/`InArea`）、`ISelectedSessionDomainService`（`Select`/`ReleaseExact` + 四个 `Find*`）、`IExpertStoreDomainService`（`Open`×2/`Close`/`CloseExact`/`HasOpenStore`）、`IPvpLobbyDomainService`（`MarkReady`/`ClearReady`/`ClearReadyExact`/`Offer`/`Withdraw`/`ReadyOn`/`OfferedOn`）。**无整表列举、无 `Add`/`Remove`/`ContainsKey`/`Values` 一类容器操作**。四个实现的容器均为私有字段。独立评审者另行复核同一结论 | 满足 |
| C-2 | 命令处理器直接向会话领域服务提问；不再有为通过判据而设的透传成员 | `ISelectCharacterSessionEntryDomainService` 上的 `Find`/`RegisterReplacing`/`ReleaseExact` 连同实现删除；两个命令处理器改为直接注入 `ISelectedSessionDomainService`。评审者确认全仓没有第二层等价包装长出来 | 满足 |
| C-3 | 判据仍通过，且判定规则与豁免清单未被放宽或扩充 | `CommandHandlerArchitectureTests.cs` 全文 diff 只有一行：反例 `InlineData` 由 `ISelectedSessionDirectory`（改名后已是合格领域形状）换为 `ISelectedSessionRuntimeDirectory`（仍在 `Abstractions`、仍是真容器、仍被拒绝）。`IsPermitted`/`IsDomainShape`/`Exempt` 一个字未动，反例类别数与用例数不变。评审者另做两次变异（给命令处理器塞一个真实使用的运行时容器；把 `Directory` 后缀加进白名单），分别被两条不同用例抓住 | 满足 |
| C-4 | 领域层 `store` 只剩一种读法 | `OnlineStateStore` → `OnlineStateRegistry`（`Application` 内部运行时内存表，三个门面包着、不跨层，`*Registry` 正是本仓「运行时注册表」类别）。`IExpertStoreDomainService` 一族的 `Store` 指玩家摆的摊，语义保留在名字里 | 满足 |
| C-5 | 行为逐值不变 | 判据 2007/2/0 与基线逐项目相同。对 55 个 `Server/` 文件的 diff 施加改名映射后做机械对消：**35 个文件零残差**，其余残差全部落在四个已授权类别内（`Current()` 纯删除、拆壳、文档搬移与措辞、判据反例替换），无第五类。删除 `Current()` 时只删它自己那条断言——同一测试方法里另两条是 `FindByWireUserId`/`FindByCharacter` 在真实实现上的**唯一**覆盖，整条删会让两个幸存成员归零覆盖；评审者 grep 复核该说法属实 | 满足 |
| C-6 | 仓库约定与代码一致 | `AGENTS.md`：项目布局句不再把这两个接缝叫注册表/目录（该说法此前与后缀表「`*Directory` 不得注入命令处理器」直接冲突，而那个「目录」正被两个命令处理器注入）；后缀表之后补入判别式——看接口说的是领域动作还是容器动作，而不是看内部有没有用字典。`Docs/` 两处活文档的陈旧符号更新；带日期的历史记载不改。评审者对三份文档做机械复扫（表格分隔行、围栏配对、标题行 diff），未发现第二处「内容对、位置错」 | 满足 |

## 独立评审

`reviews/change-review.md`，verdict `approved`。第一轮 `changes_required` 的两条阻断均在文档：
项目布局句仍把接缝叫回注册表/目录（与后缀表自相矛盾），以及本轮新插入的判别式段落插在后缀表
**中间**、空行把表截断（被打散的正好含判据允许的两种形状之一，是控制者的失误）。两条整改后经
第二轮复核关闭。评审者以 `git diff -M -U0 70a3e54e -- Server/` 的 md5 与第一轮副本逐字节相同，
证明整改是纯文档的，故第一轮的代码结论无需重推。

## 范围核验

Spec 的 Non-Goals 逐条确认未被触碰：`ISelectedSessionRuntimeDirectory` 与它持有的会话运行态
聚合零改动；`IEntryInventoryLeaseRegistry` 零改动；普通 socket 消息处理器的依赖面未变；
无可观察行为改变。

## 留给后续成果的

- `ISelectCharacterSessionEntryDomainService.ReplaceRuntimeExact` 仍是对
  `ISelectedSessionRuntimeDirectory` 的逐字转发。它早于 spec-001 就存在，且转发的正是下一个
  成果要拆的那个聚合持有者，因此本轮不算食言——但**不要因为本轮把另一层壳拆了就当它也解决了**。
- 判据的反例现在锚在 `ISelectedSessionRuntimeDirectory` 上。下一个成果拆掉它时反例要换锚点，
  **换的时候不许顺手放宽规则**——得另找一个仍被拒绝的真容器，否则那条反例就名存实亡。
