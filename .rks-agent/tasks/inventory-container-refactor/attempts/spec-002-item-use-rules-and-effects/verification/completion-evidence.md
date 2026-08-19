---
kind: rks-verification
decision: pass
---

# 完成验证：道具使用的判定、效果与诚实的拒绝

## 被验证的主张

子契约 `TaskContract.md` revision 2 的 AC-1..AC-6 已达成：道具使用拆成只读判定与效果两部分，
不消耗类道具不再被销毁，冷却按归档声明的共享标识生效，两条协议的拒绝原因由命令结果承载
并只映射到客户端确实会显示且语义相符的提示码，没有相符文案时不显示提示。

证据于三轮评审通过后重新采集，未沿用实施阶段的旧结果。

## 条件与证据

| 条件 | 证据 | 结论 |
|---|---|---|
| AC-1 判定可由角色与道具标识单独问出，覆盖四类拒绝分支 | `IItemUsabilityDomainService.CanUseAsync(CharacterId, int, CancellationToken)` 的参数只有角色与道具标识；`ItemUsabilityDomainServiceTests` 覆盖冷却、等级、职业、场所四类拒绝，以及「归档未声明即无限制」「声明了却读不出即拒绝」两种边界，另有 `Asking_does_not_start_a_cooldown` 证明提问不改状态 | pass |
| AC-2 不消耗道具使用后数量不减且应答成功（存储侧） | `UseStackableHandlerTests.An_unlimited_item_is_used_without_leaving_the_pile` 断言存储数量不变且 `ErrorCode` 为 null；变异验证：让 `PlanAsync` 对每个道具都产生减数量效果，该用例变红 | pass |
| AC-2 同上（实机侧） | **未覆盖**，见下方「实机走查」 | 见备注 |
| AC-3 冷却按 `[cooltime group]` 共享，无组时按模板独立 | `Items_sharing_a_cooltime_group_share_the_cooldown`、`Items_without_a_group_cool_down_separately`、`A_group_number_and_an_item_number_are_not_the_same_cooldown`、`A_cooldown_that_has_elapsed_no_longer_refuses`；变异验证：冷却键忽略共享组，第一条变红。实机另有独立证据，见下 | pass |
| AC-4 失败原因由命令结果承载，处理器不再内联推断 | `CharacterContainerDomainServiceTests.The_item_protocol_handlers_decide_nothing_about_items` 以反射断言两个协议处理器的构造参数中既无仓储也无容器、且含 `ICommandBus`；`The_item_commands_reach_storage_only_through_a_container` 断言两个命令处理器持有容器而非仓储 | pass |
| AC-5 每条拒绝路径的码或语义相符、或静默 | `ItemRefusalCodeTests`：静默码 `2` 与 `0x002C` 分支链、`0x0013` 分支链、共用兜底表 `0x01B897C0` 三张表逐一比对均不相交；`Every_use_refusal_is_answered_with_a_code` / `Every_move_refusal_is_answered_with_a_code` 遍历两个枚举，任何未决定码的拒绝原因都会使 `ErrorCodeFor` 抛出而变红；`Every_refusal_the_judgement_can_give_has_a_status_of_its_own` 断言五个拒绝裁决映到互不相同的 status。选码与 `cmd-reason-messages-20260818.md` 的逐条对照由第 2 轮评审独立复核 | pass |
| AC-6 其余行为未变（自动化） | `dotnet test Server/86JP.Server.slnx -m:1`，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均指向 `D:/project/DNF/DNFClient-debug/Script.pvf`：Pvf 60、Application 368、Host 4、Infrastructure 170（跳过 2）、Networking 46、Protocol 200，合计 **848 通过 / 2 跳过 / 0 失败**，即基线 791/2/0 加本次新增的 57 个用例。第 3 轮评审逐行 diff 确认两个 `Encode` 与基线完全一致、`PlanAsync` 的全部条件行一字未改、每请求仍恰好一帧 | pass |
| AC-6 其余行为未变（实机） | revision 2 要求的两项均已取得：喝药回血且数量减一、冷却期内再按无效。证据见下方「实机走查」 | pass |

## 实机走查

2026-08-19，工作树构建的 `Dfo.Server.Host`，配置覆盖为 `--Networking:Enabled=true`、
`--Database:Path=<数据库副本>`、`--Pvf:ArchivePath=D:/project/DNF/DNFClient-debug/Script.pvf`。
为不改动既有测试数据，先把数据库复制一份，只在副本上放置测试道具，走查后删除副本。

