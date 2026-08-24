# Routing Decision

- TaskContract: `.rks-agent/tasks/a21-world-entry-protocol/attempts/spec-002-login-response-and-roster/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 终点是一条可观察事实——A21 客户端在「选择角色」界面上渲染出
  账号已有的角色。两处待改点的目标字节已由实测逐字段定死，边界不再浮动。
- Requirement stability: 六条成功条件逐条可判定，每条都有具体的字节或截图判据。
- Execution direction: 以已知良好会话的字节为准绳改布局，不动架构；账号里需要有角色，
  取得方式（测试夹具或建号路径）是实现层选择，不改变成果含义。
- Remaining uncertainty: 只剩一处局部切分——"外观条目 23 字节 / 尾部 36 字节"由单样本差值
  加上游结构双向印证得出，而非多样本差分独立确定。契约已把它写进 Assumptions 并指明
  实机异常时首先怀疑这里。

## Marginal Control Value

- User precision signal: 用户要求「用 sdd 渐进重构」并要求协议文档随任务落地。
- Direct failure mode: 本成果最可能的失败是**把"客户端没崩"当成"我们发对了"**。LOGIN 应答
  就是活例：它与 A21 差一个字节且版本号不同，客户端照样接受并推进。Direct 之下这类差异
  会被留在原地，直到某个后继包体依赖那个字段才暴露，届时无法分辨是哪一处引入的。
  花名册那 15 字节尾部同理——长度改变会让其后一切错位，而错位的症状通常只是"界面不对"。
- Useful control: 一份 Spec 写明每条具名字段的目标字节与其证据，加一次针对完整改动的
  独立评审，专查"具名字段是否真有实测依据、结构是否闭合"。
- Time-for-quality rationale: 改动本身小，评审成本低；spec-001 的实测表明这类评审确实
  抓到了三条 A 级，其中两条是我方自己写下的错误结论。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 两处改动共同决定同一条可观察事实（选人界面是否正确渲染角色），
  且都在同一条链路上——LOGIN 应答之后紧接着就是花名册。无法分别验收：只改花名册，
  LOGIN 应答仍与已知良好会话不同；只改 LOGIN 应答，界面上仍看不到角色。
- Why Direct is insufficient: Direct 不会留下"这条字段的目标字节是什么、凭什么"的可复查记录，
  而"没崩即认为对"正是本成果唯一会致命的失败点。
- Why Multi is unnecessary: 单一验收边界，不存在需要分别接受或分别回滚的子成果。
- Reroute conditions: 若"账号里要有角色"这一步本身需要改动建号协议（而非仅是夹具），
  则把建号拆成独立成果并退回根控制器重新切分；若某处必须改动服务架构才能表达 A21 语义，
  停下并回到用户确认。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: sdd-lite 的代码类 Profile（由该 Skeleton 选定）
- Why this is sufficient: 需要的控制只有两项——把每条具名字段的目标字节固定进一份 durable
  Spec，以及一次针对完整结果的评审来核对这些字段是否真有实测依据、结构是否闭合。
  Lite 正好提供这两项，再加新鲜验证。两处改动无排序依赖。
- Why lighter is insufficient: Direct 之下无法核对"没崩"与"发对了"的区别。
- Why heavier is unnecessary: Standard 的 Plan 要防的是早期选择扩散，但这里两处改动彼此独立、
  当轮即可验证。Governed 要防的是高后果逃逸路径；本成果不触及安全、凭据、权限、隐私、
  资金合规、不可逆外部变更或数据丢失。数据库改动限于测试夹具，且开发阶段擦库不迁移。
