---
kind: rks-review
verdict: approved
---
# 装备实例、字段修复与强化保护券：rev2独立完整结果审查

## 目标与判决

2026-09-30，由生产者之外的新鲜独立reviewer执行complete mixed result review，未再委派。应用`reviewing-work/references/code.md`与`references/deliverable.md`，依据本仓AGENTS.md、[TaskContract revision2](../TaskContract.md)、[Spec](../spec.md)、[Plan](../plan.md)、`sdd-execution.yaml`及TASK-005；该gate不要求单独challenge set。

目标工作树：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`；HEAD/基线`6e60c4c67417ce06b88e9e99277fb1e4c0239726`，未提交。完整33个Server变更/新增/删除文件集合指纹为`5c90145124ed6e703b514505c21b569524ff29e9a74e818131a04045cc4dd087`。reviewer独立从Git取得完整集合、逐文件计算SHA256、按排序compact JSON重算集合指纹，与主仓库`artifacts/equipment-protection-20260930/current-manifest.json`一致，无遗漏。仓库无`.codegraph/`。

**approved**：当前完整目标的代码、自动证据和可验收交付物未发现未解决的阻断缺陷，可以进入rev2最后人工验收。本批准不证明SC-6、SC-7登录可见或SC-8实机保护失败/重登通过，不授权提前提交、seal或标ClientVerified。

与TASK-004部署manifest独立比较，只有三个测试文件变化：强化DomainTests、HandlerTests、PVF强化DefinitionSourceTests；产品代码没有增量。未变身份、规则迁移、强化低5位映射、跨功能writer与golden基线的完整审查沿用[字段修复独立审查](change-review-field-fix.md)，并由本次逐文件身份复核衔接；旧批准不能批准新增保护范围。本次重新检查当前规则/领域/实例/handler/notification/PVF内容源、三测试差分及其直接相互作用，独立核对新增发放与当前人工清单。

## 决定性检查

| 检查面 | 当前实际证据与判断 |
|---|---|
| 规则与保护资格 | PVF source仅识别`protect equipment`与`new protect equipment 10`；真实7804/10007840既有断言保持，两种增幅券新增null断言。prepared rule依据原处罚判断可破坏，超级仅当前等级大于保留级时适用；共存先选超级，再按空间/槽稳定选取。成功、保级及普通降级不扣券。费用、概率和未证wire均未改变。 |
| 资产边界与实例 | domain自动只扫描本角色QuickSlot/Material，过滤到期实例；显式券槽拒绝错action、过期、非携带、缺失及与目标/材料冲突。越权测试读取另一角色真实库存。券只在ConsumeProtection决定成立时进入材料成本；装备仍由UID实例写Attribute低5位，保留高3位、独立附魔次数及其他core字段。 |
| 确定性结果与身份 | 新普通回0/超级回10、两种跨空间优先和+10超级不适用用例直接核对SQLite结果；比较完整82字节core，仅预期byte9变化，核对UID及Clear/重新Load结果。期限0/未来/到期/过期、错增幅券、成功/非破坏不扣和非法槽的用例断言资源或装备结果，支持相应拒绝/消耗语义。 |
| 提交后即时与重读 | HandlerTests使用实际CommandBus、SQLite和注册encoder；BeforeSend读取已经提交的金币/材料/券/装备。两种券分别覆盖1张清空和2张余量，NOTI0E真实entry字节、低5位和独立附魔字段与重读的完整NOTI0D相符；账号虚拟无色槽358核对已提交账户余额。Completed按receipt.Changes投影实际自动消费槽。 |
| 失败与重放 | 强化core UPDATE触发器在SQLite flush时ABORT，测试证明券、材料、金币与装备整体回滚，无成功通知，移除触发器后原sequence可重试。两券同连接/同sequence再送只补ACK，券/材料/金币不再扣；没有以本次范围扩展库存通知重投契约。 |
| 协议证据 | CMD-0x0050明确把optionalTicketSlot含义列为Unknown。当前ACK保留请求回显，自动用券的库存效果由receipt.Changes→NOTI和重读支持；没有把服务端回归当ClientVerified，也没有擅改未知字段。 |
| 文档一致性 | Contract/Spec/Plan新增范围一致，增幅材料/券准备与增幅功能实现分开。Human清单已修正当前33文件/4557证据与沿用运行产品的区别。P-01与R-01是同次新0→1，先完成原R-02退出重登，再自然训练P-02/P-03；P-04重新核对保护后重登。UID689恢复仅附加核对，不替代新请求。 |

## 原始自动证据与运行身份

reviewer直接解析原始UnitTestResult outcome：本轮完整Release Application **2356 Passed**、Infrastructure **408 Passed / 2 NotExecuted**；其余六个未变程序集沿用TASK-004完整结果：Protocol1202、Persistence385、Networking72、Host24、HttpApi50、Pvf60。当前必要证据合计**4557 Passed / 0 Failed / 2 NotExecuted**，不是本轮运行了whole-solution test的声明。两项NotExecuted为既有missing-archive条件测试。

另独立解析worker最终保护Application84、真实PVF12、wire8，全部Passed；Release solution构建日志为0警告/0错误。保留的`task005-protection-application-final.trx`确为83通过/1失败，错误为`Container character:1:Main is already registered`；最终测试已按Clear→Select设置，产品未改。这是fixture修正，不是产品行为RED或不存在的产品修复。reviewer执行`git diff --check`通过。

reviewer逐字节比较当前Host Release输出与现有runtime的Abstractions/Application/Data/Persistence/Protocol/Networking/Host七个主DLL，全部一致；因本轮只改测试，沿用部署有依据。只读现场核对PID41000实际exe路径为主仓库`artifacts/equipment-reinforcement-field-fix-20260930/runtime/Dfo.Server.Host.exe`；7001/10010/10011/18080均属该进程并Listen，无游戏Established连接；bootstrap返回apiVersion1/status ready，stderr0字节。未启动、停止或修改服务。

## 请求物资发放的独立边界审查

读取`artifacts/equipment-supplies-20260930/Grant/Program.cs`、备份/差分脚本、production receipt及日志。Grant通过实际Application/Infrastructure/Persistence模块注册的Transactional CommandBus，角色AddGold、账号材料Add及离线容器Add共同结算；没有虚构选中会话或改装备。已存在receipt拒绝再发，89516金币/9796无色前值guard阻止重复执行；副本先运行，生产仅一次。reviewer没有运行Grant或任何发放/恢复脚本。

reviewer以只读SQLite独立比较before-grant.db与当前原库全部业务表：仅`accounts`账号4的cube_clear **9796→59796**、`character_stats`角色2的carried_gold **89516→10089516**；`items`仅新增17行、`item_slots`仅新增17个角色2/template槽记录，没有删除或改变原记录，其余业务表相同。所有已有item_core完整字节保持，全部world物品UID/位置/记录保持。新增模板集合及角色2各模板总量恰好对应一次receipt：矛盾10000、强烈5000、净化/清除和四赋予四扭转各100、四保护券各50、宝珠20。备份quick_check=ok。

现场UID689的82字节core强化1/独立附魔0，UID339强化0/独立附魔0；保留真实低费样本，没有通过改等级或概率制造保护通过。新items行是堆叠模板载体，不是新装备实例。

## 剩余人工门与交接

阻断finding：无；缺失review前置输入：无。审查期间发现主Human清单仍写旧32文件/4515计数，控制器已同步，本review已核对修正。

历史PVF活动日期与core实例ExpireTime为不同来源；自动覆盖只证明当前实例期限规则，客户端活动限制仍由P-00核对。SC-6结构认可及新强化/重登、SC-7登录可见、SC-8两种真实保护失败与重登尚无用户反馈。随机未出现失败保持待验，不改变生产样本或成功率。

控制器消费本review后刷新completion-evidence与清单可验状态，维持verification/manual_required。`completion-evidence.md`已有rev2进行中notice，其rev1字段修复历史不能继承成rev2完结。最终用户门通过前不seal、不commit、不标ClientVerified。
