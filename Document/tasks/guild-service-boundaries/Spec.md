# 公会服务设计

按需要共同提交的状态以及触发业务的角色划分所有者。协议处理器负责鉴权上下文、工作单元和发包。复合面板从多个领域读取数据，读取操作不产生奖励或权益。

| 业务入口 | 协作顺序和依赖 |
| --- | --- |
| 建会、审批、邀请、离会 | 名称会话保存本连接许可。会籍服务维护会长和会员关系，审批及邀请接受共用入会路径。职级规则由同一处解析默认值和公会覆盖值。 |
| 资料编辑及捐赠 | 公告、会标、光环、宣传和推荐频道交给资料服务。捐赠服务在现有工作单元依次协调金币、公会点数及个人捐赠流水。 |
| 角色进城 | 记录签到并记录本月出勤贡献，两项都执行。二者的变更结果取并集。每日贡献奖励保留自身结算边界。 |
| 购买与赠礼 | 商店核准商品、授权和余额，按原有检查次序提交。适用的商品走共同的权益激活入口，仓库商品调用扩容操作。赠礼保留赠送者、每日限额与领取条件。 |
| 权益消费及过期 | 属性、经验加成、传送次数、过期清理共用权益状态与现行时钟。传送服务负责组队流程。 |
| 组合读模型 | 公共会籍查询供聊天、登录、基地等复用。面板组合会籍、签到、月度贡献与权益。流水由捐赠读取，保留原有排序。 |

验证先覆盖各所有者和 DI，再覆盖协议调用、重复操作、跨事务失败与返回结果。结果和已有基线差异记录在同目录 result.md。

## 服务与依赖

| 所有者 | 写入或读取的事实 | 主要协作方 |
| --- | --- | --- |
| `GuildMembershipDomainService` | 建会、申请、入会、会籍备注、职务、会长转让、退会与解散 | 审批和接受邀请复用入会逻辑。`GuildRankPolicy` 解释职级覆盖、审批及任命权限。 |
| `GuildProfileDomainService`、`GuildDonationDomainService` | 前者维护公会资料和会标，后者维护捐赠流水并读取历史 | 两者复用既有 GM 账户。捐赠服务同时调用角色金币服务。 |
| `GuildAttendanceDomainService`、`GuildMonthlyContributionDomainService`、`GuildContributionDomainService` | 分别维护签到、月度积分与结算、贡献币奖励 | 进城先在同一事务记录签到与月度出勤，提交后读面板。每日奖励有独立的事务及去重键，聊天也走该奖励入口。 |
| `GuildContentPurchaseDomainService`、`GuildBenefitDomainService` | 购买者决定是否扣款；权益服务准备并写入有效期或剩余次数，读取加成，负责消耗与过期 | 购买与礼物领取在各自事务复用同一个权益实例。仓库商品改由 `GuildWarehouseCapacityDomainService` 校验并写容量。 |
| `GuildMembershipQueryDomainService`、`GuildDiscoveryDomainService`、`GuildOverviewDomainService` | 分别提供会籍与名单、查找与推荐、面板快照 | 概览组合会籍、签到、月贡献、权益、盟会五个来源。协议用同一快照按收件人生成面板。 |
| `GuildNameOperationSessionDomainService`、`GuildInvitationSessionDomainService` | 前者以选角会话为键保存一次名称检查，后者保存邀请双方及到期时间 | 金币改名与改名券共用已核准的名称。重查或取消后，较早的异步检查即使返回成功，也无法重新发布旧许可。 |

名称和文本格式复用 `GuildTextRules`。创建费用和捐赠换算使用 `GuildEconomyRuleService`，会标与光环使用 `GuildProfileRuleService`，商品与加成配置使用 `GuildProductRuleService`。月度贡献资格和推荐地下城的内容读取由 `GuildMonthlyContributionRuleService` 提供。
