# Routing Decision

- TaskContract: `.rks-agent/tasks/change-risk-is-measured-repeatably/TaskContract.md`
- Contract revision: `2`

## SDD Readiness

意图稳定：所有者要的是「可持续测量」的系统，不是一次性报告；两处硬边界（产出不进
git 追踪、按日期时间命名）由他明说。目标仓库与语言范围已在澄清中收敛到
`DNF-JP-86-Server` 的 C# Server。结果可观察：入口命令能不能从新 shell 跑通、
`.crap/<时间戳>/` 会不会新建且不覆盖、每个方法有没有明确证据状态、聚合与明细对不对得上，
都是能当场验的事实。显式要求可枚举，已展开成 SC-1..SC-12。

剩余不确定性只在**工具选型**这一层（net10.0 下拿方法级覆盖率与复杂度用什么），
它不改变任务语义，属于契约刻意留给下游的执行方向选择。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 所有者点名要 SDD，说明他愿意为精确付时间。Direct 下最可能的失败是
  **交出一个「跑过一次、报告好看」的临时采集器**——crap 技能把这条路径点名为不合格
  交付，而它在对话里极难被发现：报告数字可以完全正确，系统却没留在仓库里、第二次跑不出来。
  与之并列的是**静默填零**：覆盖率报告映射不上的方法被当成 0 覆盖，CRAP 会被系统性抬高，
  最后按错误的热点排序去建议改造。durable Spec 把「持久化」「证据状态不得静默转零」
  钉成可复查的验收条件，complete-result review 再逐条对回去，正好压住这两条。
  这一堆产出（工具、配置、四层聚合、报告、文档、基线运行）共用同一条验收线——
  系统跑得出可信的同快照方法级 CRAP——没有能独立验收、独立回滚的第二个边界，
  所以是一个 Spec，不是 Multi。
- Reroute conditions: 若实测发现 C# 侧根本取不到可信的方法级覆盖率映射，
  任务退化为「复杂度诊断 + 采集阻塞报告」，验收线改变，回到意图对齐重定契约；
  若所有者改口要把 `ClientHack/`（C）一并纳入，两种语言的复杂度/覆盖率语义不可比、
  需要各自独立验收，届时回到 `multi-spec-workflow`。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `sdd-standard` 的最接近 Profile（实现型任务）
- Rationale: Lite 不够，因为**中心执行方向本身未定且是有序耦合的**：复杂度取值方式
  决定函数身份与源码区间，覆盖率工具与格式决定能不能按方法映射，两者共同决定 join 键，
  join 又决定聚合与报告的全部结构。这四步是链式的——覆盖率格式选错（比如只拿到
  文件/类级的 Cobertura），上面那三层白建，而 Lite 要到完整结果评审时才发现，
  返工代价是整个采集管线。一份钉住工具、格式与 join 键的 `plan.md` 让这个选择在动手前
  就被写死并可被质疑，正是 Standard 相对 Lite 多出来的那个控制。
  Governed 不选：本任务只读源码、只写一个不被追踪的目录，不碰安全、凭据、隐私、
  资金合规，无不可逆外部变更，也没有持久数据丢失路径；独立挑战与独立工件批准
  换不来对应的风险削减，只是加时间。
