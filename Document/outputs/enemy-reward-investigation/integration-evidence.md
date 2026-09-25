# 当前服务端接入边界（代码证据）

基线：main 4970b691。只读调查，不修改生产代码。

## 已有能力

- `QuestContent.cs` 的 `QuestMonsterRewardItem` 是7字段模型，`QuestDefinition` 只有 MonsterRewardItems；`PvfQuestDefinitionSource.ProjectMonsterRewardItems` 按7字段读入，概率限定0..100。
- `QuestMonsterDropDomainService.DrawAsync` 通过角色/账号凭证读取当前任务和主背包，交给规则服务。对外调用无需传背包快照。
- `QuestMonsterDropRules.DrawMonsterRewards` 依次匹配 monster/dungeon/difficulty；难度目前是精确匹配；count 次 p/100 抽样。库存+本次 planned 与 seeking 所需数量、maxStack 比较，再生成带 Owner、QuestId、GrantLimit 的 DirectQuestItem。
- `ApplyMonsterKillCommand.GrantQuestItemsAsync` 死亡结算时复核队伍、任务仍在身、当前库存及 GrantLimit；通过既有 itemGrants 发放，再同步背包和任务进度。APC 无经验奖励，但该发放步骤本身不要求怪物类型。

## 尚缺

- `DrawEnemyRewards` 是空壳；当前规则编排调用它，但模型尚无 enemy 数据。
- `DungeonCellFactory` 已给 APC 生成 Ground 独立掉落，但 Personal 调用仍在 `if(isMonster)` 内。若 enemy 数据覆盖 APC，必须扩展这个入口，同时禁止旧 monster 行匹配同号 APC。
- 现有任务目标常量 `HuntEnemyTargetTypes` 为 Monster=1、AiCharacter=2、PassiveObject=3；独立掉落表枚举为 Monster=0、APC=1。不能直接强转互用。最终是否复用任务目标枚举取决于原始消费链核对。
- 场景物件目前有房间预声明奖励和拾取链；本轮搜索未发现物件破坏事件进入 QuestEnemyDefeated 的生产调用点。不能仅完成 monster/APC 就声称所有 enemy reward 闭环。
- 两种任务奖励若各自生成，需要共享本次 planned 额度或在统一规则管线中处理；不能各自拿旧库存独立累加。死亡二次限额是保护，不能替代生成语义。

以上描述当前代码，不证明其难度比较、抽样次数或上限语义与70台服原端一致。调查报告必须把原端纠正点和本服选择分开。
