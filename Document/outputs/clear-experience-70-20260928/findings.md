# 70台服普通通关经验算法调查

日期：2026-09-28。只读调查；未修改或部署游戏算法。

## 证据边界

本次分析的是 `Research/1995chen-dnf/other/等级补丁/70/df_game_r` 的 ELF 服务端，不是客户端。结论属于 **70Reference / 静态指令证据**，不直接证明 A21 官服数值。该文件也是第三方仓库提供的样本，不能保证从未被修改。

## 已确认的普通通关主链

`CParty::clear_reward`（0x085AE0D4）在 0x085AF856..0x085AF9D6 构建队伍通关基数，之后逐角色计算。

```text
L = 地下城标准等级（异次元有另外的标准等级分支）
M = get_mob_reward(L)
K = BattleData.GetTotalKilledMonsterCount()
P = ServerParameter 内按队伍人数选取的倍率 + PartyExpBonusEvent 的增量
D = ServerParameter 内按难度选取的倍率
W = dungeon.get_exp_weight()

shared = trunc(M * K / 2 * P * D * W)
personal = trunc(shared * BaseExpPenalty(characterLevel, L)
                        * MemberPenaltyRate() / memberCount)
```

`MemberPenaltyRate()` 在这个样本中直接返回 1。以上为普通路径，RiskDungeon 跳过等级差惩罚；地下城 +0x89E 为真时另走按角色等级取 M 的分支，暂不命名该标志。QuickParty 随机奖励在后面还有一次变换，不能把上面的 personal 当作所有活动条件下的最终值。

地址证据：

- 0x085AE274：普通地下城标准等级。
- 0x085AF872：取该等级的怪物经验表值。
- 0x085AF930：取实际击杀怪物总数。
- 0x085AF947：除数只读常量 0x08CBBBD4，解码为 float 2.0。
- 0x085AF88C / 0x085AF8CA：人数索引参数和组队经验活动增量。
- 0x085AF979：难度索引参数；与击杀公共公式 0x085A2458 读取同一个参数位置。
- 0x085AF997：地下城经验权重。
- 0x085AFCC5：逐角色等级差惩罚。
- 0x085AFCDE..0x085AFD33：成员惩罚、除以人数、向零截断。

人数/难度表的消费位置已确认；本轮没有追完 ServerParameter 解析器到 PVF 标签的字段写入链，不应直接将它们与 A21 同名标签宣称为字节级对应。

## 基数来自怪物经验数据库表，非任务经验表

`CDataManager::set_mob_reward`（0x083605A8）执行：

```sql
seLect level, exp from monster_reward_ref
```

SQL 字符串位于 0x08C32E24。启动装载 level 与 exp 到数组；`get_mob_reward`（0x08360A6E）按等级读取，合法等级范围为 1..199，越界返回 0。

本轮未取得该数据库的实际数据，不能声称当前 `MonsterExperienceWorth` 拟合曲线就是 70 原表。现有曲线来自另一个参照实现，本次只确认使用“按等级取怪物基础经验”的模型。

## 等级差系数

`BaseExpPenalty`（0x08360914），差值 = 地下城标准等级 − 角色等级：

| 差值 | 系数 |
|---|---:|
| ≤ -7 | 0.05 |
| -6 | 0.20 |
| -5 | 0.50 |
| -4 | 0.75 |
| -3..0 | 1.00 |
| 1..3 | 1.12 |
| 4..5 | 1.00 |
| 6 | 0.75 |
| 7 | 0.70 |
| 8 | 0.60 |
| 9 | 0.50 |
| ≥10 | 0.05 |

例如角色 80 级、地下城标准等级 64 时，普通通关基数只保留 5%。本例是条件演示，不能以房间怪物等级直接代替地下城标准等级。

## 分项加成结构

`set_clear_reward_exp_parameter`（0x085ADB00）收集各加成参数；`getClearRewardBonusExp`（0x085AD278）输出独立分项并求和，与当前 ExperienceBreakdown 的设计方向一致。

- 评价倍率经 `ServerParameterScript::GetExpBonusWeight` 取得（0x085ADD41），分项为基数乘倍率并截断（0x085AD2F5..0x085AD324）。
- 可见装扮、宠物、经验道具、频道、网吧、活动、部分 premium 等独立来源；经验道具还受活动互斥判断，不代表所有效果必然叠加。
- 有 `calDeadUserDungeonPenalty` 前置调整，不能概括为仅“死亡就扣固定比例”。其计数的生产链本轮未展开。
- 最后累加分项（0x085AD79E 起），而非将所有倍率连续相乘。

## 对当前实现的影响

当前 `DungeonClearExperienceRules.BaseWorth` 读取 `QuestParameters.ExperienceByLevel`，乘通关倍率、难度和地下城权重；上下文没有击杀数、队伍人数，规则也未应用角色等级差。其注释将“任务与通关共用表”写成了确定事实，这一点没有本次 70 证据支持，应在后续修复时撤回。

本次实测当前实现：普通怪 90、Boss 362；地下城 84 结算基础 709719、评价 141943，总计 851662。入账加法正确不等于奖励算法正确。70 模型已经提供足够明确的重构方向，但不能据此给出 A21 官方精确经验值。

建议下一步：通关上下文补实际击杀数与队伍人数，普通通关改用怪物基础经验来源及等级差规则；保留统一 ExperienceBreakdown。A21 数值用已有 PVF 和明确可配置规则，不直接复制 70 数据库或活动常量。统计口径需防重复死亡报告重复计数，并明确召唤物/APC 是否参与。

## 附件

- `clear-reward.asm.txt`：普通通关完整函数。
- `clear-parameters-bonuses.asm.txt`：参数收集与分项求和。
- `base-and-penalty.asm.txt`：怪物经验表读取、等级差、成员惩罚、击杀公共公式。
- `mob-table-and-death.asm.txt`：数据库装载和死亡惩罚。

生成工具：`Document/outputs/mail-system-investigation/mail_elf.py`，Capstone 从符号函数入口线性解码，未执行 ELF。
