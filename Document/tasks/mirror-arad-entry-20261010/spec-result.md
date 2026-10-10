# 镜像阿拉德实现结果

2026-10-10；基线 `b9717c65968fb15fe013a7c46e6b2c045ac931be`；分支 `task/monster-declaration-parser`。仅本地实现与验证，未部署、未改客户端、未写运行数据库。

## 结构与职责变化

| 重构前 | 当前实现 |
|---|---|
| 02B6未处理，8000没有可进入迷宫 | 专用Mirror入口服务校验PVF历史DGN/任务配对，复用共享入场事务和地图加载 |
| 普通Quest以永久active/cleared为模型，不能承载当局可重复任务 | 独立MirrorAradQuest服务按角色+RunId保存当局状态；普通Quest调用链保留 |
| 地下城离开没有供任务组件消费的事实 | 地下城发布DungeonParticipantLeft；任务组件Reliable订阅清理对应局 |
| 任务刷新可能覆盖临时任务 | 既有快照合并当前镜像任务；MirrorQuestChanged经EventBus投影，消费时读当前快照 |

本次只迁移镜像任务生命周期，没有将普通Quest全面事件化。地下城生产离场事实，不判断任务删除规则。

## 已完成行为

- CMD02B6读取两个u32，合法当前选图/队长/配对/区域/等级/前置及日限校验。失败回同opcode专用错误，不借用0010的错误表。
- 成功先NOTI0278(u32任务)，再共用地图加载；不回会触发000F/0010重复入场的成功ACK。
- 本局001F接取，击杀、通关/不复活、限时、已清房间数和MAP目标依据权威实例事实推进；客户端trigger不能直接推进。
- 接取重放不重置；领奖marker、物品和每日次数同事务，重复领奖不再发物品；放弃保留本局tombstone，不能重新领奖。
- 离场后旧任务立即失去授权；可靠清理按精确RunId执行，延迟/重复事件不删新局。再次抽中相同任务可重新接取。
- 真实PVF读取393个合法配对、每日10次入场/5次任务奖励、等级奖励与第5次奖励。任务领奖次数与入场次数分别记录。
- 历史MAP起点标签别名与单向路线预检查修正，实际移动仍校验入口；普通、循环及普通Quest回归保留。

## 验证

独立审查 approved。完整项目回归：Application3495通过、Protocol1426通过、Persistence修复Schema断言后468通过、Host156通过。Infrastructure852通过、2个既有计数失败、2个缺PVF测试在配置真实PVF时跳过；另在无PVF环境运行这两项，2/2通过。

审查后定向新鲜验证：领域94/94、协议61/61、真实PVF镜像/地图/循环4/4，全部无跳过。不同运行之间有重叠，不能将次数累加为新增测试数量。

既有失败：`PvfQuestEnemyRewardTests.A21_APC_object_and_repeated_candidate_declarations_are_preserved` 与 `PvfQuestDefinitionSourceTests.Projects_every_neutral_eligibility_input_from_the_real_archive`，预期2814任务、当前资源实际3252。用b9717c65原始reader隔离重跑，两个用例同样失败；未为了全绿修改这些资源快照断言。[证据清单](verification/evidence-manifest.md)、[契约覆盖](verification/completion-evidence.md)、[独立审查](reviews/change-review.md)。

## 未覆盖与待实机

历史DGN怪物尚未使用镜像包装图的角色等级缩放/revision table；疲劳、材料仍沿用实际历史图规则，不宣称完全复刻上游镜像数值。当前catalog没有ClearMap候选，MAP-only支持与上游DGN兜底的兼容差异已记录。未逐一实跑所有历史迷宫。

部署后验证：打开镜像入口→入场及自动接任务→完成/领奖→返回城镇清理→再入场；另测失败重试、30条普通任务时显示、重复领奖与断线重入。协议字段/回包为ClientStatic和CurrentImplementation，当前未记为ClientVerified。
