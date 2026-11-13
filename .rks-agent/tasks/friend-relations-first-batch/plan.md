---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan: 角色关系系统与好友第一批（好友 + 黑名单）

## Planning Choice

选 task-units。客户端证据（RQ-001 – RQ-005）的结论会改变后续推送顺序、同频道显示方式和若干字节取值，这些细节要等证据出来再写；后续工作链条长（领域与存储、迁移、两组协议、在线推送、聊天接入、测试服），每段都有可独立检查的结果，按单元推进可以在中断后从 Task 文件恢复。

## Approach and Shared Decisions

下列决定供各单元共同依赖，执行时不再各自改。路径相对 `Server/src/`。

**SD-1 关系 owner 与接口**
- `Dfo.Server.Abstractions/Relations/` 放契约：
  - `CharacterRelationKind`：封闭枚举，本期只有 `Friend`、`Blacklist`。
  - `ICharacterRelationQueryDomainService`：只读。之所以用 `*DomainService` 后缀，是因为 NOTI 处理器只允许注入这类接口（`NotificationHandlerArchitectureTests`），先例是 `IMailboxQueryDomainService`。
  - `ICharacterRelationDomainService`：写入。
  - `ICharacterRelationDomainStore`：存储。
- `Dfo.Server.Application/Relations/` 放唯一实现，并提供 `AddRelations()` 注册。上限等规则都在这里。
- 写操作返回具名结果：枚举加必要的数据，例如删角色返回受影响的 owner 集合。名字到 ID 的解析不归关系服务。

**SD-2 被动性**

两个关系服务的构造依赖只能有 store、事务访问器（`IDomainTransactionAccessor`）和时钟。不得依赖 `IEventService`、通知、投影、发件箱、发送端。AC-010 加一条架构用例守住这一点。

**SD-3 存储**

表 `character_relations`：

```
owner_id   INTEGER NOT NULL
target_id  INTEGER NOT NULL
kind       INTEGER NOT NULL
created_at INTEGER NOT NULL      -- UTC 秒
PRIMARY KEY (owner_id, kind, target_id)
CHECK (owner_id <> target_id)
INDEX (target_id, kind)          -- 反查
```

- 两个 ID 列外键引用 `characters(character_id)`。
- 没有名字列。
- 读取时联表过滤 `delete_flag=0`。
- 实现放 `Dfo.Server.Persistence/Relations/`，在 `PersistenceModule` 注册。
- 结构版本 4→5：`CharacterDatabaseSchema.Version`、`Tool/DbMigrate` 的 `Steps[4]`、`Tool/DbMigrate.Tests`。

**SD-4 展示数据**

列表需要名字、等级、职业、转职，都按角色 ID 取当前值。现有只有单个查询，因此在角色领域增加按 ID 批量读身份的只读能力（一次最多查上限数量的角色），关系服务不自己联表读这些列。

名字到 ID 的解析由协议 CMD 调用现有的按名精确查有效角色的能力。目前只有 `IMailboxQueryDomainService.FindRecipientAsync`，1:1 对话已在复用它；若需要上提到角色领域，由 T-002 决定，不在协议层另写查询。

**SD-5 协议放置**
- 0x0122、0x0123 放 `Dfo.Server.Protocol/Game/UnitedFriend/` 切片。
- 0x0076、0x0077、0x0078 放 `Game/Blacklist/` 切片，0x0078 从 `UnansweredReports` 迁出。
- 全量列表、删除通知、上下线通知（NOTI 0x0111、0x0112）以及「补发黑名单列表」要被多个切片和推送路径共用，按「切片内不跨协议引用」的规则上提到 Game 层，即 `Game/` 下的共享文件，先例是 `ChatConversationFrames.cs`。
- 现有 `CrossServerFriendBaselineMessage` 换成真实全量列表。
- 线上文本按 UTF-8。

**SD-6 推送路径**

推送全部由调用方完成，统一用 `NotificationAudience.TheseCharacters(观察者)` 投影，通知处理器按收件人组装。处理器只注入关系只读服务、选中会话、频道、角色等 `*DomainService`。

| 时机 | 挂载位置 |
|---|---|
| 上线 | `GameSelectCharacterCommand` 在入场发布之后投影 |
| 下线 | `SelectCharacterLifecycleServices` 生成清理计划时，像 `ConversationDepartures` 一样记入「好友下线」的观察者；三个投递点（选角顶替、回选人、断线）各自加上对应通知。三处缺一处就算未完成 |
| 升级 | 订阅已有的 `CharacterLevelChanged` |
| 转职 | 在 `SetGrowTypeAsync` 的唯一应用点补一个领域事件，比照升级 |
| 删角色 | `GameDeleteCharacterCommand` 在同一写入单元里调用关系写服务清理，提交后向返回的 owner 投影 |

