# Routing Decision

- TaskContract: `.rks-agent/tasks/one-reward-behind-four-faces/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标是「玩家翻开的那张牌上真的显示出这一局的装备与金币」，
  可观测、无歧义，且已有确定的验收方式（实机 + 服务端逐字节日志）。前一个 Spec 之所以失败，
  不是因为目标含糊，而是因为对协议的读法错了；读法现已由十四轮实机与反汇编定死。
- Requirement stability: 可枚举且可逐条核对。线上结构写定在三份协议文档里，
  每一条都能单独成为一条要求：三组 = 三行；slot = 列；条目 0 是金币且其 itemId 被忽略；
  条目 1 是装备；条目 2 起不上屏；应答是八条状态快照；`a`/`b` 是座位号；免费翻牌 `c = 0`；
  记录 1–3 的填充是 `c = 0`。
- Execution direction: 已知且很窄。规则层一个值不动，边界只有两个编码器
  （`DungeonClearPayloadCodec.EncodeClearDungeonReward` 与
  `CardRewardPayloadCodec.EncodeSelectCardResponse`）加它们的判据。
- Remaining uncertainty: 只剩一条已在契约 Assumptions 里具名的证据缺口——
  那个身份值在组队时是否等于自己的队伍座位号（单人实测为 `0`）。它不改变本任务要交付什么，
  只决定日后若被证实，改动落在哪一处；契约已要求把这一格集中在一处。

## Marginal Control Value

- User precision signal: 偏精确，且这一次的信号比上一轮更强。用户逐条主持了十四轮实验的
  设计（`c` 改 3、装备与金币对调、三组各放一件、四列各放一件、主动推应答、`a` 写 0/1/2/3、
  应答全填充），在证据出现后要求先同步协议文档再判定旧 Spec 的结局，并明确选择判 `failed`
  重开而不是在旧边界里打补丁。
- Direct failure mode: **判据钉错对象。** 这不是假想的风险，是刚刚发生过的事实：
  前一个 Spec 的 1964 条判据全绿、完整变更评审 `approved`、验证 `pass`，而真实客户端上
  牌面一律 NONE。原因是那些判据断言的是「编码器写了这些字节」——在那个层次上，
  判据和实现是同一枚硬币的两面，永远自洽，加多少条都发现不了。
- Useful control: 一份把要求**用「按客户端的读法把帧解回来之后得到什么」来表述**的 Spec，
  加上一次不共享我的结论的独立评审。前者让「判据钉的是解码结果而不是编码动作」成为一条
  会被单独检查的验收；后者去核对判据的**对象**，而不只是它的红绿——这正是上一轮唯一
  没有被任何控制覆盖的缺口。
- Time-for-quality rationale: 重复上一轮的失败代价极高（又一轮全绿交付 + 实机才发现 +
  再判一次 failed），而识别它的成本只是把每条要求改写成解码后的可观察量，并让评审者比着
  三份协议文档核一遍。协议文档已经写完，评审者有确定的比对基准，成本比上一轮更低。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个验收边界——翻开的牌上要么显示得出东西要么显示不出。
  两个编码器共享同一份线上结构（同一套 kind、同一个 slot 轴、同一份内容来源），
  拆开会让「内容放在结算帧、状态放在应答帧」这条唯一的分工横跨两个 Spec。
- Why Direct is insufficient: Direct 给不出「有人独立地检查判据钉的是什么对象」这件事，
  而这正是上一轮唯一失守的地方。没有独立评审时，我的判据和我的实现出自同一个误解。
- Why Multi is unnecessary: 只有一个连贯的验收与回滚边界。付费牌与黑砖牌的送达路径已被
  契约明确排除在外，它们在本任务里只是帧上写空的两组字节。
- Reroute conditions: 若实机证明牌面内容并非全部来自结算帧组 0（例如出现只能由应答提供的
  内容），本任务的核心分工失效，须回到意图对齐；若付费或黑砖牌被发现必须与免费牌同时实现
  才能让面板正确工作，范围扩大，回到对齐重定。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: 需要的控制只有两件——把要求表述成解码后的可观察量，
  以及一次独立的完整结果评审。两件 Lite 都有。
- Why lighter is insufficient: Direct 没有独立评审。上一轮的失败恰恰是「作者自己看不出自己
  钉错了对象」，而这一族错误按定义无法自查。
- Why heavier is unnecessary: Standard 相对 Lite 多出来的是显式 Plan，
  它的价值在于「有一簇互相牵制的实现决定必须先定在纸上」。**这一次没有这样的一簇**：
  规则层一个值不动，结构由三份协议文档给定，边界只剩两个编码器，写 Plan 只会把协议文档
  抄一遍。上一轮用的正是 Standard，它的 Plan 阶段也没能拦住这次的失败——失败点在判据的
  对象，那是方法问题不是排期问题。Governed 多出来的独立挑战与产物批准更挣不回时间：
  本任务不触及安全、凭据、隐私、合规或不可逆外部变更，且回滚已经演练过一次。
