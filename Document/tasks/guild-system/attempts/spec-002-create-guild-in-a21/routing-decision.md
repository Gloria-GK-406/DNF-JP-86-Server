# 实施路线

- [任务契约](TaskContract.md) 第 1 版，[独立审查](task-contract-review.md)通过，文件 SHA-256 `5383350C8DE3E6CAED4ACD144452F552B4E867FD69CC94EE22E53EF5BBC49861`。
- 采用 `single-spec-workflow`。原生 UI 创建、扣款、识别角色身份共同形成一个独立客户端可用结果。上游证据给出可执行方向，待实机确认协议与刷新。
- 采用 `sdd-standard` 的 `planned-delivery`。协议、真实数据和界面之间的取证顺序会影响布局及验收，实施计划统一规定实验路径。无需对玩家库做现场变更。

## 执行配置

- Profile `planned-delivery`，控制器 `sdd-standard`，long-running `no`。
- 子任务基线 `b69a272fd4d8082d1794747f4016f2f278fea53b`。
- 门槛 `spec: none`、`plan: none`、`implementation: auto`、`review: auto`、`verification: auto`。
- 实现阶段绑定 `test-driven-development`，使用默认产物路径。上级 [multi-spec-map.md](../../multi-spec-map.md)。
