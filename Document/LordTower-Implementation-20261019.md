# 领主之塔实现与验证边界

日期：2026-10-19；分支`task/lord-tower-20261019`；对应[#165](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/165)。完整服务端候选实现已通过独立评审并部署，见[部署记录](LordTower-Deployment-20261019.md)。用户原生客户端验收尚未完成。

## 已实现

受管活动2329默认常驻，通过033F投影3505开放与关闭。公共准入头部独立于普通Maze，领主塔专用事务实例支持八阶段、全员扣票、逐怪与终局奖励、独立掉落、地图NPC交易、06:00在线及重登补票；规则和来源见[已接受设计](LordTower-Design-20261019.md)。服务端不修改客户端或PVF。

`Server/src/Dfo.Server.Protocol/Game/BossTowerCommands.cs`中的准入及动作命令携带冻结多成员session/account集合，命令注册为Transactional及账号Exclusive。投影按run gate、调用flow直接安装的selected-session账户租约、命令事务顺序执行；不以异步helper返回租约。`BossTowerProjectionDelivery.cs`、`BossTowerPartyCoordination.cs`及ProtocolModule完成动作、离队、踢人、转移队长、精确断线清理和超时投影。

| 入口 | 当前行为 |
|---|---|
| 0010 | 经已有intent、leader、roster校验后全队Boss准入；库存扣票变化在0333/0334前投影。 |
| 0025 | 全部有效成员加载完成，领域Start提交后才全队001E，不等待Illusion专用009F。 |
| 0027 / 0028 / 002B / 0029 | 专用击杀、死亡、拾取；明确拒绝复活币，不进入普通死亡/虚弱路径。 |
| 002D | 整层清空后接受原生(255,255)标记，兼容nextStage%2,Y0；成功仅0334，随后0025→001E，无需普通CMD成功ACK。 |
| 0048 | event2只ACK；event1结果页leader选择btn0/1/2为重试/别图/回城，全队先ACK。 |
| 000F | btn0/1合法消费旧Result及timer后才进入已有选图投影；0010负责下一次扣票，0048不提前建run。 |
| 002A / 0084、队伍及清理 | 个人退出、队伍变更与精确session清理；不新增虚弱、不退款、不补发旧奖励。 |

四个独立消息切片为`BossTowerInfo`(0333，8B)、`BossTowerMap`(0334，12+14M+18I)、`BossTowerReward`(0335，接收者XP及固定八成员槽)、`BossTowerContinue`(0336，1B)。当前地图没有初始道具，0334的itemCount为0。021F卡片图鉴原消息保持。

结果页仍可转移有效队长，既有receipt和资产不变，新队长取得重试及选图权限。独立加载超时120秒、结果兜底20秒是RuleService策略，均不是战斗限时；btn0/1保留20秒兜底直到合法000F释放。加载超时退出先移除已死亡成员，避免退场的中间状态被误判全灭结算。

BossTowerTicketRefilled经LiveOnly订阅投影到当前精确会话库存；选角发布后重查独立补票周期以覆盖06:00竞态。GameServerStartup在存在PVF时预热BossTowerRuleService.DefinitionAsync并验证阶段池。

## 自动验证

专项输出与TRX在`artifacts/lord-tower-implementation-20261019/protocol/`。统一Release检查、独立评审与部署验证均已通过；用户实机验收仍按下方边界执行。

统一记录在`artifacts/lord-tower-implementation-20261019/full-r2/`：Release构建零警告零错误；8个测试项目共7598项，6712通过、886跳过、零失败（无PVF模式）。同一产物的真实PVF领主塔专项17项全部通过。前端158项及构建、WireSmoke 5项、Release guard 11项通过，记录在`full/`；前端构建保留现有大chunk提示。

首轮全量42项失败保存在`full/failures.json`，修复后对应聚焦检查为142通过/3跳过及Host 7通过，记录在`regression/`。旧夹具建局明确开启事务，合成准入显式注入规则double，精确命令名单与日切记录器适配新增Boss命令；未放宽生产事务或会话检查。Boss预热在装备校验之后、监听之前，保留原有启动失败顺序及资源完整性校验。

| 检查 | 结果 | 原始记录 |
|---|---|---|
| Boss协议codec/实际handler/多人并发 | 20通过 | boss-focused-r4.trx |
| Protocol完整项目，无PVF | 1587通过，60资源跳过 | protocol-regression.trx |
| Boss领域、准入、难度及命令架构 | 49通过，1资源跳过 | app-focused-final.trx |
| Networking完整项目，无PVF | 95通过 | networking-regression-final2.trx |
| 真实TouchSocket合成链 | 002D→0334→0025→001E、0027→0026/0335/0336、0048悬停ACK通过 | boss-wire-r3.trx |

上述服务端合成socket验证不是原生客户端WireCaptured或ClientVerified。新codec和大部分接线测试在实现后加入，作为回归证据，不称为TDD。结果期队长转移用例先记录真实行为失败：`result-leader-red.trx`断言新leader未被同步；移除BossRun中禁止Result同步leader的条件后，`app-focused-final.trx`通过并验证receipt及资产不变。对应命令为`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Result_leadership_transfer`；后续聚焦覆盖包含BossTower/DungeonAdmission/DungeonDifficulty/CommandHandlerArchitecture。

保留的失败记录有明确原因：wire-r2是在测试直接Select但未认证/发布在线角色时被新多会话安全门拒绝，现使用真实注册、登录兑换和角色发布；Networking旧断线夹具直接Begin未开新共享占用要求的事务，现使用测试专用事务命令种入实例。两者均未放宽生产安全检查或增加PVF依赖。

## 原生待验收

首层走门的实机接收日志已定位误拒绝：客户端发送双255，旧实现要求parity唯一坐标。修复及回归说明见[换层修复](LordTower-MoveFix-20261019.md)；这不等同于修复后原生已成功加载第二层。

亡者峡谷入口可见且可点击、首次扣票与加载、八阶段合法走门、逐怪/终局经验及掉落显示、NPC兑换、结果三按钮及重试恰扣一票、零疲劳/退出/全灭无新增虚弱、多人Ready/队长变化/退出/断线，以及06:00在线和重登补票。0333练习语义、0048三按钮和002D成功无ACK已静态闭合；视觉时序及实际客户端消费仍由用户验证。

新增静态证据为`artifacts/lord-tower-implementation-20261019/protocol/a21-*.asm.txt`，来源A21 RVA dump SHA256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`。手工结论进入[0333](../protocol/game/server-to-client/NOTI-0x0333.md)、[0334](../protocol/game/server-to-client/NOTI-0x0334.md)、[0335](../protocol/game/server-to-client/NOTI-0x0335.md)、[0336](../protocol/game/server-to-client/NOTI-0x0336.md)、[0048](../protocol/game/client-to-server/0x0048.md)、[002D](../protocol/game/client-to-server/0x002D.md)，机器生成块不修改。
