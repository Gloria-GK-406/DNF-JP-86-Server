# TASK-21 执行覆盖与证据边界

三 logical（65556/66250/66251）到两个 typed whole，旧 SortItem/SortLock fine 删除；接线为 Direct/Protocol，两个 whole 均无 registration exit。一个 Sort 请求（或一次锁动作）至多一个 ordinary Write([],Ordinary,None)。空列表保持旧 fine 仅 IAccountCommand 的原 selected-participant 范围；不是任意协议可空参与者的规则。原 action 内 world/online/cargo 判定正常拒绝；不以 action 前 guard 或 RequireRollback 代替。非效果执行没有 Main、footprint、effect ledger、attempt、重复帧重放入口。

Sort 的原领域 Sort → 原 character.Load（含 LegacyWorn normalizer 写）→原 account.Load→最终 clock，全在同一真实未结束的 DomainTransaction。新 flow 的 forwarding observer 调原实现且以引用证明同事务。原 receipt 声明精确搬迁；原 InventorySorted/SortLockChanged record/composer 字节后缀未改；projections 复用现有总线和单 recipient prepared batch。

真正产品前 source、driver、capture、raw/meta 共42项由 before-manifest.json 封存。old-oracles04 的56/56为原40（InventorySort12 + Warehouse28）和16个新原路 oracle。迁后 focused56/56，最终 App full 亦包含这56；原12身份及断言意图保留。stale/tower 的旧 fine 驱动改真实 decoder/dispatcher，stale wire 不含旧 session，只有本地只读 query proxy 返回真实旧捕获，IsInWorld 仍问真实 registry，分别保失败 ACK/静默和完整资产不动。tower 原两 receipt 判断改实际拒绝 ACK/资产不动与成功 ACK→full→locks/资产/core 断言。

最终 before-after 对照10组：Level 全资产/UID/core/锁槽与原帧；Toggle/Toggle/Unlock/Unlock 四 ACK（02CA 后三02CB）；原 SQL action 19/1811 与真实 deferred-FK native Commit 19/787（实际 FK1、child>0、完整 sorted stage、Load/clock完毕）；Sort/Lock成功投影 encoder Failed 隔离；composer Failed 和无事务 direct ACK 原 cause；stale Sort/Lock；empty committed refusal ACK。same-run 回滚/empty 资产所有列完全相等，不豁免时间/GUID/BLOB/NULL。跨 run 只按 Task§6校准 items 两时间的 runner UTC 窗口及事件三 GUID 的跨phase双射，保 sequence/type/schema/payload/time/其它列、Delivery 外键和 batch/origin关系，200处具名时间记录及完整GUID映射在 oracle-before-after-checks.json，raw全保。

empty committed refusal 的旧 raw IOException 与新 typed PostCommitPublicationFailure（inner同原cause）是 Task§10经独立边界复核允许的明确分类差异；不能声称所有旧断言原样或异常完全相同。新 final r2 动态 Assert.Same 精确物理sourceclose，JSON由 ReferenceEquals产生；旧empty动态仅Single/IsClose/nofence，其hardcoded sourceCloseExact不是独立Same证据。旧物理身份的其它支持为未改共同source-close实现及旧SQLcase引用断言，详见 source-close-evidence-r2.md。新case同时断言空Tx真实Committed、零participants、零batch/attempt/ledger、无fence、恰一次close、EndedAJob及完整链。

Level→Default mutation 真正可编译，真实 wire [0,1,2] 排序 UID2→UID1 错误，1行为RED，旁边2正控仍pass；finally全byte恢复，同3目标全GREEN。最终r2只加强emptycase的close身份，mutation目标/产品未变。

freeze01 首轮 Net full 有两 Connection refused（BossTowerFrameIntegration、SelectCharacterCompensation）；保完整raw/stack/source/端口选择诊断，同freeze01两target复跑2/2。没有直接根因证据，不登记新偶发，不归入原Dim，不声称端口耗尽或新App断言修复网络。授权后仅empty断言变freeze02，并重新Release和三full+四controls。最终Net02 199/199、0skip/0fail；原Characterization10全部pass，Golden15逐byte对HEAD及f01dc226。

最终freeze02 Release0警告0错误；App5128=5110pass+准确原Dim2fail+16skip，Protocol1773pass，Net199pass。加Task20另1800 full库存为8900=8880pass+2fail+18skip，原8883逐display/status/multiplicity全保，N17=App16+Protocol1、D0；四fresh controls不加业务N。DFO原CLI expected不变74、七unique controls、outside/exe/orphan0，无新增故意违规。套件资源仅观察，RecordedOutbound并非真实socket/A21实机，单CMD≤2s仍T-012最终测量；Task执行不批准自身结果，不终结Spec。
