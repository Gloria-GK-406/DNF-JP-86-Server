# 70 服务端精灵秘药与经验路径调查

日期：2026-09-28。证据为 `Research/1995chen-dnf/other/等级补丁/70/df_game_r` 的 ELF 静态反汇编，SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。本报告属于 **70Reference**，不证明 A21 客户端或当前服务器的规则。未运行该程序、未部署或改动服务端。

## 结论

70 的普通击杀路径没有从死亡包读取精灵秘药倍率，也没有在已追踪的击杀经验计算、加成汇总、入账函数中读取角色的经验药字段。死亡包在 `DisPatcher_DieMob::dispatch_sig`（`0x081EB0C4`）解码，最终由 `CParty::kill_monster`（`0x085A27E8`）计算经验并调用 `CUser::gain_exp_sp`（`0x0866A3FE`）。该链与经验药的唯一已找到消费点分离。

服务端在成功使用堆叠道具后，从 **道具模板** 读取“经验影响”标志和 float 倍率，把它写到角色状态，并登记道具 ID 与到期秒数。普通通关时，`CParty::set_clear_reward_exp_parameter`（`0x085ADB00`）检查角色药效、校验未过期，再读取倍率写入通关参数 `+0x2C`；`getClearRewardBonusExp`（`0x085AD278`）用通关基数乘该参数形成独立经验道具加成。因而“使用道具后服务器保存状态，再在通关时计算”有指令证据；“70 普通击杀也乘该药”没有。

## 使用与状态归属

| 地址 | 70 指令证据 | 含义 |
|---|---|---|
| `0x0865E3CC` | `CUser::use_stackable` 调 `CInventory::use_item(...,1,...)`，仅返回值为 0 时于 `0x0865E40C` 调 `_processContinuousItem` | 成功消费一件后处理服务端持续效果 |
| `0x0865EA62` | `_processContinuousItem` 取服务端当前秒，处理冷却与持续效果；`0x0865EB04` 调 `_processSpeicalContinuousTimeItem` | 效果不是死亡包临时传入 |
| `0x08659B9F` / `0x08659BAE` | 查询 `CStackableItem::IsExpAffect` 与 `GetExpAffectRatio`；倍率大于 0 时 `0x08659BDE` 调 `SetExpAffectItemEffect(true, ratio)` | 经验药数据来自服务端物品模板 |
| `0x0869473C` / `0x0869474F` | 模板 `CStackableItem +0x2E7` 为经验影响 byte；`+0x2E8` 为倍率 float | 模板字段偏移 |
| `0x0863BE3C` / `0x0863BE4B` | `SetExpAffectItemEffect` 写角色内部对象 `+0xEE4` byte 和 `+0xEE5` float | 角色当前药效开关及倍率；这里的基址为 `CUserCharacInfo +0x10` 指针所指对象 |
| `0x085BFCFF` / `0x085BFCE0` | `IsExpItemAffected`、`GetExpAffectRate` 读取同一内部对象 `+0xEE4/+0xEE5` | 通关消费的是服务端角色状态 |

`7377 = 0x1CD1`。到期处理 `TimerFinishItemEffect::_processSepecialEffectContinuousItem`（`0x08637D64`）在 `0x08637D6D` 比较道具 ID `0x1CD1`，命中即于 `0x08637D8B` 调 `SetExpAffectItemEffect(false, 0)`。这是 7377 的专门清零证据。`CUser::_processEffectItem`（`0x0865EC0E`）在物品具有效果道具选项时，从模板 `+0x2DC` 取得时长并换算为秒，按道具 ID 在角色 `ContinuousItemInfo` 列表写入 `{itemId, expireAt}`；同 ID 重用时只在新到期更晚时延长（`0x0865ED2D..0x0865ED6E`），否则新增（`0x0865ED99..0x0865EDBD`）。`0x0865ECFB` 在模板具有对应属性变化信息的分支注册消息 `0x76` 的到期计时器；本轮没有以 7377 模板数据确认此分支一定执行。

到期回调 `TimerFinishItemEffect::dispatch_sig`（`0x08637AAC`）查角色持续效果列表；`0x08637C13` 将记录到期秒与当前秒比较，过期后调用上述 7377 清零分支并发送效果通知。通关参数收集还有一次惰性校验：`EnablePeriodExpItem`（`0x08659CF0`）逐条从 `CUserCharacInfo` 内部对象 `+0xEA6` 的持续效果列表取道具 ID、到期秒；查模板且 `IsExpAffect` 为真时，仅 `now < expireAt` 返回有效（`0x08659D96..0x08659DA3`），否则删除记录并清除 `+0xEE4/+0xEE5`（`0x08659DA8..0x08659DE1`）。`GetContinuousItemInfo`（`0x08677F24`）会把冷却与效果列表拷贝到 `SIG_LOAD_ETC` 结构，表明列表参与角色数据传输；本轮未追到数据库写入和重登恢复全链，不能宣称持久化事务已闭合。

