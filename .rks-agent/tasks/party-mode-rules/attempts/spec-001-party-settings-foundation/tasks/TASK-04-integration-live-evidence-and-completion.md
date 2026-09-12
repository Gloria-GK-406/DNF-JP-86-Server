# TASK-04 — 当前 A21 实机闭环、协议归档与完成验证

## Task Unit Contract

- Revision: 3。
- Outcome and references: 以当前 A21 双客户端把已通过 TASK-02/TASK-03 的设置基础跑通并取得可归因
  wire/UI 证据，更新权威协议资料，完成 Spec 的集成回归与交付证据；覆盖 Plan T-004、RQ-010 及
  AC-001..AC-010 的最终证据汇总。
- Owned boundary: `.rks-agent/tasks/party-mode-rules/attempts/spec-001-party-settings-foundation/evidence/task04/`、
  本 Spec 的结果/审查/验证工件、`protocol/game/client-to-server/0x000C.md`、
  `protocol/game/server-to-client/NOTI-0x0009.md`、`protocol/catalog.md` 与必要索引；若当前实帧证明实现
  偏差，可在本 Spec 已授权的 party Data/Abstractions/Application/Protocol 与对应 tests 内做最小修正。
  不改客户端二进制/插件，不把频道登录凭证或无关全量流量写入仓库，不实现 Story/Cycle/Normal 玩法、
  未证实的 `0x000C` response、非空 S2C 标题或 `hasExtra!=0`。
- Dependencies: TASK-01 静态证据与 14/14 probe、TASK-02 approved 领域基础、TASK-03 Round 2 approved
  协议/通知闭环；两份当前 A21 客户端目录与用户进行一次明确 UI 操作。
- Required checks: capture helper synthetic self-test；current A21 合法 create、合法 edit、非法/无权更新
  不污染的可归因证据；逐帧核对 C2S `0x000C` 与 S2C `NOTI 0x0009`，只对确实采集的形状授予
  `ClientVerified`；协议专页/index/catalog 一致性；Release solution build；全部适用测试项目及
  Infrastructure 缺 PVF 场景，逐个 TRX `UnitTestResult` 解析；Application→Infrastructure 分层断言、
  staged/unstaged `git diff --check`；新增回归固定客户端提交 `[1]=1` 时领域和后续通知统一为 `0`、其他
  11 字节不变；完整结果独立审查与新鲜完成验证。

## Detailed Plan

1. 在 `evidence/task04/` 写一个只作本轮取证的 Python TCP 中转：标准端口 `7001/10010/10011` 分别
   转发到 Host 的 `17001/20010/20011`；按 11B Channel、14B Game C2S、15B Game S2C 总长字段重组
   TCP 流。Channel 仅透明转发且不落盘；Game 只记录 C2S/CMD-response `0x000C` 与 S2C NOTI
   `0x0099` realtime、`0x000B` endpoints、`0x0009` roster/settings，写成带时间、连接、方向、
   selector/type、body/frame hex 的 JSONL，避免凭证入库。helper 自带 synthetic split/coalesced-frame
   self-test，并固定 create 的 `0x0099 → 0x000B → 0x0009` 定向记录顺序，先 RED/GREEN 证明重组、过滤
   与顺序保留。
2. 用 Release Host 和 A21 `Script.pvf` 启动隔离实机会话：Launcher API 保持 `18080`；Host listener
   改为中转后端端口，但 Channel 对客户端仍广告 `10010/10011`，bootstrap 仍广告 `7001`。启动前确认
   DNF/Host/目标端口状态；Host 原始 stdout/stderr 只写仓库外临时目录，仓库只保存 allowlist 后的
   `SET_PARTY_INFO` 接受/拒绝、生命周期与必要错误摘录，并在入库前扫描敏感值和无关 body。不部署已知
   会与 `0050_A21RuntimeFix` 冲突的 `0100_NetworkHook`，不改两份客户端配置。
3. 所有准备完成后只向用户请求一次明确操作：分别启动两个 A21 launcher 并进同频道；A 通过原生 UI
   创建一个 Normal 队伍，再编辑至少一个已知设置或标题；B 加入后尝试由非队长修改（若 UI 不提供入口，
   如实记录不可达，不伪造点击结果）。用户报告每步可见结果后正常退出客户端。纯等待不进入 Task 状态。
4. 停止本轮 Host/中转，解析 targeted JSONL 与 Host 日志：关联每个 UI 动作的连接/sequence/时间；验证
   create 是 exact 12B 或按真实证据修正，edit 是 exact `16+n`，确认服务端接受后生成完整新建状态或
   type-1 设置更新，并检查是否真实存在 CMD `0x000C` response。对无权动作同时核对没有 revision/通知
   推进；UI 不可达时以自动化拒绝证据保留为 NotClientVerified，并明确缺口。
5. 将原始最小帧、环境、操作、结果与置信度写入新的 `protocol/patterns/data/traces/` Markdown；更新
   两个协议专页中已经过时的“尚未实现”描述、实现入口/测试向量与实机状态，并同步 catalog/index。
   只有帧、处理日志和用户可见结果能相互归因的形状升级 `ClientVerified`；其余仍标 High/Hypothesis。
