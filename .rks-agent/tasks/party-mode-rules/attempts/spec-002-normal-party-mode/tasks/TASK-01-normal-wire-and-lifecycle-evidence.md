# TASK-01 — Normal target/default 与最后成员处置证据裁决

## Task Unit Contract

- Revision: 3 (current-A21 UI correction; identifies this contract and its detailed plan)
- Outcome and references: 为 Plan T-001、RQ-001/RQ-006/RQ-007、AC-001 提供可消费的 Normal
  `ModeTargetRaw`/default 与最后成员处置 verdict；固定来源、真实帧和 UI 结果，但不修改生产规则。
- Owned boundary: 本 Task 文件、相邻 `evidence/task01/` 下的探针/输出/定向捕获工件，以及
  `protocol/patterns/data/traces/` 的 Normal 专项证据记录；若必须增强既有 capture helper，只能做不改变
  生产协议行为的诊断性修改。不得修改 Server 生产/测试代码或把协议状态提前升级。
- Required checks: 固定输入哈希；上游/A21 地址与字节锚点可复现；定向代理 split/coalesced 与 allowlist
  自检；真实 Normal 无队创建和最后成员处置帧可与 Host/UI 对齐；verdict 明确处理 `0`/`0xFFFF`，并用
  反向证据清单证明没有把 Story/Cycle 或公开目录混入。
- Dependencies: accepted spec-001 at `d1351ce6047ea84db1c764efc3ad9466781f07af`，其固定 A21
  EXE/dstr 探针、`0x000C`/NOTI `0x0009` 样本和现有 capture proxy。

## Detailed Plan

1. 冻结证据输入：记录 task worktree accepted baseline；复算
   `DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe`、`dstr.dat` 与相关 `DNF.exe`/ClientHack 配置哈希；
   运行 spec-001 探针确认 14/14 锚点未漂移。
2. 复核上游 `ServerS4A21` 的 `SetPartyInfoRequest`、`PartyHandler.Handle_SET_PARTY_INFO`、
   `PartyInfoNotiBuilder` 和最后成员离队/解散路径。固定实际 commit，区分它的兼容实现、注释和当前 A21
   可采用事实，不从 `UserMax=4` 等上游默认反推 JP-86 规则。
3. 在当前 A21 EXE 中从已知 Normal default builder `0x00C24785..0x00C24833`、automatic builder
   `0x0240BF41..0x0240C05A` 和 `0x000C` generic edit 向上下游追踪：找出无队创建的触发条件、target 来源、
   title/index 分支，以及“最后成员/移除队伍”对话框和发送的 command。把每个结论记为 Confirmed static、
   High 或 Hypothesis，并为关键分支增加精确字节锚点。
4. 复用/按需最小增强 `capture_proxy.py`：仅保留 `0x000C`、Normal 创建/设置、NOTI `0x0009`、现有队伍
   生命周期和实际移除 command；对拆包/粘包和新 allowlist 加自检。准备隔离 Host 与两路当前 A21 客户端
   的端口、日志归档和回滚步骤；不得碰基准客户端目录。
5. 在所有自动准备完成后向用户提出一次最小 UI 操作：从无队创建 Normal，记录创建前后；按证据需要做
   一次编辑；使队伍剩最后成员并选择“移除队伍”或客户端实际等价选项。每一步立即按时间、连接和
   sequence 对齐 C2S/S2C、Host accepted/rejected 和 UI 状态；强制结束进程不作为正常处置证据。
6. 写 Normal 专项 verdict：逐字节列出创建设置，裁决 target `0`/`0xFFFF`，描述最后成员命令与状态机，
   列出 T-002 可实现的最小规则、仍未知项和一个会推翻结论的反例。历史样本保持原值，只有当前实帧能
   升级相应路径为 `ClientVerified`。
7. 收尾检查：重跑探针/代理测试，确认没有修改生产/测试代码、没有遗留 Host/DNF/监听，`git diff
   --check` 通过；在本文件追加 Changed / Why / Checks / Remaining 结果供 T-002 直接消费。

## Result — Round 1

### Changed

- 新增 `evidence/task01/runtime-verdict.md`，以两次当前 A21 无队 Normal 创建、一次两人队长离队和一次
  最后成员离队，裁决 target/default、C2S→S2C 前导字段投影、标题丢失与队长生命周期。
- 修订本 Spec/Plan/TaskContract：canonical Normal target 为 `0`；C2S 前两字段交换后再应用 canonical
  `[1]=0`；预设索引/raw-dstr 必须保留。队长离队结论随后在 TASK-04 被当前 A21 UI 证据纠正。
- 公开队伍列表只记录缺失边界和旧服对照，仍保持在本 Spec 之外。

### Why

此前 direct 编辑样本前两字段恰好同为 `1`，掩盖了方向相关的交换关系。新的 create 帧以
`00 03`/`00 06` 打破对称，当前错误投影又与用户可见“无队伍名”逐步对应；同轮空 body `0x000D`
证明队长解散全队是服务端领域选择，而不是客户端发送了不同命令。

### Checks

- 固定专项 probe：5/5；接受基线 probe：14/14。
- capture proxy 既有 self-test 与新增 Normal allowlist self-test 均 PASS。
- 当前 A21 两次无队 Normal create：C2S category `2`、target `0`，Host accepted；客户端存活。
- 两人队长离开：C2S empty `0x000D`，Host accepted，S2C type-3 发给两端，用户确认整队错误解散。
- 创建预设标题：C2S `[1]=3/6`，当前回送 titleIndex/raw-dstr 均为 0/空，用户确认显示“无队伍名”。

### Remaining

- `3`/`6` 对应的具体可见预设文案等待用户补充，只影响资料映射，不阻塞 T-002/T-003 的字段投影。
- 最终实现后的客户端创建、标题、队长离队和最后成员离队需在 T-004 重新实机验证。

## Result Correction — Round 2

TASK-04 的最终实机证明：即使服务端发送旧队 clear 后再投影新 party，当前 A21 UI 仍显示“队伍已解散”。
因此 Round 1 依据旧版上游得出的“多人队长自动转移”被撤销；当前 A21 的有效裁决是队长离队整队解散。
target `0`、C2S 前导字段交换和标题回送结论不受影响。