**SD-7 黑名单接入聊天**

在 `GameSendChatCommand` 得到成员集合之后，按「收件人是否拉黑了发言者」逐个剔除（RQ-041 列出的全部 mode 都经这一处）。1:1 建会话在 `ConversationCommands` 双向检查，拒绝时给发起方发结果码 77 的 NOTI 0x016A（要扩展编码器，按结果码分支）。`ChatPreSendChecks` 只看发言者，保持不变。

**SD-8 执行器语义**

所有 CMD 在唯一执行器中串行，投影在作业内编码。因此 RQ-055 的顺序由「一次变化一个作业、作业内先提交后投影」保证，不另做排队包复核。重连后旧会话收不到推送，由通知层按角色解析当前连接来保证；有测试证明即可。

## Work and Dependencies

| 工作包 | 交付 | 覆盖 | 依赖 | 为什么单独成包 |
|---|---|---|---|---|
| T-001 客户端证据 | 研究记录 `research/client-evidence.md`。各协议页人工区的结论与证据标签，含上限、末尾字段、黑名单显示清除、同频道显示方式，以及下线通知与 USER_LEAVE 的先后。需要时修订 Spec 的 Amendments | RQ-001 – RQ-005，RQ-070 的证据部分 | — | 结论决定 T-003 – T-005 的细节；只读的静态分析，可以独立完成 |
| T-002 关系领域与存储 | SD-1 – SD-4：契约、领域服务与规则、SQLite store、结构 v5 与迁移、批量身份读取、被动性架构用例 | RQ-010 – RQ-020、RQ-060 | T-001 的上限结论（未出结论时先取 64，结论出来后只改常量） | 后续所有协议与推送都消费它的接口，接口错了会扩散 |
| T-003 黑名单协议 | `Game/Blacklist/` 切片 0x0076 – 0x0078；共享的黑名单列表构建；0x0078 退出 UnansweredReports；协议页更新 | RQ-037、RQ-039、RQ-040 | T-001、T-002 | 协议面小，好友全量之后补发黑名单要用它 |
| T-004 好友协议与登录列表 | `Game/UnitedFriend/` 切片 0x0122/0x0123；Game 层共享的 0x0111/0x0112 消息与处理器；登录时发真实列表，替换空基线；RQ-036 的补发；协议页更新 | RQ-030 – RQ-036、RQ-038、RQ-040 | T-001、T-002、T-003 | 操作者自身的闭环，不涉及其他玩家 |
| T-005 在线推送 | SD-6 的五个时机，包括转职事件与删角色清理 | RQ-050 – RQ-055 | T-004 | 跨生命周期多个挂载点，风险集中，单独审查 |
| T-006 黑名单生效 | SD-7：聊天逐收件人过滤；1:1 拒绝 77；相关协议页或开发说明 | RQ-041 – RQ-043 | T-002（只用只读接口） | 改的是已有聊天代码，与好友协议独立 |
| T-007 测试服与验收流程 | 全量构建与测试；主干实际数据库备份并迁移；本分支测试服；编号测试流程文档；请用户实测 | RQ-071，SC-10 | T-001 – T-006 | 需要用户参与，放在最后 |

执行顺序：T-001 → T-002 → T-003 → T-004 → T-005，T-006 可以放在 T-002 之后的任意位置，T-007 最后。协议页随所属工作包更新，不另设文档包；`catalog.md` 的汇总在各包中同步修改。

## Verification

- **每个单元**：完成时跑它涉及的测试项目，确认构建通过（TreatWarningsAsErrors）。
- **完整结果**：
  - `dotnet build Server/86JP.Server.slnx` 通过，解决方案测试全部通过，含架构测试、`Tool/DbMigrate.Tests`；
  - 对照 Spec 的 AC-001 – AC-070 逐条确认；
  - AC-071 由 T-007 的测试服与用户反馈给出。
- **涉及多人顺序的部分**（AC-050）：以协议用例或集成测试为主。双客户端实机由用户在 T-007 里完成。

## Next Planning Boundary

下一个单元是 TASK-01，对应 T-001：用 `DNF.unpacked-b1-debug.exe`（SHA-256 `1F8244D3…B00DD`）静态反汇编各 handler，入口 RVA 见各协议页，做法参照 `chat-routing-first-batch/research/conversation-re-20261112.md`。

T-002 的细节可以和 T-001 并行规划；T-003 – T-005 的细节要等 T-001 的结论出来再写。
