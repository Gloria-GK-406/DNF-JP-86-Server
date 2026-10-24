# TASK-28：Cera 未迁旧路径的事务与失败出口事实

本报告仅调查 P4 T-004 的下一个旧边界，没有协议迁移、恢复 API 或可见变化裁定。原 HEAD 为 `1aae55555ed207f3ced6593180fa8d211b60d09f`；全部原 tracked 保持只读。正式结论来自 `source-freeze-04.json`、`task-28-build-04` 和 `task-28-final-01`，前轮原日志与源快照保留。

## 实际接法与证明范围

E 内独立 net10.0 可执行项目 ProjectReference 原 Application.Tests，反射仅用于原 internal CeraShopFixture 的 public 构造／Host／成员和 SQLite 的只读泛型 ReadAsync。购物车 body 按真实注册 decoder 的字段构造；从 fixture 同 provider 解析 `GameMessageDecoderRegistration(selector=1,type=0x0040)` 所指解码器，经真正 `IGameMessageDecoder.Decode` 产出 CeraShopPurchaseRequest，再调用同 provider 的 `INetworkMessageDispatcher.DispatchAsync`。未调用 fixture.Handle、未手造 request、未直接调用 fine 充当协议，未在生产 entry 外套 TestGameplay。

原 ICeraShopDomainService 实例由其原 DI descriptor 构造，观察 decorator 逐参数委托；旧 fine handler wrapper 只记录原调用和在 C3 解析 scoped 故障探针。事务、规则、存储、管线、通知组装、注册编码器、同 provider 发件箱均为原实现。domain-enter 的只读快照可能先加入原连接串的 SQLite participant，明确存在这个观察影响；不会加入第二个 fallible participant，参与者实际顺序／类型和每个阶段均在 JSON。C 的人工 participant 是另一个明确标出的 nonfallible 受控源。

为观察实际发送／关闭，IOutboundSender 后端由 probe 的记录转发器接原 `Networking.Outbound.OutboundSender`，传输是 E 中可计数的 in-memory IOutboundTransport/IOutboundConnection。原注册适配器和 GameplayOutboxes 不变，只有一个 provider；不使用 ShopSender 原 Discard 摘要冒实际输出。记录的每个 OutboundEntry.Frames 与 transport 实际接收 bytes 相同，实际 sender 报 Delivered；成功对照包含原 ACK／wallet／inventory 顺序。传输 SendAsync 全在 executor 外，未预先关闭。没有 TCP、TouchSocket 入站拆帧或 A21 客户端实机主张。

项目只采用既有 friend 程序集名 `Dfo.Server.Networking.Tests` 访问原 internal sender／transport 接缝，没有改原 InternalsVisibleTo 或原工程。该探针 DLL 位于 `task-28-inspection/bin/Release/net10.0/`，不是原 Networking.Tests 正式全量程序集。`final-01/assemblies.json` 保存实际 Name／Location／MVID／SHA；verification 的 originalLoadedAssemblies 逐个核原当前 Release build output 与实际加载 copy SHA 相等。探针例数不计入原 xUnit 库存，也不冒七 control。

## 七个实际观察

每例使用原 fixture 自己独占的新临时 SQLite，原 Dispose 只清自己的写／只读池；未调用 ClearAllPools。每例保存全部生产 user table 的 schema、完整列名和逐列类型／值，NULL 与 BLOB hex 单独表示，按全部列排序；同 run 不规范化 SQL 时间或 GUID。通常110表，B另有两个 probe-only表共112表。前／domain中／结束及 A/B retry 快照保留。

