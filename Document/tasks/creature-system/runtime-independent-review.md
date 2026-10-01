```yaml
kind: rks-review
verdict: changes_required
```

审阅目标是基线 `b8d2a4dca763ab27ccee391c10e45a66dee18c34` 上的当前未提交改动，包括相关未跟踪文件。依据 `reviewing-work` 的 code 标准、指定契约及 70/A21 证据，只评审本轮稳定范围。发现以下实际缺陷：

1. **P1｜清房经验可能永久漏发。** [MonsterDeathSlice.cs](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:868) 在死亡 claim 固定后，才为队员逐个另起事务发宠物经验；单人路径也在固定后结算（第 582–603 行）。任一结算失败会中断死亡响应；组队重试在第 757–766 行因 claim 已用而直接返回，无法补齐尚未结算的队员。**最小修复：**把清房奖励纳入可重试的结算记录，并在重复死亡报告时补结未完成队员；完成奖励前不要使该房间的奖励机会不可恢复。

2. **P2｜零饱食回城未稳定恢复为 1。** [CreatureRuntimeDomainService.cs](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Server/src/Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs:104) 先按休息时间增加浮点饱食，再仅在结果 `<=0` 时设为 1。战斗中精确归零的宠物回城后，只要下一次调度晚于状态切换，数值便是略大于 0、持久化整数仍为 0，却可能发出复活通知。这不符合 [70 证据](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Document/tasks/creature-system/evidence/tw70.md:17) 的回城保底规则。**最小修复：**在战斗→城镇状态切换时记录并结算零值复活，原子写入 1；用延迟一秒以上的可控时间覆盖该边界。

3. **P2｜城镇宠物生死通知发给不可见角色。** [CreatureRuntimeDelivery.cs](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Server/src/Dfo.Server.Protocol/Game/Creatures/CreatureRuntimeDelivery.cs:32) 直接取同一区域全部成员，未检查发送者和接收者的 `SharesTownPresence`。区域快照确实包含不共享在场信息的成员，而既有城镇投递会筛选该标志。因此这些连接会收到本不应看到的宠物死亡或复活通知。**最小修复：**城镇分支仅在主人共享在场信息时选取同样共享的成员，并保留主人自身为收件人。

本次为只读代码审阅，未修改文件、运行测试、部署或重启。孵化和进化特殊流程未纳入 verdict。