# TASK-38 — 队伍建立、设置与查询协议归并

本 Task 承接 Plan P7 T-006，先迁不依赖两塔协调器的五条队伍入口，稳定共用投影能力。对应消息65538／65546／65547／65548／65702与 RequestPartyInviteCommand、AcceptPartyInviteCommand、SetPartyInfoCommand、QueryPartyRealtimeCommand 四条旧 Direct 命令。前一 TASK-37（`dd7e96ec`）已收口 T-005；退队、踢人、换队长确有两塔共享调用链，留同簇下一单元处理，不提前闭其行，无目标偏离。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-37（已提交）；覆盖 RQ-001／003／006／007／009／014，AC-001／003～005。
- Outcome: SetUdpIpPort、RequestPeer、ResponsePeer、SetPartyInfo、CallPartyMemberRealtimeInfo 五完整协议 CMD 与薄 handler，四旧 fine 与其注册／转发删除；旧队伍状态、拒绝、精确受众、投影帧序和水位保持。
- Boundary: Party 切片及其共用投影／真实状态拥有者的必要接缝、接线、正式测试、九条 ledger 行、AGENTS 清单和本 Task。旧直接能力不凭迁移新增事务／Main／幂等；不改执行器、runner、领域规则或 raw／恢复权限，不提前迁两塔与城镇用例。
- Intentional change: 默认保持原可见行为；若正式旧新对照发现 whole 生命周期带来的实际差异，按 reset 在 Spec 变化表加一行并随本 Task 审，不修订 Spec／Plan。中心方案或验收边界变化先返主控。
- Result review: required；一次 sol6.1 执行、一次 Astra 独立快审，不做挑战。

## Detailed Plan

1. 先补／复用正式旧入口表征，经实际 decoder／dispatcher／注册投影核邀请→接受→设置→单人查询顺序、前选角 UDP 观测、非队伍请求／缺会话／陈旧目标无领域写、精确受众与256B标题边界。保既有 Party handler／notification／town／characterization 强断言；需要迁测试驱动时保持行为保护，不以新整CMD入队次数代替“无领域写”。对真实 encoder／投影失败补旧 oracle，涉及关闭则连生产 sender 到实际 transport 观察。
2. 共用 PartyNotificationDelivery 的具名编排由 CMD 吸收或调用参数式纯投影帮助器，不让新 CMD 注入旧 Delivery、IEventService、sender、bus、registry 或 locator。保单一 PartyDeliveryStream 真源、generation／revision／FullStateMembers／墓碑，以及 XO-6 required 阶段和仅失败精确收件人最多两次重试。必要时用 Abstractions 具名领域状态契约暴露原状态拥有者／同一实例，不能把含发送编排的旧 Delivery 换后缀藏入领域门面。旧未迁消费者通过窄适配器继续用同一算法／状态，不复制两套循环或提前改城镇语义。
3. 五 whole 直接调用原领域能力；UDP 保认证后且选角前可保存和 TCP 地址归一，邀请展示仍来自 memberStatus，接受／设置保原捕获时点，查询仅回请求者且不推进完整状态水位。handler 只交精确 Dispatch／消息入队；请求身份显式，保纯 parser／encoder 不变。删四旧 Direct 类型及全部生产引用，保其余三队伍 fine／两塔调用可用。
4. 可独立的纯共享抽取先在旧入口正式测试验证，准备可单独提交的 patch（仅本机），随后迁 whole；纯搬迁与语义分开检查点。无独立抽取时 Result 写清不可分原因，不把临时源码副本或原始日志提交。
5. 冻结后 Release build、相关聚焦以及 Application／Protocol／Networking 三项目 full；若触及其他项目实际运行面则补对应 full。保原类并行／5m，require_escalated＋console detailed＋DFO，准确披露两 Dimension 基线及任何新失败；涉及真实 PVF 的新增测试采用已有前置属性。本 Task 尚非 T-006 收簇，八项目在剩余三协议闭合时跑。
6. 只闭五消息／四旧命令九行，original／非目标不变，strict validate／selftest，AGENTS只增清单。一页 Result交独立快审；主控提交后继续同簇退队／踢人／换队长及两塔共用协调链。

## Result

Outcome: done

**交付与选择。** 五唯一 whole／薄 handler、四旧 Direct fine 及生产引用删除；不增事务、Main、幂等或恢复许可，无有意可见变化。具名编排归 `PartyProtocolCommandProjection` CMD 共享基类，legacy adapter 共用同一算法；归一签名／能力参数后与 HEAD 原算法相同。Networking 的状态契约 alias 原 registry／同一 live stream，保纯 Parties 边界、FullState 与墓碑。AGENTS 只加清单，仅闭九 ledger 行。

**旧新证据。** `verification/logs/task38-before/pure-preparation.patch` 为可独立应用 `dd7e96ec` 的八文件纯准备补丁；真实恢复旧 route 后 build **0警告／0错误**，Protocol **113/113**、Networking **11/11**、App 队伍／架构／写入口表 **90/90**（`prep-entry-fixed-*` 日志）。同正式 decoder／dispatcher／actual encoder／production sender wire oracle 覆盖 UDP 已登录未选角存储与 TCP 归一、非队伍无状态／通知、邀请接受／请求者单播、失败精确目标一错重试／二错停止后续且水位不进、健康 socket；保原256B标题／队伍／两塔表征。新补双向 old/new FullState／水位／墓碑及缺 selected 无领域写。首次全量遗漏 StreamOf 分类，补自身写断言＋W 行后重新冻结、全量重跑，未放宽判据。

