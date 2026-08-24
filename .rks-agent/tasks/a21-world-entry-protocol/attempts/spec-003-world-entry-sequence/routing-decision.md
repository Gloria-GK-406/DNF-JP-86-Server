# Routing Decision

- TaskContract: `.rks-agent/tasks/a21-world-entry-protocol/attempts/spec-003-world-entry-sequence/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 终点是一条可观察事实——客户端选中角色后留在游戏世界里。
  边界由一次实机对照定死：22 条协议已一致，差异清单逐条列出并已归档。
- Requirement stability: 七条成功条件逐条可判定。
- Execution direction: 以已知良好会话的字节为准绳改布局，不动架构；实机复现的手段
  （线级客户端建号 + 记录型中转 + GUI 驱动）已经就位。
- Remaining uncertainty: **断开的直接原因尚未定位**。这不改变成果含义（终点仍是"留在世界里"），
  但意味着工作从诊断开始而不是从改码开始，契约已把它写进 Assumptions。

## Marginal Control Value

- User precision signal: 用户要求「用 sdd 渐进重构」并要求协议文档随任务落地。
- Direct failure mode: 本成果最可能的失败是**把数据差异当成布局差异去改**。差异清单里
  `ITEM_LIST`、`SKILLINFO`、`GAME_OPTION`、`HOTKEY_OPTION` 的大小都与参考不同，但那是
  因为本仓用的是新建的一级角色而参考那条角色有装备有技能。照着"对齐大小"去改，会把
  正确的编码器改坏，而且改坏之后客户端未必立刻报错——正如 spec-002 里 LOGIN 应答差一个
  字节客户端照样接受。第二种失败是**照抄上游**：上游对 subtype0 的尾部就是一段写死的
  64 字节 blob，自认"尚待实机确认"。
- Useful control: 一份 Spec 把每条改动的目标字节与其证据写死，并明确哪些被判为数据差异
  及理由；加一次针对完整改动的独立评审，专查这两类失败。
- Time-for-quality rationale: 前两个成果的实测表明这类评审确实抓到了五条 A 级，其中三条
  是执行者自己写下的错误结论。本成果的差异面比前两个都大，评审的边际收益只增不减。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 差异清单里的各项共同决定同一条可观察事实（客户端是否留在世界里），
  且在定位到原因之前无法判断哪几项是必需的。分开验收会得到"改了但仍然断开"这种无法接受
  的中间态。一个连贯的验收边界。
- Why Direct is insufficient: Direct 不会留下"这条字段的目标字节是什么、凭什么"，
  也不会强制分辨数据差异与布局差异。
- Why Multi is unnecessary: 单一验收边界。若定位后发现某一项本身构成独立可验收的成果
  （例如 subtype 6 单独就能让客户端留下），可退回根控制器重新切分。
- Reroute conditions: 若定位到的原因需要改动服务架构；或若差异清单里的项数多到无法在
  一个成果里验收（例如需要为多条缺发协议各自建切片），退回根控制器重新切分。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: sdd-lite 的代码类 Profile（由该 Skeleton 选定）
- Why this is sufficient: 需要的控制是把每条具名字段的目标字节与判据固定进一份 durable Spec，
  加一次完整结果评审。诊断本身用 `systematic-debugging` 在实现阶段内进行，不需要额外的
  工作流阶段。
- Why lighter is insufficient: Direct 无法阻止"对齐大小"这类看似合理的改动。
- Why heavier is unnecessary: Standard 的 Plan 要防的是早期选择扩散；这里的顺序由客户端
  的反应决定，写在 Plan 里的顺序会在第一次实机之后作废。Governed 要防的是高后果逃逸路径；
  本成果不触及安全、凭据、权限、隐私、资金合规、不可逆外部变更或数据丢失。
