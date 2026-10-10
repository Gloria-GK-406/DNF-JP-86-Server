# 完成证据

2026-10-10，独立审查批准后定向验证通过。实现完成；客户端验收待后续部署。当前五个受影响项目共6399个不同测试用例通过，另有2个已在基线reader隔离复现的计数断言失败。未宣称完整Solution全绿。

| 契约 | 证据 | 结论 |
|---|---|---|
| SC-1 | final-mirror-protocol.trx 61通过；final-mirror-domain.trx 94通过；final-mirror-content.trx 4通过 | 原始02B6向量、专用拒绝重试、0278顺序、无重复ACK与真实32/14299规则通过 |
| SC-2 | MirrorAradQuestTests；真实SQLite schema/跨表回滚 | 当前run授权、五类目标、直接权威通关补算、领奖幂等及失败回滚通过 |
| SC-3 | Committed_departure_is_consumed_by_the_registered_reliable_subscriber_without_touching_new_run | 真实注册/Lifecycle/EventService/Dispatcher可靠清理、旧局重复事实不误删新局通过 |
| SC-4 | full-Application/Protocol/Host；final-Persistence；full-Infrastructure | 普通任务、循环、共享地下城与组合根回归通过；Infra两个既有资源总数失败单列 |
| SC-5 | spec-result.md 与02B6/0278协议人工区 | 明确未部署/未实机、残余数值行为与待测试流程 |

完整TRX的路径、SHA与计数见 evidence-manifest.md。真实PVF路径 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，配置PVF的缺资源测试跳过，另移除PVF_ARCHIVE_PATH执行 missing-archive.trx，2/2通过。

## 复跑

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~MirrorAradQuestTests|FullyQualifiedName~MirrorDungeonEntryTests|FullyQualifiedName~DungeonVisitDomainServiceTests|FullyQualifiedName~DungeonStructureRuleServiceTests|FullyQualifiedName~CycleDungeonContentTests|FullyQualifiedName~QuestProgressDomainServiceTests'
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --filter 'FullyQualifiedName~MirrorDungeonEntryCodecTests|FullyQualifiedName~SelectDungeonHandlerTests'
dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --filter 'FullyQualifiedName~PvfMirrorDungeonTests|FullyQualifiedName~PvfDungeonMapDefinitionSourceTests|FullyQualifiedName~PvfCycleDungeonTests'
```

## 基线失败对照方法

将 `git show b9717c65:Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs` 复制为测试程序集内临时 BaselineProbe 类型，只重命名类型以便并存；复制两个原断言夹具，使用相同当前PVF。两项都复现预期2814/实际3252，见 baseline-pvf-counts.trx/log。临时三个cs已删除。没有运行完整基线套件，不能据此概括其他基线行为。
