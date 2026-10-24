# TASK-11 本地实现映射（B2/P2，r4＋最终 phase 收口）

完整 GameUseStackableCommand 取代原 network handler Resolve/组包和 UseStackableCommand fine handler；薄 handler 仅 bus.SendAsync。原 GameSlot→BusinessSlot→canonical GameSlot 顺序保留，全部 selected 业务检查、effects.UseAsync、绝对剩余槽读取在一个 Write(MainSettlement) 内；正常 selected 拒绝也 Freeze Inventory 后提交。未 selected 是初始封闭 Rejected，只原 Silent ACK；后续选角无升级能力。新 CMD 无 legacy bridge/ambient、nested bus、locator/queue、Compose。

唯一原 CWT 持有物理 connection ledger。Admit 只验证/登记身份并返回 Rejected/Selected/CommittedReplay；旧 Dispatch 再 Enter legacy，新 bus 直接建 request。128窗口、latest ordinal、full hash/length/selector/type、原 entry.Ordinal 重试不变。Finalize Pending 在所有已启动 unit drain/Close/scope cleanup 后执行。无 Main 的通用正常拒绝最终 Rejected，Use 的 selected 拒绝仍必须其 Main。

Main runner 的 participants 精确唯一且包含 reporter，逐人当前性核验；允许其它账号的参与者。事务显式 Completion 决定 rollback/committed/uncertain；不以 IsSettled 推已提交。recorder 只在本 job/request/owner/live Main transaction、未冻结时有权；完整 applied-effect switch 由一个纯 accumulator 持有，各请求与 legacy frame 分开。领域实际写前 Demand 及原 TASK-10 链保持。

原 ACK→virtual resource→source absolute slot→optional XP 次序和所有 codec/bytes 保持；没有 Creature 附帧。具名 SettledRequiredExplicit permit 只在 Main Settled 后签发物理目标单次 attempt。Committed replay 在 ordinary Write 内 direct reader(metadata)，结束后 sink MirrorMerge→Create→batch→Town；sink 没有 ICommandBus/locator。CommittedReplayCurrentStateBatch attempt 由框架在真实 replay Write 已提交结束后签发。

actual outbox adapter 仅 EncodeMessage/EncodeBatch 的窄 catch 记录原 encoder cause；callback、conversion、lookup、Current、buffer/Register 均无 proof。已发 attempt 未完成不能成功/开新 unit；故障 sticky。body→drain→Close→scope dispose→最终一次 Fail/fence，scope/lease cleanup 作为独立 occurrence，即使同一个 Exception 对象也撤 raw。仅本请求许可+真实 encoder+Committed+最终单原 cause且无typed/独立错误才 raw；其它默认全 committed/uncertain participants fence。executor/source-close/transport 算法未改。

原 EffectOperationReplayTests.Second_primary 保留 case 身份，改调用同CMD两Main真实probe：第二 action0、SQL只扣一次、原projection同引用、entry Committed、sticky与默认fence/sourceclose/原cause。Later_readonly 保留case，按原真正Transactional读取改同CMD Main后ordinary Write直接reader，原清空slot69/0x000E/item=-1重放断言保持。Refusal_only 是仍有消费者的legacy兼容证据，SharedEffect fine仅替已删除fine形状；另有新显式Rejected真实升权拒绝测试。

LegacyReplayStateProjectionDelivery 仍 pending：上游仅旧 EffectOperationLedger.Dispatch 与 JoustBetHandler，适配器下游一处 ReadReplayStateCommand dispatch，再到无bus sink。ReadReplayStateCommand 仍 Transactional；新 effect bus/CMD/sink不经旧adapter。最迟 T-011 最后消费者迁完删除。其它消息148/旧命令193/来源72及ambient/protection/final性能仍后继边界。

证据：task-11-prefreeze-focused-01（79pass）、task-11-prefreeze-replay-01（36pass）；mutation-result.json 保存真实callback错误被错误raw豁免时SQL count2/entryCommitted且fence false的RED，finally原bytes/SHA恢复后的GREEN。最终完整冻结轮另见 formal-summary.json（产生后）。ConfigureServices-only前后capture各独立，无provider/进程启动。


r4 可达性与最终轮：原Window两case已按原block SHA逐字恢复（不计新增）；真实已签发token的同请求双call、第二CMD借前请求token、原request仍open的非法跨job、同job已Close的ScopeDispose均实证encoder无额外调用/SQL不再写，TCS finally释放并收齐。真实SQLite ordinary unit写gold后阻塞在rollback participant cleanup；签发守卫拒绝且encoder0，release后真实rollback失败保原双cause、gold已回滚、首Main仍Committed并default fence。无假lease/private反射或新生产executor。实际源码TrackOperation覆盖含lease.Dispose的整个RunAsync，因此合法encoder与未完unit不同时发生。

closed scope单故障的adapter Failed→scope catch传播曾被重复计因，独立原cause Ref/SQL/encoder断言实际RED后用本次finally phase快照修复；GREEN66含原默认工作单元/请求测试。actual encoder＋scope独立再次抛同对象仍精确两个InnerExceptions且均原cause、typed/fence。freeze02 曾进入完整正式02，但随后真实组合发现 scope propagation 缺陷，因此也仅保留诊断。正式01少Window2case；最终采用后续 source-freeze-03 及其完整新八项目轮，不拼接01/02。

## 最终 scope cleanup occurrence 诊断
真实 E+C 及 E+Aggregate(C,D) 先在 scope-combination-red-01/02 证实多计传播；含 swallowed closed guard 的 4 例 RED 为 1 通过/3 失败。最终仅本次 request 的显式 scope cleanup phase 收集来源，阶段内已记录 cause 的向外传播不重复计，独立 D 保留，body E 在 scope 再次独立抛出同对象仍计两次。scope-combination-green-01 实际 93/93，先核实际 SQL/encoder/Committed、物理 fence 和 cause 引用。此前 snapshot 修复为诊断，非最终实现。
