---
kind: rks-review
verdict: approved
---

# SAO共享塔一期独立结果审查

日期：2026-10-15。审查分支：`task/next-round-20261013`；HEAD／基线：`0de58d52417b9101052c855b74f6de1b80ca4c7b`。目标为该基线上当前未提交 tracked diff 及本任务新增源码、测试、Document、协议人工区和PVF生成产物；排除无关 `Tool/WireSmoke/__pycache__`。

依据：仓库 `AGENTS.md`、本任务 `TaskContract.md` revision 1、`spec.md`、`plan.md`，以及 reviewing-work 的 Code／Deliverable 标准。独立审查者只写本文件，未改实现、启动服务、操作生产数据库或替换现用PVF。

## 第一轮：阻塞发现（修复前证据，保留）

```yaml
kind: rks-review
verdict: changes_required
```

### F-1 [P2] 玩家离开通关run后，已入包奖励的唯一刷新会被永久丢弃

位置：`Server/src/Dfo.Server.Protocol/Game/SaoTowerProjection.cs:49-51`、`:65-66`。

`SaoTowerRefreshHandler`在组合通知前后都要求玩家仍处于事件的旧run。奖励却已经在死亡事务内持久化（`SaoTowerDomainService.cs:73-78`），且该异步通知是本次新增额外奖励唯一的背包刷新路径。事件以LiveOnly注册（`ProtocolModule.cs:972`）；`DomainEventDispatcher.LiveOnlyAsync`在调用订阅者前先持久标记完成，空结果不会重放。

确定触发顺序：

1. 首队最后一个有效怪物死亡提交，袖珍罐入包并排队 `SaoTowerCleared`。
2. 在订阅者处理前，同一选中会话回城，或者成功进入下一层，当前run变为null或新run。
3. 订阅者随后执行，`:51`直接返回空；若切换发生于读取期间，`:66`同样丢弃已组合消息。
4. 回城路径 `DungeonClear/LeaveDungeonSlice.cs`仅投影离场／城镇；再次入场的 `DungeonEntry/DungeonEntryProjectionDelivery.cs:236-242`仅发送门票扣除delta，均不补发奖励背包快照。奖励持久存在，但客户端看不到新增袖珍罐，直到其他全量背包同步（例如重新登录）。

这影响SC-3的玩家可见派发结果，也使本次通关刷新无法可靠完成。它不是需要假定发送器故障才发生的风险：异步事件与正常离场／再次入场即可形成上述顺序。

最小修正：将“当前角色已提交奖励的背包真源刷新”与“仅适合旧run的层数提示”分开。保持精确选中会话校验；即使run已变，当前背包快照也应获得可达的同步路径。或者把奖励回执纳入死亡提交后的有序客户端投影。不要仅移除所有会话校验后发送历史快照。补一个延迟订阅者、先结束旧run／进入新run再处理事件的回归，断言奖励格仍被同步；保留替换会话拒绝行为。

## 已核对的关键边界

- 真实死亡接线：`MonsterDeathDomainService`只在有效death claim将清空Boss房间时调用SAO完成；`ProcessMonsterDeathCommand`声明全体参与账号与选中会话，走同一个事务。SAO再核对当前run、claim、清图结果及当前成员集合，没有以客户端结算请求直接授予首通。
- 当前成员：生产 `DungeonRunDomainService.DungeonRunAggregate.Participants`从当前party及ActiveParticipants构造；本次SAO成员数量／账号校验对的是当前集合，未发现给已离场旧成员发奖的新增路径。
- 唯一首通与100层循环：Store以season／round／floor／revision作CAS，run_id唯一，首通主键约束同轮同层；100层回到下一轮1层。旧快照与重放无法再次命中CAS。
- 原子性：进度、首通、逐成员领取记录、ItemDelivery和领域事件都通过同一个ambient SQLite participant；任一发奖异常要求事务回滚。既有ItemDelivery的InventoryThenMail接缝承接满包路径。未发现本次新增独立连接提交。
- 入场：共享进度重映射实际DGN，Layout冻结进度；费用仍由公共Admission收口，失败要求回滚；无疲劳、没有新增每日3次限制。再次挑战的特殊匹配限定在SAO目录支持的ID集合。
- schema：仅增加三个新表及约束，未修改既有角色／绝望塔／配额表形状。
- 03E9：两u32编码与静态读取顺序一致，登录／选图已有接线；TryRound映射和发送时机明确列为待实机，没有宣称WireCaptured／ClientVerified。

