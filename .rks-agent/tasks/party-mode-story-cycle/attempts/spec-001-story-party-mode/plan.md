---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan: A21 Story 队伍模式闭环

## Planning Choice

采用 task-units。Story 的产品规则必须消费先取得的 target/default 与客户端分支证据，实机验收又必须消费
已通过自动化的实现和定向捕获配置；在证据到达前预写后续细节会诱导猜值。分成“证据裁决 → 规则/领域
实现 → 协议与回归收敛 → 实机/资料闭环”可以在任一停止条件触发时保留可审计结果，并让后续单元只依赖
已经检查过的输出。

## Approach and Shared Decisions

- 代码与任务工件只在根 worktree
  `E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-mode-story-cycle` 中修改；接受基线固定为
  `9a3a0b492da11f48525d1225d02513e0dd1cd458`，父控制器拥有接受提交与失败回滚。
- 先读取 `ServerS4A21` 的 Story 创建/编辑/进入行为作为上游基线，再用当前固定 A21 B1 的构包/读取和资源
  路径裁决；上游只证明参考语义，当前 A21 冲突优先。
- 从已接受 Normal 的 `0x000C` direct/inline-title、C2S→canonical 前导字段交换、title、revision/generation
  与成员 audience 继续，不复制另一套设置写入或通知管线。
- `PartySettings` 继续 byte-exact 保存；只为有证据的 Story 语义增加类型化读取或规则。没有消费者约束的
  字节不命名、不归零、不参与拒绝。
- 先写失败测试锁定证据支持的 Story target/default、非法组合与拒绝原子性，再改
  `PartyModeRuleService`/领域边界；codec 只负责形状，handler 不重复规则。
- 实机阶段沿用 debug/play 分离和 NetworkHook 定向 allowlist。先固定 EXE/插件/服务端身份与端口，再由
  用户在原生 UI 创建和编辑 Story；只保存 `0x000C/0x000D`、直接相关 `0x0009` 及被证据证明的 Story
  消息，不做全量敏感帧归档。
- 若 Story UI 实际只进入快速匹配，或 target 被用作尚无法约束的索引，立即停止输入并交父控制器；不在
  本 Spec 内补快速匹配或猜合法范围。

## Work and Dependencies

| Work | Covers | Result boundary and dependency |
|---|---|---|
| T-001 Story wire、target/default 与生命周期证据 | RQ-001, RQ-005 | 固定上游/客户端输入，闭合 Story UI 选择到 `0x000C` 构包及设置消费者，形成字段/约束/运行时捕获计划。无生产改动；未能得到安全可实施边界则停止。 |
| T-002 Story 规则与领域原子行为 | RQ-002, RQ-003, RQ-004 | 用 T-001 裁决以 TDD 扩展集中规则服务与必要领域状态，覆盖创建/编辑/成员/离队和拒绝无污染。依赖 T-001 的合法 target/default 与特有行为边界。 |
| T-003 协议投影、通知顺序与回归收敛 | RQ-003, RQ-004, RQ-006 | 补必要 codec/handler/notification、精确 audience/乱序和兼容回归；若 T-002 不需要新协议则只证明现有链可表达。依赖 T-002。 |
| T-004 当前 A21 实机、协议资料与完成证据 | RQ-005, RQ-006 | 原生 Story create/edit/特有操作定向闭环，修正静态/实现冲突，同步协议样本/trace/index/catalog，完成全量检查。依赖 T-003 的可运行候选。 |

## Verification

- T-001 对每个用于语义裁决的上游文件、客户端 VA/RVA/字节窗口、资源和工具输入记录路径、大小及
  SHA-256；构包写入与下游读取至少各有一侧直接证据，单纯 UI 文本只作搜索种子。
- T-002/T-003 的 RED 必须因缺少目标行为失败，GREEN 覆盖合法/非法 target、容量、非队长、旧会话、
  generation/revision、成员 audience、离队/解散和 Normal 回归；每项 TRX 检查 individual result。
- T-004 把同一 exact session 的原生 UI 动作、C2S body、Host 结果、S2C 投影和 UI 状态按时间线归因；
  未能归因则保留 `Implemented`/`High`，不写 `ClientVerified`。
- 最终运行 Release solution build、全量适用测试、分层断言、协议链接/样本/trace 一致性与
  `git diff --check`；确认没有 Cycle、Issue #20、快速匹配或客户端持久改动。

## Next Planning Boundary

先物化 TASK-01，只交付 Story 静态/上游字段与生命周期裁决及安全实机捕获计划，不修改生产代码。只有
该结果能给出可测试的 Story 合法边界，才详细规划规则实现。
