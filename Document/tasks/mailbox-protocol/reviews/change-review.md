---
kind: rks-review
verdict: approved
---

# 邮箱协议完整增量独立审查

审查目标：`mailbox-core` 工作树相对 `HEAD=1f800a01fc8a544db12e9696064a0845fe4fdc7e` 的未提交生产文件、相关测试与协议文档，包含新增但未跟踪的 `Game/Mailbox`、发送与资产服务、容器添加实现。依据 `TaskContract.md`、`spec.md`、`plan.md`，应用 `reviewing-work/references/code.md` 与 `deliverable.md`。本轮只读生产代码，未操作游戏、数据库、服务或客户端，也未独立运行测试；部署与实机回归由控制方负责。

当前目标内未发现需要阻断代码审查的缺陷。`MailboxDelivery` 将当前连接对应的选角身份传入事务型 `MailboxCommand`，命令再次核对连接、账号、角色与在线状态；领取键仅经 `MailboxIds.DecodeAttachment` 转内部 ID，仓储查询以收件角色过滤。`MailboxDomainService` 在同一写事务中先标记领取，再发放装备、堆叠或金币；重复领取、越权、容量不足和金币上限均不能产生成功结算。发送将客户端槽位解析为当前角色持久包中的真实物品或装备 UID，选中堆叠只从对应槽扣除；邮件插入和资产扣除共享事务。删除当前角色不存在的邮件幂等成功，仍拒绝删除有效未领附件。

资产通知由事务内准备的绝对值构成，命令提交后才发布；发送和领取先送背包、虚拟材料或金币变更，再送成功 ACK。`0061` 对物品和金币分别使用可回显的附件领取键，正文继续使用邮件 ID；`005F` ACK 回显原 wire 键。打开、查询、发送、领取、状态动作的解码器对当前已知形状设定长度和字段边界，`0086` 只执行 0/2，其余动作拒绝。系统来源使用无参 `MailSource.FromSystem()`，仓储写入 `senderId=0` 与空名称；`0061` 的摘要、正文均按空系统名称编码。登录与打开共用 `ReadStorageAsync`/`MailboxProjection`，避免两条路径投影分歧。

测试源码覆盖真实 SQLite 的选中堆叠扣除、邮件插入失败回滚、重复及越权领取、删除保护与幂等、纯正文/金币/物品投影、系统空名称和请求边界。`verification/player-send.md` 记录先前 Application 1992/1992、Protocol 1110/1110 与 Release 发布通过；这些是执行者报告，本审查不把它们表述为本轮独立重跑或实机通过。

剩余边界：纯正文无附件的 `005E` 哨兵、非 ASCII 收件人名字代码页、成功 ACK 与在线 `0063` 的 UI 效果，以及 `0061` 时装/宠物可变字段，仍须同版 A21 实帧与用户实机确认；当前不能标为整个邮箱 `ClientVerified`。`Document/outputs/mail-protocol-integration/system-source-sample.md` 和 `Document/tasks/mailbox-protocol/implementation-status.md` 保留早期历史状态，分别描述可自定义系统发件人和未注册发送/打开，读取时应以较新的 `player-mail-marker.md`、`verification/player-send.md` 与当前代码为准；建议在最终交付说明中明确这一时序。`git diff --check` 对现有 CRLF 文件报整文件尾空白，不能用作有效的新增行空白结论。

## 实机发信余额偏差与 A21 成功回调定向复核

本节是新证据后的复审，取代前文对当前完整增量的 `approved` 判断。实机发信 mail17 携带 100 金币：数据库 208984→208884，只扣 100；用户 UI 最终 208679，比数据库权威余额少 205。当前 `MailboxCompletedHandler` 对发送先投影绝对资产、后送成功 `005E/013B` ACK，因而成功回调仍可再次改变客户端本地状态。差额 205 是这一次观察值，尚不能命名为固定邮资或证明无金币邮件也恰扣 205。

同版 A21 dump 的直接指令链：`0x0111F959` 将两种发信成功导向 `0x01A52BA0`；该函数 `0x01A52BEF` 调 `0x01A44B60→0x01A42FE0`。后者在 `[esi+0x14]` 非零时逐项调用 `0x016F4F40/0x016F34D0/0x016F3360` 并清本地发送选择；无条目时仍走 `0x01A4302D` 之后的 UI 更新 `0x01A406E0`。这证明 ACK 成功后存在本地发送状态变更，足以要求权威投影最后覆盖；仅凭此切片不能给每个间接对象精确命名，也不能单独量化金币扣款。UI 与 DB 的差异提供了本次实际错误结果。

**阻断项**：发送成功通知应将 ACK 放在发送者的全部绝对资产通知之前，包含背包物品、清槽、账号材料和金币。只移动金币不足以覆盖成功回调对附件/本地选择的后续变更。发送无附金币时 `MailboxAssetDomainService.PrepareAsync` 目前返回 `Gold=null`，因此还必须从同一事务内读取发信后的权威携带金币，并在每次发送成功 ACK 后无条件投影该余额，包括纯文字邮件。领取仍可保留既有资产→ACK顺序。发送失败仍只回错误 ACK，不发成功资产投影。修复后需要协议顺序测试及至少一次同版实机金币和无金币发送核对；实机之前不能把邮资金额或纯文字本地扣费定为事实。

## 发信 ACK 顺序修复复审

当前工作树已针对上一节阻断项修补，复审结论 **approved**。`MailboxCompletedHandler` 只对 `MailboxSendResponse` 先排 ACK，再排全部发送者资产通知；领取继续资产先于 ACK，失败仍由 `MailboxDelivery` 单独发送错误 ACK。`MailboxCommandHandler` 在发送成功的同一事务中先准备资产变更，再读取角色最终 `GoldAsync` 覆盖投影的 Gold；纯正文和不附金币的邮件也会收到 ACK 后的权威余额。`MailboxProtocolIntegrationTests` 新增 `005E/013B × 0/100 金币` 四组合，断言 ACK 为首帧、最终金币通知为持久化值；已有混合发送与领取顺序测试仍在。控制方报告定向 39 项通过；完整 Application 回归在运行中，本审查未独立重跑。静态指令和实机样本单列于 `Document/outputs/mail-protocol-integration/send-ack-local-state.md`。

非阻断文字精度：当前两处代码注释把“纯文 ACK 必扣邮资”和“ACK 直接改真实背包”写得比证据更确定。单次 205 差额尚未定位为邮资，静态链只闭合本地发送选择与 UI 更新；建议收窄注释。该措辞不改变修复的必要性与现有代码行为。修复后的同版 UI/DB 对照仍须由实机验证，不能据静态审查宣称显示已恢复。

## 完整回归发现后的测试预期复核

控制方报告 Application 1993/1993、Protocol 1110/1110 通过；Persistence 首轮 367 通过、2 失败，正在重跑。两处测试修正已独立核对，不改变上述审查结论：`SchemaOwnershipTests` 只将表数量从 45 改为 46；`Document/tasks/mailbox-core/verification/baseline-schema.md` 记录更早基线和当前均为 46，且本增量未新增表。`EntryLoadCharacterizationTests` 只补充 `GoldAttachments[0]` 的 1055/123 快照字段；fixture 的邮件 800 原有 `gold=123` 且已有附件 1054，`CharacterDatabaseSchema.MigrateMailboxGold` 会为尚无 gold 附件的邮件插入独立 gold 记录，自动生成下一个 ID 1055。该快照补充反映新投影字段，不掩盖生产行为变化。Persistence 重跑结果仍需控制方确认，本审查未独立执行。
