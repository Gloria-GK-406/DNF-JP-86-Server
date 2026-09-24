# Completion evidence

日期：2026-09-24。候选分支 feat/mailbox-core，基线 8923ea26a906bc878f4de2a9491ac61b368a5a9f。独立审阅：reviews/change-review.md，approved。

## 最终验证

- `dotnet build Server/86JP.Server.slnx --no-restore --nologo -v:q`：exit 0，0 warning / 0 error。
- Application.Tests，filter `FullyQualifiedName~Characters|FullyQualifiedName~Inventory|FullyQualifiedName~Transactions|FullyQualifiedName~Commands.CommandHandlerArchitectureTests|FullyQualifiedName~Mail`：最终 884/884，exit 0。
- Persistence.Tests，filter `FullyQualifiedName!~SchemaOwnershipTests.EveryTableIsDeclaredSomewhereRatherThanByAccident`：368/368，exit 0。
- Infrastructure.Tests，filter `FullyQualifiedName~PvfMailItemDefinitionSourceTests`：1/1，exit 0。
- 需要 PVF 的测试设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
- 原始输出归档在同目录 *.txt；旧869项TRX已被最终输出取代，不纳入交付。

## 如实保留的验证限制

Persistence 原有 SchemaOwnership 表数断言45与基线46不一致，本次未加表，单独排除此项；见 baseline-schema.md。不能声称全部现有测试无失败。

最终应用广泛验证第一次出现旧 EquipmentDurabilityTests.Wear_stops_at_zero_and_still_acknowledges_an_already_broken_piece 失败（883/884）；quiet输出无具体堆栈。没有改动耐久代码/测试；随后该类5/5及相同广泛集合884/884通过。原因未定位，保留首次输出与复跑输出，不将一次重跑成功当作根因结论。

## 成功条件映射

|条件|证据|
|---|---|
|SC-1|Mail/Builder只读内容和ImmutableArray；BuilderRejectsInvalidStructureAndCopiesAttachmentList、非法模板/收件人测试|
|SC-2|真实SQLite的发送资产守恒、SQL触发器故障、环境事务失败不可提交测试|
|SC-3|EquipmentRetainsUidAndCoreWhileDetachedAndAfterClaim及装备转交仓储测试，原UID/core/items行保留|
|SC-4|系统离线投递与Reopen后读取/领取测试|
|SC-5|独立附件ID、越权、重复、两服务并发领取测试|
|SC-6|满包部分规划回滚、金币上限全额失败、SQL异常和账号材料回滚测试|
|SC-7|已读、未领附件删除拒绝、期限过滤/领取拒绝及旧9999哨兵兼容测试|
|SC-8|服务重建、旧库迁移、登录读取回归；新增25封分页测试验证总数/旧20封倒序/不写已读；正文附件仅对选中页读取|
|SC-9|完整解决方案构建，架构及库存/角色/事务回归；Application没有引用持久化/网络实现|
|SC-10|Document/MailboxCore.md及spec-result明确服务级交付；没有宣称A21实机邮箱已验收|

## 仓库处置

源main HEAD核对仍为基线；源已有未提交文件不在任务工作区，不纳入提交。一个本地accepted-result提交封存代码、测试及文档，最终哈希由git返回后对用户报告，避免文档自引用。保持命名分支及工作区，未部署、未改玩家数据库、未合并或推送。
