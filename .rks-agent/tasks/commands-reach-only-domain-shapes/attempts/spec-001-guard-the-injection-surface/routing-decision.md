# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根控制器给定并已收窄到一句可判定的话——每个命令
  处理器的每个构造器参数都能由类型本身归类。既不含内存容器的形状改造，也不含大聚合拆解。
- Requirement stability: 要求可逐条枚举。27 个处理器、55 个依赖已全部落到文件与行号；19 次
  白名单外的注入已分成 2 处硬违规、14 次命名不到位、1 次应豁免；3 个零消费方接缝、3 个真
  store 均已具名。这是一张能对账的清单，不是「大概要改哪些地方」。
- Execution direction: 已知。判据的半成品已存在（`CommandHandlerArchitectureTests` 已在反射
  遍历构造器），要做的是把黑名单换成按类型判定的白名单；改名的目标形状由「名字说出类别」这
  一条决定。
- Remaining uncertainty: 只剩非语义的局部选择——三个 store 的确切新名、五个 `Application`
  内部服务是补后缀上提到 `Abstractions` 还是加门面、判据用什么方式判定类别。三者都不改变本
  成果的含义，留给 Spec 与 Plan。

## Marginal Control Value

- User precision signal: 偏精度。所有者要求走 SDD、指定 multi-spec，且要求「跑完了再和我说」
  ——执行期间没有人工检查点。
- Direct failure mode: 两个，且都不会被判据发现。其一，**判据对死件失明**——三个待退役接缝
  各自带着测试，删漏一个、或只删接口留下实现、或删了实现留下注册，全量跑绿都不会有任何反应。
  其二，**按字符串改名会误伤** `IExpertStoreRegistry`，那里的 `Store` 指玩家摆的摊；这类错误
  改完仍然编译、仍然全绿，只是从此名字在说谎。
- Useful control: 可枚举的成功条件加完整变更评审。C-1、C-5、C-6 三条都是「把清单摊开逐个核
  对」，这正是判据给不出的那种证据；评审是唯一能发现「删漏了一个」的手段。
- Time-for-quality rationale: 值得。本成果的主要产出是一条**长期生效的机检约束**，它一旦立
  住，后续两个子成果与此后所有新代码都自动受约束。同一个仓库里 `AGENTS.md:249` 那张没有判据
  守护的 handler 白名单已经漂移到有 8 个 handler 违反，是现成的反面教材。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个验收口径——「行为逐值不变，且注入面可由类型判定并被判据守住」。改
  名、消违规、退役死件、立判据四件事共享这同一个口径，任何一件单独完成都不构成可交付成果：
  只改名而不立判据，约束仍靠人的记性；只立判据而不改名，判据只能退化成人工维护的类型名单。
- Why Direct is insufficient: Direct 给不出「把清单摊开逐个核对」这个控制，而本成果的两个失
  败模式恰恰都是漏项与静默失真，判据对二者均失明。
- Reroute conditions: 若发现三个 store 的改名会牵动持久化层的公开契约以致行为无法保持，或五
  个 `Application` 内部服务的裁决要求改动它们的调用方语义而非仅改名与移位，则本成果的边界被
  证伪，应退回根控制器重新划分而不是在成果内扩张。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: 本成果有一处真实的决策耦合，Plan 正是为它买单——十四次「名字没跟上」
  的裁决（尤其五个 `Application` 内部服务是补后缀上提到 `Abstractions`、加门面、还是改文档承认
  跨层）**直接决定判据能按什么规则识别类别**。C-3 要求归类由类型本身可判定而不是人工名单，
  所以裁决一变，判据的判定依据就得重写。先定裁决再写判据，Plan 把这个顺序固定下来；顺序反了
  就是返工，而返工发生在最终评审之前、评审看不到。另有一处次序约束同理：三个 store 改名必须
  先于引用新名字的判据落地。ordinary review 加新鲜验证足以保护「行为逐值不变」与「删干净了
  没有」这两条。
- Why lighter is insufficient: Lite 没有 `plan.md`，上面那个耦合只能在实现中临场决定。它不是
  「实现局部的安全选择」——它跨越四件工作里的两件，且错了要重写判据本身。
- Why heavier is unnecessary: Governed 的独立挑战与审计强度证据，在这里挂不到任何高后果失败
  路径上。本成果是行为保持的内部重构：不碰安全、凭据、授权、隐私、财务、法务，无不可逆的外部
  变更，无持久数据丢失路径；全部工作在一条本地分支上，可整体回滚。它的失败模式是「漏项」与
  「名字说谎」，二者由可枚举的成功条件加完整变更评审来防，而那是 Standard 已经配的控制。
