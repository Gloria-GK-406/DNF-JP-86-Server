# 角色、会话与当前事实

修改角色属性、进度、会话身份、疲劳或客户端设置前阅读。规则分层见
[Architecture.md](Architecture.md)，运行期投影重建见 [SelectedSessionCurrentView.md](SelectedSessionCurrentView.md)。

## 角色事实与静态规则

身份、等级、经验、当前基础属性、已学技能和 SP/TP 账本属于角色，从 `ICharacter` 按 ID 现查。
经验与等级的唯一修改入口是 `ICharacter.AddExpAsync` / `SetExpAsync` / `SetLevelAsync`。
`ProgressAsync` 从累计经验和 `IExperienceRuleService.CharacterScale()` 推导等级；86 级经验上限取
PVF 表索引 84。数据库 `level` 仅为 SQL 兼容投影，启动时归一旧数据并装载上限与投影触发器。
`ExperienceAward.Total` 是封顶存储结果，`NotificationTotal` 是该次回包累计值，不可持久化。
满级溢出由角色实例调用账号胶囊领域服务，与角色更新同事务。详见 [经验规则](CharacterExperience.md)。
这些查询不要求角色在线。角色实例只保留 ID 和取数服务，不缓存等级、技能组合或查询结果；
数据库读取复用当前命令的 SQLite 事务，提交、回滚之后再问仍从当前事实作答。

静态规则由共享规则服务持有。`ICharacterStatRuleService` 准备职业与 1—86 级的不可变基础属性，
`ICharacterSkillRuleService` 返回按职业/技能共享的不可变规则，显式接收职业、等级和已学事实。
规则可以持有不可变静态声明与预计算表，但不能查询角色存储或缓存角色状态。职业拆装集中在
`CharacterCareer`，支持集合由 PVF 声明决定。属性与技能算法依据分别见
[基础属性规则](CharacterBaseStatRules.md) 和 [技能规则](CharacterSkillRules.md)。

`SkillPointLedgerAsync` 保留四个 long 余额及未知技能诊断，`RemainingSkillPointsAsync` 仅为旧协议
夹取到 ushort 的兼容读法。未知已学技能仍保留，并以零投入兼容处理、标记不完整和记录警告。
已学事实只含页号、技能 ID 和等级；slot、extra_values、`SkillTreeIndex` 属于动态配置/表现边界，
沿用现有存储与技能页投影。普通页账本不代表自由决斗频道的无限点数策略。

选角基础属性以及副本选择界面的基础属性/身份/进度/技能/余额投影由领域服务刷新。
`CharacterDerivedStatsStorageRecord` 的历史基础列不是当前基础属性真源，Progress1/2、百分比、
宠物等动态或未建模字段仍保留；装备和 Buff 对战斗属性的合成未实现。副本进出判定、接任务、
卡牌及无经验授予结果的发帧回落读取当前角色；非空授予结果继续代表该次写入的回值。
教程命令在现有事务中检查当前等级，不能凭旧会话的 1 级把已升级角色改回 2 级。

`SelectCharacterResult` 只在选角响应组包期间存在，不能存入会话运行时目录。后续投影通过
`ISelectedCharacterCurrentViewDomainService` 在精确会话校验前后重建，物品、角色、任务、账号等
可变内容分别从所属领域读取。`ProtocolMirror` 是协议投影，不是当前点数真源。

角色服务在命令事务内读取同一 SQLite 参与者的未提交事实，提交／回滚后重新查询当前值。
事务外的一次调用只回答查询时的事实，不承诺多个调用之间的全局原子快照。

## 状态归属

| 状态 | 所有者与使用边界 |
|---|---|
| 身份、等级、经验、基础属性、已学技能、SP/TP | `ICharacter` 按 ID 查询；不要求在线，不缓存角色查询结果 |
| 坐标、血蓝百分比、冷却、灵药加成 | `IOnlineCharacterRuntimeDomainService`，记录属于精确会话代数，无互斥锁 |
| 任务 | `ICharacterQuestLogDomainService`，进本由 CMD 收集迷宫筛选所需 facts |
| 物品、耐久、修理结果 | 物品领域；外观、进图与回城读取当前库存，不维护会话物品副本 |
| 客户端设置 blob | 账号 `IClientSettingsDomainService` 与角色 `ICharacterOptionsDomainService`，见下节 |
| 角色兼容设置 | `ICharacterSettingsDomainService`；外观领域及账号主设置保存读取可见性回退位 |
| 暗骑连招 | `IDarkKnightComboDomainStore` / `IDarkKnightComboBook`；领域书本门面无生产消费方，不能由接口存在推断玩法已接入 |
| 疲劳 | `ICharacterFatigueDomainService`；入场及新房间记账由 `DungeonVisitDomainService` 调用 |
| 在线身份与会话阶段 | `OnlineStateRegistry`；通过领域门面读写 |

