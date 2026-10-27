# B6 后继 WHAT 裁定（2026-10-25）

## 来源与状态

主控依据根 TaskContract r4 的 Goal、Constraints「可见行为变化须列明」和用户授权「你来做主」及继续完成 Spec2，消费 committed done TASK-32 检查点 `17d924a06b1237ca2b4f5997983c0a4923dfc050`。其52场 matrix-04、report、Result R1、fresh independent approved R1及 Controller Consumption R1 是旧事实，不是下面新语义的批准。TASK-31 `e878c4ede03bc46e313b3bb3fa64de845d77e160` 已交 Mailbox；当前 Spec B5 / Plan P6 R2 的相关旧批准仍只涵盖原范围。

本记录裁定下一受影响 Spec B6 的 WHAT，须 fresh challenge / fresh independent Spec review；之后另行受影响 Plan P7、fresh challenge / fresh independent Plan review、主控消费和 amendment-only 本地检查点，才详细规划新 Task。当前只文档，不改产品/正式测试/canonical，不创建 Task33，不运行 native，不改变根/child/A/baseline/profile/auto门或远端限制。

## 六条消息及真实事务边界

四 remaining opening 是 CompoundItem、LotteryOpening（mode0/1同消息）、VendingMachine、EpicCraft；Rental 是 RentEquipment 和 ChargeRentPoint。六个原 Main 各自仍是一个 Main 写单元；Lottery mode1 原独立 ReadPremiumSnapshot 为另一个 ReadOnly，Rental 原 ReadRentalRefresh 为另一个普通 Transactional 写单元。拒绝不凭空执行成功后 refresh。保各自已有真实动作和响应次序、材料/物品/UID/租赁link/星/金币/事件/提交点、Main一请求一次、同序号和迟到重放当前投影，不递归 bus、不套 Mailbox 自结算。

## 新边界的裁定

| 情形 | 后继要求及相对旧变化 |
|---|---|
| 健康成功、正常返回拒绝/空提交及早拒绝 | 保原响应/帧序和资产事实；正常返回拒绝原来 Committed 的不伪称回滚。原无会话/参数门不放宽。 |
| 四 opening 原 action 失败及真实 native Commit 健康确认回滚 | 保原错误码/拒绝响应、完整回滚、原 cause 与无新增 Fence/关闭；恢复须来自本次真实阶段与完成事实并待完整清理健康，不能包住 guard/登记/cleanup 求恢复。 |
| Rental action SQL / native Commit 异常确认回滚 | 保原非恢复出口，无 ACK、实际来源关闭；不得因复用 opening 路线而新增普通异常恢复。 |
| Rent 的真实 NoRoom / InventoryFull 正常返回、RequireRollback 且完整健康确认回滚 | 有意修复旧 Frozen=false 后 Settled 抛 typed/Blocked/断线的缺口：正常返回 RentEquipmentResponse(Result:2)，wire body 为01＋uint32(2)，不是 Error:2；全资产/事件不变、Main/ledger 记 ConfirmedRollback，不记 Committed/Blocked，不新增 Fence/Block/DiscardExact/关闭，不额外 refresh。同帧重试遵守既有 ConfirmedRollback entry/原Ordinal，不复用已提交重放路径。若有第二错误/guard/取消/unknown/不确定/cleanup，仍按实际默认失败，不享此健康结果。 |
| C1/C2 等真实 Uncertain、非法生命周期/guard 或独立 unit/lease/whole scope 错误 | 保真实 Completion / typed / 原 cause 及独立发生，按既有默认失败终判；实际 Committed/Uncertain 参与者依现有条件精确 Fence/physical Block/DiscardExact/来源关闭，不从局部回滚快照改写事实，不靠同 cause 引用抹独立错误。纯 ConfirmedRollback 后第二普通错误不被一概伪称 Committed/Uncertain 或强加围栏。 |
| 旧 fine scope 折入唯一 whole scope | 有意采用 whole 完整失败边界：四 opening 不再把提交后的 scope 失败映普通拒绝并健康结束；Rental 不再为保旧无围栏而绕默认事实保护。whole scope 错误前可能已登记原 success/normal refusal 帧，按真实 unit 状态/原顺序保留并置后实际来源关闭，不补失败 ACK、不强制零帧、不撤帧/提前 flush。原 fine 故障在响应前，新的真实 whole scope 在 body/投影后，必须明列位置及线级差异，不能假称完全同点。 |
| Lottery mode1 premium / Rental 独立 refresh 的故障 | 不能回滚已提交 Main，也不新增可恢复 ACK；保持第二独立单元的真实完成事实。迁后该错误进入 whole 默认保护：若 Main 已 Committed，精确参与者接受既有 typed/默认 Fence/physical Block/DiscardExact/来源关闭。这是旧 catch 外无新增围栏出口的有意保护增强，不能遗漏其 VC。 |

上述有意变化应单列 VC-B6 并对应现有 RQ/AC；不是所有 remaining 协议自动放宽。其余 raw/default/保护不随之扩大。保护只针对本次精确参与者与实际旧物理来源，不误伤真实替换身份/新连接及无关玩家；实际发送/关闭顺序和原链直接验证。

## 原行为保留与有限能力

既有三个 raw 出口类型不新增第四类。六消息原 committed replay 是既有 CurrentStateBatch 同一语义；后继只可在逐具名旧 decoder/原 encoder 注错 oracle 证明后，为六个新 whole 注册该现有 family 的有限 owner。TASK-32 没有做这项 encoder 动态注错，不能称已证明；受影响 Plan 须把切换前旧 oracle、切换后同 provider 新 oracle及默认/来源/cleanup反例纳入，不满足则回受影响门。首次成功投影不得冒 replay raw；不授 SettledInitialBatch 或 RequiredExplicit 新 owner。不能借普通 IOException 或业务组帧/clock冒 encoder 来源。

现实际已有十个 WriteResult consumer（早期P6的“五consumer”只指原四opening+Cera子集，Task31审核已明确实际全10原样保持）。四 remaining opening 可作为具名后继消费者，Rental不增这种普通异常恢复，Mailbox SelfSettledAction六logical/五CLR唯一 whole不扩。具体API/HOW归受影响 Plan，不在Spec写实现配方；默认 Write/ReadOnly sticky、来源事实/guard、三个raw其它owner、Compose禁用/F-132保持。

根「纯搬迁与语义改动分开提交」继续生效。Plan须给出可执行分离：有意 NoRoom 修正与默认失败/作用域语义不得混进声称纯搬迁的提交；允许把本质带该语义变化的 whole 切换清楚标为语义实现单元，把可独立的纯结构/共享业务抽取另单元，不把整个改动笼统冒称等价搬迁。若真实安全分离需要另一最小前置单元，临近规划记录其依赖，不新增空壳或为形式保第二长期scope。

## 后续与非结论

T-004其余 Rename/Hatch/SharedEffect/GenCeraTicket、T-006～012及72来源/八过渡等未完成责任保持。TASK-32只142 focused/52 controlled native场，不是新9075/八full/DFO74+7/2秒/TCP/A21；代表抽样局限照旧。B6应针对最小真实风险改变WHAT，不能为取得审查批准制造通用框架或扩其他玩法。产品落地前新Task required result review；所有变更仅原worktree本地检查点，Astra/用户收尾门保持。
