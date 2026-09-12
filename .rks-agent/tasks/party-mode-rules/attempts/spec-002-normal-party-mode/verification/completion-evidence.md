---
kind: rks-verification
decision: pass
---

# Completion Verification：A21 Normal 队伍模式闭环

## Claim

当前完整结果满足子 TaskContract revision 3 的 SC-1..SC-5：Normal target/default、C2S 到 S2C
canonical 投影、创建/编辑约束及普通成员/队长离队语义均由当前 A21 证据闭合；实现保持精确会话、
generation/revision 与分层边界，不扩大到 Story、Cycle 或公开队伍目录。

## Conditions and Decisive Evidence

- SC-1：审查后 fresh 运行固定 A21 EXE/dstr 探针；EXE 哈希为
  `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`，dstr 哈希为
  `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`；共享设置锚点
  `14/14 PASS`，Normal 专项锚点 `5/5 PASS`。真实 12 字节创建样本与 trace 证明 Normal target 为
  `0`，并把 `0xFFFF` 闭合为旧服务端 placeholder。
- SC-2 / SC-3：审查后的完整 Release solution build 为 `0 warning / 0 error`；九个测试项目逐项解析
  TRX 的每个 `UnitTestResult`，合计 `3085 Passed / 2 Skipped / 0 Failed`。Application 1379/1379、
  Protocol 800/800 覆盖合法/非法 Normal、字段交换、标题回送、创建/编辑权限、失败不污染、竞态与
  revision 单调性；Infrastructure 两项 skip 是提供真实 PVF 时按设计跳过的缺档案反例。
- SC-3 / SC-4：领域和投影回归覆盖普通成员离队刷新、多人队长离队整队解散、单人离队、会话清理及
  四种既有乱序反例；生产与测试范围内 `PartyRetirement|Retirement` 搜索为 0。实机 trace 把最终
  disband 路径与中间 replacement 反证明确分开，未把错误中间实现冒认为最终码实测。
- SC-4：既有邀请、接受、名册、HP、详情、离队、清理、设置 `[1]=0` 与三类共享基础均包含在上述
  Application/Protocol 全量通过结果中；项目分层断言亦随 Application 全项目通过。
- SC-5：独立完整结果审查 `approved`，无剩余 P1/P2：[`change-review.md`](../reviews/change-review.md)。
  `0x000D` 专页、game index、catalog 与 NOTI `0x0009` 均同步普通成员、多人队长和单人队离队状态；
  三份最小样本经 fresh 解析为 `12 / 61 / 5` 字节。
- 捕获证据边界：fresh self-test 同时通过 TCP 拆/粘包、C2S/CMD allowlist、同 type S2C 与错误 selector
  反例。两份 JSONL 分别为 35/25 行，联合规则扫描 `invalid=0`；误收的 S2C `NOTI 0x000D ITEM_LIST`
  已移除，现只保留 C2S/CMD `0x000C/0x000D` 与 S2C/NOTI `0x0009/0x000B/0x0099`。
- 仓库与运行状态：staged/unstaged `git diff --check` 均通过；任务端口
  `7001/10010/10011/17001/18080/20010/20011` 无监听，DNF 与 Dfo.Server.Host 进程为 0；生成的
  `TestResults` 与 Python `__pycache__` 已从交付边界精确清理。

## Preserved Boundaries

inline raw-dstr 标题 UI、非队长设置失败表现、公开队伍目录与可见范围广播、Story/Cycle 规则、P2P/UDP
及玩法效果仍是明确未验证或后继范围；本次 pass 不把这些能力升级为已实现。
