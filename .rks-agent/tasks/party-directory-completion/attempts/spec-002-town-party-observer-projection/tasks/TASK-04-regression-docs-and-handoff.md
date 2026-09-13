# TASK-04 — 回归、资料与运行时交接

## Task Unit Contract

- Revision: 1
- Outcome: 完成组合回归、协议知识库更新、runtime probe/验收交接与 Spec 完整审查输入。
- Dependencies: TASK-03 accepted result。
- Owned files: protocol 记录/catalog、本 Spec 证据/任务文档；仅修复前序实现的审查缺陷，不扩展范围。
- Required checks: Release build、聚焦/全量 TRX、分层断言、无新增目录 0x023C、diff-check、完整独立 review。

## Result

### Completed result

- Protocol record: `NOTI-0x0009.md` 新增 2026-09-13 town-visible 非成员投影节，明确服务端实现、
  `0x0002 -> 0x0009` 因果顺序、exact-session/竞态语义和 A21 ID `0x11` 尚待双客户端实机；
  `protocol/catalog.md` 同步状态。没有新增 `0x023C` codec、message、handler 或注册路径。
- Release build: 初跑 `--no-restore` 因该新 worktree 的 11 个项目没有 `project.assets.json` 而失败；
  逐路径确认缺失、solution restore 后同一构建成功，`0 warning / 0 error`。这是环境准备恢复，不是
  产品代码修复。
- Full regression: 首轮并行 solution test 证明了缺失 `PVF_ARCHIVE_PATH` 会使 Protocol 42 个
  archive-dependent case 失败，且一个真实 TCP case 与并行项目宿主争用；显式指定三份同 SHA-256 的
  A21 `Script.pvf` 之一并以 `--maxcpucount:1` 串行重跑后，9 份 TRX 的每个 `UnitTestResult` 均已解析：
  `3123 Passed + 2 NotExecuted + 0 Failed + 0 Other = 3125`。
- Static checks: `git diff --check` 通过；`Server/src` 新增行中的 `0x023C/023C` 命中数为 `0`；
  Application/Protocol 的既有分层断言包含在全量通过集。
- Runtime handoff: 本 Spec 不把自动化升级为 `ClientVerified`。后继 Spec-003 需要两名同区、非同队观察者
  走原生 ID `0x11`，验证创建、设置更新、普通离队、解散、换区/重选/断线后的出现与移除。
- Remaining: 完整 mixed code/deliverable 独立 review，以及审查后的 fresh completion verification。

### Independent review Round 1 remediation

- Verdict: `changes_required`，F-001/F-002 为 P1、F-003 为 P2；正式证据见
  `../reviews/change-review.md`。
- F-001 RED: requester-only realtime 先发送同 revision 后，完整 mutation 的预期四阶段为 `0`；修复为
  realtime 只受 full-state watermark 约束、绝不推进它。目标测试转绿。
- F-002 RED: observer 与 read-repair 的 fail-then-success 在单次顶层调用中均只尝试 `1` 次；town
  `0x0002` 首次失败后只存在 newcomer 方向的 party repair。修复为因果所有者对 Failed exact recipients
  原位重试一次，town detached retry 改为 `0`；瞬时失败、持久失败后同 revision 再入、town 成功重试后
  `0x0009` 和 read-repair `0x0009` 均有生产入口判据。
- F-003 RED: 阻塞旧 generation read-repair 时，新 generation mutation 已完成（expected false,
  actual true）。修复为 mutation/read-repair 在 generation gate 外共同按 CharacterId 稳定获取 participant
  gates；新 mutation 只能在旧发送完成后继续。跨 generation 顺序测试转绿。
- Focused GREEN: remediation Protocol `5/5`；party/town/cleanup Protocol `96/96`；
  select/party/cleanup Application `57/57`。等待独立 Round 2 复审后再做 full completion verification。

### Completion verification after approved review

- 最新独立 Round 2 verdict：`approved`，F-001/F-002/F-003 全部闭合，无新 blocker。
- Fresh Release build：`0 warning / 0 error`。
- Fresh focused：Protocol party/town/cleanup `110/110`；Application party/select/cleanup `79/79`。
- Fresh serial solution TRX：逐条解析 9 份结果，`3127 Passed + 2 NotExecuted + 0 Failed + 0 Other = 3129`；
  两个 NotExecuted 是显式 PVF 环境下与成功路径互斥的缺失 archive 判据。
- Fresh static：`git diff --check` exit `0`；当前和基线 `Server/src` 均无 `0x023C/023C` 命中；
  Application 生产引用边界未改变。正式映射见 `../verification/completion-evidence.md`。
