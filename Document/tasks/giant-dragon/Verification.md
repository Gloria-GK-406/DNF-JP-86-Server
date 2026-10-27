---
kind: rks-verification
decision: manual_required
---

# 巨龙候选验证

验证对象：`codex/167-giant-dragon` 相对 `63fb3993` 的本任务变更，暂定版本0.2.10；资源身份 `56CE679A73E32132BFF7E1B2003E68804639A90F07C05898A1967C1A0D4FB48C`。此记录仅支持“候选已实现并可进入用户测试协调”，不支持#167全部完成、全量测试全绿或A21已验收。

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

## 未验证条件

1. 仍未取得本轮WireCaptured / ClientVerified。原生入口实际可达、选图、Boss机关、原生计时、掉落与翻牌、退出/重登、多人和跨日必须按 [实机清单](Human-Test-List.md) 验证。
2. 20735六分钟目标仍沿既有客户端计数，没有新增服务器耗时验证。配置控制准入，不承诺客户端按钮动态隐藏。
3. 本次已核实实际数据库/PVF并完成授权切换，详见 [部署记录](Deployment-20261027.md)。基于现役3822源保留已有补丁，仅改4项；31项原生输入一致，81项聚焦回归通过，实际客户端/服务端使用同一01F1包。初始D45候选的自动结果仍仅适用于其身份。
4. 本次已保留实际原产物/配置/PVF及在线、停服后SQLite backup API一致备份；备份副本兼容检查与实际启动后的112表对账均未发现数据变化。部署成功不代表客户端登录、机关或奖励通过。
5. 用户实机通过后才能创建PR，并仍需当前提交云agent明确通过和CI成功。当前未创建PR、未合并、未关闭issue。
