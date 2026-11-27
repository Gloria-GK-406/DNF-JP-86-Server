# 实施路线

- 契约 [TaskContract.md](TaskContract.md)，第 2 版。
- [独立审查通过](task-contract-review.md)，匹配 SHA-256 `A2B6DCBF247E18F1D3C719B66C750F2DD78BA771AFE65710AFED570E775B6896`。

## 就绪与结构

基础数据、一次扣费、加入与离会、角色删除约束形成一个可在真实 SQLite 上独立验证和回退的结果。采用 `single-spec-workflow`。显式契约和完整结果审查能覆盖静默部分提交、越权与删除条件遗漏。

## 控制器

采用 `sdd-lite` 的 `focused-delivery`。本阶段仅在分支与隔离测试库使用既有事务和迁移机制，独立结果审查和回归可直接验证，无需操作现场预检或另列计划。

## 执行配置

- Profile `focused-delivery`，控制器 `sdd-lite`，long-running `no`。
- 子任务基线 `9ca273980004e7648525c95f648d2588932d0046`。
- 门槛 `spec: none`，`implementation: auto`，`review: auto`，`verification: auto`。
- 实现阶段绑定 `test-driven-development`。使用默认产物路径。
- 上级 [multi-spec-map.md](../../multi-spec-map.md)。
