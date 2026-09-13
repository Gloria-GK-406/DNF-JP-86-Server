---
kind: rks-plan
contract:
  path: ./TaskContract.md
  revision: 1
spec: ./spec.md
mode: task-units
state: ready
---

# Plan: A21 公共队伍目录阻塞闭合

## Task Units

| ID | Outcome | Depends on |
|---|---|---|
| TASK-01 | 复核冲突报告，固定两个窗口、主目录真实数据链、协议归属、服务端最小边界和 runtime frontier | none |

## Checks

- 固定当前 B1/dstr 与接受基线哈希。
- 对五份报告计算 SHA-256，确保后继引用的是审查版本。
- 逐项检查 AC-001..005；搜索 verdict 中是否仍把 `0x023C` 称为 ID `0x11` 主目录数据源。
- 独立完整结果审查后执行新鲜哈希/状态/内容验证。

