# 物件破坏与个人任务奖励：70 服务端补查

日期：2026-09-24。范围：只读 ELF、现代参照源码及本轮 A21 实帧；未改生产逻辑。

## 结论

70 构建不能提供“物件实例编号”或“按物件数量扣减的任务奖励”完整实现。其破坏入口按模板代码校验地图存在性，然后推进地下城完成条件。现代参照服另行实现按同型号未记录槽逐个消耗，再调用个人任务奖励；这是参照实现策略，不是70原版算法证据。

## 70 ELF：Confirmed static

文件：`E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r`。
本轮 SHA256 复核：CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7。

1. `DisPatcher_DieMob::dispatch_sig` 081EBB0E 检查物件标志，081EBB2A 调 `CParty::DestroyPassiveObject(code)`，随后直接退出该分支，不进入普通 kill_monster。
2. `CBattle_Field::DestroyPassiveObject` 082FF3EC 完整153B：遍历当前CMap+0xA8的整数向量，找到相等代码即返回true。无erase、置死、数量扣减、实例坐标匹配，也不调用任务发奖。
3. 向量来源：CMap::set_map 0834E826..0834E8C2 从普通被动物件声明list逐项push首整数。该链是代码存在性校验，并非一次性实例校验。不能直接扩大到A21特殊物件全部声明。
4. `CParty::DestroyPassiveObject` 085B6540 完整98B：上述校验成功后调用 ClearCondition(0, code)，满足则 ClearDungeon。没有直接 CheckQuestMonster 调用。
5. `CClearCondition::ClearCondition` 082FEFCE：以条件type和code匹配，未达到所需数时增加该条件完成数及总完成数（082FF099..082FF0B2）。这是“地下城目标计数”，不是按实际地图剩余机器数限额。函数有目标数封顶，不能因此宣称存在逐物件去重。
6. 既往四个已定位 CheckQuestMonster 直接调用者为普通击杀、Boss Stage、Death Tower、WarRoom；现有证据未闭合类型3物件奖励来源。未重新作全镜像间接调用穷尽，不断言所有其他路径不存在。

完整关键函数见 `tw70-destroy.asm.txt`；调度与set_map证据见上级目录 `die-mob.asm.txt`、`map-setup.asm.txt`。

## 现代参照服：ServerCode，不当原版权威

`E:/Project/86JP/Server/DfoServer/Game/Dungeon/DungeonInstance.cs:323` 的 TryRecordNextMapOwnedPassiveObjectDeath：当前房间SpecialPassiveObjects中找到首个同code、尚未记死的index，记录后返回；同码全部用尽才拒绝。不能区分“另一台真实死亡”和“第一台重新构包上报”。

`Network/Handlers/Dungeon/DungeonCombatHandler.cs` 接受上述事实后依次处理任务击杀进度、机制和通关条件，再调用 CheckPassiveObjectDrop。它将物件任务奖励接入是现代实现选择。

该handler只有actorDefined为true才按耗尽状态拒绝；未声明code的路径并非此处全面拒绝，本服不应直接照搬。其DieMonsterRequest旧偏移也与本轮A21不符。

## A21实帧：ClientVerified 的窄范围

同房间两台8961机器，03:38:58.134和03:39:54.820 UTC 两帧除随机u16与4B校验外完全相同。六个物件样本的4B尾全部精确命中已记录MD5折叠校验公式。结合构包链，当前常规物件路径无可用实例标识；不能用随机数代替它。详见 findings.md、generator-frames.json 和 passive-builder.asm.txt。

## 对本服设计的影响（建议，未实施）

- 先修正A21 decoder的u32对象值、变长明细和物件标志，将物件路径与怪物经验/掉落分开。普通怪物actor字段仍需独立按其构包链回归，不能只扩大一个字段就视为完成。
- 维持进房预生成；同房间同code的物件形成待消费奖励队列/次数预算。一次认可的破坏消耗一份，最多当前Cell声明数。队列下标只是本服结算凭证，不声称对应画面某一台。
- 与当前任务、库存上限及事务成功/失败边界结合；失败释放claim，重访不重新生成或恢复额度。
- 房间/运行身份和次数封顶只能限制最大产出。报文重放可用会话序号等已证机制防护，但同一台重构新包、多人对同台各报一次，无法从当前code-only报文证明同一实体。不要将正文hash或随机数包装成精确去重。
- 实施前仍需决定正常组队的单一报告来源/多人分配规则，并核对主机与队友是否同时发送0027；此项需要组队实测或客户端报告资格门取证。单人收集零件闭环可单独验收，不能替代组队保证。

## 当前无需用户继续操作

单人同房间双样本已足够支持上述边界；无需再破坏更多发电机。组队调查如继续，应另安排两客户端观察同一台的上报者。
