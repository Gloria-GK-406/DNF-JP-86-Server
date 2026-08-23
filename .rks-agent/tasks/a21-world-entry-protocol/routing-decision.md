# Routing Decision

- TaskContract: `.rks-agent/tasks/a21-world-entry-protocol/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标是单一可观察事实——A21 客户端从启动走到站进城镇。
  用户已明确排除服务架构改动、明确允许数据库改动、明确要求协议文档同步落地，
  三条边界都不再浮动。
- Requirement stability: 沿途协议可逐条枚举（帧层、Channel 四切片、CHANNELINFO、
  LOGIN、花名册、选人入场序列），每条都有可判定的验收：产出字节与该客户端接受的
  字节一致。
- Execution direction: 方向已定且已验证可行——以实测字节为准绳，证据来自已有的
  455 帧已知良好会话、`[FrameCapture]`、`[ReadTrace]` 与客户端反汇编三条取证路径；
  上游 `ServerS4A21` 只作线索。
- Remaining uncertainty: 只剩局部非语义选择——某条布局先用抓包比对还是先开
  `[ReadTrace]`，以及各成果的先后顺序。这些不改变任务含义。

## Marginal Control Value

- User precision signal: 用户明说「最好用 sdd 来渐进重构」，并要求文档在收尾时已落地，
  是对精度与可交付完整性的明确偏好，而非求快。
- Direct failure mode: 这类工作的真实失败形态是**把猜测当结论固化进编码器**——
  上游 A21 适配里就有整段写死的字节和自认「尚待实机确认」的包体。Direct 之下，
  一处照抄或一处推测会以「客户端能跑」的形式通过，直到后续包体错位才暴露，
  且届时无法分辨是哪一处引入的。上一个同规格根任务的实测也指向同一形态：
  四条 A 级缺陷全部由评审捕获、零条由测试捕获。
- Useful control: 每个成果一份 Spec 明确「这一段的线级事实是什么、凭什么证据」，
  加一次针对完整改动的独立评审，专门核对每条具名字段是否真有实测依据而非推测。
  这正好覆盖上述失败形态。
- Time-for-quality rationale: 协议错一个字节的代价是客户端崩溃或静默错位，排查成本
  远高于评审成本；而每一段改完即可用实机验证，评审有确定的判据可依，不是空转。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 沿途各段有各自独立的验收与回滚边界，且顺序成立即可分别交付：
  帧层改对之后客户端才可能收到任何完整包；频道段通过后客户端才到登录；
  登录与 CHANNELINFO 通过后才到选人；花名册通过后才谈入场序列。
  每一段都能单独判定「客户端走到了下一屏」，也能单独回退而不牵动其余。
  用户要的「渐进重构」正是这个形状。
- Why Direct is insufficient: Direct 无法在每段留下「这条字段的证据是什么」的可复查
  记录，而这正是本任务唯一会致命的失败点。
- Reroute conditions: 若实机证明各段无法分别验收（例如客户端在拿到完整入场序列前
  根本不显示任何中间屏），则退回单一 Spec 一次性交付；若某段发现必须改动服务架构
  才能表达 A21 语义，停下并回到用户确认，不在本任务内扩大授权范围。
