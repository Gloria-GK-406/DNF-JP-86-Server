# 实施路线

- 子契约 [第 1 版](TaskContract.md)，[独立审查](task-contract-review.md) 通过，SHA-256 为 `6E106CBE1DEE332B6147E77BC33FF999639A3E1E4FD315B4CAB6DBF27A315EC0`。
- A21 普通与全量名单是同一个只读成果，采用 `single-spec-workflow`。已知风险是按请求的一方选错公会、漏掉离线成员，或混用两种回包尾字节。规格和独立代码审查可以分别固定隐私边界、帧形状及验收依据。
- 使用 `sdd-lite` 的 `focused-delivery`。名单能够直接复用成员关系和在线会话，一个边界内即可构建和验证。若后续分页出现不同的访问或一致性规则，按新证据重新判断交付范围。

## 执行配置

- Profile `focused-delivery`，控制器 `sdd-lite`，long-running `no`。
- 子任务基线 `23f2518aa36652934f41f7653a928b1f157cd75d`。
- 上级 [分期控制记录](../../multi-spec-map.md)。
- 阶段门槛 `spec: none`、`implementation: auto`、`review: auto`、`verification: auto`。使用默认产物路径。
