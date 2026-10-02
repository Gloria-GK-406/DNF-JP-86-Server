---
kind: rks-review
verdict: approved
disposition: aligned
review_round: 1
target: spec-004-official-mail accumulated implementation
accepted_head: 573be46b075d8363f0882c2f40a5a0135d1ada0c
approved_spec_sha256: 4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F
owned_manifest: ../verification/owned-files.sha256
owned_manifest_sha256: 963AF5D85502E0647C943437118CAB2DE591992D05808B3D6C429BD16A1B3908
owned_file_count: 29
---

# Spec004 官方邮件累积漂移审查

## 范围与独立性

本轮在 fresh reviewer `/root/gm_mail_drift` 中执行，与生产者、Spec reviewer 和后续完整结果 reviewer 分离。适用 `reviewing-work/SKILL.md` 及 `references/drift.md`，仅判断当前已实现官方邮件结果与批准目标、root 方向的一致性；不做最终产品质量或完成判决，不代替独立完整审查及主控新鲜验证。

治理依据为根 `../../../TaskContract.md` revision 1、`../../../multi-spec-map.md`，子 `../TaskContract.md` revision 1、`../spec.md` 与 `spec-review.md` 的独立批准。当前子阶段为 `drift-check / in_progress`。实现采用 planless focused，没有另行 Plan 或 Task Unit registry 需要补造。遵守父级中文/Document 约定及工作区 `AGENTS.md` 的分层与证据边界；当前工作区不存在 `.codegraph/`，因此未使用 CodeGraph。

已直接检查清单全部 29 项实际代码、测试、页面和文档；对 tracked 文件检查完整 Git diff，对 untracked 文件读取其全文，未把 `git diff --stat` 当成完整结果。`implementation-report.md` 与 `verification/browser-verification.md` 仅作证据索引，实际判断另据源代码、测试内容、日志、三张截图及原始 `artifacts/gm-tests/spec004/browser-database-proof.json`。写报告前再次核对 HEAD、批准 Spec 和 manifest SHA；29 项逐文件 SHA 全部一致，清单没有缺项或身份漂移。

## 裁决与决定性证据

**approved / aligned**：未发现当前积累的实现遗漏一个批准结果、扩大授权范围、破坏 root 的阶段分解，或改用不相容的资产/通知路径。建议主控消费本 checkpoint，随后按既有控制器继续；本报告自身不推进任何游标。

下列路径均相对于工作区根：

