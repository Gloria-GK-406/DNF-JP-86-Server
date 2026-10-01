# TASK-009 当前投影自查与检查命令

此文件对应 TASK-009 Revision 2 的未提交实现。测试及源码仅证明 CurrentImplementation；未启动用户服务、未连接运行库或实机验收。

## 实际检查

环境：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。所有 dotnet 串行；fixture 使用临时绝对 SQLite 路径。

- Fresh build：`dotnet build Server/86JP.Server.slnx --no-restore -v minimal`，0 警告、0 错误，见 TASK-009-build.txt。
- Application：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-build --logger 'trx;LogFileName=TASK-009-application.trx' --results-directory Document/tasks/item-effects-unification/verification -v quiet`。这是完整 Application 项目，而不是过滤的专项，也不是全方案。最终2567通过、0失败、0跳过。首次2566/1失败另保留为 TASK-009-application-rename-classification-failure.trx/.txt。
- Protocol：`dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-build --logger 'trx;LogFileName=TASK-009-protocol.trx' --results-directory Document/tasks/item-effects-unification/verification -v quiet`。完整 Protocol 1226 通过、0 失败、0 跳过。
- Networking：`dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-build --filter 'FullyQualifiedName~PreparedBatchTests|FullyQualifiedName~TouchSocketNetworkMessageSenderExplicitDeliveryTests|FullyQualifiedName~ExactConnectionCloseTests|FullyQualifiedName~ServerMessageEncoderRegistryTests|FullyQualifiedName~GameInboundFrameCounterTests' --logger 'trx;LogFileName=TASK-009-networking.trx' --results-directory Document/tasks/item-effects-unification/verification -v quiet`，21 通过、0 失败、0 跳过。
- 新 snapshot/gate 开发专项：`--filter 'FullyQualifiedName~ReplayStateSnapshotTests|FullyQualifiedName~SelectedSessionTransitionGateTests'`，23 通过、0 失败、0 跳过。完整 Application 执行包含同一最新代码；重叠不累加覆盖数。
- `git diff --check`，clean。

## 新未跟踪源码与纯读链复读

- `IReplayStateProjection.cs`：needs/touched 只接受已具名类别和 physical addresses；unknown/default/virtual/unsupported 容器拒绝。Snapshot 拥有 copied private buffers；数组的 outbound adapter 每次重新复制。
- `ReplayStateDomainService.cs`：单次 `ReadReplayStateCommand`、Transactional/ISelectedSessionCommand；ambient SQLite 必须存在。前后 exact safety；实际 owner table 校验。仅按 needs 解析领域依赖；Inventory-only 不请求 skill ledger。沿 table/repository/query 读取，没有 nested bus/Seeking.Synchronize/Recover/Reconcile/Ensure/pet Advance。Appearance 的 worn pet marker 使用纯 `SupportCreatureMarkerProjection.CompleteAsync`，按捕获 At 计算；不调用 Normalizer。
- `ReplayStateProjectionDelivery.cs`：前置原 connection/context 与 session 检查；outer Exclusive 覆盖 read/materialize/whole typed batch。Read45借此 Exclusive，而不是另排 Shared。没有使用旧 receipt 或原消费 ACK；XP bonus None。
- `SelectedSessionTransitionGate.cs`：borrow 依据 LeaseOwner 的实际 AccessMode。Exclusive owner 可经 Shared child 借 Exclusive；held Shared 明确拒绝升级，不排队自锁。pending/canceled/closed frame 不算 held；显式 Acquire FIFO 未改。
- `TouchSocketNetworkMessageSender.cs`：actual registry entry 与 original context ReferenceEquals，捕获 entry.Session；全组 registration/endpoint/actual encoder/frame cap 预检，逐 frame 自有 copy，全部完成后才第一 send。每一帧原 Debug wire selector/type/name 日志保留。replacement 后不再次按 ID 找 transport；transport late failure 仍允许部分投影，并非原子网络。
- Inventory pure read 接缝只有 repository+projection；entry Load 仍先 LegacyWornNormalizer。Opening final receipt/Recipe Refresh/Select current-view 及 support current-view 使用 pure；selection entry 主角及 support 保留维护。
- CeraShop receipt 与 PartyMemberStatus 仍调用 entry Load，Sort 直接 business container；TASK-009 明确排除其业务改写，不能宣称全仓库 Load 已纯读。
- `BalanceAtAsync/GetSnapshotAtAsync/ReadAtAsync` 只 lazy 当前周期读取；旧 clock 入口 delegates。明确 At 覆盖 fatigue、item usage period/cooldown、premium usage period、BlackDiamond rolling display 与 wire lifetime。BlackDiamond eligibility/extra fatigue 是 configuration，不增加 expiry 业务；不声称冻结所有服务器时钟或奖励估价。

## 故障与证明边界

- `TASK-009-red.trx`：旧 sender 顺序编码，先 reset 已实际发到 ITcpSession proxy，后 encoder 抛出；要求零 frame 的行为断言失败。新 PreparedBatch 10 例 GREEN 使用生产 registry/sender，以及实际 Protocol pet/skills/extras/avatar encoders；不是编译失败 RED。
- `TASK-009-ordering-red.trx`：旧 replay outer Shared。真实 Production CommandBus 的 45/57/UoW60 与真实 SQLite GoldProbe 在 snapshot read 完成、batch sender pre-send barrier 期间允许更新。要求第二 settlement 排队的行为断言失败。新 Exclusive GREEN 同一测试防止提交超车。
- Party XP secondary recipient 的 ApplyMonsterKill→XP 通知链仅为只读结构反例来源，未将 GoldProbe 测试说成实际 Party XP 通知竞态实测。
- 真实 legacy worn slot24 read 保持 SQL row/identity，entry Load 才移 slot25；真实 SQLite transaction 在外部连接写入 Gold/account cube 后仍读同一旧 snapshot，下一次 fresh read 才显示新值；读失败保留 assets。跨日 awaited clock 变动仍同一 At；后来的 current-day view 重置不写旧 row。
- Production Protocol delivery、bus、真实 SQL 与实际注册 encoder 的组合证明六 inventory 当前表和 equipped pet 的两条 NOTI2。该 fixture sender 是具名 typed batch adapter；physical registry/cap/captured transport 证明另由 Networking 生产 sender 测试给出。
- 完整 Application 首次检查发现 RenamePetEffect.ItemUid 的具名架构分类漏项。根裁定最小添加 RenamePetEffect singular world-instance 分类，保留 generic/pile 负例并加明确正例；失败证据保留。只改测试分类后 fresh build 与完整 Application 重跑2567通过0skip，Protocol/Networking生产源码未变，不机械重跑。

本单位不含 raw request fingerprint/operation ledger/commit mark/effect footprint 收集，旧 guards 尚未退出；这些由 TASK-010 使用本 API 完成。最终全方案检查及集中人工验收仍在 TASK-011。