## 内容与声明核对

读取 `sync-manifest-v2.json`、每阶段工具实现、原始到final核对日志、内容说明和25／55层修复依据。独立重新计算五份Source、Output、Backup的SHA-256，全部匹配清单；Source与Backup一致。五份均 `Deployed=false`，本审查不将其当作同步完成证据。

补丁工具限定SAO路径的历史固定expiration声明，保留其他token；25／55层仅在已知旧尺寸、单BB、出生／Boss格均0,0匹配时将宽度改为1。生成器逐项核验目标字节及非目标文件表项，并校验旧body chunk与名称／哈希区保持一致。原始→final核对日志记载server／protocol2为100个目标项，其余三份为102个目标项，分别保留711910／711908项；v2清单与说明一致。

名称调查保留“100层DGN直接名称无dnf占位、第一层MAP另有PVP无名、缺少用户确切key”的证据边界。绿色原生卡面未列为坏素材；03EA、每日补票等明确在一期外，未据此扩张审查范围。

## 验证边界与结论

本次独立运行 `git diff --check` 无输出，复核产物哈希；为避免与主代理占用相同构建输出，未重复构建／执行测试。已阅读新增Store／Domain／Content／Payload测试及既有MonsterDeath事务测试，主代理提供的targeted26通过仅作为验证线索，不冒充独立复跑。SAO通知尚无覆盖F-1时序的回归。

结论为 **changes_required**，唯一阻塞项F-1。修复后重审该路径及直接回归即可；随后仍需主代理完成必要回归、按已授权范围同步PVF并记录实际同步回证。以上代码和档案检查不替代任务列出的真实客户端验收。

## 第二轮：F-1修复复审

```yaml
kind: rks-review
verdict: approved
```

复审目标仍为同一HEAD上的当前未提交实现；范围仅F-1修正、新增 `SaoTowerNotificationTests`、`IllusionTowerDomainServiceTests`的测试夹具隔离及直接相关说明。第一轮其他检查保持有效，PVF v2产物未变，本轮不重新宣称内容同步完成。

**F-1：resolved。** `SaoTowerProjection.cs:48-50`仅用当前角色／精确会话拒绝无效接收者，不再因旧run结束或替换而提前丢弃背包刷新；`:60-66`从当前库存真源读取并组合完整容器快照。`:54-59`将层数提示限制在城镇或原run，`:68-69`在组合期间发生run变化时只移除层数消息。`:70`仍做最终精确选中会话校验。因此第一轮“run变化使奖励库存帧必然变为空”的确定故障路径已经消除，没有发现该修复引入的阻塞回归。Document的一期说明同步改为上述行为。

新增 `SaoTowerNotificationTests.Delayed_first_clear_event_refreshes_inventory_after_the_old_run_has_ended`用真实PVF和SQLite角色／库存读取组合通知，断言当前无run时仍返回InventoryFullSnapshotMessage，能让旧版`:51`的提前返回失败。主代理报告先RED后GREEN；本轮独立检查其源码，未复跑。准确证据边界：测试直接构造通知并从无run状态调用Compose，没有真的创建／结束旧run或走完整事件dispatcher，也未覆盖组合期间切run；它回归保护的是F-1的已确认无run分支，不当作整个异步事件生命周期的验证。

`IllusionTowerDomainServiceTests.cs:1216-1217`仅在既有合成Fixture中注册NoAltarContent／NoTournamentContent；`:1226-1237`提供本地固定内容，避免返程配额投影意外触及真实PVF。注册放在调用方configure之前，保留显式测试覆写能力，未修改生产内容源、规则或入口。该改动与主代理所报no-PVF两个夹具失败直接相关，没有发现扩大产品行为的变更。

本轮 `git diff --check` 无输出。为避免同输出构建竞争，仍未独立构建或复跑测试；已存在的no-PVF TRX是修正前2失败快照，不能据其宣称全量已通过。新测试和夹具复跑结果由主代理在最终completion evidence收口，不影响本轮代码审查对F-1修复的判定。

当前结论为 **approved**：审查范围内无未解决阻塞发现。此结论是实现／已准备内容产物的审查通过，不是PVF已部署或真实客户端已验收；后续同步回证、最终必要回归及实机边界仍按任务计划记录。

## 最终同步与新增验证证据

本节是第二轮之后的证据更新，产品源码未变，当前审查结论保持 **approved**。