**最终检查。** `dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1` **0/0**；本机项目串行 `DFO_GAMEPLAY_ASSERTIONS=<独立目录> dotnet test <csproj> -c Release --no-build --no-restore -m:1 --blame-hang-timeout 5m --logger 'console;verbosity=detailed'`，保原 xUnit 并行。聚焦加 `--filter`，**90＋115＋11 全过**；R2 full：

| 项目 | 总数 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|---:|
| Application | 5395 | 5377 | 2 | 16 |
| Protocol | 1781 | 1781 | 0 | 0 |
| Networking | 203 | 203 | 0 | 0 |

App 仅原 `Dfo.Server.Application.Tests.World.DimensionDungeonTests` 的 `Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`。三项目 **7379＝7361通过＋2失败＋16跳过**；相对 TASK37 display/status/重数 **N=8／D=2**（两项仅重命名，净增六）。其余五项目1800为 TASK37 库存，合计 **9179＝9159通过＋2失败＋18跳过**，不是本轮八 fresh full。`verification/logs/task38-final-r2/` 保存命令／TRX／结果；新鲜三程序集 DFO **70＝原67反例＋3 controls**，outside／exe／无归属0，其余四 control 沿用 TASK37。strict／八坏例 selftest、九行／全部 original／非目标比对及 diff-check通过。

**遗留。** 交一次 fresh Astra 快审；退队／踢人／换队长及两塔链留同簇下一 Task，本 Task 不收 T-006；两 Dimension 与 A21 实机边界未解。未提交／推送／PR／部署／清理，产品测试停写。

```yaml
kind: rks-verification
decision: pass
```

## TASK-38 Independent Quick Review R1

**approved** — 本 Task 局部结果快审通过，未发现需返工的实质缺陷。

```yaml
kind: rks-review
verdict: approved
```

目标：`task/refactor-session-20261021`、`dd7e96ec` 上 TASK-38 未提交差异及最终 R2 证据；合同 r1、Spec B6 的 RQ-003／006／007／009／014、Plan P7 T-006。按 2026-10-25 process-reset 与 reviewing-work 的 Task Unit／Code 准则独立检查；不生成挑战，不代表 T-006 或整个 Spec 完成。

- 五消息各有唯一 whole 与薄 handler，四旧 fine 类型、生产调用和注册已删除；原邀请展示读取、接受／设置捕获时点、查询单播及 UDP 已认证未选角／TCP 地址归一保持。未新增事务、Main、幂等、raw、恢复或 core 权限。
- 直接比对旧 `PartyNotificationDelivery` 与新 CMD 基类：归一方法名、状态访问及能力参数后算法逐行一致；八文件纯准备 patch 中算法亦相同，仍走原路由。最终是 CMD 继承具名阶段，legacy adapter 共用算法；Networking 状态契约 alias 原 registry／同一 live stream，未让 Parties 接受 SessionIdentity。`StreamOf` 自身 Writing 断言与 W 表项齐全。
- XO-6 Required 顺序、仅失败精确目标最多两次尝试、FullState／水位／墓碑及请求者查询不推进水位均保留。核实际 encoder 注错与生产 sender 真实 wire 用例：一次错只重试失败目标并继续、两次错停止后续且水位不进，连接保持健康；核双向新旧状态共享、缺 selected／非 party 无领域写。原 party／两塔强断言未削弱。
- 亲读旧 route `prep-entry-fixed-*` 日志：build 0警告／0错误，App 90、Protocol 113、Networking 11 全过。亲读最终 TRX：聚焦 90／115／11 全过；full App 5395＝5377通过＋Result 所列两项 Dimension 失败＋16跳过，Protocol 1781、Networking 203 全过。其余五项目仅沿用 TASK-37 库存，不是本轮八项目 fresh；DFO 三程序集 70 条均为原反例／control，范围外、exe、无归属均为0。
- 独立解析账本确认只改五消息／四 fine 九行，全部 original、非目标行与其他组不变；strict 与八坏例 selftest 日志通过，`git diff --check` 通过。

遗留按 Result：两 Dimension 内容失败、A21 实机边界，以及退队／踢人／换队长和两塔共享链；由主控消费本评审，并按已准备的独立纯抽取与 whole 两个检查点提交。评审仅追加本节，未修改产品／测试或提交。

## Controller Consumption R1

接受独立快审 R1，TASK-38 结果通过。主控亲读实际差异、最终三项目 TRX、DFO 与纯准备 patch；重新运行 strict／八坏例 selftest，并独立比对确认仅五消息／四命令九行改变、全部 original 与非目标保持。纯准备旧路已独立提交 `59b2f37e`；随后提交五 whole 迁移及本记录，最终受测产品／测试不因分段提交改变。没有有意可见变化，不修订 Spec／Plan，不新增权限。此时闭合68/159业务消息、55/204旧命令、1/73来源，5保护／8过渡仍待收口；T-006尚未完成，继续剩余三条队伍协议及两塔必要共享链。
