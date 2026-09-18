---
kind: rks-verification
decision: pass
---

# TASK-01 完成验证

验证的精确声明：TASK-01 已交付固定、可重跑的 Story wire/target/default/生命周期静态证据和安全实机捕获
计划，且没有修改生产代码；本声明不表示 Story Spec 或客户端验收完成。

## 条件与结果

| Task 条件 | 决定性结果 |
|---|---|
| 固定输入路径、大小、SHA-256、commit | `input-manifest.md`；探针 fail-closed 验证当前 B1/dstr 与 5 个固定 upstream blob |
| 上游与当前 A21 分离 | `upstream-baseline.md` 明确只将上游判为 opaque pass-through；客户端裁决独立记录 |
| 12-byte Story 字段逐项覆盖 | `story-field-map.md` 覆盖 `[0]`、`[1]`、`[2]`、`[3..6]`、`[7]`、`[8]`、`[9]`、`[10..11]`，未闭合项标 unknown |
| 构包与消费直接证据 | 当前 B1 的专用/通用 `0x000C` builder、`0x0009` 后 Story 显示/查找/进度链均有字节锚点 |
| VA/RVA 基准 | ImageBase `0x00400000` 与换算规则已记录并由探针检查 |
| 排除公共目录/快速匹配 | 没有调用链证明 `0x023C`/`0x01BE` 与 Story 基本设置不可分割；继续排除 |
| 可重复验证 | 新鲜运行 `19/19` anchors、`6/6` resources、`5/5` upstream blobs PASS |
| 修改边界 | `git diff --check` PASS；所有变化均在本 task root，无产品/测试/协议/客户端变化 |

## 限定

本 Task 的完成揭示了 Spec 层 blocker：尚无原生 Story create/edit 帧，不能裁决合法 target/default 与
opaque 联动值。该缺口不降低 TASK-01 的静态证据交付，但禁止继续 T-002。