| 批准结果或约束 | 实际实现与一致性判断 |
|---|---|
| 窄 SendMail 能力，独立身份及端口 | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:22-44` 对全部邮件路由赋 SendMail metadata，继承现有 `GmAccessBoundary` 的真实端口、远程 HTTPS、会话、防伪及 no-store 门。`GmOfficialMailDomainService.cs:58-59,96-100,140-145` 在事务写门后复核当前身份及权限。专用收件人投影只提供批准的最小角色身份；未借邮件增加 QueryPlayers 的账号资产查询。 |
| 个人草稿、模板及显式确认 | `GmOfficialMailDomainService.cs:22-92` 实现个人稿件/模板、版本冲突、个人记录与预览；模板丢弃收件人。`Web/gm-admin/src/OfficialMailView.tsx:34-61,90-130` 提供中文编辑、保存/加载、模板、真实归一预览及确认；收件人跨页保留明确 ID。没有把这些结果缩减成仅一个发放 API。 |
| 明确角色、永久普通资产与客户端可表示边界 | `GmMailRuleService.cs:16-65` 归一 CRLF、ID 排序去重、严格 UTF-8 和 511 字节上限，基于既有 package-opening/placement 事实拒绝期限、权益、虚拟资源及不支持类别，限制展开附件。`GmOfficialMailDomainService.cs:101-124,147-159` 重新解析有效角色和账号归属；30 天期限、独立 detached UID、系统邮箱保管及 wire ID 复核均在发送链路。后台没有新增背包直写、装备定制、全服条件发送或已发送邮件修改。 |
| 已有领域事务和领取生命周期 | `GmOfficialMailDomainService.cs:94-131` 在原账号执行租约及 CommandBus 事务中调用 `IMailboxDomainService.SendAsync`，同批收据、成功审计与新装备一起结算。`PersistenceModule.cs` 只补接既有 detached 世界物品分配器；`InventoryItemTables.cs` 仅调整说明。原 `MailboxDomainService.ClaimAsync`、装备转移及材料领取路径保持复用，没有建立另一套玩家资产真源。 |
| 持久防重、确定/未知结果与可恢复记录 | `CharacterDatabaseSchema.cs:232-234` 的个人表与 `(administrator_id,request_id)` 主键、`SqliteGmMailPersistence.cs:39-52` 的收据读写、`GmOfficialMailDomainService.cs:38-43,99-100,127-138` 的已提交重放与成功/失败审计形成批准路径。HTTP 发送捕获区区分 Unknown 与已确认回滚，UI `OfficialMailView.tsx:63-84` 保留同一冻结输入/UUID，查询 404 不生成替代请求。没有以按钮状态或进程字典代替持久防重。 |
| 提交后当前邮箱提醒 | `GmOfficialMailEndpoints.cs:52-66` 在发送命令完成后发布中立 `MailboxChanged`；`MailboxChangedHandler.cs:9-14` 经邮箱领域读当前数量并复用原 `MailboxAlarm` 编码器，提醒异常只影响提醒结果。Application/HTTP 未增加对 Protocol、Networking 或 Persistence 的引用；既有玩家邮箱通知仍保留。 |
| 中文可操作结果与隔离验证 | 直接读过 `OfficialMail.test.tsx`、`GmOfficialMailHttpTests.cs` 和 `GmMailBrowserFixtureTests.cs`，覆盖编辑/冻结/失权、重启/并发、真实 PVF 领取、原子故障及未知提交等批准方向。三张实际页面截图可见明确收件人、每人/全批汇总、确认、邮件 ID/UID 与历史；原始数据库 proof 对应 3 封邮件、8 附件、2 收据及 2 条成功发送审计，单人纯文字邮件无附件。没有只用 UI mock 代替全部可操作结果。 |
| 使用说明和证据等级 | `Document/gm-official-mail.md` 的 MAIL-01..09 保留真实客户端验收；`protocol/game/server-to-client/NOTI-0x0063.md` 的新增接线明确为 CurrentImplementation，不升级字段语义或 GM 路径为 ClientVerified。与 root 的“实现及自动验证完成，客户端清单另验收”假设一致。 |

## Root 累积方向

本阶段从已接受 `573be46b` 的管理员、查询和同 Host 骨架继续完成根 SC-4，并贡献 SC-6 的邮件说明及验证。新增三张 GM 个人文档/收据表仍在既有玩家数据库和唯一 Host 中；没有扩展为独立进程、多服后台、任意 SQL 管理或运营调度。

root map/workspace 和 Spec003 执行游标的 tracked 变化为已接受结果封存、基线推进及 Spec004 激活记录，已分别看过 diff；它们属于主控 durable artifacts，不归入 29 项生产身份。SC-4 在 map 仍为 not_started、child 为 running 是待接受状态，当前漂移报告不擅自把它改成 satisfied/completed。

根 SC-5 点券能力及 SC-1/6 的最终集成、CI/发布与总验收仍有既定剩余覆盖；未实现的后续结果没有被本 child 宣称完成，也不是当前官方邮件结果的遗漏。本审查不为未来 child 创建或增加额外范围。

## 证据边界与控制器交接

没有阻塞漂移 finding，也没有要求改合同、替换 Task 或终止 Spec 的依据。已读历史完整构建/前端与 solution 日志及实际浏览器原始证据，本审查未运行 build/test、不主张新鲜验证已经完成。生产者报告的“实际浏览器待完成”描述早于主控的浏览器记录；后者已有截图与数据库证据，未将旧描述误判成实现缺口。

实际浏览器 runner 因 App 关闭失去最终退出码，`browser-verification.md` 已明确此限制；主控另做 fresh VERIFY_ONLY 自检及退出核对即可沿原授权路线补齐完成证据。这不改变已实现结果、Spec 验收或 central approach，因此不是漂移阻断项。真实 A21 官方邮件显示、提醒及领取表现仍待用户验收；本裁决不作 ClientVerified 声明。

本轮仅新建此 `reviews/drift-review.md`。写前检查了绝对输出路径处于任务工作区且祖先无 reparse point；未修改产品、Spec、合同、root/child 游标、Git HEAD，未启动服务或操作现服/真实玩家库/客户端/PVF。完整结果挑战、最终独立审查、主控新鲜验证与接受由主控继续负责。
