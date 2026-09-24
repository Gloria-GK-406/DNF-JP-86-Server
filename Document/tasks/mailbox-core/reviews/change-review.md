---
kind: rks-review
verdict: approved
---

# 邮箱核心完整变更独立审阅

审阅对象：工作树 `mailbox-core` 相对 `8923ea26a906bc878f4de2a9491ac61b368a5a9f` 的完整候选，包括新邮箱模型与服务、SQLite 仓储与迁移、装备原 UID 转交、账号材料、PVF 寄送规则、登录读取、测试及 `Document/MailboxCore.md`。依据 `TaskContract.md`、`spec.md`、`plan.md`，并按 `reviewing-work/references/code.md` 检查正确性、失败处理和回归。

结论：未发现未解决的阻塞缺陷。发送与领取共用域事务及同一 SQLite 参与者；写操作先取得写入资格，失败回滚；装备位置转移保留 `items` 原行和 UID；附件领取条件更新防止重复入账。角色发件的堆叠物、账号材料和金币分别从权威账本扣减，领取按物品到达规则或账号材料袋入账，金币截断会使领取回滚。旧装备附件保留且禁止猜测领取，旧金币迁移一次，旧 `9999` 期限哨兵保持无限期语义。登录现在先取轻量邮件头并只展开展示页 20 封，维持原分页成本。服务未发送协议报文，文档明确 A21 客户端尚未验收。

审阅中提出并已修正的事项：

- 账号材料起初会被普通堆叠物路径写入角色容器；现由 `MailStackTransfers` 按 `IAccountMaterialRuleService` 分流至 `IAccountMaterialBagDomainService`，真实 SQLite 用例核对余额、回执和失败回滚。
- 旧 `9999` 到期哨兵起初被显示成超长倒计时；`SqliteMailboxRepository` 现在统一将其解释为无限期，登录和服务读取一致。
- 登录起初先展开全部邮件再挑 20 封；现在 `MailboxQueryDomainService.PageAsync` 先按同一可见性和期限规则选择展示页，`ReadInboxHeadersAsync` 不读取全量正文及附件。

验证：独立重跑邮箱核心集成测试 11/11、邮箱/装备仓储及登录仓储相关测试 34/34，均通过；`git diff --check` 通过。主执行者最终报告 Application 回归 884/884（包含新增分页用例）、Persistence 排除一条已证实陈旧基线断言后 368/368、PVF 新内容源 1/1、完整解决方案构建零警告零错误；这些较大范围检查在本审阅中未独立重跑。Application 广泛验证首次出现原有耐久用例失败（883/884，quiet 输出无堆栈），随后该类独跑 5/5 和相同集合复跑 884/884；原因未定位，不把复跑通过视为根因结论，原始输出与解释见 `verification/completion-evidence.md`。旧 869 项 TRX 已由最终 `verification/*.txt` 输出取代，不纳入最终验证证据。

非阻塞边界：无法恢复原始资产事实的旧附件仍不可领取；无限期旧附件信也不能普通删除，需后续人工修复迁移。此处理避免将历史资产静默丢弃，已在业务文档明确。A21 邮件 UI、CMD/NOTI 编解码和实机行为仍属合同范围外，不能据此标记 `ClientVerified`。
