---
kind: rks-review
verdict: approved
---

# 主动深渊入场独立结果审查

审查目标：`task/abyss-research` 工作区当前未提交变更；接受基准 `b596b06bcef121e6444127850980032115a1b0fb`。审查范围为 TaskContract revision 1 / spec 的主动准入、全队成本、共享冻结方案和入场/切房投影，不包含部署、柱战斗、奖励、随机出现及华丽深渊。

依据：本工作区 `AGENTS.md`、TaskContract.md、spec.md、implementation.md，以及 reviewing-work 的 `references/code.md`、`references/deliverable.md`。独立阅读完整生产 diff（忽略仅行尾差异）、全部新增生产与测试文件，并追踪公共入场事务、共享 run、塔入口及实际 cell 创建行为；没有改产品代码，没有运行构建或测试。

## 初轮发现（历史）

初轮结论为 `changes_required`，R1 已经下面的定向复审关闭。

### R1 — 主动模式可以绕过 CMD 判定进入不支持深渊的塔（P2）

证据：`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:527-544` 在到达新 `DungeonEntryProjectionDelivery` / `EnterDungeonCommand` 之前，先根据 `illusionTower.SupportsDungeonAsync(message.DungeonId)` 调塔投影并直接返回；该条件没有请求模式限制。`Server/src/Dfo.Server.Application/World/IllusionTower/IllusionTowerDomainService.cs:216-217` 支持 11000 / 11001。塔 `EnterAsync` 只收到成员、leader、seed、dungeonId，没有收到 RequestMode / PracticeMode，也没有主动深渊判定。

后果：持有效选图 intent 的塔请求把 offset5 改为 1 后，仍可能通过既有塔等级/票据准入并创建塔 run；它不会返回 ManualHell，不检查深渊配置，也不会以 HellUnavailable 拒绝。主动+练习组合也可走同一路径。这违反 TaskContract 的“不支持时拒绝、run不变”和“主动资格失败不得降级”，以及 RQ-001 / RQ-002 的 CMD 统一路由边界。新增 `Manual_hell_request_is_never_silently_entered_as_an_ordinary_dungeon` 使用普通 dungeon150 与无塔 fixture，未覆盖此实际生产分支。

最小修复：限制塔专属分支只处理非主动请求，让模式1经过统一 CMD 判定和主动准入后以不支持拒绝；或者在统一 CMD 决定 Normal 后再进入塔专属路径。补真实 handler 分支回归：11000 / 11001 + RequestMode=1 必须拒绝且普通/塔 run、资源不变，正常塔请求仍走原入口。无需扩展塔深渊功能。

## 通过静态审查的部分

- `EnterDungeonCommand` 实现 `IMultiAccountCommand`，生产注册使用 Transactional / Exclusive；账号租约按ID排序获取，事务位于租约内。公共 visit 的事务适配器在存在当前事务时直接加入，不提前另行提交。
- 全队等级、任务与每成员票据计划在任何扣票之前完成。run准备、首房疲劳和分堆扣票加入同一领域事务；后续扣票异常沿命令管线回滚。公共 run 准备只在事务提交时发布，领域测试对提交/放弃及 SQLite 扣票异常检查实际余额和 run。
- 内容源只解析 WDM / DGN / MAP / ETC / AIC；规则服务负责难度/每order组选择、成本公式和线级演员数量限制；领域服务查询角色/任务/库存。新增 CMD 构造器只有领域服务，领域上下文没有连接或网络消息。
- 冻结方案保存在共享 Layout；封印 cell 首次选图消费固定 MapId，重访消费原 cell。START_MAP 的普通向量保留，hidden template 的 order / 1 / FF 与 frozen actor来源一致；普通掉落生成被显式隔离。
- 02A7 使用独立NOTI编码器，并在 START_MAP 后仅于首次深渊房发送 APC code / level 表。新消费者原始指令支持“AI角色资源索引”判断，未把包当即时波次刷怪；协议人工区及 catalog 已同步当前接线与证据边界。

