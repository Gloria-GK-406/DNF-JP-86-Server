# 基线证据

```yaml
workspace_root: /Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021
workspace_kind: linked-worktree
task_branch: task/refactor-session-20261021
spec_baseline: 7fc8acdcabeb12356e4c9ca98686af2f451ffb58
preexisting_changes: []
durable_artifact_paths: [.rks-agent/tasks/online-identity-state-machine/]
```

## 构建与测试（基线 `7fc8acdc`，2026-10-21）

- `dotnet build Server/86JP.Server.slnx`：0 警告，0 错误。
- `dotnet test Server/86JP.Server.slnx`，工作区根放入用户提供的 `Script.pvf`（git 忽略）后：

| 测试项目 | 通过 | 失败 | 跳过 | 总计 |
|---|---|---|---|---|
| Dfo.Pvf.Tests | 80 | 0 | 0 | 80 |
| Dfo.Server.HttpApi.Tests | 50 | 0 | 0 | 50 |
| Dfo.Server.Persistence.Tests | 475 | 0 | 0 | 475 |
| Dfo.Server.Protocol.Tests | 1694 | 0 | 0 | 1694 |
| Dfo.Server.Networking.Tests | 97 | 0 | 0 | 97 |
| Dfo.Server.Infrastructure.Tests | 967 | 0 | 2 | 969 |
| Dfo.Server.Application.Tests | 4283 | 2 | 16 | 4301 |
| Dfo.Server.Host.Tests | 165 | 0 | 0 | 165 |

基线已有的 2 个失败，与本任务无关（任务范围外，只记录）：

- `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`

两者都报 `InvalidDataException: Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`，是地下城内容/迷宫生成问题。
