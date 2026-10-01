---
kind: rks-verification
decision: manual_required
---
# 当前完成证据：失败规则已部署，等待最后实机验收

准确声明：**独立失败规则服务、完整损毁产物和原等级追加奖励已实现并部署。2026-09-30 23:44新服务PID40292 ready，允许验收。** 最后用户门尚未通过，不提交、seal或将整根契约标完成/ClientVerified。

TaskContract rev4，基线6e60c4c67417ce06b88e9e99277fb1e4c0239726；44个Server变更指纹a0cda7a88dc3f7d24ec4b8a49bba6ac77aeaf3f5b0bf889ea6ed9a3b88b447dd，17个TASK008增量。初始33文件与上轮approved身份一致，真正未变范围继承旧证据。[本轮完整mixed review](../reviews/change-review-failure.md)第2轮approved，首轮长剑嵌套value误拒与整改均保留。

## 当前条件覆盖

| 契约条件 | 决定性证据与状态 |
|---|---|
| SC1-3 实例/规则/领域、身份/事务寿命、业务兼容 | 未变范围按身份继承；本轮最终Application2383通过，顶层内容修正后关联134通过。新规则只读facts，实例改状态，旧处罚/单奖励入口关闭；NPC行为保持。自动及独立复核通过 |
| SC4 wire/提交后通知/持久化 | 真实SQLite/CommandBus覆盖完整奖励ACK、虚拟358最终余额单条NOTI、commit及中途发奖失败回滚、重放/重试/重读。Protocol1202通过，编码布局保持。新的GUI/重登仍待用户 |
| SC5 可供验收部署/备份 | 第2轮approved后fresh退出/源/包/配置guard通过，备份before-deploy.db，精确停止PID40060并Hidden启动新PID40292。四端口7001/10010/10011/18080均归新PID，bootstrap ready，stderr0；实际PVF路径正确，44源/69包读回无变化。后端通过 |
| SC6 最后用户门 | 当前[F验收清单](../Failure-Human-Test-List.md)，新增失败材料即时显示及正常重登待反馈，原未确认项保留 |
| SC7-9 物资/保护/期限 | 原发放和三PVF期限修正的证据保留。用户随后主动丢弃两强化券并损毁白装；本轮不恢复/重发资金、材料、券或装备。切换前后47业务表逐行完全相同，三客户端PVF SHA保持。后端通过，原未确认人测门保留 |
| SC10 独立失败服务与完整材料 | RED确认无色缺失和痕迹1vs4；70基础分解加原等级追加、保护无产物、全容量规划和资产原子结算自动通过。真实DI只读UID689、白装、稀有和保护及概率穷举通过；新的损毁/重登需用户确认 |

## 最终证据

共同工件根 `E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-reinforcement-failure-20260930`。

- 必要程序集4537通过/0失败/2条件跳过：Application2383、Infrastructure421、Protocol1202、Persistence385、Networking72、Host24、HttpApi50；不相加重复专项、不声称本轮全solution test。Infra两skip是配置真实PVF后不适用的missing-archive场景。局部返修RED2→GREEN14，关联业务134通过。
- `release-root-scope-build.log` 0警告0错误；`publish-final.log` 成功。`delivery-manifest.json`包含44源及69包文件，`fresh-identity-verification.json`读回通过；runtime11个Dfo DLL与Release、Probe共同引用对应一致。
- `actual-di-probe.json`和`probe-readonly-final-check.json`：真实UID689原+10非拒绝，计划10无色+1个3166；白12866计划33无色；稀有10111原+11计划2无色+1个3166+1痕迹；普通/超级保护产物为空、归0/10；真实instance不写，副本与生产47表相同。为规则/DI证据，不是生产损毁提交或GUI证明。
- `deployment.json`、`fresh-runtime-verification.json`、`postcheck-assets.json`：PID40292/ready/四端口/空stderr/正确PVF路径；生产quick_check ok，47表及三PVF保持。生产备份before-deploy.db仅作本轮备份，不自动恢复整库。

部署核验时角色2金币52816456、账户4无色55546、UID689强化10/独立附魔0；普通/超级强化券已不存在，两增幅券各50。后续测试以点击前后实际差值为准，不能机械恢复旧金额或券数量。

## 未验证条件

新服务的材料即时显示、自然失败结果及正常退出重登保留，以及原契约最后结构/保护反馈，由用户按F清单确认。自动及独立通过仅支持当前可验收状态；纯等待操作不建立todo，使用question主动收集。
