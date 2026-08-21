---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-2, SC-3, SC-4, SC-5, SC-7]
depends_on: []
---

# TaskContract: 九个仓储退回单表读写并接入用例边界

## Context

### Source Request

根任务的第一个子结果，由用户划定：「第一个干掉除了 item 以外的大部分问题。」

### Current Situation

九个仓储各自 `new SqliteConnection` 再 `BeginTransactionAsync`，共 26 处落在用例边界之外。它们各自提交，互不知晓，因此一次用例内的多处改动没有共同结算点，多次读之间也没有共同快照。

清点其 SQL 实际触及的表，可见"仓储"这个名字与它们做的事已经不符：

| 仓储 | 触及的表数 |
|---|---:|
| `SqliteAccountRepository` | 1 |
| `SqliteAccountEntryStateRepository` | 8 |
| `SqliteCharacterRepository` | 4 |
| `SqliteCharacterEntryStateRepository` | 33 |
| `SqliteCharacterPvpSkillProfileRepository` | 3 |
| `SqliteDungeonPersistentEffectRecoveryRepository` | 3 + `sqlite_master` |
| `SqliteExpertSkillContractReconciliationRepository` | 3 |
| `SqliteInventoryEntryRepository` | 14 |
| `SqliteEntryInventoryMutationRepository` | 3 |

其中 `SqliteCharacterEntryStateRepository` 一个类读 33 张表拼成一条 `CharacterEntryStorageRecord`。这段编排是取数过程而非存储访问，它住在 SQL 层意味着领域层拿到的是成品，既无法复用也无法被自动化。

`SqliteDungeonPersistentEffectRecoveryRepository` 另有一处 `TableExistsAsync` 查询 `sqlite_master`。它是建表还是构造副作用时代的防御，如今 `CharacterDatabaseSchema` 是唯一建表归属者且创建全部 52 张表，该判定恒为真。

按表拆分后，仓储总数将接近数据库的表数（52 张）。这是预期结果而非失控：每个仓储只剩单表的增删改查，样板化正是后续引入 ORM 或代码生成的前提。

物品容器路径（`SqliteItemStore` / `SqliteCharacterStore`）已在用例边界内，本子任务不动它们。

## Goal

九个仓储各自只表达单张表的读写，其连接与事务来自当前用例边界而非自行开启；跨表组装移入领域层并集中在单一处。

## Scope

### In

- 九个仓储按其触及的表拆分，每个拆出的仓储只读写一张表。
- 仓储不再自行开启连接或事务，改由当前用例边界提供。
- 入场装载的跨表组装移入领域层，集中在单一处。
- 移除因建表集中化而恒为真的表存在性探查。
- 随拆分作机械调整的既有测试。

### Out

- 不动物品容器路径（`SqliteItemStore`、`SqliteCharacterStore` 及其会话）。
- 不退役内存载体构造——属根任务的第二个子结果。
- 不做泛型仓储与 attribute 映射。
- 不拆 BLOB 列。
- 不改数据库的表与列，不写回填或迁移路径。

## Constraints

- 对外可观察行为一字不变。既有测试的语义只允许因类型或依赖变化作机械调整。
- 一次用例内的多处改动必须全有或全无，不得因拆分退化为逐条提交。
- 跨表组装在领域层必须只有一处定义，不得散入各调用方。
- 仓储不得自建表，不得查询表结构元数据。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`。
- C# 文件为 CRLF、无 BOM UTF-8；net10.0，TreatWarningsAsErrors。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 每个仓储只读写一张表 | 逐仓储清点其 SQL 触及的表名，每个恰好一张 |
| SC-2 | 九个仓储不再自开连接或事务 | 这九个文件所拆出的全部代码中，新建数据库连接与开启数据库事务的处数为零；今天为 26 与 13 |
| SC-3 | 一次用例内的多次读看到同一份快照 | 一次跨多表的读进行到一半时，由另一条连接提交一处改动；该用例前后两次读的结果一致 |
| SC-4 | 一次用例内的多处改动全有或全无 | 一次用例同时改两张不同表，令其中一处在写出阶段失败，用例结束后两处改动均不可见 |
| SC-5 | 跨表组装归领域层且只有一处 | 组装代码位于领域层；持久化实现中不再出现跨表拼装；该组装在领域层的定义处唯一 |
| SC-6 | 表存在性探查已移除 | 持久化实现中对 `sqlite_master` 的查询为零 |
| SC-7 | 对外可观察行为一字未变 | 协议编解码用例与既有帧序列金样全部通过；全量结果不低于基线 1011 通过 / 2 跳过 / 0 失败加本次新增 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 承接根 TaskContract 的 task 授权行：「好，那就开始任务吧。整个任务会比较大，但是应该是没难度的。全部完成后再和我说吧」，其中「第一个干掉除了 item 以外的大部分问题」即本子结果 | 在根任务工作树内执行上述 Scope；评审与新鲜验证通过后由根控制器做一次已接受结果提交；不含合并、推送、PR、清理 |

## Assumptions

- 九个仓储触及的每张表都能被单独读写。若发现某张表的写必须与另一张表在同一语句内完成（例如以子查询互相引用），该仓储脱离本子任务的接受边界，须交回根控制器判断归属。
- 按表拆分带来的往返次数增长不构成性能问题。用户已就吞吐量给出边界（"能保证上百人并发就很不错了"）。
