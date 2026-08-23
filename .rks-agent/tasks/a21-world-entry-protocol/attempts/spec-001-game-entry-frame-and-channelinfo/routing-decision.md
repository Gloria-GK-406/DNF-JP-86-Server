# Routing Decision

- TaskContract: `.rks-agent/tasks/a21-world-entry-protocol/attempts/spec-001-game-entry-frame-and-channelinfo/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果的终点是一条可观察事实——客户端连上游戏端点后发出
  `CMD 0x0001 LOGIN` 而不是断开。三处待改点都已由实机字节定死，边界不再浮动。
- Requirement stability: 六条成功条件逐条可判定，每条都有具体的字节或日志判据。
- Execution direction: 方向已定：以已知良好会话的字节为准绳改线级布局，不动架构。
- Remaining uncertainty: 只剩一处局部判定——入站帧头最后三字节的内部切分。它不改变
  成果含义（帧总长 14 已由实测确定），且契约已要求「判不出就如实记为未判定并选择不
  依赖它的实现」。

## Marginal Control Value

- User precision signal: 用户明说「最好用 sdd 来渐进重构」并要求协议文档随任务落地，
  是对精度与可交付完整性的偏好。
- Direct failure mode: 本成果最可能的失败是**把上游写法当成结论**。上游把末三字节读作
  `seq(2)+extra(1)`，而实测计数按那样读是 0/256/512。Direct 之下这类「照抄且刚好不崩」
  的判读会被写进代码与文档，成为后续包体错位的根因，而且事后无法分辨是哪一处引入的。
- Useful control: 一份 Spec 明确写出每条具名字段的证据来源，加一次针对完整改动的独立
  评审，专查「具名字段是否真有实测依据」。这正好覆盖上述失败形态。
- Time-for-quality rationale: 三处改动本身很小，评审成本低；而错一个字节的代价是客户端
  静默断开或错位，排查成本远高于评审。判据明确，评审不会空转。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 三处改动共同决定同一条可观察事实（客户端是否发出 LOGIN），
  无法分别验收——只改帧头客户端仍会因 CHANNELINFO 断开，只改 CHANNELINFO 则
  `0x04DD` 仍被误读。一个连贯的验收边界。
- Why Direct is insufficient: Direct 不会留下「这条字段的证据是什么」的可复查记录，
  而这正是本成果唯一会致命的失败点。
- Why Multi is unnecessary: 单一验收边界，不存在需要分别接受或分别回滚的子成果。
- Reroute conditions: 若实机表明客户端在发出 LOGIN 之前还需要本成果范围外的其他消息，
  则把该消息并入本 Spec 或退回根控制器重新切分；若某处必须改动服务架构才能表达 A21
  语义，停下并回到用户确认。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: sdd-lite 的代码类 Profile（由该 Skeleton 选定）
- Why this is sufficient: 需要的控制只有两项——把「每条具名字段的证据来源」固定进一份
  durable Spec，以及一次针对完整结果的评审来核对这些字段是否真有实测依据。Lite 正好
  提供这两项，再加新鲜验证。三处改动没有排序依赖，实现层面的选择都能就地安全决定。
- Why lighter is insufficient: Direct 不留下逐字段的证据记录，而本成果唯一会致命的失败
  正是「照抄上游的判读且刚好不崩」——没有可复查的字段证据，评审无从核对，事后也分不清
  是哪一处引入的。
- Why heavier is unnecessary: Standard 的 Plan 要防的是「早期选择错了会在评审前扩散」，
  但这里三处改动彼此独立、当轮即可验证，没有会扩散的早期选择。Governed 的独立挑战集
  与制品审批要防的是高后果逃逸路径；本成果不触及安全、凭据、权限、隐私、资金合规、
  不可逆外部变更或数据丢失，`protocol/` 的凭据禁令是排除性约束而非本改动的处理对象。
