# 公会重构交付与验证

2026-11-28，`feature/guild-system` 工作树，以 `e493c238f` 及实施前已有的本地改动为基线。验收范围见 [任务契约](TaskContract.md)。

| 契约项 | 已落地的行为 | 核验入口 |
|---|---|---|
| RI-1 | 权益、红包只查询正式成员。解散单独取成员和申请人。改名券、金币改名、会标共同刷新本会与盟会各自的面板，本会成员的角色外观按原可见范围更新。 | [资料变更投影](../../../Server/src/Dfo.Server.Protocol/Game/GuildManagement/GuildProfileProjection.cs)、[投影测试](../../../Server/tests/Dfo.Server.Protocol.Tests/Game/GuildManagement/GuildProfileProjectionTests.cs) |
| RI-2 | 城镇、仓库、商店、合成核验同一基地实例。选图离城前记录各队员的会话、门区实例和会籍；正式进入时核验本次选图。取消、重选、换角和退回原会均不能复用旧凭证。 | [基地访问服务](../../../Server/src/Dfo.Server.Application/Guilds/GuildHallAccessDomainService.cs)、[入场负例与正例](../../../Server/tests/Dfo.Server.Application.Tests/Guilds/GuildDungeonAccessTests.cs) |
| RI-3 | v40 为每次入会分配不可复用标识。旧流水仅按原有归属口径回填一次，并报告同秒歧义行数。贡献榜、个人月度累计与领奖资格使用当前会籍；全会历史保留。公会地下城核对入场会籍，普通疲劳副本在通关成立时冻结参与者会籍，月度与组队收益在个人结算时复核。 | [升级实现](../../../Tool/DbMigrate/GuildMembershipUpgrade.cs)、[升级说明](../../DatabaseSchemaMigration.md)、[普通本通关测试](../../../Server/tests/Dfo.Server.Application.Tests/World/DungeonClearDomainServiceTests.cs) |
| RI-4 | 入账上限、余额和历史已花由同一 GM 账户维护。金币不足的改名调用可以正常返回并提交工作单元，名称和余额均维持原值。 | [GM 账户](../../../Server/src/Dfo.Server.Persistence/Guilds/SqliteGuildPointAccount.cs)、[改名领域服务](../../../Server/src/Dfo.Server.Application/Guilds/GuildRenameDomainService.cs)、[余额、故障回滚与重试](../../../Server/tests/Dfo.Server.Persistence.Tests/Guilds/GuildDomainTests.cs) |
| RI-5 | 仓库通过显式托管端口绑定原物品事务。容量、摘要、历史移出通用公会存储。收益和界面公用职级默认值及覆盖值的解析。仓库审计写失败时核对库存、UID 和堆叠数量，重试后核对唯一审计行。 | [托管契约](../../../Server/src/Dfo.Server.Abstractions/Guilds/IGuildWarehouseCustodySession.cs)、[仓库存储](../../../Server/src/Dfo.Server.Persistence/Guilds/SqliteGuildWarehouseStore.cs)、[职级解析](../../../Server/src/Dfo.Server.Application/Guilds/GuildRankPolicy.cs) |

## 本次运行

| 检查 | 结果 |
|---|---|
| [整套解决方案构建](../../../artifacts/guild-refactor-20261128/delivery-build.log) | 成功，0 警告、0 错误。 |
| [项目引用检查](../../../artifacts/guild-refactor-20261128/reference-check.log) | 13 个生产项目、32 条直接引用通过。 |
| [契约定向回归](../../../artifacts/guild-refactor-20261128/contract-final.log) | 189/189 通过，涵盖应用 57、协议 55、持久化 38、内容 6、迁移 33。 |
| [完整迁移测试项目](../../../artifacts/guild-refactor-20261128/db-migrate-final.log) | 62/62 通过，与前项重叠。全部数据库验证使用隔离数据。 |
| [解决方案 Unit 分类](../../../artifacts/guild-refactor-20261128/unit-delivery.log) | 2,077 通过、3 项失败。失败为 CMD 构造依赖、规则服务读取内容源、地下城请求新旧判定位置三项架构检查。三项在实施前的工作树源码副本上亦失败。[应用基线](../../../artifacts/guild-refactor-20261128/baseline-Dfo.Server.Application.Tests.log) · [Host 基线](../../../artifacts/guild-refactor-20261128/baseline-Dfo.Server.Host.Tests.log) · [协议基线](../../../artifacts/guild-refactor-20261128/baseline-Dfo.Server.Protocol.Tests.log) |
| [扩展跨域回归](../../../artifacts/guild-refactor-20261128/cross-delivery.log) | 239 通过、8 项失败，涉及 7 个选角投影顺序用例与 1 个勋章容器拒绝码用例。实施前快照的选角夹具先因缺少签到投递对象而失败。在隔离副本只补该测试对象的构造后，同样 8 项的测试名和断言消息与本轮逐一相同。[基线对照](../../../artifacts/guild-refactor-20261128/baseline-observable.log) |

本次没有启动 A21 客户端或迁移玩家库。人数上限沿用此前待产品依据的裁定；基地战和旧基地升档的开放状态沿用原工作树。现有用户实测记录保存在 [公会功能目录](../guild-system)。本地验证日志位于工作树忽略的 `artifacts/guild-refactor-20261128`。
