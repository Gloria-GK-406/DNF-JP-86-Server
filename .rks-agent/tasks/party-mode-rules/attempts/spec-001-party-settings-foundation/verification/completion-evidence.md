---
kind: rks-verification
decision: pass
---

# Completion Verification：队伍设置模型与 `0x000C` 基础闭环

## Claim

当前完整结果满足子 TaskContract revision 2 的 SC-1..SC-7：A21 队伍设置已经从精确请求形状经集中
规则与原子领域变更投影为确定性通知，回归玩家队伍标志统一为 0，现有组队行为与分层未回退，协议资料
如实区分实机确认、静态 High 与尚未实现边界。

## Conditions and Decisive Evidence

- SC-1 / SC-2：审查后的 fresh Release solution build 通过，0 warning / 0 error；完整 Application
  1379/1379，其中 Party 31/31。`PartySettingsDomainTests` 覆盖三类/非法类别、默认 Normal、容量、
  round-trip、准入和 `[1]=1 -> 0` 且其余 11 字节不变。独立
  [`change-review.md`](../reviews/change-review.md) 复核了规则唯一入口及 Application 不引用 Infrastructure。
- SC-3 / SC-7：固定 A21 EXE/dstr 探针 fresh 运行，输入哈希匹配且 `byte_anchors=14/14 PASS`；
  `0x000C`、NOTI `0x0009`、catalog/index、真实样本与 static/live trace 已由独立审查判定一致。完整
  Protocol 797/797，含 direct/inline-title 严格正反 codec 与 type-1 两个 byte-exact 分支。
- SC-4 / SC-5：完整 Protocol Party 72/72、Application Party 31/31。四个确定性乱序测试覆盖 settings
  越过 accept/leave，以及较新 rejoin/leave 越过旧 clear/acceptance；当前成员视图、完整状态覆盖面、
  exact session audience、拒绝不污染与 revision/generation 单调性全部通过。
- SC-6：审查后串行执行完整 Release solution 测试，最终有效组合 3082 Passed / 0 Failed：Launcher 63、
  Pvf 60、Application 1379、Host 17、Infrastructure 301、LauncherApi 50、Networking 62、Persistence
  353、Protocol 797。带 PVF 时 Infrastructure 的 2 个缺档案条件用例 skipped；清除
  `PVF_ARCHIVE_PATH` 后这两个对偶用例 fresh 2/2 Passed。
- 捕获辅助验证：`capture_proxy.py --self-test` 通过，`test_capture_proxy.py` 2/2；固定 A21 探针如上
  14/14。所有 Python 检查禁用 bytecode 写入，两处审查前生成的 `__pycache__` 均已精确清除。
- 仓库与运行状态：staged/unstaged `git diff --check` 均为 clean；没有 DNF / Dfo.Server.Host 进程，
  34112、35169、7001、10011 均无监听。当前变更集中在根任务共享 worktree 的预期源码、测试、协议与
  工作流工件内。

## Preserved Boundaries

无队 direct create、inline-title 实帧、非队长设置 UI、`info0==0` type-1、`0x000C` CMD response、
三类玩法语义和公开队伍目录仍是明确未验证或后继范围；本次 pass 不把这些项目升级为已完成。
