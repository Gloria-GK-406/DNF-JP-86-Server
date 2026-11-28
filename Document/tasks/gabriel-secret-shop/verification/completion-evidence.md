---
kind: rks-verification
decision: pass
---

# 加百利实机候选的自动验证

核对任务契约第 1 版。候选源代码为 `5a9143e1c88502f42929a80fe22dcec7d58e3fa0`，当前交付到 `94757a1e1e4846a323184799a5aa13f707956c87`，二者之间只有实机步骤修订。[独立审查](../reviews/change-review.md)已通过。

| 验收 | 证据 |
|---|---|
| SC-1 / SC-2 | 使用真实 dev PVF 验证 59 个副本权重组、9 个基础等级档和实际商品。确定性回归覆盖三种加百利、德利拉回退、人数档、重复 ID、双人结算顺序和个人库存。 |
| SC-3 / SC-4 | 通过真实 SQLite 和生产协议路由验证金币／道具成交、实际账号材料入包、拒绝、库存耗尽、回滚、相同帧重放、重新开窗和离图失效。普通购买与既有结算相关用例一并复测。 |
| SC-5 | Release 发布目录包含 Host 和频道脚本。Host DLL 版本为 `0.3.5+5a9143e1c88502f42929a80fe22dcec7d58e3fa0`，SHA-256 为 `C6CF1E57E8FCA595AB1612E194A8221EE9742E20E2230BBF112CFB75826065B7`。隔离 PVF 的三组覆盖均重新解码验证，详见[实机步骤](../user-test.md)。 |

## 检查结果

- `dotnet build Server/86JP.Server.slnx --no-restore` 通过，0 警告、0 错误。项目依赖检查通过，覆盖 13 个生产项目和 32 条直接引用。
- Application 定向 79 项通过，覆盖 `DungeonSecretShopTests`、`DungeonPickupTransactionTests`、`EffectSettlementRegistrationTests`、`GameplayWriteEntryTableTests`、`ExplicitFootprintLifecycleTests`、`NpcShopPurchaseTests`、`DungeonClearDomainServiceTests` 和 `BuyItemCommandFlowTests`。
- Protocol 2612 项通过，另有已知基线失败的选角入场第 32 帧字节指纹。Infrastructure 真实 PVF 商店相关 8 项通过，另有普通 NPC 商店样本断言与基线相同。
- Host 的频道和启动器 HTTP 启动测试 27 项通过。
- 对不含本功能的 `fe6c55321` 单独取源码、构建并运行，复现选角入场第 32 帧、普通 NPC 商店样本断言和 `PlayerTradeDomainService` 状态普查共三项既有失败。

以上检查支持候选交付。A21 窗口显示、原生客户端成交与双端可见性由用户实机确认。玩家数据库副本和测试连接在用户确定测试窗口后准备。

## PR 前同步主干复验

在 `10fa2d379` 上重放功能提交，源码增量与实机候选一致。对 `f05960d16` 运行 Release 构建和以下验证。

- `dotnet build Server/86JP.Server.slnx -c Release -p:ContinuousIntegrationBuild=true` 通过，0 警告、0 错误。
- `dotnet test Server/86JP.Server.slnx -c Release --no-build --no-restore -m:1 --filter "Category=Unit"` 通过 2211 项，0 失败、0 跳过。
- 同一测试入口用 `FullyQualifiedName~DungeonSecretShop|FullyQualifiedName~SecretShopCodecTests|FullyQualifiedName~DungeonClearProjectionDeliveryTests` 定向执行，使用原始 dev PVF，30 项通过，0 失败、0 跳过。
- `Tool/ProjectReferences` 的 4 项校验通过，检查了 13 个生产项目及 32 条直接引用。
- [本轮实机记录](../user-test.md#本轮实机结果)涵盖用户确认的单人金币路径及正常概率恢复。远端 Linux CI 与 Codex 审核记录由 PR 保存。