| 例 | 注错与实际阶段 | 真实结束和出站 |
|---|---|---|
| success | 单水商品110000；真实 domain receipt，SQLite写提交 | Committed；cera100000→96400、items1；实际成功 ACK、wallet、inventory 一批按序发送；无围栏／close |
| refusal | cera设1，正常不足余额返回 | 无资产变化但原事务确为 Committed；实际失败ACK11；无围栏／close，不假称 RequireRollback |
| A action | 水＋宠物105016；水另收费100gold；宠物list_type7的slot INSERT trigger原生19/1811 | domain未返回receipt。异常前已暂存wallet、gold99900、2个item/1个slot及events/deliveries；实际 ConfirmedRollback，110表全部列 exact before=after；失败ACK1实际Delivered |
| B native Commit | 真实grant INSERT触发probe_child写缺失parent，外键DEFERRABLE INITIALLY DEFERRED | domain已返回receipt，真实连接FK1、child1、库存／events已暂存；`SqliteTransaction.Commit`原生19/787。真实 CommitFailure Source是SqliteDatabaseParticipant、Kind=KnownStorage、Phase=FallibleCommit，cause与日志SQL同对象；CompletionConfirmedRollback、112表全部列 exact rollback；失败ACK1实际Delivered |
| C1 nonfallible | 同真实transaction加入CommitCanFail=false participant，仅其Commit抛固定IOException | 真实SQLite资产已提交，CompletionUncertain；typed NonFallibleCommitContractViolation.Inner就是固定cause。旧handler仍发送失败ACK1，无围栏／close |
| C2 rollback | A的真实SQL1811，再由受控participant.Rollback抛第二固定cause；原SQLite仍实际回滚 | CompletionUncertain，typed RollbackFailure→Aggregate保原SQL与第二cause引用；资产全部列 actual rollback；旧handler仍失败ACK1，无围栏／close。此例真实回滚不代表不确定状态可一般性重试 |
| C3 fine scope | fine原DI scope实际解析probe IAsyncDisposable，在正常购买提交之后DisposeAsync抛固定cause | 资产Committed，fine-scope-dispose事件确在原handler return后；旧外层handler捕获原IOException并发送失败ACK1，无围栏／close。不是transaction rollback或人为在handler外throw |

所有七例 dispatcher 实际正常返回；domain-enter 的真实 SelectedSessionCommandScope.Current=null。每例最终 Standing.Fenced=false／ConnectionRevoked=false、SelectedSessionSafety.IsBlocked=false，真实提交close条目0、transport.CloseAsync0、失败通告0，close cause表对精确原context为空。每例实际发送一批，所有条目及实际bytes都有 Delivered 报告；root chain 为当前真实连接shop-test、depth0，原cause日志引用可核。无close的结论来自完整 outbox entry＋实际 transport 计数＋真实围栏状态，并非缺一个observer就推断。

A/B 的原异常同对象均经过 CommandLoggingBehavior 和 CeraShopPurchaseHandler 两条 Error；原执行器没有未处理失败，因为旧handler已经转换响应。C1/C2两条Error保原typed对象，C3只在Cera handler记固定清理cause。`Exceptions`使用引用相等字典分配ID，Inner/aggregate、nativeSQL参数和stack均保存，不用序列化文字充当同对象。

A/B移除本fixture trigger后再从decoder送同一合法cart，实际新购买分别收到两商品／单商品成功ACK；只收费该次，cera分别76600／96400、gold99900／100000。Cera是 NonEffect：这些是回滚后的新合法购买，不是同entry／Ordinal replay或幂等保证。C1/C3不自动重发已提交购买。

## 本次核验和未做

- `task-28-build-04` Release --no-incremental 自然exit0，0warning/0error；`task-28-final-01` 七例自然exit0。verify.py逐个核全部上述阶段、cause图、KnownStorage、完整资产、wire ACK布局、sender bytes、围栏／close和assembly身份。没有 native 仍在运行。
- build01的CS0542（probe logger类与Log方法同名）和build02的CS8619（快照匿名分支nullable差异）为探针自身编译纠正，源和原log保留，不是行为RED。build03／success01／matrix01已通过；随后仅补Tx身份／参与者／CommitFailure的只读字段，freeze04重建并重跑全部七例。原轮次没有被覆盖。
- 原 strict和原八selftest只读通过，canonical无修改、不新造坏副本。独立DFO目录实际无违规JSONL；这只表示本probe运行未记录违规，不能当正式74/7或七control通过。
- Task27原9014＝8994pass＋原exact2Dimensionfail＋原18skip仅作已接受库存引用。本单元没有八freshfull，没有新xUnit case、协议CMD或ledger行，不宣称Spec002完成、性能≤2秒或实机。
- stale／替换、取消、名字牌／扩容／权益和其它购物车组合本次未动态扩展；原覆盖仍在Task27库存，不能从本次7例推导全部情况。C1/C2/C3为受控故障，不声称生产已发生同类事件。

## 交主控的裁定输入

A/B已补足旧native确认回滚失败ACK的实际依据。若后继要保该响应，P4把commit-result消费限定四opening，需受影响Plan HOW审查，不能直接在Task借用。

C1/C2的typed不确定以及C3的已提交scope失败在这个旧IAccount-only路径均实际变成失败ACK/no-fence/no-close。后继若沿新默认sticky／typed收口，结果可能改变；这是需主控与受影响Spec/Plan门裁定的具体WHAT问题，不能默称纯搬迁。此报告不预定保留或修正，不授权新VC、广泛恢复、raw许可、清sticky或修改既有安全不变量。
