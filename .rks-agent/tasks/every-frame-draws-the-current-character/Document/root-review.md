---
kind: rks-review
verdict: approved
scope: change
baseline: addbeb52
accepted_result: 0476c922
contract_revision: 1
reviewed_at: 2026-09-08
---

# 根任务独立审查

审查范围为根 TaskContract revision 1，以及 addbeb52 → 77ff3236 → 19fa03d6 → 1d887c59 → 0476c922 的完整结果。直接检查了生产与测试差异、相关读取和发送链、四项 Spec 的记录及独立评审、spec-004 整改日志和根最终全量原始输出。没有使用执行者的口头结论代替代码或日志证据；没有运行 build/test、实施变异、修改旧成果或推进控制器。

结论为 approved，无剩余阻断项。批准适用于合同实际列明的四项结果及其记录的限制，不承诺任意并发交错下的快照新鲜度，也不把代码推导称为客户端实机验证。

## 根验收对应

| 条件 | 独立核对结果与证据 |
|---|---|
| SC-1 | 六个直接 decoder 判据仍在，八成员分别断言、双项顺序以及截断/尾随错误文本均被检查。最终 decoder 行为未被后续 Spec 改写。spec-001 coverage-record 与独立 approved review 保存 M1/M2/M3/R3 的具体失败方式，并如实记载 R2 为等价负结果。 |
| SC-2 | coverage-record 分开记载从未覆盖的两项拒绝与 35aaedf3 替换 TheRosterCarriesStoredAppearancePartsAndCopesWithNone 时失去的间接覆盖；原先只断言三个成员，并未冒称全部八成员已被保护。 |
| SC-3 | SelectCharacterPreparationService:183 获取 GetAccountRosterWithAppearanceAsync，:253 从 summary.Appearances 构造入场名单；CharacterDomainService → CharacterRosterService.ListByAccountWithAppearanceAsync → DrawnAppearance.Of 是选人名单使用的同一生产路径。ProductionStateTests 特意让存储 blob 槽 0 与穿戴投影槽 12 不同；回退来源的 M-A 和共享投影的 M-B 已由旧独立 review 验证，后续 Spec 不触及该链。 |
| SC-4 | 字节锚文件完全未改；根日志中全部相关判据通过。直接检查 AccountRosterStateMessage 为 SupplementalSynchronizationMessage，且在 SelectCharacterHandler:354 于 SelectedCharacterStateMessageFactory 之外构造，确认“该名单帧在锚内”的原前提错误。spec-002 W-2 记录 62 个哈希均未动及原因，无需为没有改变的锚捏造新值。 |
| SC-5 | DecodeAppearances 在生产目录仅剩声明；blob 仍由存储加载并由 CharacterEntryStateServices 搬入兼容字段，入场名单已不解码它。spec-002 D-1/D-2 明确决定保留未退休的列、加载链和 decoder/检查，供独立存档迁移处理；保留依据与零生产消费者均已说清。 |
| SC-6 | AlreadyCarriedOut 只在命名值为正、其对应端空、另一端存在且 itemId 相等时拒绝。四条分派路径各检查两端，Reversed 清零两值，外层 handler 传递已解码的 DestinationInstanceValue。测试将同槽同物但不同命名端的“原字节复述”和新穿上区分，且正面断言合法交换为 Moved。原字节复述是未实机的 premise；重新构包若与新拖动完全同形则无法区分，此发现已经明确记录。 |
| SC-7 | 三份行为清单包含新的 appearance listing 读取/空投影拒绝、移除 blob 拒绝、七个新增 replay 条件、提前目的槽读取，以及所有 Moved 的库存刷新。spec-004 又明确列出代次失效导致外观抑制、加载失败、Unchanged/Refused 不刷新和整体并发覆盖，未再声称只有预期成功行为改变。 |
| SC-8 | 重新搜索 runtimeSessions/runtimes 的 GetRequired/TryGet，核对 16 读点、12 文件。沿返回值继续检查：DungeonSelection:417 与 SelectCharacterLifecycleServices.CreatePvpParticipant:221 读取驻留库存装备；其余 CharacterInventory 访问来自入场准备结果。清单包括 PvP 对其他已就绪玩家的目录读取、命令注册及后续编码消费，表格已注明 1d887c59 的历史基准。 |
| SC-9 | RefreshSessionAsync 每个 Moved 读取一次库存，ReplaceExact 后即时外观复用同一 reloaded；真实 EnterSelectDungeonHandler 在新检查中依次读取旧装备、穿上后的新装备及脱下后的空装备。原始 P2 日志证明删除写回使界面值断言失败。仅修副本屏会漏 PvP、拆整个结果过大的选择依据与代码读者一致。 |
| SC-10 | AGENTS 的调用点实际为七个，清单含 Move；DungeonSelection 的“与入场逐字节相同”已撤销；历史调查、0x0013、0x01E4 和教学 trace 补注区分旧状态、代码推导和实机材料。旧封存成果保持历史原样，没有为当前代码重写先前 verdict。 |
| SC-11 | 直接读取 Document/root-full-suite.txt 的七个成功运行段：Pvf 60、Networking 52、Protocol 631、Persistence 333、Infrastructure 259 加 2 skip、Application 1076、Host 6，共 2417 passed / 2 skipped / 0 failed。原始输出保留；架构和字节锚判据明确通过。 |

