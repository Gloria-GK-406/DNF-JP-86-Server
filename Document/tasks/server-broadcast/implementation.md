# 全区系统广播

基线：main@ef20398a3（0.3.2）。关联：#61 聊天与通信、#343 满级邀请函。
方案与 70／第三方参照的盘点见工作区 `E:\Project\DNF\Document\全区广播接入方案-20261114.md`。
协议结论以 [NOTI-0x0056](../../../protocol/game/server-to-client/NOTI-0x0056.md) 为准，本文不重复。

## 范围

用户裁定（2026-11-14）：
- 已实现的玩法全部接入播报。
- 范围为全区（所有频道的在世界内角色），本人也会收到。
- 强化失败也播。
- 不做防刷屏。

| 种类 | 触发 | 判定数据 | 载体 |
|---|---|---|---|
| 满级 | `CharacterLevelChanged`，`Before < TopLevel == After` | `IExperienceRuleService` 的 TopLevel（86） | 01F7 文本 |
| 二次觉醒 | `CharacterGrowTypeChanged`，觉醒半字节 = 2 | grow type 高 4 位 | 0x0056 子类 0x14 |
| 强化 / 增幅（含强化券） | 强化结算 | PVF `upgrade.etc` / `amplifyupgrade.etc` 的 `[notice]`（均为 12）。成功比新等级，失败比起始等级；券只播成功 | 01F7 文本 |
| 黑钻售货机 | 售货机结算 | 奖池行第 4 列 = 1 | 01F7 文本 |
| 魔盒 / 礼包 | 开盒结算（批量开时逐次抽取） | `[randombox]` / `[broadcast]` 行第 4 列 = 1 | 0x0056 子类 0x12 |
| 抽奖罐 | 抽奖结算 | 奖品稀有度 ≥ `LotteryMinimumRarity`（默认 3） | 0x0056 子类 0x12 |
| 锻造 | 锻造结算，成败都播 | 尝试的阶段 ≥ `ForgingMinimumStage`（默认 6） | 01F7 文本 |
| 设计图（镇魂→释魂） | 制作成功 | 配方 `[broadcast type]` | 01F7 文本 |
| 绝望之塔 | 通关第 10、20…100 层 | 层号 | 01F7 文本 |
| 远古地下城拾取 | 地面拾取 | 地下城标记为远古，且稀有度 ≥ `AncientDungeonMinimumRarity`（默认 4） | 01F7 文本 |

不做：
- 合成（+N）、100 层幻影塔：本仓没有对应玩法。
- 通关用时、爆史诗数量等 70 插件的自定义公告。

## 载体选择

- 带 u16 uid 的 0x0056 子类型只在接收方本频道会话中见过当事人时显示（ClientVerified），不能做全区广播。
  这类事件改发 01F7 mode 2 系统行，文案照抄客户端简中原文（`ServerBroadcastTexts`）。
- 0x0056 只用名字在包内的 0x12、0x14。
- 子类 6 不用：客户端把等级写死为 Lv85，本服满级是 86。
- 满级文案暂不含「将获得500张邀请函奖励」，由 #343 补上。
- 绝望之塔文案不含原文的塔主半句：本服 10–90 层的塔主是挑战者自己的镜像。

## 结构

- **事实**：`Abstractions/ServerBroadcast/ServerBroadcastFacts.cs`。
  - 结算的领域服务在同一事务内 `EnqueueAsync`，只在内容或规则认定值得播时才登记。
  - 登记的服务：`EquipmentReinforcementDomainService`、`VendingMachineDomainService`、
    `PackageOpeningDomainService` / `MagicBoxOpeningDomainService`、`LotteryOpeningDomainService`、
    `EquipmentForgingDomainService`、`RecipeCraftDomainService`、`DespairTowerDomainService`、`GroundPickupDomainService`。
- **规则**：`IServerBroadcastRuleService`（Application `ServerBroadcastRuleService`）。
  - 负责开关和 PVF 未声明的阈值。
  - 稀有度与远古地下城的判定也放在这里，因为内容源只能注入规则服务（`ContentBoundaryArchitectureTests`）。
- **投递**：Protocol `Game/ServerBroadcast/` 切片。
  - `ServerBroadcastSubscriber` 以 LiveOnly 订阅九种事实：提交后只投给在线会话，错过不补播。
  - 受众是 `IChatScopeDomainService.MembersOf(ChatScope.Server)`。
  - 投递经 `ServerBroadcastPosted` 通知，每个收件人收到同一帧。
- **PVF 解析补充**：
  - `OpeningReward.Announce`：奖池行第 4 列。
  - `RecipePlan.Broadcast`：`[broadcast type]`。
  - `VendingMachineRuleService.RewardAt` 改为返回整个 `VendingReward`，`Announce` 不再在这一层丢失。

## 配置

`ServerBroadcast` 节（`appsettings.json`，缺省值在 `ServerConfigService` 里补齐）：
- `Enabled`：总开关。
- 每个 `ServerBroadcastKind` 一个开关，默认全开。
- `ForgingMinimumStage` 6、`LotteryMinimumRarity` 3、`AncientDungeonMinimumRarity` 4。

## 验证边界

- **自动测试**：
  - 0x0056 两种子类型的字节与 P0 实机样本逐字节一致。
  - 文案、受众、开关、满级与二次觉醒的判定。
  - 强化 `[notice]` 阈值（成功与失败各两种）。
  - 售货机标志行。
  - PVF 魔盒第 4 列与配方 `[broadcast type]`。
- **实机**：
  - P0 已证明各子类型的显示，以及 uid 子类型的可见范围。
  - 接入后的实际触发待用户测试，见 [user-test.md](user-test.md)。
- **未证**：
  - 礼包 `[broadcast]` 分类只出现在 4 个活动箱，第 4 列语义按参照读法（Hypothesis）。
  - 抽奖与远古的稀有度阈值是服务端取值，不是 A21 原值。
