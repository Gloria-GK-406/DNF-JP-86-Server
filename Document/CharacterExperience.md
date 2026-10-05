# 角色经验与等级

2026-10-06。当前服务端实现及自动化验证；满级聊天提示仍待用户实机验收。

## 入口与权威状态

命令通过 `ICharacterDomainService.Get(id)` 获取无缓存的角色实例。
`PlayingCharacter.AddExpAsync` 负责奖励计算与结算；任务的已定价经验效果也进入同一实例。
`ICharacterExperienceDomainService` 和 `ICharacterProgressionDomainService` 已移除。
经验估值、加成和 PVF 读取仍由原有规则与计算服务负责。

`Exp` 是成长真源。`ProgressAsync` 每次读取经验，按累计经验表推导 `Level`，可随 setter 向下变化。
`CharacterExperienceScale.ServerTopLevel` 集中声明服务器等级上限 86；完整 PVF 表可以包含更多门槛，
可玩经验上限始终取 `character/ExpTable.tbl` 的第 85 个值（索引 84），当前 PVF 为 **1046561640**。

`SetLevelAsync(level)` 设置该等级的起点经验，内部调用 `SetExpAsync`。
`SetExpAsync` 将任意 long 输入夹在零和上限之间，推导等级并协调技能；两种 setter 都不产生胶囊奖励。
初始化、教程、普通副本、任务、塔及旧副本 outbox 恢复已迁移到实例入口。

## 存储约束

游戏组件在开放网络前，通过规则服务获取不可变经验刻度并安装数据库约束。
旧越界经验被夹取；旧 `level` 被重算。历史越界部分不作为新奖励补进胶囊。
`level` 列保留为兼容 SQL 查询的派生投影，由触发器跟随经验变化；单独写这个列不能改变实际等级。
经验插入、更新越界会被 SQLite 拒绝，角色 setter 会先夹取值。

角色列表、入场、基础属性、技能点和原有 SQL 查询因此使用同一等级。角色实例自身始终从经验推导，
不把该投影列当作第二份成长真源。数据库约束安装属于启动建表/归一阶段；本次没有操作运行库。

## 奖励、通知与胶囊

| 返回值 | 含义 |
|---|---|
| `Breakdown` / `Gained` | 本次完整奖励和加成明细 |
| `Total` | 封顶后的持久累计经验 |
| `NotificationTotal` | 归一后的奖励前经验 + 本次奖励，只供 NOTI 0x0025 使用 |
| `Overflow` | 本次超过人物上限的部分，交给账号胶囊领域服务 |

满级前若奖励跨越上限，人物保留到上限，胶囊只接收溢出部分。
已经满级时，人物经验保持上限，胶囊接收本次完整奖励，通知累计值为“上限 + 本次奖励”。
例如连续获得 14 和 57，通知分别为 `1046561654` 和 `1046561697`，存储始终为 `1046561640`。
没有新奖励的死亡报告仅投影人物上限，不重复携带上次奖励。

普通击杀、队伍击杀、通关、任务与塔通知使用 `NotificationTotal`；会话和持久状态使用 `Total`。
协议字段布局保持现状。累计通知无法用 u32 表达时，在任何写入前拒绝整次奖励；不截断奖励。
胶囊仍按现有 PVF 比例与容量限制累加，荣誉经验同时沿用既有结算规则。

所有奖励加入调用方事务，保留 Effect 的归属校验、精确会话检查和提交失败隔离。
角色和账号 SQLite 写入共享一个事务参与者。操作 UUID、怪物 UID、任务完成记录和塔收据继续由
现有用例边界防重复；`AddExpAsync` 本身不把两次独立的相同奖励当成同一个操作。
旧 outbox 保留其持久化的副本内经验基准，归一后只通过实例结算一次 `RawGain`。

## 验证与部署边界

回归保护包括：86 级之后的 PVF 门槛不能突破上限、经验决定等级、满级连续奖励、跨满级溢出、
胶囊容量饱和、旧越界归一、写入失败与外层回滚、setter 升降级、SQL 越界拒绝、等级投影和通知选择。
关联任务、塔、资产、会话及协议测试覆盖既有行为。测试不等同于客户端验收。
本次修改在现有 `task/warehouse-storage` 工作区；运行服务未替换、生产数据库未修改。

### 自动化结果（2026-10-06）

使用 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，串行运行全解决方案测试，启动顺序修正后完整复测 Host。
汇总 5850 个测试：5846 通过、2 跳过、2 个已有失败。Application 2986、Persistence 448、Host 147 全部通过。
`git diff --check` 通过。新增的等级上限与经验推导用例先在旧实现失败，再在新实现通过。

以下两项在未经修改的 `02cf9dca778069e24d9ebf0c1bb47a4e4246bd7a` 源码快照中复现相同错误，保留为原分支问题：

- `CardRewardTests.The_card_protocols_are_wired_into_the_production_graph`：测试注册缺少 `INetworkMessageSender`，无法构建 `NotificationBus`。
- `IllusionTowerStageFrameIntegrationTests.Raw_tower_start_and_death_pickup_frames_reach_registered_decoders_and_real_run(11000)`：塔拾取等待时出现空引用。

本地 TRX：各测试项目 `TestResults/character-progression-final.trx`、Host 的 `character-progression-host-final.trx`。
基线复现结果保留在忽略目录 `artifacts/character-progression-baseline/source/Server/tests/`；源码快照不建立 Git 分支或工作树。
测试夹具的等级和经验已同步为同一表格状态；三个既有事件架构断言也更新为当前事件分支注册事实。