## 检查证据和限制

`verification/build.log` 记录 solution build 成功，0警告 / 0错误。调用者报告专项 Application32/32、Protocol80/80、Infrastructure PVF3/3；这些结果不是本审查者重新执行的结果。

审查时 full solution tests 尚在执行。读取初次 TRX 得到 Pvf80/80，Application3114通过 / 1失败；失败 NotificationRegistrationTests 的错误是 logger 写入失败，未到达断言期望的 UnhandledProbe 文本。调用者已将 Host 启动大量失败追溯到沙箱 Windows EventLog 权限拒绝，并准备以测试进程环境禁用 EventLog provider 重跑。尚未读取到该复跑结果，不能据此宣称全量回归通过，也没有把环境故障归为本功能代码缺陷。

ClientStatic 只证明字段读取/相关状态和02A7资源消费链；门票公式、坐标及 hidden actor约定仍有 UpstreamImplementation 成分。服务端测试是 CurrentImplementation。没有主动深渊 WireCaptured / ClientVerified；入场成功不代表柱、波次、奖励闭环完成。网络投影失败的既有清 run路径发生在业务事务提交后，当前交付明确未新增网络失败退款，这与扣票事务异常回滚须区分。

复审范围：修复 R1 后检查入口分流和塔普通回归；同步读取最终测试结果。审查批准本身不替代调用者的新鲜完成验证。

## 修复后定向复审

结论：`approved`。R1 已解决，当前审查范围没有未解决阻断；原完整静态审查中未变化的证据继续适用。目标仍为同一工作区相对同一基准的未提交成果，包含塔入口保护、追加领域测试及协议人工区 / catalog。

`DungeonEntrySlice.cs:527-546` 现在以 `message.RequestMode != 1` 限制塔快捷分支。模式1通过短路跳过 `SupportsDungeonAsync` 和塔投影，继续到统一 `EnterDungeonCommand`；主动领域服务在无深渊定义时拒绝，在 practice非0时也拒绝，因此不会为11000/11001创建塔run。非主动入口仍沿原塔分支。修复只改变主动模式的错误入口，没有新增塔深渊功能。

直接读取 `tower-red.log` 确认旧实现失败来自实际 handler 调用塔 `SupportsDungeonAsync`；不是编译失败。读取 `tower-green.log` 和 TowerGreen TRX 确认 DungeonEntry / IllusionTower相关103例通过，含11000/11001 × practice0/1四个主动请求与普通请求仍触达塔查询的一例。probe覆盖的是生产入口分支；它不宣称独立完成塔全生命周期或深渊实机验证。

追加 `Registered_CMD_commits_manual_entry_and_ticket_debit_without_an_outer_transaction` 使用真实 ProtocolModule 命令注册和 `ICommandBus`，检查命令退出后票、疲劳与published run，并断言没有残留外层事务，弥补只手工开启事务的验证边界。新增全队任务两例读取实际 cleared quest存储，证明队长 Facts不能代替队员完成事实：队员不足时全队门票不变、无run；全员满足时准入并扣票。静态检查测试 oracle与生产条件一致；调用者报告专项已通过，本审查者没有另行执行测试。

再次检查0010 / 001C / 001D / 02A7人工区和catalog：CurrentImplementation接线、ClientStatic原生读取/AI资源消费链、UpstreamImplementation成本与投影约定以及未部署/未实机均明确区分。

`final-build.log` 记录当前全solution构建0警告 / 0错误。`host-environment-check.log` 记录沙箱外Host定向4/4通过；调用者说明首次失败为Windows EventLog权限、环境变量禁用无效，未修改产品日志。定向复审读取 `final-tests.log` 时只记录最终Pvf80/80完成及Application开始，全量还未结束。因此本次批准是独立代码/成果审查通过，不是最终全量测试通过声明；调用者仍负责收齐最终suite结果后作完成判断。
