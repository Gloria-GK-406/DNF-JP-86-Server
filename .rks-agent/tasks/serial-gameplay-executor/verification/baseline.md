---
kind: rks-verification-baseline
---

# 基线：`b76504bb`

产品代码与 `a02bce46` 相同。在 Script.pvf 存在（工作区根目录）的情况下，尚未加入任何表征测试时，做一次完整构建和全量测试，完整输出见 `logs/baseline-build-test.txt`（文件开头为 `label baseline` 与 `HEAD b76504bb4edd4ab5a2d09c4adb907d32a98fe9a2`）。

命令：

```text
dotnet build Server/86JP.Server.slnx -c Release
dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"
```

- 构建：0 个警告，0 个错误（日志第 28–29 行）。
- 测试：只有 `Dfo.Server.Application.Tests` 有 2 个失败，其余项目全部通过。

| 项目 | 通过 | 失败 | 跳过 | 总计 |
|---|---:|---:|---:|---:|
| HttpApi.Tests | 50 | 0 | 0 | 50 |
| Protocol.Tests | 1694 | 0 | 0 | 1694 |
| Pvf.Tests | 80 | 0 | 0 | 80 |
| Networking.Tests | 97 | 0 | 0 | 97 |
| Persistence.Tests | 475 | 0 | 0 | 475 |
| Infrastructure.Tests | 967 | 0 | 2 | 969 |
| Application.Tests | 4324 | 2 | 16 | 4342 |
| Host.Tests | 165 | 0 | 0 | 165 |

加法（8 个项目）：

- 通过：50 + 1694 + 80 + 97 + 475 + 967 + 4324 + 165 = 7852
- 失败：2
- 跳过：2 + 16 = 18
- 总计：50 + 1694 + 80 + 97 + 475 + 969 + 4342 + 165 = 7872；核对 7852 + 2 + 18 = 7872。

2 个失败（日志中 `[FAIL]` 恰好 2 处，第 8655、9101 行）都在 `DimensionDungeonTests`，原因相同：迷宫 7100 的内容问题（`Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'`）：

- `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`

这与 `session-currency-checks` 的基线一致。本次没有出现 AGENTS.md 登记的偶发项，也没有其他失败。

与上一个任务基线（`ea6a1544`）相比，Application.Tests 的总数从 4323 变为 4342，通过从 4305 变为 4324；其余项目数字不变。全量总数 7872 与 `research/05-tests.md` 一致。