## 跨成果交互与边界裁定

spec-002 只切换入场名单来源，保留 spec-001 的 decoder 判据，因此后续读取路径不再依赖 blob 的拒绝。spec-003 的 MoveAlreadyMade 不满足 spec-004 的 Moved 门，不会刷新或推送外观；真正成功的两方向移动都刷新驻留库存。会话副本里的 Roster/RosterEntry 仍是入场值，但其生产消费发生在入场阶段，16 个驻留读者不会随后拿它们画装备；未找到它们绕开此次刷新成为新旧混用的反例。

RefreshSessionAsync 在 await LoadAsync 前取得整个 state，之后调用只检查代次的 ReplaceExact。无交错时只换 CharacterInventory，八个成员及位置持久化标志保留；发生同代次并发写入时，整个 Result 可能相互覆盖。此风险在源码、清单、spec/plan 与 AGENTS 中一致披露。旧 spec-004 BF-7 原要求允许“约束窗口或列明风险”，而合同不修改旧六处写点；本根审查接受这一限定处置，不据此扩大为全局并发更新工程。

SC-6 的两类界限必须继续保留：未证明客户端重复发送原始字节；若真实新拖动把正在移动物品的标识填在空目标端，守卫可能误拒。当前材料写明两种实例字段解释都符合已有观测、需要实帧区分。批准的是已写明的条件行为和无法区分时的发现，不是所有实际复述已经被治愈。

最终生产差异没有新增协议类型、字段布局或注册，没有改动架构守卫及例外名单，也没有引入 Application 对 Infrastructure 的引用。Move 命令的持久化执行边界仍为原来的 transactional 边界。此次审查未触及 #17/#18、CRAP、客户端、只读参照或其他 worktree。

## 证据强度与原始日志

spec-004 的 P1/P2/P3/P4 原始日志分别为 1/6/2/1 条预期失败：对象身份、界面/刷新数量、注入的 InvalidOperationException 与 InvalidDataException 逃逸、过窄刷新门。当前 MoveItemSpaceSlice SHA-256 为 93abc8df226df8bd06bf986891769bcccc37cefbfae033e6ef1653ea4c4da4a9，与还原记录一致。最终产品及现状文档相对 0476c922 无差异，git diff --check 无输出。

root-full-suite 的 Hosting failed to start 文本不是判据失败：异常为 InvalidOperationException，栈根 NotificationHandlerRegistry 构造器，对应 NotificationRegistrationTests 中显式断言 Host 启动拒绝的负例；不能只看到 fail 日志就当作测试失败，也不能只因最后绿色而跳过身份检查。两项 skip 为日志明确列出的 missing-archive 检查。

旧 spec-001 至 spec-003 的 scratchpad/efdcc 原始变异日志在当前 worktree 相对路径不可见。本轮对其历史变异采用已封存独立评审、具体失败记录及仍存在的检查代码，不声称重新执行或直接读到那些旧日志。根最终全量和 spec-004 原始日志均已直接检查，SC-11 当前接受结果的原始输出要求已满足。
