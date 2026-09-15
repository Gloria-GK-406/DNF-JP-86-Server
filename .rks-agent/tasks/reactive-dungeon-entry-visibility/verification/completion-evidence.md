---
kind: rks-verification
decision: pass
claim: 响应式地下城入口可见性候选结果达到双客户端实机验收条件
contract:
  path: ../TaskContract.md
  revision: 1
---

# Completion Evidence

## Success Conditions

| Condition | Result | Fresh evidence |
|---|---|---|
| SC-1 | Pass | 队伍入口协议测试分别断言两名接收者的 receiver-first 成员状态、完整成员集合、WireUserId、稳定槽位、UDP host 槽位和各自 `START_MAP participantSlot`。 |
| SC-2 | Pass | 领域与投影测试覆盖两种加载顺序、重复提交、错误 run/修订、旧 revision 重读最新快照，以及同 run 较新修订不会被旧失败清理。 |
| SC-3 | Pass | `SelectDungeonHandler` 和 `FinishLoadingHandler` 均只委托共享 `Game.Dungeons` 协调层；领域参与者不含 connection、session 或 wire 身份。独立变更审查最终 verdict 为 `approved`。 |
| SC-4 | Pass | 测试覆盖投影前 replacement、CMD 执行期间 replacement、LoadingComplete 回调期间 replacement、成员离开与新 run 替换。 |
| SC-5 | Pass | 单人和队伍选图回归通过；单人最新快照保持 `Participant=null` 哨兵语义；城镇离开只发生在全部 required delivery 成功之后。 |
| SC-6 | Pass | 当前候选树全量测试 3285 通过、2 项既有条件测试跳过、0 失败；最终构建与 diff 检查见下。 |

## Fresh Commands

### Full solution tests

```powershell
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-protocol\Script.pvf'
dotnet test Server/86JP.Server.slnx --no-restore --logger "console;verbosity=minimal"
```

结果：退出码 0。`Dfo.Pvf.Tests` 60、`Dfo.Launcher.Tests` 63、`Dfo.Server.Persistence.Tests` 353、`Dfo.Server.LauncherApi.Tests` 50、`Dfo.Server.Infrastructure.Tests` 312、`Dfo.Server.Protocol.Tests` 900、`Dfo.Server.Networking.Tests` 65、`Dfo.Server.Application.Tests` 1465、`Dfo.Server.Host.Tests` 17，合计 3285 通过；Infrastructure 中 2 项既有“缺少 archive”条件测试按设计跳过。

### Final build and patch integrity

最终构建要求：退出码 0，0 warnings，0 errors。

最终补丁要求：`git diff --check` 与暂存后的 `git diff --cached --check` 均无输出并返回 0。

## Boundary

该证据不等价于客户端实机验证。合并、部署和双客户端互相可见验收不在本契约授权范围内，结果尚不标记为 `ClientVerified`。
