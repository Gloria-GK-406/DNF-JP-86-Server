---
kind: rks-review
verdict: approved
---

# 玩家发信增量独立审查

审查对象：`mailbox-core` 工作树，以 `1f800a01` 为基线的未提交发送增量；覆盖 `MailboxSendWire`、协议注册/命令/通知、发送领域服务、堆叠扣除及新增协议至 SQLite 测试。依据 `TaskContract.md`、`spec.md`、`plan.md` 与 `reviewing-work/references/code.md`。本结论仅是该增量的代码审查，不代表 A21 客户端验收或最终构建/测试完成。

初审发现请求已指定堆叠槽位，但旧 `MailStackTransfers.TakeAsync` 会按模板 ID 扫描 Main/Pet 并扣先遇到的堆叠；玩家选择第二格时可能扣第一格。当前修补将选择位置放入 `MailAsset.Stackable.SourceSlot`，从 `MailboxSendDomainService` 传到事务内的 `TakeAsync`，只允许从对应角色、容器和槽位扣除。新增测试覆盖两个相同模板堆叠只扣第二格，以及重复槽位、过期数量和错误模板拒绝；另有邮件插入失败时装备、堆叠、金币及邮件整体回滚测试。原阻塞缺陷已解除。

线格式解析与现存三条 A21 C2S 样本相符：`0x005E` 单附件、`0x013B` 多附件分别解析名称、金币、槽位/模板/数量、正文；畸形长度、额外字节和未知非零尾字段拒绝。发送命令核对当前选角会话；装备通过选中槽位取得权威 UID，再由核心验证归属与可交易性；邮件和资产在同一事务内提交。发送方资产绝对投影先于成功 ACK，收件人通知在提交后定向发布。当前代码未见额外阻塞缺陷。

剩余验证边界：纯正文无附件哨兵尚无同版实帧；非 ASCII 收件人编码仍需要客户端样本；`0x005E/0x013B` 成功 ACK 和在线 `0x0063` 的客户端 UI 效果尚未实机验证。测试源已检查，本审查未独立运行测试；控制方仍需按计划完成最终 Application/Protocol 回归和部署前检查，不应据本结论标记 `ClientVerified`。
