# 持久化测试迁移结果

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`。
- 范围：`Dfo.Server.Persistence.Tests`（含 `Inventory/Lifecycle` 的实际生产程序集测试）和必要 Support；无提交。未改任何 wire golden。
- 旧整背包 Save/lease 写回已改成真实事务中的 `Create / Take / Put / Update / DeleteItem`。保留 UID、pile 无业务 UID、完整装备/时装/宠物字段、跨容器转移、显式消耗、重复 custody 拒绝和事务回滚断言；不恢复旧兼容入口或伪事务。
- `CharacterSide` 仅公开现有 item store，角色穿戴种子使用真实 lifecycle session；原低层表测试仍按其层次保留。

## 读取观测与夹具变化

- `InventoryLoadCharacterizationTests` 历史 SQL 种子及所有 core 字节保持不变。`StorageRecordDump` 对 `ItemReference` 使用受控观测形状：owner/container/slot、完整 core、实际 UID、avatar/creature detail、Count、IsPile 均被观察。
- 历史种子明确写 `slot.kind=world`，新读取以持久化 kind 为身份权威，因此旧 fixture PVF 导出的 `Identity=null` 改为实际 UID：角色按 slot 顺序 `7,8,6,9`，账号仓库 `11,10`。未通过改种子或隐藏 UID 消除差异。
- 普通 SplitPile、EntrySaveReconciliation 和其他新增生命周期夹具的消耗品种子修为 `kind=template`、正确 template ID/count，避免把 pile 错种为 world。历史 characterization 不在此修正范围。
- DailyRefill 的旧“底层行必须重建”断言改为 Update 后行、slot、template 保留且数量正确；不要求生产代码重建行。
- EntryRepository 自建只读 schema 加齐目前 store 读取的 creature 列。Avatar 无效数据测试改为真实 SQLite 占位与细节，不再使用只返回假 table row 的 seam。损坏 socket 的 seed 连接短暂关闭 CHECK，随后恢复；生产读取仍执行校验。

## 检验与唯一生产修复

1. 全量首轮：448 个，435 通过 / 13 失败 / 0 跳过。日志 `artifacts/item-container-lifecycle/persistence-migration-test.log`。
2. 真实 SQLite 颜色越界 RED：原两个颜色上界样本均预期 `InvalidDataException`、实际 `OverflowException`，见 `persistence-migration-targeted.trx`。
3. 经父代理授权，最小修改 `SqliteItemDomainStore.cs` 和 `SqliteItemDomainStore.Mail.cs`：共用 `ReadAvatarDetail`，以 Int64 读取并验证两个颜色在 `0..65535`，越界明确抛出 `InvalidDataException`；不改正常值或 schema。
4. 加入两颜色的 -1、65536、long.MaxValue：全量 **452/452，通过，0 跳过**，真实 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，见 `persistence-migration-green.trx` / `.log`。
5. 随后增加邮件读取同一损坏颜色的 6 例：支持的装备邮件被 SQL 注入非法 avatar 列后拒绝，事务回滚，再次正常领取保留 UID/core/custody。邮件并未因此开放时装业务。**MailCustody 定向 13/13，通过，0 跳过**，见 `persistence-mail-colors.trx` / `.log`。因此最终项目预期 458 个；新增后完整套件交父代理做最终串行验证，不能把452与定向13相加为独立总数。
6. 此批授权文件 `git diff --check` 通过。全仓其他 src 文件的空白提示已报告父代理，不越权修改。

以上均为 CurrentImplementation 自动化证据，不代表 A21 实机验收。报告完成后归还全部写入权与构建权。