独立读取 `artifacts/sao-core-20261015/content-sync-receipt.json`（At=`2026-10-15T04:58:27.7009117+00:00`，AllSynced=true），并重新计算其中五个Target现用文件及五个Backup的SHA-256。五个Target均同时匹配receipt的ActualSha256／ExpectedSha256和v2 manifest的OutputSha256；五个Backup均匹配各自OriginalSha256。由此确认四个开发client和配置使用的server PVF文件已经落地，第一／二轮“仅准备、尚未同步”的描述为当时历史状态。

| Id | 独立复核现用Target SHA-256 |
|---|---|
| server | FF883D739DB35EE552F5EFABFE0CEE99849CB6E053C4FEAA79FB08ADA0B3192A |
| client | 0B75CEC45068F7C01F68EACCC6F44D85E24A7A3B2E441C9DA33FCBF4C85492BE |
| debug | AC02923435FE13261200D08496207BA3FE9020017771F4FCFC6034DCD9200068 |
| protocol | 0B75CEC45068F7C01F68EACCC6F44D85E24A7A3B2E441C9DA33FCBF4C85492BE |
| protocol2 | 950E98959C15B79BCD820C736DF40CE62C47EAB4275BF3C28F3D76FEBFA2981F |

receipt记录ServerStarted=false、DatabaseModified=false；本次只核对PVF现用文件和备份，未独立把此记录升级为生产进程／数据库前后快照比对证据，也未执行任何服务或生产库操作。

阅读 `SaoTowerDomainTests.Real_entry_resolves_shared_floor_charges_one_ticket_and_rollback_preserves_inventory`新增末段：在真实PVF创建的层2副本中找到最后有效清图claim，通过真实SAO领域服务和ItemDelivery提交首通，断言现有持久容器中10004331数量为1、领取记录为1、共享NextFloor=3。这新增了真实物品内容、库存和共享进度联通证据；它直接调用CompleteClearAsync，不声称是完整MonsterDeath网络handler或真实客户端验收。

独立读取主代理生成的TRX结果（未重复执行测试）：

- `tests/sao-core-targeted-final.trx`：13执行、13通过、0失败；包含上述真实入场／奖励用例、100层创建、Store竞争／循环和F-1通知回归，均Passed。
- `tests/sao-core-no-pvf.trx`：总3817，执行3424、通过3424、0失败，393未执行；此文件已更新，取代第二轮引用的旧2失败快照。
- `tests/sao-core-real-pvf.trx`：3895执行，3894通过、1失败；失败身份为 `EffectOperationReplayTests.An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus`，异常TimeoutException。
- `tests/sao-effect-replay-recheck.trx`：该class隔离22执行、22通过。隔离通过不抹去当时全量失败；该历史失败随后由下述最终全量复跑收口。

再次 `git diff --check` 无输出。以上同步和测试证据未产生新的阻塞发现；批准边界仍不包含玩家实机、03EA专用翻牌或每日补票。

### 最终真实PVF全量收口

独立读取 `artifacts/sao-core-20261015/tests/sao-core-real-pvf-final.trx` 和 `real-pvf-final-test.log`：该次Dfo.Server.Application.Tests全量运行于2026-10-15 13:00:36至13:02:18（+08:00），总3895、执行3895、通过3895、失败0、跳过0，日志与TRX一致。以上为对主代理运行产物的独立核对，不是审查者重新执行测试。

因此真实PVF全量当前状态为通过，不再待复跑；先前EffectOperationReplay的超时及22/22隔离复核仍保留为历史证据。产品源码未变，Document/SaoTowerContent新增实际同步表的五份哈希与本审查前述独立现用文件核验一致。当前审查结论继续为 **approved**，实机／03EA／每日补票边界不变。

### SchemaOwnership回归期望同步

只读核对 `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs:58-61`新增四行：表数期望由78改为81，等于既有78表加本次新增的3表；同时对sao_tower_progress、sao_first_clears、sao_first_clear_rewards分别断言完整列名／SQLite类型形状。这三个形状与 `CharacterDatabaseSchema.cs:232-234`定义一致，并遵循既有ShapeAsync排序口径。改动保持全部原有表形状断言，没有以降低断言或只调计数掩盖结构差异。

此次仅同步测试对已审查schema的期望，不改变产品代码或生产数据库，未发现新的阻塞问题；审查结论仍为 **approved**。Persistence全量复跑及Infrastructure结果由主代理最终verification记录，本次未将尚未提供的复跑结果作为独立核验通过。
