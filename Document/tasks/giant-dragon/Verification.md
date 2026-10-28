---
kind: rks-verification
decision: manual_required
---

# 巨龙候选验证

验证对象：`codex/167-giant-dragon` 相对 `63fb3993` 的本任务变更，同一功能版本0.2.10；实际资源及运行身份见 [部署记录](Deployment-20261027.md)，本服计时回归使用现役01F1资源。此记录仅支持“候选已实现并可进入用户测试协调”，不支持#167全部完成、全量测试全绿或A21已验收。

## 自动验证

完整stdout/stderr和TRX位于本工作区 `artifacts/giant-dragon/`，不随Git提交。测试使用临时库，未接触玩家库。

| 检查 | 当前结果 | 证据 |
|---|---|---|
| 解决方案构建 | 成功，0警告/0错误 | `build-candidate.log` |
| Application全量，最终恢复PVF | 6069通过、2失败、0跳过，共6071 | `application-candidate.log`，`tests/application-candidate.trx` |
| Protocol全量，最终恢复PVF | 1798通过、0失败/跳过 | `protocol-restored-final.log`及同名TRX |
| Infrastructure全量，最终恢复PVF | 974通过、0失败、2跳过，共976 | `infrastructure-candidate.log`及同名TRX；两跳过是配置档案时不适用的missing-archive用例 |
| 原资源兼容聚焦 | 70通过、0失败、11跳过，共81 | `original-resource-compat.log` / TRX；恢复专属用例缺少town100/任务时明确跳过，普通路径继续测试 |
| 内容恢复回读 | 4项修改、16任务登记正确，其他712006条目不变 | PVF旁JSON及工具readback；真实内容入口/任务测试 |
| 隔离Host烟测 | 成功，bootstrap ready，仅3个TCP监听，EXE/PID一致，无UDP监听 | `smoke-20261027-154936/result.json`；PID10424，已在finally结束 |
| 协议文档对账 | 失败18项，与精确基线输出相同 | `protocol-doc-sync.log`、`protocol-doc-sync-baseline.log`，逐行Compare-Object无差异 |
| 差异空白检查 | `git diff --check`通过 | 本次最终检查 |

Application失败完整身份：

- `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`

两项都是DGN7100 cell(1,0)缺少Left兼容MAP。以 `git archive 63fb3993 Server global.json` 导出精确基线，在原始D45档案复现相同两项失败，见 `baseline-dimension.log`、`tests/baseline-dimension.trx`；本任务没有修改异界地图。不能把它们说成已修复或将全量结果说成全绿。

文档对账18项涉及已有收集箱wire override以及若干已注册协议未进catalog身份表。本任务未增decoder/encoder或wire identity。精确基线Server、protocol及catalog直接链接的原始Document文件对账得到相同输出；保留失败，不顺手改写其他功能记录。

## 行为证据

- 实际DGN3200–3202解析与生产入场路由、真实出生房演员、8疲劳、重复请求不再扣费；新房与回访不再扣费。疲劳持久化/事务回滚沿真实SQLite测试。
- 原生恢复城镇gate链与世界图30保留，新增16项任务全部经过真实定义源读取；3536–3540各自任务门控、实际任务栏门票、所有声明房间创建、3540专用QuestLinked迷宫。
- 全队中有人未解锁时，按该成员slot整队拒绝；人数/练习限制在消费前执行；真实门票扣取后建房失败，门票、疲劳及run一起回滚。
- 20728真实内容两人目标：单人、重复同一角色、普通房清场均不完成；两名不同角色Boss清场完成。重复可靠投递只有一次进度变化通知。客户端计数返回Unchanged、裸通关事实不推进，随后可靠清图仍能完成。
- TDD入口扣费及人数限制的初始失败分别为“期望8实际1”“4人/练习未拒绝”。任务用例最初失败先定位为列表未登记，完成资源恢复后核验真实目标；不把内容缺失误记为纯规则实现的RED。测试总数断言保留原3295项并另验新增16项。

## 架构自查

这是作者按 [架构漂移审核规范](../../ArchitectureReview.md)、架构/事务/状态/事件/准入专项进行的职责自查，不是独立审核或云agent通过结论。用户禁止本会话使用subagent，未派发审核。

Protocol只增加既有请求的路由。巨龙领域服务读取会话/位置/任务owner并组合Admission→Structure→Visit；无连接扇出或消息编码。内容字段在Infrastructure解读、规则冻结到layout；通用疲劳和门票仍由既有owner写入。组队任务消费既有可靠清图事实，任务进度仍只有一份持久trigger，无平行通关表、缓存或独立每日计数。未新增数据库schema或跨层项目引用。

