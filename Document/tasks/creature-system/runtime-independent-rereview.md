```yaml
kind: rks-review
verdict: changes_required
```

**发现 1｜清房经验仍可能重复发放。** `AdvanceCoreAsync` 将经验写入事务后，准备清除待结算队列；但在数据库提交与内存参与者提交之间，另一条死亡处理路径仍可调用 `QueueRoomExperience` 改写同一条目。此时内存提交因条目已变化而静默跳过，数据库经验已生效，旧奖励却仍留在队列；下一次调度会再次发放。依据：[队列写入与结算](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Server/src/Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs:45)、[静默跳过](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Server/src/Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs:307)、[事务提交顺序](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs:78)。最小修正：让入队与结算提交互斥，或在提交时合并新入队奖励并确保已发奖励原子移除；增加“数据库提交期间再次入队”的可控交错用例。

旧发现中的**零饱食回城**与**城镇可见范围**，按当前代码和指定用例已在静态层面修正。清房经验的回滚重试、重复事件及换宠用例覆盖了顺序执行情形，但未覆盖上述并发窗口。升级、进化与延迟投影链在本次限定范围内未发现另一项可证实的阻断缺陷；这不等于实机验证通过。孵化领域方法不代表孵化 C2S 已闭合，特殊成长仍应标为未完成。

本轮未运行测试，也未修改文件或操作服务。