注意模板标志和倍率属于所有 `IsExpAffect` 堆叠道具的通用逻辑；`0x1CD1` 专项清零可以锁定 7377 与此状态的关联，但本轮没有读取 70 样本的 7377 PVF 实际倍率或持续秒数。

## 普通击杀链与包字段边界

`DisPatcher_DieMob::read/process`（`0x08232AF4/0x08232AFE`）只是返回 0 的空桩；真正入口是 `dispatch_sig`。它在 `0x081EB0E9..0x081EB5D2` 从 `PacketBuf` 读多个 short/int/byte 并在 `0x081EBB73` 调 `CParty::kill_monster`。首两个 `get_int` 位于 `0x081EB179`、`0x081EB1C1`；`kill_monster` 的 `0x085A3205..0x085A327C` 将其传入击杀历史日志。仅凭数据类型不能将其命名为“药效倍率”；目前没有指令将它们传给经验药状态 setter 或通关药效字段。

`kill_monster` 在 `0x085A3198` 调 `getMonsterTotalExp`（`0x085A23DC`）：该函数按怪物基础经验、地下城权重、难度参数、组队率、命名怪物条件计算公共经验；队伍成员分配在 `0x085A33E0..0x085A35BA` 读取怪物经验表、等级差惩罚、成员惩罚和人数。随后 `0x085A36A2` 调 `sumGainedEachExp`（`0x085A2488`），加入疲劳成长、Seria、周末、premium 等分项，但函数没有调用 `IsExpItemAffected`、`EnablePeriodExpItem` 或 `GetExpAffectRate`。`0x085A3832` 最终调 `gain_exp_sp`，后者处理入账、升级和通知，也没有这些读点。全 ELF 对 `GetExpAffectRate` 的直接调用引用仅 `0x085ADCA8` 一处，即通关参数收集。此为该 70 样本普通击杀链的**静态否定证据**，并非对所有特殊模式、其他版本或 A21 的全局否定。

## 通关经验道具分项

`CParty::set_clear_reward_exp_parameter` 在 `0x085ADC8A` 查 `IsExpItemAffected`，为真才在 `0x085ADC99` 查 `EnablePeriodExpItem`，仍为真才于 `0x085ADCA8` 读 `GetExpAffectRate`。所得 float 写到局部 `-0x20`，于 `0x085AE036..0x085AE039` 写通关参数结构 `+0x2C`。`0x085AE057` 查询活动互斥；若存在重叠经验活动，`0x085AE073..0x085AE078` 将该项置零。因此“喝了就必加通关经验”也需要活动条件。

`getClearRewardBonusExp` 在 `0x085AD421..0x085AD43D` 对参数 `+0x2C` 执行 `trunc(baseExp * ratio)`，写独立结果 `+0x2C`；后续各分项求和。该 ratio 在这里是**额外经验比例**，不应直接读作最终经验倍数：若模板 ratio 为 1.0，该项是再加一份基数；本轮没有证实 7377 模板的实际 ratio。通关基数和其它规则见同目录外的 [`clear-experience-70-20260928/findings.md`](../clear-experience-70-20260928/findings.md)。

## 对 A21 当前现象的判定边界

当前已知 A21 对 7377 发 `CMD 0x002C`，服务端扣一瓶并进入冷却，而所测普通怪仍给 90 EXP。70 证据支持将**道具消费、药效状态、有效期、通关奖励分项**分别核对；90 EXP 未变符合此 70 样本普通击杀不读该药效的路径，不能据此认定 A21 官方也只作用于通关，或当前服务器效果已正确保存。A21 是否在死亡包携带经验相关字段、官方 7377 模板倍率与适用范围、服务端已保存何种效果，仍须以 A21 `ClientStatic/WireCaptured/ClientVerified` 与当前实现证据分别核实。

取证方式：对该 ELF 的 `.symtab` 定位符号，Capstone 按函数地址解码，使用本仓库现有 `Document/outputs/mail-system-investigation/mail_elf.py`；地址均为 ELF 虚拟地址。没有使用上游 C# 作为行为证据。
