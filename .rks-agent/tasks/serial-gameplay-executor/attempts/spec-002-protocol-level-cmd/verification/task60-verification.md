---
kind: rks-verification
decision: pass
---

# TASK-60 验证

结论仅覆盖 T-008 的婚礼／房屋／胶囊十二消息：十个 whole CMD 吸收原编排，十二 message 与十个 fine 闭合。InitializeHousing 仍留选角，AdvanceMarriageWedding 与 CompleteWeddingCleanup 仍留定时来源；没有改根／子契约、Spec、Plan、生产默认或超时。

## 正式结果

fresh Release solution build 零警告、零错误，命令为 `dotnet build Server/86JP.Server.slnx -c Release -m:1`，原始输出 `/private/tmp/task60-build-final-r3.log`。此后产品与测试源码保持不动。最终八项目只采用 `verification/logs/task60-final-r4/<项目>/` 的 run.json、test.log、results.trx；八项实际工具调用均为 require_escalated，元信息逐 run 记录。项目串行，原类并行、5m、detailed、独立 DFO 均保持。

| 项目 | 总数 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|---:|
| Application | 5964 | 5946 | 2 | 16 |
| Protocol | 1790 | 1790 | 0 | 0 |
| Networking | 203 | 203 | 0 | 0 |
| Persistence | 484 | 484 | 0 | 0 |
| Infrastructure | 976 | 974 | 0 | 2 |
| Host | 203 | 203 | 0 | 0 |
| HttpApi | 57 | 57 | 0 | 0 |
| Pvf | 80 | 80 | 0 | 0 |

两项失败与原披露身份完全相符，均为 `InvalidDataException: Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`：

- `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`

原 violations.py 及未改 expected 严格通过：App 61 负例＋1 正对照，Networking 6 负例＋1 正对照，Protocol／Persistence／Infrastructure／Host／HttpApi 各 1 正对照，Pvf 不执行控制；共 67 负例＋7 正对照，outside／exe／orphan 均 0。输出在 `logs/task60-final-r4-main-dfo-<项目>.log`。没有新例外归入已知失败清单。

## 业务与用例

旧树新增 MarriageHousingWireBehaviorTests 十二用例：八 Housing 未婚正常拒绝的 native envelope、住房操作 ACK→槽→房屋投影、Capsule Take 槽→ACK／pending，以及 LovePoint 成功仅 actor 和非法槽拒绝。`task60-old-green-final-r2/Application` 为新增十二＋既有六＝18/18，基准 e41e0fdd。另补正常 SameTown Unavailable 不二次拒绝的回归，在原婚姻入口与原三 fixture 必要窄源上 `task60-old-unavailable-r2/Application` 1/1 绿；仅脚本内临时恢复旧源，finally 全部恢复，没有整树重放。十三个纯旧行为用例补丁为 `/private/tmp/task60-pure-tests.patch`。

App 库存新增 27＝十三旧行为＋十二正式结构绑定＋两个 LovePoint Theory，替换移除原 LovePoint Fact 身份一条，净增 27－1＝26；原业务断言保留，其余七项目零增减。纯领域结果／事务测试由测试侧 MarriageTestCommands 保持真实 Transactional driver，协议路径继续跑真实 whole。Ceremony 原 fixture 的残缺 current-view DTO 明确为无发布视图，保原 ACK／受众／持久断言；真实移动的 TownAreaProjectionDelivery 既有用例仍驱动生产 whole body，Networking 原真实故障用例仅三处作业名文本适配。

Unavailable 以本地 CeremonyAttempt 区分正常业务返回和实际异常：仅首次 Response 的 MarriageWeddingUnavailableException 回滚后独立拒绝，Proposal 与第二拒绝不捕获。双方 Prepare 各自提交，Admissions 全部 Move 后全部 Appearance；Guestbook Read 仍有写事务。Housing／Capsule 只将原特定 action 拒绝 RequireRollback，未扩 WriteResult、Self 或 native Commit→ACK。三效果 whole 仅复用主控批准的 CommittedReplayCurrentStateBatch（phase2-backlog），无首次成功 raw 或新注入类别。

## 诊断证据与限制

首 App full 的旧 LovePoint 编码失败断言未适配 VC-B40，修为编码故障的 typed／原 IOException／精确保护，以及 outbox→IOutboundSender 提交边界受控丢弃 ACK 后的原同 frame／当前 points 字节／不二次扣券两分支；不是实网丢包验收。正常 Unavailable 另经旧 green 后修正来源标志；聚焦四例全绿。Networking r2 202/203 是旧 inbound 作业名令注入未触发，三处名字适配后原三个 socket 用例全绿，真实 EOF／healthy／唯一 Error／SourceConnection 断言未放宽；r2 不作验收结果。

Host r3 在误用 use_default sandbox 下出现子进程启动／退出超时与 AppleCrypto 临时 Keychain 错误，按主控裁定仅停止本轮拥有的进程树，run.json 标记 partial/interrupted/non-acceptance；未改 Host 源码。相同源码升为 require_escalated 的 HTTPS 与子进程代表 3/3 绿，随后八 full 全部在规定权限环境重跑为 r4。r2／r3 全部只作诊断，不混入正式八项。未做 A21 实机、冷 runtime、整树旧重放或专项性能验收。
