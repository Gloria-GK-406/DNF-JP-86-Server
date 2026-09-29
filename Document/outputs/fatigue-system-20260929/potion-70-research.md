# 疲劳药：70端参考与A21配置核对

日期：2026-09-29。只读调查，未改变运行服务、数据库或药品配置。

## 证据范围

- 70Reference：`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，本轮用 `Document/outputs/mail-system-investigation/mail_elf.py` 读取符号与指令。此为社区样本，不能假定功能完整或未经修改。
- PvfObserved：当前 `DNFClient-A21-protocol/Script.pvf`，由 `Tool/PvfProbe` 解码。
- CurrentImplementation：当前分支的 CharacterFatigueDomainService。
- 本轮无疲劳药A21实帧/实机验收，未证明70与A21的消息编号相同。

## 70端恢复主路径

`Dispatcher_StackableActionUse::process` (0x0825E11E) 经 check_error、Action::check、Action::run，成功后调用 CInventory::use_item 与 _processContinuousItem。

`StackableAction::Action::proc_addFatigue` (0x0827FBD6)：

1. 检查角色/物品和 action 参数存在。
2. 恢复量取物品 action 参数向量第0项，钳制为非负。
3. getCurCharacFatigue（已用疲劳）小于恢复量时，返回0x5F。也就是说恢复空间不足时拒绝，非直接浪费超出部分。
4. 写入 max(0, used - recovery)，调用 SendFatigue (0x08656540)，返回成功。

这条路线不修改累计已消耗疲劳。70样本此处操作基础疲劳栏，并非通用 gainCurCharacFatigue 的先恢复 premium 栏算法；我们现有服务采用合并余额，不必复制其存储分栏。

另有旧路径：CUser::use_stackable (0x0865E0AE) 在道具成功消耗后按若干硬编码ID恢复6/10/20/30/40点，随后 SendFatigue、CMD 0x2F ACK。这条旧路径没有上述“恢复空间不足”检查，不能与 action 路线混为一谈。A21候选药品使用 `[use action packet] 1`，应优先核对 action 请求。

## 70端通用限次机制及不能照搬之处

- `importLimitItemUsageInfo` (0x0890D871)：读取 `[Limit Item Usage Info]` 三元组；`Add` (0x0891B4A8) 的参数为类型、物品ID、上限。
- `SetLimitInfo` (0x086888F8)：把配置与角色载入的物品ID/次数/时间合并，未有记录时初始化次数0。
- `LimitItemUsage::isUsable` (0x08695F68)：类型0/1使用跨日判断、跨日清0，然后 used < limit；类型2只比较次数、不做每日清0。
- `isAnotherDay` (0x08688FBA)：环境重置时间 + 记录时间 + 当前时间，调用 CheckDailyScheduleTime。不是每个角色各开timer。
- `useIt` (0x08695F28)：可用则次数+1并更新完整时间。
- `CUser::UseLimitItem` (0x08688AE6)：按物品ID匹配记录，调用 useIt，并把角色号/类型/物品ID/次数/时间传往 DB_UpdateLimitItemUsage。

**限制**：本样本 DB_UpdateLimitItemUsage::makeRequest (0x0843F030) 只有空函数体，dispatch (0x0843F022) 直接返回1。全符号函数的直接CALL扫描没有找到 UseLimitItem/IsLimitItemUsable 调用者；上述实际疲劳药处理链也未出现限次调用。只能证明限次结构与算法存在，不能声称此样本已把疲劳药限次、重登持久化做通。间接调用不由直接CALL扫描排除。

70的 SendLimitItemUsageNotiInfo (0x08688F02) 使用 NOTI 0x11F，body三个int：物品ID、上限、当前次数；可选通知发生在useIt之前。当前A21协议库的同号消息是另一身份，**不得直接沿用编号或假定更新顺序**。

## A21药品配置

| ID | 名称 | `[add fatigue]` | `[total usable count]` | 冷却 |
|---|---|---:|---:|---:|
| 2600026 | 缓解疲劳之灵药 | 30 | 1 | 10秒 |
| 10000541 | 魔界抗疲劳秘药(30点) | 30 | 1 | 10秒 |
| 416 | 魔界抗疲劳秘药 | 50 | 3 | 1秒 |
| 7456 | 精力唤醒药剂 | 40 | 未声明 | 24小时 |
| 7518 | 精力唤醒药剂 | 10 | 未声明 | 24小时 |

上述均为 `[expert town potion]`、`[use action packet] 1`，使用场景 village/seria room。
416说明明确每日3个、疲劳低于106才可用；这支持其每日上限含义，但说明文字不是A21边界比较指令的证明。
不能把未声明 total usable count 的24小时冷却道具也一律当作每天1次。

当前A21的 `etc/chn_server_limititemusageinfo.etc` 中 Limit Item Usage Info 没有上述候选药品；有 reset/refill 等其他配置。不能以这张表代替药品自身 total usable count，也尚未证明A21是否对不同疲劳药做共享次数分组。

## 建议迁移边界

1. 内容源读取 action 恢复量及 total usable count，不硬编码所有疲劳药一律1次。
2. 日次数复用现有完整时间戳+DailyResetRuleService；暂建议角色+物品ID计次。跨药品共享次数仍是A21待核对项。
3. 使用命令事务内完成：道具/场景/冷却/每日额度检查，恢复空间检查，扣1个道具、计1次、RefundAsync。
4. RefundAsync 已有，恢复只减少 Used，不撤销 ConsumedToday；使用前的容量检查放在道具规则层。
5. 事务成功后更新疲劳与道具显示。A21 action CMD、次数投影和错误码需要客户端/实帧核对，不照抄70消息编号。
6. 优先以2600026做验收：恢复30、每日1次、第二次拒绝且不扣药、重登仍拒绝、跨业务日恢复额度。

当前仅完成调查，尚未发放或实现疲劳药。
