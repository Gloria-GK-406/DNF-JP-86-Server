# TASK-27 / F-122 覆盖与语义核对

本单元是 P4 T-004 内 F-122 的纯命名闭合，消费 Spec B3 RQ-013、AC-008 及相关 AC-009/011。精确 26 路径见 ownership-before.json。没有新协议 CMD、来源/过渡 ID 或 ledger 行；两 canonical 从完整 tracked 分区开始即属于 nonowned，不因任务编号或路径后缀被排除。Spec002 其余工作、Cera 协议迁移和单 CMD 2 秒测量不在本单元。

## 结果含义与消费分支

- `INotificationBus.cs` 首项更名 Registered，原无显式底层类型、原声明顺序及四数值 0/1/2/3 与 default0 保持，无 alias。`semantic-equivalence.json` 保存完整原/新枚举对照。
- `NotificationBus.AttemptAsync` 仍先检查调用者取消、精确 session/current 校验，再 Compose；空消息 Declined；第二次取消检查后原 SendBatch 或 Debug 丢弃，随后返回首项。调用者取消仍重抛，其它异常仍 Warning + Failed。原 SendBatch await 只是编码/登记；发件箱提交与实际发送不被提前到当前 body。没有 await/超时/重试/flush/回滚/连接关闭变化。
- `GmCeraReminderDomainService` 的 distinct accounts、缺 session offline、Failed 收集并在循环后 throw、非首项 offline 三分支未变；首项仍 attempted。余额、audit、SQL、Cera 旧命令/Legacy footprint/恢复 catch 范围逐 byte 不变。真实 GmCeraReminderTests 的在线、缺会话、Gone、Declined、compose error、transport failure 共 6 case 全保。
- 七 quota 的字典键仍 exact SelectedSessionIdentity，原 stale prune 和同周期 skip 不变。Altar/Heras/Tournament/Dimension/GoldSilverHorn/ReviveCoin 仍 `IDailyResetRuleService.PeriodStart(clock.GetUtcNow())`、DateTimeOffset 值；Mirror 仍 UTC+8 midnight DateOnly。登记前及登记后的 `IsInWorld` 检查、精确 CharacterId 与首项 outcome 合取后才推进水位，不推进失败水位；同周期 transport failure 不再登记的原语义保持。七家族乘两个强意图共 14 theory 参数用例原名保留，不用较弱 stub/结构断言替代。
- `PartyExactNotificationDeliveryResult.Registered` 和 Town 的两处属性消费闭合。exactAudience Distinct 与原输入顺序、pending 初值、MaximumAttempts=2、仅 Failed 形成下一轮 pending、已登记 HashSet 汇集、最终按原 exactAudience 顺序过滤及 HasFailures 均未变。Town 先 publication 登记后 party read-repair 的两个调用点和其它 await 次序未变。
- 五个额外 NotificationOutcome 源只有 Failed/Gone 引用，全程只读，见 reference-closure.json。可靠事件/transport 的其它 Delivered、CardReward 标记、历史 Task 和客户端证据不追零。真实 27 个直接引用文件与 original HEAD/当前 alias、using-static、反射、nameof、字符串和 JSON 检索 raw 均保留；宽泛 JSON/ToString 命中是 transaction completion/failure，未发现生产 NotificationOutcome 字符串序列化映射。

## 一个时序观察的价值与限制

新增 `An_accepted_notification_returns_before_the_publishing_job_submits_or_sends_it`，保护的是已接受登记与实际发送之间的边界。若改名时错误把返回点移到物理发送之后，原测试多在 job 结束排空后读取，只能证明最终结果，不能直接发现这个偏移。新 Fact 沿 NotificationWorld 原 provider、GameplayExecutor、真实 bus 和 sender/outbox，在 publishing body 内真实 PublishAsync 返回后同时观察结果、WorldDelivery.Registered 与 Sent；此时两者为空。body 结束并由原 SettledAsync 排空后，一批、精确 recipient、两条按序消息实际发送一次。helper 是仅测试的窄入口，不 fake result/bus，不启第二 provider，不提前 flush，不改原 PublishTo/Project 方法或其 finally。

它先在旧名 Delivered 和未改产品源上通过 `task-27-observation-old-02`；首轮仅因 helper 未传 RunAsync 必需取消参数 CS1501，诊断保存在 `task-27-observation-old-01`，补 CancellationToken.None 后自然通过。此为 fixture 编译纠正，不是行为 RED。本次无行为新增，不造编译 RED/mutation；旧名 GREEN 证明既有语义，新名 focused 再验证同事实。focused-summary.json 记录 App64→65、Protocol106→106、所有旧 display/status/multiplicity 保留及 quota14/GM6 的精确身份。

## 文档与差分证据

AGENTS 仅当前通知结果那一条句子改名/释义；NOTI-0x0009 仅当前服务端 town publication 那个 bullet，明确 Registered 为接受登记，按登记顺序继续 read-repair，不承诺物理收到。机器区、历史客户端/截图/抓包、证据等级和其它正文逐 byte 保留；无参照 S4A21 文档写入。

`check-semantic.py` 对 26 个完整原/新文件做逐 byte 有界归一：只允许列出的精确标识符/注释/两个文档区间，并移除单个捕获的新 Fact/helper；不通过任意 whitespace/token 丢弃隐藏行为差异。全部原方法/参数/业务表达式、条件/常量/调用/await 顺序其余 bytes 相同，LF/CRLF/BOM 保持。人工逐分支读 source 和 owned-diff.patch，结论如上；归一只作为差分边界证明，不独立推断运行语义。

## 其它验收与局限

同源构建、八项目 full、逐 case N/D、DFO74/7、Golden15/Characterization10、source/runtime/pure family 全 owner 与最终完整 ownership/manifest 在相应独立证据文件中，须全部当前成立后才写 Task Result。ledger-readonly.json 是原 strict 与原八 selftest 的一次只读执行；没有制造物理坏副本或修改原工具。原两 exact Dimension 失败及十八 Skip 不宣称修复；Host 历史失败不作豁免。套件时间不是单 CMD≤2秒，T-012 仍负责该测量。没有客户端实机验收、推送、PR、合并、版本/tag/部署或 Task28；独立结果快审由主控另行发起。
