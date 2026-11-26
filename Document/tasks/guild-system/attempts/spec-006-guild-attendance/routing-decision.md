# 实施路线

- [子契约第 1 版](TaskContract.md)及[独立审查](task-contract-review.md)通过，SHA-256 为 `FD85868572FB7F97A178133D70294F18466E427063EF499E5EAD9B6BAB8C34D6`。
- 使用单一规格、sdd-lite、focused-delivery。持久签到、计数和明细须同时验证，避免两个显示区读取不同日期或成员范围。集中检查数据库迁移和原生协议即可覆盖这段工作。
- 子任务基线 `039bfe81214f36669e63aabc5e800c8d656b53bc`。上级 [分期图](../../multi-spec-map.md)。