6. 若实帧与实现冲突，先把差异写入 evidence，再在当前 Spec 边界内以失败回归固定它，做最小修正并
   重跑 party 聚焦测试；若冲突要求新增玩法语义或未知响应，停止并回到 controller，不用宽松兼容掩盖。
   用户随后明确暂不实现回归玩家玩法，因此把实帧中的 `[1]=1` 作为可兼容输入，由规则服务在领域写入
   前规范化为 `0`；不改历史样本，也不触及角色持久化中的其他回归玩家字段。
7. 运行 Release build、全部适用测试项目（固定 PVF），另跑 Infrastructure 无档案适用场景；逐项解析
   每个 TRX 并核对总数/failed/skipped。运行分层断言与两类 diff check，把命令、环境和结果写入
   completion evidence。
8. 汇总 TASK-04 Result 与 Spec result，交独立 reviewer 按 TaskContract/Spec/Plan/完整 diff、原始实帧、
   协议状态和 TRX 审查；修复阻断项后再做一次新鲜 completion verification。只有全部 AC 有证据或被
   明确标注为不允许冒认的手工缺口时，才由 controller 决定子规格/根任务状态。

## Task Readiness Review — Revision 1

```yaml
kind: rks-review
verdict: changes_required
```

- P1：原白名单只有 `0x000C`/`0x0009`，不能证明完整创建状态中的 `0x0099` 与 `0x000B` 及三阶段顺序。
- P2：原计划未明确 Host 原始日志只能留在仓库外，而未注册 Game body 会进入其 warning，存在把无关或
  敏感流量原样入库的风险。
- Revision 2 已把两种通知加入最小白名单与 synthetic 顺序测试，并把原始 Host 日志限定到仓库外临时
  目录；仓库内只允许扫描后的必要摘录，同时修正 AC 范围为 AC-001..AC-010。

## Task Readiness Review — Revision 2

```yaml
kind: rks-review
verdict: approved
```

- Reviewer 确认定向白名单与 synthetic self-test 已覆盖完整创建三阶段，Host 原始日志的仓库外边界与
  入库前扫描可执行，AC 范围完整；无新的执行前阻断项。

## Result — Round 1

### Changed

- 以双 A21 客户端完成既有两人队伍 direct `0x000C` 编辑：记录真实 12 字节 C2S、两端各一份 18 字节
  `NOTI 0x0009 type=1`、服务端接受日志和 UI 标志差分；同时确认创建完整状态的
  `0x0099 → 0x000B → 0x0009` 顺序可被两端消费。
- 更新 `0x000C`、`NOTI 0x0009`、catalog/index、静态与实机 trace；只把实际归因的 direct/type-1 分支
  标为 `ClientVerified`，inline-title、无队 create、非队长 UI、`info0==0` type-1 与 CMD response 保留
  为 High/未验证。
- 用户在 UI 差分后明确暂不实现回归玩家玩法。TaskContract/Spec 修订为 Revision 2，TASK-04 修订为
  Revision 3；规则服务新增规范化，任何 `0x000C` 设置在领域保存前把 `[1]` 强制为 `0`，其余 11 字节
  不变。历史实帧保持原值并标明发生在产品策略引入前。
- 关闭本轮隔离 Host/TCP 中转；当前标准和后端取证端口均无监听，未留下运行中的 DNF/Host。

### Why

实帧证明 `[1]=1` 会直接开启客户端“赛丽亚 Bonus”标志，但服务端没有对应奖励行为。将用户的禁用决策
放在规则服务并在领域写入前归一化，可让所有后续 type-0/type-1 投影自然一致，同时不让协议层复制业务
规则，也不伪造历史样本。

### Checks

- TDD：新增领域行为测试先以实际 `1` 对期望 `0` 失败，再由最小规则/领域改动转绿；当前
  `PartySettingsDomainTests` 10/10、Application Party 31/31、Protocol Party 72/72。
- 捕获 helper 内置 self-test 通过，unittest 2/2；原始最小帧、allowlist Host 摘录与实机 trace 已入库。
- 最终适用测试组合 3082 Passed / 0 Failed；带 PVF 的 Infrastructure 另有 2 个按条件 skipped，移除
  PVF 后这两个缺档案用例 2/2 Passed。逐项 TRX 和两次无关波动的处理见
  `evidence/task04/verification-return-flag/summary.md`。
- Release solution build：0 warning / 0 error；Application→Infrastructure 断言包含于完整 Application
  回归；staged/unstaged `git diff --check` 均通过。
- 完整结果审查首轮发现设置更新可能越过接受/离队 revision，导致新成员缺完整状态或 recipient-specific
  clear/acceptance 被高水位误删；两个回归先 2/2 RED，随后用按精确会话成员记录的完整状态集合修复，
  GREEN。复审又覆盖较新重新入队/离队反向越过旧副作用，两个反例同样先 2/2 RED；最终由最新成员
  视图过滤迟到 clear/acceptance，四个乱序用例 4/4、完整 Protocol 797/797。审查同时指出并已修正
  NOTI `0x0009` index 长度和服务端默认目标文档。

### Remaining

- 当前 Spec 的实现与证据已覆盖，等待完整结果独立审查与审查后的新鲜完成验证。
- “查找队伍”公开目录、同城镇/大区广播和列表排序属于本 Spec 外的后续能力；本轮只完成只读诊断，
  未把它混入设置基础结果。
