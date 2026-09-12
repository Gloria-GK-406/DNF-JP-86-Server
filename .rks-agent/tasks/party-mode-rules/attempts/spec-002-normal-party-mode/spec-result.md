---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result：A21 Normal 队伍模式闭环

## Delivered

- Normal wire 类别保持 `2`，canonical target/default 修正为当前 A21 已证实的 `0`；非零 target
  确定性拒绝，`0xFFFF` 不再作为默认 placeholder 写入新队。
- C2S `0x000C` 的 `[special,titleIndex]` 显式投影为 S2C canonical `[titleIndex,special]`；只把
  special/回归标志归零，预设标题索引与 inline raw-dstr 标题不再被误清。
- 当前 A21 生命周期按实机结果收敛：普通成员离队仅刷新幸存队伍；多人队长及单人队长离队均清除旧
  party 并整队解散，不创建旧版 replacement party。
- 自动化、定向原始帧、三份最小样本、协议专页、trace、game index 与 catalog 已同步；捕获白名单
  同时约束方向、selector 和 type，已清除误收的同号 ITEM_LIST 帧。

## Verification

- 完整结果独立审查 `approved`，无剩余 P1/P2：[`change-review.md`](reviews/change-review.md)。
- 审查后新鲜完成验证 `pass`，逐项覆盖 SC-1..SC-5：
  [`completion-evidence.md`](verification/completion-evidence.md)。

## Repository Result

- 接受前基线：`d1351ce6047ea84db1c764efc3ad9466781f07af`。
- 本文件、验证证据及完整已审查 diff 由父 Multi Spec 控制器作为一个本地接受结果提交；无需父级回滚。

## Successor Guidance

- 公开“查找队伍”目录及附近角色广播目前未按频道隔离，且现有通知 audience 只有精确角色集合；后继
  子规格应以可组合的频道/城镇/区域约束统一查询与通知接收者选择，不把消息主题硬编码进作用域模型。
- Story/Cycle 的模式规则仍未研究；后继必须复用本次接受的设置值对象、集中规则服务、精确会话
  audience 与 revision/generation 交付语义。

## Artifacts

- 协议记录：`protocol/game/client-to-server/0x000C.md`、`protocol/game/client-to-server/0x000D.md`、
  `protocol/game/server-to-client/NOTI-0x0009.md`、`protocol/game/index.md`、`protocol/catalog.md`。
- 当前 A21 样本和实机 trace 位于上述协议专页引用的相邻 `samples/` 与
  `protocol/patterns/data/traces/a21-normal-party-live-20260912.md`。