计时奖励的数量常量和材料元数据读取均在巨龙规则服务；入场只冻结不可变策略。运行owner从既有入场时刻和死亡claim时刻传入事实，规则返回新的共享掉落列表，最终物品仍由既有Cell死亡提交编号/发布、原拾取链写背包。未增加另一个计时器、奖励余额、死亡集合或直接发包路径。临时采样的协议号白名单已退出Networking源码，传输实现恢复取证前基线。

8557日限额采用周边南部溪谷每日任务的明确准入/清算接缝，新增专属任务quota owner，复用既有每日计数表与刷新规则；任务事实投影传递可接取结果，CMD不读取内容或计数表。限定8557且不改变其他Daily/SpecialDaily任务行为，不新增schema、每日任务完成镜像表或传输特判。计数、消耗、发放和任务完成都在已有清算事务中；完整回滚由真实SQLite测试核对。

## 计时奖励与实机边界

普通3200–3202已修正为10094602未精炼结晶，按用户批准的可见3–9刻度近似规则计算；18.75秒首次减为8，此后每12.5秒减1，最高9、最低3。入场计划冻结策略，最终Boss死亡claim的服务端时间决定数量，同次死亡提交生成共享结晶，3326原生气息及其他掉落保留。8557已在本服登记为84级白鸣每日任务，10个结晶换1张490000338通行证，沿用既有任务/物品owner，不增加内容补丁或兑换服务。规则、外部来源及原生整数精度边界统一见 [TimeAttackReward.md](TimeAttackReward.md)。

计时奖励基础版本自动回归：Application全量6097通过、2项相同7100基线失败；Protocol全量1798通过；撤掉临时逐包采样后网络接收回归12通过。日志在私有运行根 `reward-research/time-reward-*.log`。本轮六项真实三图/时长测试先观察到旧实现结晶数量为0的预期失败，见 `crystal-reward-red.log`；修正后相关回归见 `crystal-reward-green.log`，覆盖该版本计时边界、原气息逐项保留、空原结晶、多Boss最后一名、组队共享、个人及其他掉落、真实SQLite回滚重试及重复报告。8557使用真实内容与临时SQLite验证9个不足、10/12个准确扣10发1张、重复提交及同日重接拒绝。

本轮完整Application回归6109通过、2项相同7100基线失败，见 `crystal-reward-application-full.log`；最终任务/巨龙/角色当前状态及重放等相关639通过、0失败/跳过，见 `crystal-reward-final-focused.log`。Protocol全量1798通过，最终角色投影验证日志为 `crystal-reward-protocol-final.log`。8557还覆盖20个材料不能批量交两次、下一刷新周期可重接，以及额度/扣料/发票/任务完成一起回滚后重试成功。没有对玩家库运行这些测试。

本次可见刻度近似修正：真实三图六种时长测试先确认旧曲线5项失败、1项通过（`visible-curve-red.log`）；修改后巨龙、8557兑换、死亡事务及房间掉落相关159项全部通过（`visible-curve-green.log`）。边界覆盖18.75秒首次减量、每12.5秒两侧和最低3个，气息数量始终与原掉落相同。该轮只更改时间常量及相应测试，没有修改物品或任务生命周期。

构建、隔离烟测及部署状态见私有运行根 `reward-deployment.json`；尚无成功记录时视为未就绪。用户已确认普通3200入图、怪物、走门及两次完成报告采样，尚未验收新数量的真实地面显示、拾取、重登保留。#167仍不满足整体完成条件，保持manual_required。

## 未验证条件

1. 尚无本轮原生客户端双向WireCaptured。用户已确认登录/频道、3200入图/怪物/走门，以及9→3提示条显示，按有限范围ClientVerified记录；其他图入口、Boss机关、计时奖励、掉落与翻牌、退出/重登、多人和跨日仍必须按 [实机清单](Human-Test-List.md) 验证。
2. 20735六分钟目标仍沿既有客户端计数，没有新增服务器耗时验证。配置控制准入，不承诺客户端按钮动态隐藏。
3. 本次已核实实际数据库/PVF并完成授权切换，详见 [部署记录](Deployment-20261027.md)。基于现役3822源保留已有补丁，仅改4项；31项原生输入一致，81项聚焦回归通过，实际客户端/服务端使用同一01F1包。初始D45候选的自动结果仍仅适用于其身份。
4. 本次已保留实际原产物/配置/PVF及在线、停服后SQLite backup API一致备份；备份副本兼容检查与实际启动后的112表对账均未发现数据变化。登录及频道链路接受不代表机关或奖励通过。
5. 用户实机通过后才能创建PR，并仍需当前提交云agent明确通过和CI成功。当前未创建PR、未合并、未关闭issue。
