---
kind: rks-verification
decision: pass
---

# 完成核验：史诗碎片拾取与账号图鉴

本次声明：任务契约 revision 1 的本地实现与自动验证完成；不声明客户端已验收，不含部署。

## 当前检查

独立审查为 [approved](../reviews/change-review.md)，含新增登录读取和组合根断言续轮。审查后重新构建/运行，退出码均为 0：

| 检查 | 结果 | 证据 |
|---|---|---|
| `dotnet build Server/86JP.Server.slnx --no-restore -m:1 -v quiet` | 0 警告，0 错误 | [build.txt](build.txt) |
| Persistence：EpicBook / SharedMaterialMigration / ReadOnlyCommandTransaction | 19/19 | [persistence.trx](persistence.trx) |
| Protocol：GroundPickup / SelectCharacter | 109/109 | [protocol.trx](protocol.trx) |
| Infrastructure：PvfEpicBook / PvfHellReward，实际 protocol2 PVF | 3/3，0 跳过 | [pvf.trx](pvf.trx) |
| Application：Transactions / Characters / CommandHandlerArchitecture | 424/424 | [application.trx](application.trx) |
| Host：CarriedGoldComposition / HostCompositionArchitecture / ComponentLifecycle | 8/8 | [composition-unsandboxed.trx](composition-unsandboxed.trx) |
| `git diff --check` | 无空白错误 | 本次现场命令；已有 LF 转换提示不构成错误 |

测试命令均为 `dotnet test Server/tests/Dfo.Server.<Project>.Tests/Dfo.Server.<Project>.Tests.csproj --no-build --filter '<上列名称的 FullyQualifiedName 组合>' --logger 'trx;LogFileName=<表中证据名>' --results-directory Document/tasks/epic-book-pickup/verification -v minimal`。使用 `DOTNET_PROCESSOR_COUNT=4`、`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf`，串行运行。统计和证据 SHA-256 见 [check-results.json](check-results.json)。

额外 Host 检查首次在 sandbox 下 3 通过/5 失败，Windows EventLog `.NET Runtime` 写入被拒绝，见 [composition.trx](composition.trx)。仅调整执行权限，保持相同代码、筛选和 PVF，重跑 8/8 通过，确认测试环境权限干扰。没有产品代码修复或运行服务变动。

## 契约覆盖

| 条件 | 决定性证据 |
|---|---|
| SC-1 | 真 PVF 检查710映射、450004→10300000、普通3330不识别；深渊奖励映射复用统一内容解析 |
| SC-2 | `Saturation_account_isolation_reopen_and_read_only_queries` 验证账号隔离/重读及只读事务；迁移同账号两角色合并31；登录准备仅调用只读余额接口 |
| SC-3 | 临界溢出、已满新增0；`Epic_pickup_bypasses_full_bag_and_rolls_back_with_the_floor` 三组边界均成功消费地面 |
| SC-4 | 上述拾取集成验证普通满背包拒绝、碎片成功、回滚恢复余额/地面与重复拾取无再次到账；普通拾取及事务回归通过 |
| SC-5 | 全量/增量ID和累计值编码、满额省略变更帧、ACK→移除→余额顺序、队员仅移除及登录原位置均通过；真实PrepareAsync读取13余额；协议明确上游证据与实机未验收 |
| SC-6 | 多角色及账号仓库旧碎片转入账号、普通材料保留、重复迁移不增长、封顶、无效core拒绝、强制删除失败全部回滚 |

## 保留边界

非空图鉴客户端显示、跨角色与重登显示、目的槽0拾取通知的实际效果需要部署后实机测试。这是契约中明确保留的验收边界。本次没有客户端 patch、部署、在线DB操作、推送或PR。
