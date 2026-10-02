---
kind: rks-review
verdict: approved
target: ../spec.md
target_sha256: 4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F
contract_revision: 1
review_round: 1
---

# 官方邮件 Spec 独立审查

## 目标与判据

本轮为 ready Spec revision 1 / round 1，代码基线为 `573be46b075d8363f0882c2f40a5a0135d1ada0c`。reviewer 在新鲜独立上下文中执行，与 Spec producer 和挑战生成者分离，未再次分派 reviewer。

适用 `reviewing-work/SKILL.md` 与 `references/spec.md`；依据根 `../../../TaskContract.md`（SC-4、SC-6）、子 `../TaskContract.md`（全部约束及 SC-1..3）、工作区 `AGENTS.md` 及父级用户指示。审查完整 WHAT：个人草稿/模板、选人、文本和附件预览、永久附件、原子批量、持久防重与个人记录、提交后当前邮箱通知。核对 `sdd-execution.yaml` 当前 spec-review 的 reads/writes 与独立 auto gate。

## 裁决

**approved**。当前 Spec 的结果、边界、约束和 AC/EV 足以按授权实施与验收。未发现需要在下一阶段前修正的实质缺陷，也未缺少必要输入或独立能力。没有以缺少内部实现设计、额外形式化证明或下一阶段可便宜控制的风险要求扩张文档。

- 根/子合同要求的邮件完整流程均进入 RQ-001..009；草稿和模板持久化、版本冲突、明确收件角色、冻结确认及超时恢复分别有 AC-001..009 的观察判据。
- 发放仅限永久普通装备、永久堆叠物和金币；不支持内容明确拒绝。文本、附件展开、物品状态及协议 ID 边界清楚，全批邮件/附件/新装备/成功收据/成功审计同事务。
- 防重绑定管理员和 UUID，归一输入相同返回既有收据、内容变化冲突，已提交重放不重新依赖目标或内容可创建性。顺序/并发/重启/响应丢失均有验收要求；拒绝、确认回滚与未知状态分开，不把查询 404 当作回滚事实。
- SendMail 的个人邮件能力与 QueryPlayers/ViewAudit 分离，写入事务内重新核验当前身份/权限/收件人归属；跨管理员对象、前端旧响应和本地待确认状态的隔离均已明确。
- 通知只发生在成功提交后，经中立接缝读当前邮箱投影；提醒失败不撤销或重复资产发放，真实客户端收到/已读/领取不由后台测试替代。

## 挑战裁决

匹配 `spec-challenges.md` 的目标 SHA-256 与代码基线；该集合保留 0 项挑战，因此没有待逐项判定的 confirmed/rejected/needs-evidence 项。独立检查结果同样未产生阻塞发现，批准依据包括下述实际源码，而非仅采纳挑战集合的结论。

## 关键事实抽查

路径均相对于当前任务工作区，均属 CurrentImplementation：

| 主题 | 实際源码与审查含义 |
|---|---|
| 邮箱事务与领取 | `Server/src/Dfo.Server.Application/Mail/MailboxDomainService.cs:39-74,94-122,147-176`：系统邮件允许 detached 装备、堆叠物及金币；领取先标记附件再授予，任何异常回滚当前 ambient transaction。RQ-006/AC-006 不允许 catch 单人失败后部分提交，与实际行为相符。 |
| 永久物品与正常初始状态 | `Server/src/Dfo.Server.Infrastructure/Content/PvfPackageOpeningDefinitionSource.cs:151-204` 与 `Server/src/Dfo.Server.Application/Inventory/PackageOpeningRuleService.cs:58-79` 提供 placement、supported、耐久/封装及期限/权益事实；`Server/src/Dfo.Server.Application/Effects/OpeningEffectHandlers.cs:24-38` 也显示现有初始 core 边界。Spec 明确额外类别拒绝和期限/权益为零，未把普通堆叠物当作能保存任意实例状态的载体。 |
| 装备与材料生命周期 | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:506-551` 可分配 detached 世界 UID；`Server/src/Dfo.Server.Application/Inventory/EquipmentTransferDomainService.cs:50-101` 查询 detached 并将同一 UID 授予正常容器；`Server/src/Dfo.Server.Application/Mail/MailStackTransfers.cs:16-21,72-89` 依托 placement 与领取容器并保留账号材料路径。增加适用领域接缝和接线属于下一阶段授权实现，不要求在 Spec 写内部方案。 |
| 文本与附件可表示性 | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:614-675,758-763` 合并标题/正文、以 u8 表示摘要数，并对 512 字节写入取前 511 字节；`Server/src/Dfo.Server.Application/Mail/MailboxQueryDomainService.cs:13-16` 读取每页 20 封，玩家邮件 decoder `Server/src/Dfo.Server.Protocol/Game/Mailbox/MailboxSendWire.cs:25-28` 同样限制 10 个物品。GM 每封至多 10 个物品加 1 行金币、严格 511 字节避免截断，与现有形状相容。`Server/src/Dfo.Server.Protocol/Game/Mailbox/MailboxIds.cs:5-9` 的附件 ID 限制及 codec 的 signed int mailId 由 RQ-006/AC-006 明确要求检查。 |
| 账号边界与重新鉴权 | `Server/src/Dfo.Server.Application/Commands/Pipeline/AccountExecutionBehavior.cs:23-48` 排序获取账号租约，`UnitOfWorkBehavior.cs:36-49` 在返回前提交；`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs:148-154` 仅解析未删除角色的账号。`Server/src/Dfo.Server.Application/Administration/GmAccessDomainService.cs:39-45` 查询当前会话/管理员启用状态/权限，`Server/src/Dfo.Server.GmWeb/GmAccessBoundary.cs:20-50` 已有端口、传输、权限与写请求防伪边界。Spec 要求事务内复核，能够阻止排队前的身份及归属变更造成错发。 |
| 审计与通知接缝 | `Server/src/Dfo.Server.Persistence/Administration/SqliteGmAdministrationPersistence.cs:30-37` 经共享 SqliteDatabase 写审计，能够与成功发放同事务结算；`Server/src/Dfo.Server.Protocol/Game/Mailbox/MailboxDelivery.cs:91-111` 证明现有通知类型携带旧计数且属于 Protocol。Spec 已要求新的中立通知只读当前邮箱计数，避免直接复用该旧计数投影作为 GM 重试结果。 |

## 非阻塞边界与后续验收

实际附件类别筛选、初始化、SQLite 防重竞争、失败注入、事务内重新鉴权及中立通知接线尚未实现，本裁决不声明其正确；按现有 AC-001..009 和 EV-001..003 实现并取得新鲜证据即可，无需先扩张 Spec。离线补提醒与通知持久 outbox 已明确不在承诺内。真实 A21 邮件展示、附件领取和重新登录表现仍需手工清单验收，不能升级为 ClientVerified。

## 写入边界

写前核对了目标 SHA-256、Git HEAD、工作区根及 review 输出绝对路径。输出处于本子任务 reviews 目录，路径祖先无 reparse point，文件事前不存在；本轮仅新建 `reviews/spec-review.md`。没有修改 Spec、合同、根/子控制游标、产品代码或 Git，没有操作现服、玩家数据库、客户端或 PVF，没有启动测试服务。