选角入场结果是一次性组包值。登录落点、当前坐标、后续只读视图的具体边界统一见
[选角结果与当前视图](SelectedSessionCurrentView.md)。不要把登录准备当成在线查询。

## 在线身份与会话有效性

**在线身份只有一条记录、一把锁。** “这个账号/角色/连接现在是哪个会话、走到哪一阶段”只在
`OnlineStateRegistry` 回答：每个账号至多一条选中会话记录（`Selected` → `InWorld`，可带隔离标记），
在线用户上的当前角色由这条记录投影出来，不另存。连接被撤销（协议拒绝、被顶替）时在线用户立即删除、
选中会话记录保留，`StandingOf` 报告 `ConnectionRevoked`，直到拆除流程 `ReleaseExact`。不要再在旁边
加平行的会话目录、活跃角色表或隔离集合。

**「会话还有没有效」「那个角色在不在线」只问具名判定。**

- `IsSelected(session)`：当前选中且连接仍归它，任何阶段都算。只用于选角工作流。
- `IsInWorld(session)`：有效且已从它自己的游戏连接进入世界。其余玩法、投影、投递都用它。
- `TryGetInWorld(角色 / 线上号 / 连接)`：手里只有键、要找在世界的会话时用。

拆除、补偿、断线清理处理的是连接已撤销的残留记录，问 `StandingOf` / `ReleaseExact`。三个判定都不看隔离标记，隔离由 CMD 入口与显式工作单元的精确保护检查负责。

不要手写「`Find…` 出来再比较」「查不到就当不在线」，也不要拼在线用户的 `CharacterActive` 标志。
`Find…` 只允许下面几种用法：

- 请求方按自己这条连接解析会话；
- 清理路径取出要释放的会话；
- 查到之后必须经过一处具名判定；
- 只用于加锁，或只做身份一致性快照，不用它决定对方在不在线；
- 不以在线为前提的业务动作（如比武淘汰），发送前仍以 `IsInWorld` 把关；
- 客户端设置确认「这条连接上还没有选中会话」；
- 删角色确认「这个账号没有任何选中会话」。

**会话期间，角色归属以会话为准。** 角色属于哪个账号在建角时写定，没有任何路径改写；唯一会变的是删除。
删角色入口在账号仍有选中会话时拒绝（`CharacterDeletionStatus.CharacterSelected`），按账号而非按连接判断：
被顶替连接的会话要等它自己的断线清理才释放，这期间的删除也一并拒绝。因此已经通过 `IsInWorld` 或执行器
精确会话守卫的在线路径，直接信任会话里的账号与角色，不再按数据库重查归属。按角色查账号
（`ICharacterTable.FindOwnerAsync` / `BelongsToAsync`）只留给没有会话可依的路径：离线投递、跨角色目标、
GM、选角与入场，以及需要账号值本身（而非核对归属）的地方。

## 疲劳

`CharacterFatigueDomainService` 将配置总量与账号黑钻加成合并，
按 `IDailyResetRuleService` 的当前周期读取已用量；`SpendAsync`/`RefundAsync` 在命令事务内
通过条件写入更新，退款不降低当日累计消耗。`DungeonVisitDomainService` 先检查
`ConsumesFatigue`，由 `DungeonAdmissionDomainService` 检查需要疲劳的副本拒绝零剩余角色，
Visit 在创建出生房时默认扣除 1 点；声明 `[use fatigue only start dungeon]` 的副本按本局冻结值仅入场扣除，后续房间不扣。入场凭证不含疲劳费用，避免重复扣除。其他副本首次进入新房间扣 1 点，重访不扣；
不消耗疲劳的副本跳过这两步。当前实现和自动测试不等同于 A21 实机验收。

## 客户端设置

C5 主设置、170两个快捷聊天bank由`IClientSettingsDomainService`按账号独立更新；
C6完整文本按账号＋profile存入`account_hotkey_options`。登录使用普通profile，选角职业10使用缔造者profile1，
其余使用0；精确连接上下文归属上传，未下发／无法归属的profile不猜写。旧`character_hotkey_slots`和账号
二进制槽列只保留兼容，不用于A21的1C7文本回放。C0由`ICharacterOptionsDomainService`保存角色516B完整包，
AD／1C7／187 在登录与选角序列回放；blob 保存不表示所有内部字段语义均已建模。
主设置的三个外观选项由 `ClientAppearanceOptions` 只读解释，角色兼容可见性位作为回退；
`SaveMainAsync` 在有效选中角色的显隐语义变化时同事务记录 `ClientAppearanceSettingsChanged`，
由事件消费读取当前事实并刷新投影，不另存一份显隐真源。外观职责见
[角色外貌统一计算](CharacterAppearance-20261014.md#职责)。
协议依据和实机边界见 [客户端设置协议](../protocol/patterns/game-settings.md)。
