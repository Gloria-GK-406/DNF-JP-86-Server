---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 3
state: ready
---

# Specification: A21 Normal 队伍模式闭环

## Why and Outcome

玩家应能在当前 A21 原生 UI 中创建、编辑并正确结束一个 Normal 队伍；服务端应保存和回送客户端真正
使用的 Normal 设置，而不是把历史 placeholder 当成规则。结果必须解决 `ModeTargetRaw=0` 与现行默认
`0xFFFF` 的关系，并把有证据的 Normal 规则收敛到已接受的共享模型，且不预判 Story/Cycle。

## Scope

包含 Normal 类别的上游/客户端/运行时证据、target/default、创建与编辑约束、队长解散/最后成员处置、领域与通知
行为、自动化和协议资料。继续保持回归玩家队伍标志为 0、未知字节无损和精确会话/代际语义。

不包含 Story/Cycle 特有规则、公开查找队伍目录或广播、列表刷新/排序、普通成员编辑权限变化、P2P、
副本战斗或奖励系统。

## Definitions and Invariants

- `Normal` wire 类别为 `2`；`ModeTargetRaw` 是设置字节 `[10..11]` 的 little-endian `u16`。
- `canonical Normal settings` 指当前 A21 创建/编辑/读取证据共同允许并能稳定回显的 Normal 设置；若创建
  默认和编辑状态使用不同 sentinel，规格结果必须分别命名其状态，不得伪装为一个值。
- 任何合法 Normal 行为仍经过 handler -> Command -> domain/rule -> Event/Notification；通知不进入领域锁。
- C2S `0x000C` 的前两字段先按当前 A21 证据投影为 canonical S2C 顺序；仅 canonical `[1]`
  （回归/特殊队伍标志）归一化为 0，canonical `[0]` 的预设标题索引与独立 raw-dstr 标题不得丢失。
- 同 party generation 中，较旧 revision 不得覆盖、清除或错误确认较新成员状态。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 以固定上游版本、当前 A21 构包/读取和真实帧确定 Normal 的 target/default 语义，明确解释 `0` 与 `0xFFFF`。 |
| RQ-002 | 集中规则边界表达 Normal 合法组合、canonical default 和必要的 normalization/rejection，不让调用方复制判断。 |
| RQ-003 | 无队当前会话可按当前 A21 合法形状创建 Normal 队伍；既有队仅当前队长当前会话可原子编辑。 |
| RQ-004 | 非法 target/类别/容量/编码、越权、旧会话和竞态失败不改变队伍快照、邀请或通知状态。 |
| RQ-005 | Normal 创建、编辑和成员变化产生确定性的成员视图与通知；普通成员离开只刷新幸存者，队长离开则清除所有原成员并解散整队，且不回退已接受的乱序保护。 |
| RQ-006 | 当前 A21 原生 UI 对创建、编辑和队长解散/最后成员处置形成可归因闭环，抓包与服务端记录能逐步对应。 |
| RQ-007 | 自动化、协议专页、样本、trace、index/catalog 与证据等级一致，并保持 Story/Cycle 和公开队伍目录边界。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 固定输入与可复现探针/反汇编记录能指出所有用于结论的 Normal 构包、读取、默认或移除分支；至少一份真实无队 Normal 创建帧确定 target。结论明确判定 `0`、`0xFFFF` 是等价、不同状态或现行错误，并记录冲突来源与置信度。 |
| AC-002 | RQ-002 | 给定任意合法 C2S 形状，decoder 明确交换前导字段为 canonical S2C 顺序；规则只把 canonical `[1]` 归零并保留标题索引、raw-dstr 与其余未知字段；Normal 合法值被确定性接受/规范化，非法组合被确定性拒绝；新队默认投影与 AC-001 的当前 A21 结论一致。 |
| AC-003 | RQ-003 | Given 当前无队会话，When 提交合法 Normal 创建输入，Then 原子创建一支以本人为队长的队伍并发完整状态；Given 既有队，只有当前队长的当前会话可编辑，成功只发布同 revision 的设置投影。 |
| AC-004 | RQ-004 | 每个拒绝类别都有状态前后对照：队伍 generation/revision/settings/title/members、待处理邀请和通知均不被失败路径污染；并发结果保持一个单调的获胜 revision。 |
| AC-005 | RQ-005 | 自动化覆盖 Normal 创建、接受、编辑、普通成员离开、多人队长离开整队解散、单人离队、会话清理和四种既有乱序反例；解散只投影旧 generation/party id 的 type-3 clear，迟到 side effect 仍按最新精确成员视图过滤。 |
| AC-006 | RQ-006 | 当前 A21 完成无队创建、既有队编辑、多人队长离队解散以及最后一人离队；每一步有定向 C2S/S2C 帧、Host 接受/拒绝与 UI 结果，客户端保持存活且最终队伍状态正确。 |
| AC-007 | RQ-007 | Release build 0 warning/error，全量适用测试逐项 Passed，分层断言通过；独立完整结果审查确认协议资料没有越级 `ClientVerified`，也没有引入 Story/Cycle、公开目录或成员权限变化。 |

## Success Evidence

- EV-001（AC-001/002）：Normal 专项静态 trace、固定探针输出、真实创建样本与规则 byte-exact 测试。
- EV-002（AC-003..005）：领域/Command/codec/notification 状态、audience、revision 和竞态测试。
- EV-003（AC-006）：最小定向实机 transcript、帧样本、Host 摘录与用户确认的 UI 观察。
- EV-004（AC-007）：完整结果独立审查、审查后 fresh build/test/diff checks 与完成验证工件。
