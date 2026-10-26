# 第二阶段待收口事项

迁移冲刺期间的主控裁定及结构债在此登记；它不是完成度账。五类迁移账全部闭合后统一回写 Spec，独立审一次，再完成结构优化与最终验收。

| 事项 | 为什么需要 | 来源与处理阶段 |
|---|---|---|
| 拆除协议 CMD 的共享基类，增加禁止继承共用基类的判据及反例。 | 当前迁移复用 PartyMutationProtocolCommandHandler 等实质阶段；冲刺不为结构美观返工。 | 用户 §8，TASK-58 起登记既有模式；第二阶段整理。 |
| 去除生产 CMD 的 null!、可空依赖和 Func 能力参数，并增加非空依赖判据及反例。 | 临时共享阶段的构造形状仍包含这些入口；需在来源迁完后统一拆解依赖。 | 用户 §8，TASK-58 起登记既有模式；第二阶段整理。 |
| 整理误导命名、AGENTS 清单与实现文档。 | 切片、旧适配及共享阶段迁移后，历史名称和描述需统一与实质职责一致。 | 用户 §8，TASK-58 起；第二阶段整理。 |
| 使用内存配置的测试宿主明确关闭不参与断言的 JSON 配置热重载。 | TASK-58 两项 Host 构造挂起，空目录仍挂，关闭 reload 后完成；Protocol 同形调用再次挂起。对 App、Protocol、Networking、Infrastructure、HttpApi 的同类内存测试构造补显式参数，保留真实 Host、DI、启动顺序和排空断言，生产配置及测试热重载功能的边界不变。 | TASK-58 主控诊断裁定；第二阶段核实测试边界与文档，无新运行时能力。 |
| 原生 SQLite 提交测试用真实事务失败事实替代格式化异常栈的帧名判断。 | TASK-58 默认运行时下，预热真实异步 CommitAsync 路径可复现三个缺失 Commit 帧的失败，但同一异常的 FallibleCommit／KnownStorage／真实 SQLite 参与者和完整回滚仍被直接观测。统一核对实际阶段、来源和原因对象，保留外键、已执行写、资产与重试断言。 | TASK-58 主控诊断裁定；仅测试判据稳定性修复，第二阶段核对取证方式，无生产能力变化。 |
| Joust 下注复用既有 WriteResult 主结算，保正常业务失败及真实健康提交回滚的 ACK6。 | 旧真实 SQL ABORT 用例已守完整回滚与同帧重试；动作 catch 只包领域调用，槽位映射仍先于事务，取消、typed 不确定、guard、footprint 和提交后失败不借此恢复。只增加该既有能力的具名消费者，不增 API 或注入类别。 | TASK-59 主控裁定；第二阶段统一回写 Spec 并核正常拒绝与默认 VC-B40 边界。 |
| 允许 Joust 整 CMD 注入既有 IReplayStateProjectionSink，完成首次成功后的当前态刷新。 | 原 LegacyReplayStateProjectionDelivery 会嵌套派发 ReadReplayStateCommand；新路径保原第二个 Write，经领域 reader 显式 metadata 读取 Inventory／Joust，再以 null attempt 调原 sink，保持 DetailsChanged→刷新→ACK、精确连接与当前性。仅投影能力，不增加 API、raw 出口或嵌套 bus；架构判据保具名理由与正反例。 | TASK-59 主控裁定的新具名注入；第二阶段回写 Spec 并整理投影边界。 |
| 允许 Joust 整 CMD 注入既有 ISelectedSessionSnapshotQuery，捕获下注成功广播的原选中会话集合。 | 该接口由同一 SelectedSessionDomainService 实现，只返回原 selection 快照；保 Snapshot 采样时点与 TheseSessions 受众，避免 cast 或改变广播范围。为真实接口登记具名正例，保 Registry／持久化／总线及伪 Query 的拒绝反例，不开放通用 Query 类别。 | TASK-59 主控裁定的新具名只读注入；第二阶段统一归整领域查询边界。 |
| Housing、Capsule、WeddingLovePoint 复用既有 CommittedReplayCurrentStateBatch 登记出口。 | 三条效果请求迁入 whole 后仍需在重复已提交帧上投递原当前态；只给总线的已提交重放分支，不授予首次成功 raw 出口、不扩 WriteResult 或 Self，原 Main、MinimumReplayNeeds、物品槽及 Lost ACK 幂等守护保持。 | TASK-60 主控裁定的既有能力具名消费者；第二阶段统一核对登记出口与 Spec。 |
| 允许选角／回选整 CMD 使用具名 ICommandSelectedSessionCleanup 固定清理能力。 | 原准备单元内清理、无外层事务的回选清理与失败后补偿不能并成一个写事务；普通失败仍 sticky。能力仅调用既有精确 cleanup，不接业务委托或向调用方返回 footprint；入口冻结目标及两塔当前 run 的全部精确 owner，保原剩余队员结算和独立位置事务。仅当前未关闭请求／作业与具名注册可用；单元内须实际持有该请求正在执行 action 的唯一 unit，单元外须已无在途工作。清理期间拒绝普通新单元与重叠清理，跟踪并排空未 await 清理；能力结束即失效。失效权威首次被观察记入失败，最终不能转为成功；不清 sticky、不恢复普通业务、不新建作业或嵌套派发，失败保护集合不由 owner 推导。 | TASK-61 主控裁定的新固定机制能力与具名注入；第二阶段回写 Spec 并核正反例，其他生命周期来源须各自实际迁入后再显式授权。 |
| 允许选角整 CMD 注入既有 ISelectedCharacterStateMessageFactory、SelectCharacterDeliveryOptions 和 LotteryCounterExperimentOptions。 | factory 仅将本次 SelectCharacterResult 按原序转为消息并预校验 EncodeBody，不查询世界或派发命令；两个不可变选项仅控制原协议尾帧／显式客户端计数实验，不是游戏规则源。保既有生产默认、预校验时点与测试接缝，架构判据仅准这三个真实具名类型及其理由，不开放通用 factory／options 类别。 | TASK-61 主控裁定的纯协议投影及选项注入；第二阶段统一回写 Spec 和整理切片文档。 |
| 允许 ICommandConnectionClosures 增加具名 ResolveAndRequestCloseAsync(NetworkConnectionId)，用于选角被顶替连接的旧关闭点。 | 旧 SelectCharacter 对来源使用 exact context，对 pendingCloses 中其他 ID 在最后关闭点从传输 INetworkConnectionContexts 解析；在线用户记录可能已移除，不能替代传输解析。既有 OutboxConnectionCloser 复用其原 ID 解析并立即登记实际 context，后续同 ID 替换仍受既有发送端保护。CMD 不注入连接目录，来源仍走 exact 方法，不改变 F-131 链精度，不以空默认实现吞掉关闭。 | TASK-61 主控裁定的既有关闭能力窄扩展；第二阶段统一文档与注入边界。 |
| 允许 CommandRequestContext 提供只读 HasOnlyFailure(Exception cause)，选角过期物品业务拒绝使用实际 action cause 与唯一失败发生共同判定。 | 单纯 actionFailed 布尔值不能排除随后 lease 清理独立失败，且两个发生可能复用同一异常对象。只读判定要求已记录失败恰一项且其异常为原 action cause；保已有 FailedIndependent 发生计数，不清 sticky、不恢复工作单元、不授权 raw 出口。原正常拒绝仍保旧 0x02，独立基础设施失败按 VC-B40。 | TASK-61 主控裁定的只读失败事实观察；第二阶段统一回写 Spec 与具名消费者，非新恢复类别。 |