### 已覆盖

**使用与扣减**（AC-6）。角色 5 的快捷栏持有 `8474 德利拉的新手HP药剂`。按键使用后回血且数量减一，
服务端日志：

```
Use stackable Game:01-00-00-00 character=5: used item 8474 from slot 4, 8 left
```

**服务端冷却是权威的**（AC-3）。此项由用户设计：客户端自己按 `[cool time]` 拦截并显示倒计时，
所以正常情况下服务端那道冷却永远轮不到说话。走查时临时把服务端读到的冷却值 ×2
（药剂客户端 10 秒、服务端 20 秒），撑出一段「客户端放行、服务端仍在冷却」的窗口。
连按三轮的日志是干净的交替：

```
used item 8474 from slot 4, 8 left
slot 4 refused as OnCooldown
used item 8474 from slot 4, 7 left
slot 4 refused as OnCooldown
used item 8474 from slot 4, 6 left
slot 4 refused as OnCooldown
```

数量 9→8→7→6，三次拒绝一个都没扣。该 ×2 探针在走查结束后已还原，
`PvfItemUseRuleDefinitionSource` 现为 `CooldownMilliseconds: Math.Max(0, cooldown)`，
全仓无 `TEMPORARY` 残留，还原后重新构建并重跑全套。

### 未覆盖：不消耗道具的实机表现

AC-2 的存储侧行为已由自动化证明，实机侧未能覆盖：**客户端拒绝对任何一个不消耗道具发出使用请求**，
服务端因此一帧都没收到（日志中该时段只有 `0x00A3` 心跳，无 `0x002C`）。

依次尝试过六个 `[unlimited *]` 道具（分四类门槛），均失败：

| 道具 | 现象 |
|---|---|
| `51 无限猫头鹰` | 客户端不绘制。该道具在归档中未声明 `[stack limit]` |
| `1120 服务器喇叭 (GM)` | 需要发言协议，本服未实现 |
| `1143 混沌王牌` | 需要白色小晶块与无色小晶块作材料，角色没有 |
| `10008052` / `10008051` / `2749196` | 客户端提示「超过使用期限无法使用」。把核心里的 `ExpireTime`（偏移 41，unix 秒，编码取自参考实现 `MailboxSelfTest.cs:165`）改成一年后仍无改善，客户端依旧不发帧 |

因此契约 Assumptions 里那条前提**原样保留、仍未验证**：客户端在发出使用请求时自行把数量减一，
而成功应答不带剩余数量，服务端无法经由应答纠正它。若日后证实客户端对不消耗道具仍做本地自减，
则需要一条把真实数量送回客户端的推送路径，那超出本 Spec 边界。

一条间接的旁证：走查证实客户端自己实现了 `[cool time]` 并显示倒计时，说明它确实按归档执行这类规则，
因而多半也按 `[stackable type]` 决定是否本地自减。这是推断，不是证据，不计入验收。

## 因本成果而改变的可观察行为（EV-006 清单）

除下列各条外，对外行为未变。

### `0x0013 MOVE_ITEMSPACE` 的失败码

| 拒绝原因 | 旧码 | 新码 | 理由 |
|---|---:|---:|---|
| 列表不服务 | 2 | 23 | 旧码不在客户端分支链里，一个字都不显示 |
| 物品不能占用目标槽 | 2 | 23 | 同上 |
| 交换时只给了部分堆叠 | 2 | 23 | 同上 |
| 交换时被顶出的物品放不下 | 2 | 23 | 同上 |
| 合并会超出堆叠上限 | 2 | 17 | 客户端对这条有专属文案 |
| 两槽皆空 | 4 | 2 | 旧码说「金库空间不足」，与原因无关 |
| 读写间槽位改变 | 4 | 2 | 同上 |
| 无会话 / 无绑定容器 | 4 | 2 | 同上 |

### `0x002C USE_STACKABLE` 的失败码

