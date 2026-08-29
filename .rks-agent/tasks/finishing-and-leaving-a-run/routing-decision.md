# Routing Decision

- TaskContract: `.rks-agent/tasks/finishing-and-leaving-a-run/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 四件都来自一次实机验收的观察，每一件的结果都在屏幕上——结算界面出不出得来、人回不回得到城镇、地上那堆材料写着几个、牌能不能翻。
- Requirement stability: 每一件的成因都已定位到帧号或文件行号（`BOSS_DIE_CHECK` / `CARD_START_REQUEST` 无处理器；`GiveUpGameHandler` 只回 ack；`DungeonEntrySlice.cs:279` 把实例判别值写在数量位上）。要求就是把这四处补上。
- Execution direction: 已知。三件落在既有的协议分片与内容源分层上；翻牌是新领域，但用户已经指定了它的形状（领域服务，handler 只收发）。
- Remaining uncertainty: 三个应答帧的**内容**要各自取证。这是执行内的取证，不是语义未定——每一件的可观察结果都不因取证结果而改变。

## Marginal Control Value

- User precision signal: 明确要求 SDD 流程、要求先写契约与 spec 再一次性执行、并对翻牌指定了实现形状。
- Direct failure mode: 这一圈的错法与经验那一轮同源，且已经发生过两次——**照抄上游的名字与字节**。三个帧上游都有处理器，抄它的应答会立刻"看起来能跑"，而本仓库两次因此错在没人看得出来的地方（经验通告的两个槽、频道比率的来源）。另一个具体风险是翻牌：它是发奖，直接写在 handler 里会让"一局只能翻一次"这条无处安放，而重复发奖在实机上只表现为玩家多拿了东西。
- Useful control: 每个子成果自己的 Spec 把"这个应答的内容从哪来"写成可观察判据，加上完整结果评审。评审读完成后的整体，能抓到"抄了上游、测试自洽、实机没验"这种单看一处看不出来的问题。
- Time-for-quality rationale: 这四件都会被玩家在同一局里连续看到，任何一件错都会污染下一件的实机读数——而实机读数是这条路径唯一的检验手段。上一轮正是靠这套流程在合并前拦下了"重放结算重复付款"与"到顶时报负基础值"两个只有整体视角才看得见的缺陷。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 四个成果分属三个互不相干的子系统，各自独立可验收、独立可回滚，且彼此没有实现依赖。(1) 应答 `BOSS_DIE_CHECK` 让结算界面出得来——判据是界面出现，单独交付即可玩。(2) 离开副本回到城镇——判据是不再重连，与 (1) 无代码交集。(3) 地面掉落的数量语义——判据是地上写着的数字，落在进入副本的帧上，与结算那一圈完全无关。(4) 翻牌奖励——一整个新领域，最大的一块，且带真实阻塞风险：奖励内容的来源（哪些卡、给什么）尚未取证，若取不到应当能单独阻塞而不拖掉前三个已验收的成果。
- Why Direct is insufficient: 见失败模式；四件里三件的"看起来能跑"与"真的对"之间没有任何自动信号。
- Reroute conditions: 若某两件在实现中发现必须一起改同一批文件、无法分别验收，则合并为一个 Single Spec；若翻牌的奖励来源取不到证据，该子成果按 `blocked` 封存，根任务如实记录缺口而不是编一套牌。
