# H15 光明之心礼包购买失败修复

## 请求与范围

用户反馈 H13 ok、H15 购买光明之心礼包失败；诊断后授权“你部署吧，然后给我一个待验收的新表格”。在现有 `task/solo-play-roadmap` 工作区完成本次商城限购支持，准备并切换验收服务，提供 [第二轮表](../tasks/item-effects-unification/human-test-list-20261001-round2.md)。H13 据用户反馈记 ClientVerified；H15 本轮实机仍 HumanPending。

## 根因与实际行为

旧运行 PID21384，2026-10-01 20:39:18 日志记录商品 101236 因 `PurchaseRestriction: etc/cerashop.etc: Unimplemented catalog purchase restriction` 拒绝。旧内容源把 `[buy restrict item]` 的第二列放进 unsupported 集合，丢失了限制 ID、周期参数和初始标志，未进行扣费或派送。这是 CurrentImplementation 解码后的现场日志，未新增 raw 帧归档。

PVF `Script.pvf` SHA-256 `3b2fe3da115fee949282558a864a319fc9ca197e1d1f22e656e74316a0f04b06`：目标记录 `32 101236 30 1`；礼包模板 2682780，1960 点券，10 种固定奖励及力量／智力秘药 30 个二选一。

新内容源保留类型化声明；规则服务检查商品、选择、购买等级及限购状态，构建 `SetPurchaseRestrictionEffect`。效果分发器通过持久化表的 compare-and-set 写入账号限制。余额、物品、限制在同一事务内结算，任何晚期失败均回滚；受限商品不能在一个购物车重复，普通不受限购买不额外读取限购状态。

购买完成通知及登录投影从相同持久化状态构造完整 NOTI 0x0344 列表。客户端收到它会先清空旧状态，因此不能仅发送新增的一条。新表 `account_purchase_restrictions` 只添加结构，不预置或改写测试账号记录。旧兼容状态仍保留账号一致性检查；未支持的初始标志、not-stackable-buy 等继续显式拒绝。

当前规则选择：账号范围，正周期参数按天数解释；光明之心礼包购买后冷却 30 天，0 采用永久限制 sentinel。A21 静态路径证明四字段读取、标志和通知时间语义，**不独立证明第三参数的官方运营含义及原服账号/角色范围**。这些为当前可验收实现选择，历史规则仍待核定；未宣称 70Reference 或 ClientVerified。

## 验证

证据根目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/h15-cera-restrictions-20261001/`。

| 检查 | 结果 | 证据 |
|---|---|---|
| 行为 RED：实际商品 101236 首次购买 | 按预期失败 UnsupportedProduct／限购未实现 | tests/red/h15-red.trx |
| 实际 PVF 与商城／账号聚焦回归 | 74 通过 | tests/focused/h15-focused-final.trx |
| Release 全解决方案 | 0 警告、0 错误 | build-final.log |
| Application 全套，最终源代码 | 2643 通过，0 失败／跳过 | tests/recheck/h15-Application-recheck.trx |
| Infrastructure 全套 | 752 通过，0 失败，2 按环境条件跳过 | tests/recheck/h15-Infrastructure-recheck.trx |
| Persistence 全套 | 393 通过，0 失败／跳过 | tests/final/h15-Persistence-final.trx |
| Protocol 全套 | 1226 通过，0 失败／跳过 | tests/final/h15-Protocol-final.trx |

自动覆盖：真实礼包固定奖励及两个选择；重复购买资产不变；同账号换角色；冷却结束前一秒与到期边界；永久限制；不同账号独立；点券不足和满背包不占限购；晚期物品写入失败后余额/限制/奖励一并回滚；新表持久化重开及 compare-and-set；完整限制通知编码及登录投影。

中间失败保留：聚焦测试最初缺少 fixture 的 PC-room 状态和误用了消耗品槽范围，补足真实 fixture 后通过；新解析分支最初将非法 PVF 返回 diagnostics，违反原有抛异常约束，恢复类型解码和 InvalidDataException 后通过；新增表后旧 schema 测试的 47 表基线更新为 48。Application 一次后续全跑出现 BlackDiamondTests 的 SQLitePCL.sqlite3 ObjectDisposedException，黑钻单独 3 项及完整重跑 2643 项均通过；未修改黑钻或 SQLite 生命周期实现，也不据此宣布已证明基线缺陷。

两项 Infrastructure skip 是在存在真实 PVF 时按条件跳过的“缺失档案”测试；实际 PVF 测试并未跳过。

## 部署与人工边界

发布目录 `artifacts/h15-cera-restrictions-20261001/runtime`，沿用原 appsettings.json／experience.json、原数据库和原 PVF。manifest.json 记录最终本地提交及内容哈希；部署脚本检查游戏已退出、原 PID/路径、配置/程序集/PVF 一致，先 SQLite backup，再切换进程，验证 API ready 与 7001/10010/10011/18080 端口归属。原有全部业务表行哈希保持，仅允许新增空的限购表。发生切换失败则记录失败并恢复旧服务进程。

实际部署结果、PID、启动时间与就绪信息以同目录 `deployment.json` 为准：仅 state=deployed 证明成功。部署不代购、不赠送、不清空限购记录、不改客户端。

H15 界面到账、即时限购、重登和换角色效果由用户按新表验收；H11 属性转换缺陷及 H10 缺样本等未扩大到本次修复。
