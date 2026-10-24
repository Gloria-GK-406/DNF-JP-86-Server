# spec-002 规格挑战集（2026-10-24）

- 独立生成b_spec_challenges，generating-adversarial-challenges；目标首份ready spec.md154行、Contractr1、baseline10a7218a。
- 权威：子契约/根r4/父移交规格/A已接受结果及清单。只读规格/契约/代表刷新实现，不改文件不跑测试不出verdict；不要求Spec提前接口/算法/Task细节。未发现有充分证据的high-risk，保留以下low-risk。

## CH-1：F132条件与验收措辞不全一致

- Risk: low-risk
- Failure and impact: RQ008只在采用事务内组装时要求先修F132，未采用可记禁用/不可达；AC004无条件要求已提交后失败/重发区分例，AC005也未限定路径。可能误读成强修未采用的潜伏路径、增加事务改动，或末段被另一解释退回。结合RQ008可合理消除，Plan前低成本澄清，不升阻断。
- Evidence: spec.md RQ008第41行、F132行111、AC004/005行134～135，子契约Scope条件前提。
- Minimum correction: AC004/005组装路径引用RQ008，采用则测提交后失败/重发，不采用则验证记录禁用/不可达不称已修；不改契约不强制机制。

## CH-2：F07拆分后因果顺序/周期水位线需要Plan明确

- Risk: low-risk
- Failure and impact: 原一次作业内遍历/登记/推进水位，排队子CMD间会插请求或跨周期/退出/替换；只拆人数可能漏/重刷新或改变NOTI与响应次序。规格已要求保周期/人群/水位及先列变化，不能断言必违约，可由Plan和区分验证控制。
- Evidence: RQ002、RQ013 F07、AC008与VC仅B1/B2其余帧序不变；DimensionQuotaDailyRefreshService:21-38、PremiumDailyRefreshService:47-84。
- Minimum correction: Spec不锁批量算法；Plan明确入队/执行、周期/接收者输入含义、执行当前性、水位推进，覆盖排队跨周期/替换。无法保原次序则实施前列变化，不到最终汇报追认。
