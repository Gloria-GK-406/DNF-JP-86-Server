# TASK-03 — 采集 CH50 决斗技能页实机基线

## Task Unit Contract

- Revision: 1
- Outcome and references: 取得 A21 的首屏、重登和决斗技能操作证据，为 T-002 的入口接线提供依据。覆盖 AC-001 的实机部分和 AC-002 的协议输入。
- Owned boundary: 隔离的 A21 取证环境、测试角色、相关协议证据和本单元记录。现有决斗编辑权限保持到取证结果明确。
- Required checks: 记录运行提交、ClientStore 资源、频道、实际客户端观察和相关协议，确认数据库只写测试副本。
- Dependencies: TASK-02。
- Result review: not-required。证据需要注明观察范围，整期独立审核时复查。

## Detailed Plan

1. 用当前分支构建、ClientStore 的 dev 资源和抓包配置准备隔离环境。历史数据库副本只用于技术取证，正式用户验收另核实当前主线库。
2. CH11 观察普通技能页和点数。进入 CH50 记录首屏、可用页和点数。尝试客户端实际提供的快捷槽、加点和切页操作，区分界面禁用、未发包和服务端拒绝。
3. 正常重登后核对已保存的页、点数和普通组是否受到影响。将证据归档并回写相应协议页。

## CH50 已取证

2026-11-23，隔离服 `1e99e5927`、Client-20，dev PVF SHA-256 `a97132dda0970b3e0ce7c3de8052acf0be2664f52e26c819b1d8cb6d427cfcd2`。玩家报告进入决斗场后学不了技能。本机 CH50 转发采集到第 0 页的三次加点和一次换槽。主机日志的三次学习结果均为 `UnsupportedScenario`。首屏收到 NOTI `0x0013`，客户端显示值、重登和切页仍待确认。

- [学习请求](../../../../../protocol/game/client-to-server/0x001D.md#2026-11-23-ch50-决斗加点)
- [换槽请求](../../../../../protocol/game/client-to-server/0x001C.md#2026-11-23-ch50-决斗换槽)
- 原始帧存于 `E:/Project/DNF/ClientEvidence/traces/20261123-20261123-pvp-ch50-skills/ch50-skill-baseline-1e99e5927.jsonl`。

隔离服换到 `e96b35c90` 后，用户确认单项技能学习可用。采样记录到正向加点与换槽成功，重新进入 CH50 后的 SKILLINFO 含已保存的快捷槽位置。给测试角色解锁技能类型扩展后，A21 在 CH50 连续五次发出 5 字节切页请求，旧服均回 `00 03`。

- [切页请求](../../../../../protocol/game/client-to-server/0x0104.md#2026-11-23-a21-ch50-决斗频道切页)
- 切页原始帧存于 `E:/Project/DNF/ClientEvidence/traces/20261123-20261123-pvp-ch50-skills/ch50-skill-build-switch-before-implementation.jsonl`。