| 拒绝原因 | 旧码 | 新码 | 理由 |
|---|---:|---:|---|
| 列表不服务 | 23 | 23 | 未变 |
| 归档不认识该道具（含装备） | 23 | 23 | 未变 |
| 槽为空或槽中不是客户端指名的道具 | 23 | 2 | 这是双方对背包认知不一致，不是玩家做错了什么 |
| 读写间槽位改变 | 23 | 2 | 同上 |
| 无会话 / 无绑定容器 / 角色状态未发布 | 23 | 2 | 服务端自身的问题 |
| 等级不足 | （无此分支） | 14 | 新增判定 |
| 职业不符 | （无此分支） | 8 | 新增判定 |
| 冷却中 / 场所不允许 | （无此分支） | 19 | 新增判定，两者共用客户端唯一贴近的一句 |

### 行为本身

- 归档中 84 个 `[stackable type]` 以 `unlimited` 开头的道具使用后不再被扣减。
- 6,254 个声明 `[cool time]` 的道具的冷却开始在服务端生效，其中 1,713 个按 `[cooltime group]` 共享。
- 声明了 `[usable job]` / `[minimum level]` / `[action usable place]` 的道具开始按其判定。
- **177 个道具因声明中混入本服未建模的取值而恒不可用**（职业侧 64 个、场所侧 113 个）。
  例如 `2660698 角色更名卡` 声明 `[village] [seria room] [dont use assult]`，第三个取值未建模，
  整条场所限制因此判为不可读，该道具被拒。这是契约「归档里读不出所需事实时按拒绝处理，不猜数值」
  的直接后果，不是缺陷；要缩小影响面，只需向 `ArchiveJobFamilyTokens` 与 `PlaceTokens` 补入这些取值，
  而那需要先弄清它们的语义。当前 `0x002C` 只服务主背包消耗品，这 177 个里几乎没有可触达的。

## 走查中获得的新事实

- **客户端自己实现了 `[cool time]` 并显示剩余秒数。** 服务端的应答帧不含时间字段，所以那个倒计时
  只能是客户端算的。因此这条规则有两道，客户端先生效；服务端这道是权威判定（防改包），
  正常玩家看不到它的拒绝。
- **客户端对 `[unlimited *]` 道具另有有效期检查。** 同样 `ExpireTime=0`，`[waste]` 的药剂照用不误，
  三个 `[unlimited waste]` 却报「超过使用期限」。这提示 `unlimited` 的语义可能是
  「使用不消耗，改由有效期约束」而非「永远无限」。仅为观察，未深究。
- **客户端在接受按键的那一刻就应用了 `[hp recovery]`，不等服务端应答。** 因此服务端拒绝时，
  玩家会看到「回了血但道具没扣」。这与参考实现一致——回复效果本就不是服务端发的——
  且只在客户端冷却被绕过时才可能出现。

## 补充记录

- 三轮独立评审的完整记录见 `../reviews/change-review.md`，最终 `decision: approved`，无未决阻断项。
  第 1 轮 1 条阻断 + 11 条非阻断，第 2 轮 3 条非阻断，第 3 轮 1 条非阻断；已处置 12 条，
  其余 3 条记录了不处置的理由。
- 走查期间因 Git-Bash 的 `pkill` 匹配不到 Windows 进程，一度有四个 host 进程并存、
  最早那个占着端口，导致「改了副本、客户端连的却是主库」。已改用 PowerShell 按命令行匹配终止，
  并在每次启动后核对进程数与端口归属。主检出的数据库只被用户自己的游戏行为改动过
  （喝掉一个药剂、入场补给发放一件），本次走查的所有造数都在副本里，副本已删除。

## AC-6 实机要求的重述

本次验证初判为 `manual_required`：子契约 revision 1 的 AC-6 要求三项实机观察，其中
「不消耗类道具使用后数量不减」未取得。缺口的性质不是「还没去测」，而是**当前客户端不肯为任何一个
不消耗道具发出使用请求**，四个候选各被不同的客户端侧门槛挡住（详见「实机走查」），
服务端收不到帧，实现正确与否观察不到差别。

收窄验收面不是实施者可以自行做的决定。该事实上报后，用户于 2026-08-19 授权重述
（「按1延后吧。后续再说」），子契约升至 revision 2，AC-6 的实机要求改为两项，
「不消耗道具的实机确认」记入根 `../../multi-spec-map.md` 的 Deferred Follow-up。

AC-2 不受影响：它要求的证据是自动化的，已满足且经变异验证不是恒真。本次验证据 revision 2 判为 `pass`。
