# 大会APC编号与黑龙每日次数修复

日期：2026-10-14；实现基准：`ff81596c`；分支：`task/next-round-20261013`。

## APC身份冲突

用户在黑龙第二轮必现APC不动、无法攻击，随后死亡；复活串控，黄金羊毛给APC回血。此前大会把四名对手编号固定为1/2/3/4，角色1123的WireUserId恰为2。

ClientStatic / High：S4A21大会APC分支VA01543C17..01543C2D以类别0x111及路径actorId调用对象注册VA0191E450；VA0191E760..0191E78B写入身份类别和编号。只读现场检查玩家与APC的object+0x98类别均为0x111，路径记录为(1,10412,70,5)、(2,20426,70,5)、(3,25403,70,5)、(4,15414,70,8)。现场显示的是后续抽签，不能把第二名20426认作最初报告的瑞克麦。最初APC名称不影响编号冲突结论。迟发死亡的具体伤害过程尚未单独追踪。

静态输入：`E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`；支持EXE SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。本轮未启用新的抓包或写入客户端内存。

CurrentImplementation：大会领域服务把本场玩家WireUserId作为事实交给规则服务；规则生成四个唯一且不冲突的非零对象编号，round仍为1..4。编号随计划冻结，并用于0183、Cell起始编号与实体、入场visit、任务掉落及死亡门控。例如玩家为2时对手为1/3/4/5。没有修改客户端补丁、AI或复活协议。本次只修复大会编号分配，不扩展为所有地下城对象分配重构。

## 每日次数

PVF证据：`etc/dungeonetc/dungeonincountinfo.etc`的`[dungeon incount info]`四元组含`0 122 9 0`；黄龙120与青龙121无对应次数行，因此不额外限制。

大会内容源读取每日上限。门禁签发DungeonQuotaAdmissionCost；消费阶段DirectAsync投递TournamentEntryRecorded，由统一UsageQuotaDomainService以CAS写入`character_daily_counters`，键为`tournament.122:yyyy-MM-dd`。次数消耗与创建地下城使用同一事务：成功入场扣1，放弃不退还；门禁拒绝或后续事务失败不扣。每日耗尽返回既有DailyEntryLimitReached。

提交后UsageQuotaChanged经EventBus通知投影NOTI0227，内容为地下城122与真实剩余次数。登录、加载完成和在线跨日刷新走同一快照；CharacterEntryState也用当前次数替换旧dimensions值，防止完整状态刷新覆盖NOTI结果。周期沿用统一DailyResetRuleService，当前部署配置为北京时间06:00；在线周期检测间隔一分钟。

复用既有表，无数据库结构变更，不修改既有角色次数或补票。修复前的黑龙入场没有权威计数，无法可靠追溯，首次新计数从0开始。

## 验证与实机边界

自动回归覆盖玩家编号1/2的黑龙完整四轮、未来/玩家/重复死亡拒绝、编号唯一性、真实PVF的9次配置、入场事务回滚、9次耗尽拒绝、重载持久化、完整角色状态投影、北京时间06:00重置及在线刷新失败重试、NOTI0227五字节编码。测试结果保存在`artifacts/tournament-apc-quota-20261014/verification/`，不作为实机验收证据。

本次回归：Application 3816通过；Protocol 1499通过；Infrastructure 893通过、2项既有跳过。完整状态投影保留旧条目的SortOrder及Value1，只替换权威Value2并去重；对应既有祭坛兼容性测试保持通过。

部署后重新登录，依次检查：

1. 黑龙第二轮对手正常行动、可攻击，四轮可完成。
2. 黄金羊毛回复玩家；如使用复活币，复活后仍控制自己。
3. 入场前9/9，成功入场后8/9；回城、完整状态刷新及重新登录均保持8/9。
4. 如继续测试次数边界，成功入场9次后剩余0，第10次拒绝，不额外扣材料；跨日恢复9。

黑龙及二觉功能仍待用户实机验收，本轮修复不能替代该边界。
