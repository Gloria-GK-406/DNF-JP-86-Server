---
kind: rks-review
verdict: changes_required
review_round: 1
reviewer: /root/gm_mail_final_review
target: spec-004-official-mail complete mixed delivery
accepted_head: 573be46b075d8363f0882c2f40a5a0135d1ada0c
contract_revision: 1
approved_spec_sha256: 4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F
owned_manifest: ../verification/owned-files.sha256
owned_manifest_sha256: 963AF5D85502E0647C943437118CAB2DE591992D05808B3D6C429BD16A1B3908
owned_file_count: 29
change_challenges_sha256: EEB9A80FAD1D2401088DCB26EBDCF678676A36F4958F5BC4733CF42CE837E3F2
---

# 官方邮件完整结果独立审查：Round 1

## 范围与判据

fresh reviewer 与 producer、Spec reviewer、drift reviewer、挑战作者分离。本轮适用 `reviewing-work/SKILL.md`、`references/code.md`、`references/deliverable.md`，依据子合同 r1 的 SC-1..3、批准 Spec 的 RQ-001..009 / AC-001..009、适用 AGENTS 的分层、中文/Document 及协议证据边界。当前为 final-review / in_progress，采用已配置 planless focused，没有另造 Plan。

直接检查完整 tracked diff、清单新增文件全文和实际依赖；29/29 文件 SHA 与 manifest 一致，HEAD、Spec、manifest 和挑战身份匹配。工作区无 `.codegraph/`。主控的 root map/workspace 与 Spec003 游标变更是既定 durable 控制记录，已读 diff，不属于产品 finding。生产报告只作定位索引。

## 裁决及唯一阻断项

**changes_required**：新装备虽然具有独立持久世界 UID，但初始 core 的实例字段不符合已有正常生成路径，阻止 RQ-005 / SC-2 的完整结果接受。其它已检查路径没有发现未解决的实质缺陷。

### F-001：每件 GM 普通装备的初始实例值固定为零（P2）

路径均相对于工作区根。

- `Server/src/Dfo.Server.Application/Administration/GmMailRuleService.cs:53-55` 为装备准备 `StackableItemCore.Create(..., 0)`，得到 `Core.Value=0`。
- `GmOfficialMailDomainService.cs:113-117` 展开每件装备时直接复用 `grant.Core`，只通过世界表分配不同 UID，没有重新建立该实例字段。`SqliteWorldItemTable.InsertAsync` 原样存 core；`SqliteMailboxRepository.cs:97-100` 原样存 core 与 instance_value；领取转移只建立位置，不重建 core。
- 正常初始化的实际对照为 `CharacterRosterService.cs:357-370`：新装备 instance value 用于区分不同件，生成正随机值；现有开包 `OpeningEffectHandlers.cs:57-59` 同样在每件创建时生成正值。此处判断来自现有实现约束，不把注释当作新客户端协议证明。
- 原始 `artifacts/gm-tests/spec004/browser-database-proof.json` 四个装备附件的世界 UID 为 1/2/3/4，但 instance_value 全为 0，core 的该字段也全为 0。`StateSynchronizationPayloadCodec.Entry.cs:674` 邮箱摘要和 `GameInventoryDelivery.cs:82-84` 背包条目直接编码这个值；独立世界 UID 不替代这个 wire/core 字段。

实际后果是持久化并投影了一批不具备正常初始实例值区分的装备 core；现有领取测试只证明世界 UID、槽位和资产数量，不能覆盖该初始化缺陷。本 finding **不声称已观察到 A21 显示、领取或其它客户端故障**。

最小充分整改：在每人每件展开创建时建立正常的非零、可区分实例值，保留已有耐久/封装、独立世界 UID、整批事务与原编号收据重放。不要仅在模板级准备一次值后给同批复用。补真实发送/领取判据，检查多件 core 的正常实例值、邮箱投影、领取后的背包投影及原 UID 保持；原编号重试仍恢复相同 core/UID，不新增装备。无需修改 Spec、合同、挑战集合、资产架构或增加实机门。

## 挑战裁决

匹配 immutable `change-challenges.md` 保留 **0 项**，因此没有待逐项裁决的 challenge hypothesis。本轮没有把零项当作批准依据，独立检查发现 F-001，按实际证据要求有界修正；不回写或补造挑战集。

## 其它路径的决定性证据（供 Round 2 按引用保留）

已检查 `GmOfficialMailEndpoints`、`GmOfficialMailDomainService`、`SqliteGmMailPersistence`、统一 schema，以及实际 `AccountExecutionBehavior` / `UnitOfWorkBehavior` / `DomainTransaction` / SQLite participant：写门后鉴权与有效目标/账号范围复核、共享事务、持久个人防重、成功审计、确认回滚与 Unknown 分类、提交后提醒均与批准范围相符。HTTP/业务层未引用 Protocol/Networking/Persistence。合法内容规则拒绝期限、权益及不支持类别；零金币不生成附件，严格 UTF-8 与 511 字节显示边界有服务端约束。

直接阅读 `GmOfficialMailHttpTests.cs` 的真实断言：五处中途失败全批回滚、跨管理员对象隔离、并发/重启/已删除旧目标重放、归属变更、容量失败后恢复、wire ID 耗尽及已 durable commit 后 Unknown 恢复。`MailboxChangedHandler` 查询当前邮箱数量；协议测试在相同 notice 下验证 2→0→7 的当前投影及原 Alarm 编码。原玩家邮箱和既有领域依赖保持复用。

直接阅读 `OfficialMailView.tsx`、`mail.ts`、`AdminApp.tsx`、共享 `useGmRead` 及邮件行为测试：冻结快照/UUID、按管理员恢复、超时安全重试、404 不声称回滚、失权清界面及迟到响应隔离均有实际实现和断言。中文使用说明与协议新增 CurrentImplementation 接线一致，未把自动/后台验证升级为 ClientVerified。

原始日志 `solution-build-final-r2.log` 为 0 warning / 0 error；`solution-tests-final-r2.log` 八项目合计 5355 passed / 0 failed / 2 intentional missing-PVF skips；`frontend-all-final.log` 41/41，`frontend-build-final.log` 构建成功。它们证明原冻结目标的既有自动判据，**不证明 F-001 已覆盖或主控 fresh verification 已完成**。

已看三张浏览器截图和原始数据库 proof：批量预览 2 人、每人金币 1000、3037×5 与 27312×2，结果列出邮件 ID 与世界 UID；单人纯文本零金币结果可操作。原库记录确为 2 操作 / 3 封 / 8 附件 / 4 世界 UID / 2 success audit，第三封无附件。该证据同时暴露 F-001 的 core 值；并非真实客户端验收。

## 交接与剩余边界

修正 F-001 后需当前目标的独立复审，范围限于整改和直接回归；未变化的上述证据按引用保留。主控仍须串行 fresh checks，并用新的 VERIFY_ONLY 隔离夹具取得最终退出码及停止证明；旧 main browser runner 因 App 关闭丢失 session，不能当作正常退出通过。Vite 大 chunk 警告及 A21 官方邮件实际显示/领取仍分别保留既有非阻塞限制与 MAIL-01..09 人工验收边界。

本轮仅新建此报告；输出祖先无 reparse point。未修改产品、Spec、合同、挑战或游标，未 build 共享 .NET 输出、启动服务、变更 Git 或操作现服/真实数据/客户端/PVF。报告完成后停止。
