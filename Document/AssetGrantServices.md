# 资产赋予与等级设置

## 使用方式

业务命令负责权限、账号访问租约、事务、操作编号、防重复、审计及提交后的通知。初始化、GM、商店和奖励复用领域能力，不直接写资产表。

```csharp
await premiums.For(accountId).AddDaysAsync(PremiumServiceType.CrystalContract, 30, ct);
await wallets.AddAsync(accountId, AccountCurrency.Cera, 10000, ct);
await wallets.SpendAsync(accountId, AccountCurrency.TokenCera, 100, ct);
await characters.Get(characterId).SetLevelAsync(30, ct);
await inventoryLevels.SetLevelAsync(
    ContainerId.ForCharacter(characterId, InventoryContainerType.PersonalCargo), 24, ct);
await itemGrants.GrantAllPersistentAsync(characterId, itemId, count, ct);
```

以上组合放在同一命令事务内，任一失败由调用方回滚整个业务。累加操作本身不防重复；GM 保留操作 UUID，初始化应与创建一起提交。服务不发送协议消息。整批物品发放保留独立调用时自行开启事务的行为，已有事务时加入该事务。

## 领域归属

| 状态 | 唯一修改入口 | 现有调用方 |
|---|---|---|
| 三种点券 | `IAccountWalletDomainService`；数值规则为 `IAccountWalletRuleService` | GM 发放、商城支付；商城仍负责多币种支付顺序 |
| 契约到期时间 | `IAccountPremiumServices` | 商城、开包；`AddDaysAsync` 复用正时长续期能力 |
| 角色等级和累计经验 | `ICharacter.SetLevelAsync` / `SetExpAsync` / `AddExpAsync` | 教程、经验效果、地下城持久效果恢复 |
| 仓库、背包、时装栏容量参数 | `IInventoryLevelDomainService` | 扩容道具、账号仓库升级、商城扩容 |
| 物品赋予 | `IItemGrantDomainService` | 创角、商城、NPC 商店、开包、每日补货、任务、塔奖励 |
| 随身金币 | `ICharacter.AddGoldAsync` / `TrySpendGoldAsync` | 奖励效果、商店、仓库转账等 |

SQL 表接口和物品 store 的读写原语保留在持久化及领域实现内部。邮件托管和物品移动继续使用现有生命周期、取出和接收能力，它们负责真实记录转移。`ICharacterContainerDomainService` 仅管理容器绑定，旧的 `AddAsync` / `AddToPersistentAsync` 发放入口已删除；原整批投递协作者归入物品赋予实现。

## 行为

- 契约：过期从当前时刻开始，未过期追加到原到期时间；天数必须为正。黑钻继续由全服配置控制。
- 钱包：增减量为正整数；拒绝负余额、超过 `int.MaxValue`、余额不足和不存在的账号。计划结算使用完整余额的预期值比较，防止覆盖已变化的余额。
- 角色 `SetLevelAsync`：支持合法范围内升降级，累计经验设为目标等级起点。基础属性和技能点读取当前等级，不缓存旧结果。免费技能按目标等级协调；已有技能校验等级、前置条件和有效达人契约许可，降低不合法技能等级。某页 SP/TP 超支时重置该页并重新授予免费技能；无法定价的未知技能拒绝整次设置。职业、转职和任务状态不随等级设置自动改变。
- 成长奖励通过 `ICharacter.AddExpAsync` 结算并保留预期值检查。等级由经验推导，满级溢出在同一事务中进入账号经验胶囊；显式 setter 不生成胶囊奖励。详见 [角色经验与等级](CharacterExperience.md)。
- 仓库 `SetLevelAsync`：参数沿用游戏持久化容量参数，不是第几个档位。个人仓库为 8、24、40…200；账号仓库使用 PVF 档位，0 表示未开通。商城和道具负责购买前置条件，设置服务检查合法目标、所有者和预期旧档位。降档若隐藏已有物品或存金超过目标上限则拒绝，不移动或删除物品。
- `GrantAllAsync` / `GrantAllPersistentAsync` 全量发放，容量不足则回滚；后者不要求上线或注册容器。单次到达的 `GrantAsync` 保留分批到达结果，供需要自行处理余量的业务使用。
- `ApplyPlannedAsync` 统一执行业务已经规划的空槽落位或合法堆叠增加；`PutNewAsync` 用于明确位置的新建记录，校验容量及空槽，不能覆盖旧物品。

## 验证入口

`AssetGrantServiceTests` 验证钱包边界、跨资产回滚、离线仓库升降档、角色升降级和契约续期。
`AssetMutationOwnershipTests` 扫描 Application、Protocol、GmWeb，确保钱包、成长、契约、容量的表写入只出现在对应领域所有者内。
实际商城、奖励、邮件、创建角色、扩容和协议回归由现有测试项目覆盖。自动化结果属于当前服务端实现验证，客户端设置等级后的展示与后续 Web 配置页仍需对应功能的实机验收。

2026-10-05 本地验证：解决方案构建 0 警告、0 错误；Application 2,967 条、Protocol 1,275 条、Persistence 446 条及 Host 的 GM 端点 95 条均通过，无失败或跳过。PVF 用例使用 `DNFClient-A21-protocol/Script.pvf`。

测试结果保存在 `artifacts/domain-grants-tests/` 下的 `domain-grants-application-final.trx`、`domain-grants-protocol.trx`、`domain-grants-persistence.trx` 和 `domain-grants-gm-final.trx`；构建记录为 `artifacts/domain-grants-build-final.log`。未部署到运行服务，未改动运行数据库。
