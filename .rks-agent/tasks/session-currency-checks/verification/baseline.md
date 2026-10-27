---
kind: rks-verification-baseline
---

# 基线：`ea6a1544`

在 Script.pvf 存在的情况下，做一次完整构建和全量测试（`logs/baseline-build-test.txt`）：

- 构建：0 警告，0 错误。
- 测试：只有 `Dfo.Server.Application.Tests` 有 2 个失败，其余项目全部通过。

| 项目 | 通过 | 失败 | 跳过 | 总计 |
|---|---:|---:|---:|---:|
| HttpApi.Tests | 50 | 0 | 0 | 50 |
| Protocol.Tests | 1694 | 0 | 0 | 1694 |
| Networking.Tests | 97 | 0 | 0 | 97 |
| Pvf.Tests | 80 | 0 | 0 | 80 |
| Persistence.Tests | 475 | 0 | 0 | 475 |
| Infrastructure.Tests | 967 | 0 | 2 | 969 |
| Application.Tests | 4305 | 2 | 16 | 4323 |
| Host.Tests | 165 | 0 | 0 | 165 |

2 个失败都在 `DimensionDungeonTests`，原因相同：迷宫 7100 的内容问题（`cell (1,0) has no compatible map for entrance 'Left'`）。这与上一个任务的基线一致，和本任务无关。
