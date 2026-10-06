# 邮件物品托管实现交接

状态：本地生产路径已迁移；未提交、未部署。修改范围是邮件及 GM 附件创建、对应持久化托管扩展和相关测试。

## 实际实现

- `PreparedMailAttachment` 携带真正的 `ItemRecord` 列表；邮件插入拿到附件 ID 后，交同一个 SQLite item session 的 `AcceptMailAttachmentAsync` 接收。UID、模板、数量、来源登记、库存占用和重复附件受校验，不能用普通 Snapshot/伪造 record 消除 pending。
- `TakeMailAttachmentAsync(recipient, attachmentId, now)` 联合验证收件人、可见性、期限、claimed、非 legacy 及真实独占物品行，再原子设置 claimed 并发行本事务 record。普通容器 Put/Split 安置它；未安置提交失败会回滚领取标记。
- 玩家装备/材料从容器引用 Take；GM 由 lifecycle.Create 创建，直接通过 `SendPreparedAsync` 交 record。系统裸 `Equipment(uid)` 不再认领任意无位置行。GM 幂等检查仍在创建前，InstanceValue 仍使用原有随机值；新建 Snapshot 明确设置 Marker16=-1、RandomOptionChangedIndex=255。
- 删除 `IEquipmentTransferDomainService` / `EquipmentTransferDomainService` / `SqliteEquipmentTransferDomainStore` 和两个 DI 注册；没有保留按 UID 独立 SQL 转移入口。原 `MailStackTransfers` 改为 `MailItemTransfers`，统一处理所有邮件物品的 Take 和带容量检查的 Put/Split。
- `MarkClaimedAsync` 现在只处理金钱；物品领取必须经托管适配。金币限额、全部附件失败回滚、received_gold_flag、软删除规则仍在原路径。
- 生命周期 commit 除 pending 校验外，还校验本事务刚接收的附件托管仍存在。过期/软删除不会自动销毁托管物品；同事务合法领取会撤销该附件的接收校验登记。
- Repository 的额外 item-store/accessor 构造参数可省略，供既有只读/纯金币使用；涉及物品写入则明确要求配置托管能力。正式 DI 会注入两者。

## 堆叠兼容取舍

未改变 schema。新堆叠附件使用现有 `detail_json` 保存 `item-records-v1:` 前缀及各真实 record 的 82 字节 core 十六进制数组；原 item_core/item_count 等展示列仍写入。

这**改变了旧邮件发送会重建默认堆叠 Core、清除来源实例字段的规范化行为**：新路径为满足完整 record 不丢未知字段/期限而保留各片段。不同 payload 的片段分别领取，只允许 `Snapshot.CanStackWith` 相同的堆合并；数量拆分由会话登记的 Split 完成。没有放宽既有交易限制。

旧附件 detail_json 为空时继续从原 item_core 恢复，数量须与 item_count 一致；legacy 附件仍拒绝领取。非空且非本版本前缀的堆叠格式拒绝，不猜测其意义。当前协议只有 avatar 分支解析 detail_json，因此本堆叠前缀不影响现有编码。

## 验证

- RED：真实 SQLite 中 Take 并插入邮件附件后提交，原生命周期拒绝为未安置 record（业务失败，非编译错误）。接入托管 Accept 后同一行为通过。
- `Dfo.Server.Persistence` 独立构建通过，0 警告、0 错误。
- `Dfo.Server.ItemLifecycle.Tests` 31 项通过：原 17 项；7 项新邮件能力/回滚/字节/不同 payload/旧格式/篡改校验；链接运行 5 项实际 mailbox repository 测试和 2 项迁移后的物品位置转移测试。
- 日志：`artifacts/item-container-lifecycle/mail-custody-tests.log`。
- Application 尚处于父任务迁移中，整体编译不通过。邮件范围除 GM 配置调用的两处既定外部签名依赖外没有编译诊断；日志 `artifacts/item-container-lifecycle/mail-application-build.log`。没有把错误隐藏为兼容 Core 接口。

## 父任务接续

- `GmMailEquipmentRuleService` 的 `CheckAmplificationAsync`、`QuoteReinforcementAsync` 已传 Snapshot；等待 `IEquipmentUpgradeRuleService` 及其实现迁移 Snapshot（当前两处 CS1503）。
- `IMailItemRuleService` 已迁移 Snapshot；其他非邮件调用及原规则测试需要跟随父任务整体迁移。
- 已迁移 `SqliteMailboxRepositoryTests` 的真实 record 创建夹具、原独立 SQL transfer 测试、架构测试对已删除转移类型的两个例外。Application/Protocol/Host 邮件集成测试中的旧容器夹具仍有 Get/Take/Put 迁移，由父任务全局测试迁移接续；未声称这些测试通过。
- `MailboxAssetDomainService` 仅在生成既有客户端投影 `MailboxInventoryUpdate` 时 ToCore，普通邮件业务与 GM 配置已不使用 Core 作为修改载荷。
- Main/0..2 零数量虚拟槽兼容由父任务处理，本子任务未修改。

本交接时停止所有写入和构建，产品写入权交还父代理。
