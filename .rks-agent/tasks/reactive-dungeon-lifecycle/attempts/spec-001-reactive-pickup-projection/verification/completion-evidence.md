---
kind: rks-completion-evidence
state: verified
---

# Completion Evidence: 响应式拾取投影

## Success-condition Matrix

| Condition | Result | Evidence |
|---|---|---|
| C1-SC-1 | pass | A21 静态控制流记录表明 0x0027 观察者分支关联队伍成员与地面物，不触发角色动作；协议文档已明确服务端不伪造 P2P 动作。 |
| C1-SC-2 | pass | `DungeonRunDomainServiceTests` 验证一次 commit 产生一个递增 revision，并冻结当前 active participants；Protocol 测试验证单一变更的逐接收者投影。 |
| C1-SC-3 | pass | GroundPickup 物品、怪物掉落、金币与重复拾取测试共 24 项通过；奖励仍只进入拾取者。 |
| C1-SC-4 | pass | replacement picker、departed picker、foreign Run 三个新增测试通过；观察者同样过滤失效连接。 |
| C1-SC-5 | pass | `dotnet build Server/86JP.Server.slnx -c Release --no-restore`：0 warning、0 error；受影响定向测试全部通过。 |

## Fresh Commands

```text
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~GetItemHandlerTests"
passed 6, failed 0

dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~GroundPickup"
passed 24, failed 0

dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DungeonRunDomainServiceTests"
passed 36, failed 0

dotnet build Server/86JP.Server.slnx -c Release --no-restore
0 warnings, 0 errors

git diff --check
passed
```

## Broader-suite Note

`dotnet test Server/86JP.Server.slnx -c Release --no-restore` 的受影响项目及绝大多数项目通过；未改动的 `Dfo.Server.Host.Tests` 中 `InvalidLauncherConfigurationFailsBeforeAnyListenerStarts` 在并行拉起多个 Host 子进程时有 20 秒随机超时（复跑时失败参数发生变化），与本次差异没有文件或调用路径交集。该基线/环境性失败不削弱本子契约要求的受影响测试与 Release 构建证据。

