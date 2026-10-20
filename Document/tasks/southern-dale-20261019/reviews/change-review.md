---
kind: rks-review
verdict: approved
---

# 南部溪谷完整结果审核：F-1修复复审

## 审核目标与范围

- 当前审核HEAD：`05f1a0cf527511a111901c0c7a0d8369d760ed36`；现场`git rev-parse HEAD`已核对。
- Spec baseline：`35d890662e987c85ff7e05786ae72c068f81c187`。
- 前轮完整审核实现：`7d44107ee173b2f9235d1db37010c1604272aab7`。前轮报告保存在[change-review-r1.md](change-review-r1.md)，其完整代码/测试/文档检查及未变范围的结论继续采用。
- 本轮范围：`7d44107..05f1a0c`全部10个文件的修复差异，重点复审F-1、活动通知及南部补票通知的直接回归；同时核对实际GM事件生产与领域事件/通知接线。
- 治理输入沿用TaskContract revision 1、Spec、Plan、routing-decision及工作区/仓库AGENTS；判据沿用`reviewing-work/SKILL.md`、`references/code.md`和`references/deliverable.md`。

## 发现处理

**F-1 / P2：已关闭。** 当前HEAD不存在前轮报告中的未解决阻断。

1. `ActivityNotificationSubscribers.cs:28-30`仅为南部配置变化设置`EventsChanged.RefreshQuestOffers`；实际GM入口`GmEventsDomainService.ChangeAsync`仍在配置与审计完成后生产该事件。`WeeklyAttendanceSlice.cs:119-135`除既有006C/033F等活动消息外，按当前角色和任务资格重新发送0015，并在异步读取前后核对会话。由关闭到开启、由开启到关闭均不再依赖重登或显式Replay刷新。
2. `SouthernDaleDomainService.cs:59-62`在在线补检成功后总是产生独立`SouthernDaleDailyRefillCompleted`，即使已持有2张票且无库存增量也会生产。它未伪装成InventoryChanged；事务失败不会提交此完成事实。
3. 新完成事实通过注册的LiveOnly订阅者转为刷新通知（`SouthernDaleProjection.cs:22-26`、`ProtocolModule.cs:1084`）。同一handler只在Slots非空时生成库存帧，并始终按刷新标志重读当前offers发送0015（`:39-54`）；保留精确会话校验。原通关钥匙的InventoryChanged路径默认不额外刷新offers，既有库存通知语义保留。
4. 两个新增真PVF回归捕获实际通知中的编码帧，验证0015确实包含/移除20549：活动`false→true→false`同时检查033F类别1状态；当日已完成后，日切前列表缺失，跨06:00后持票数量仍为2而列表恢复。修复前RED日志两项均因缺少0015而失败，能指向本发现；修复后两项均通过。

本轮未发现修复引入的直接回归。其他活动默认不设置刷新标志，通用活动快照、牛头/南部独立状态及既有Joust刷新行为保留；文档新增说明与代码一致。

## 直接核对的测试证据

直接读取`artifacts/southern-dale-20261019/review-f1-red.log`及`remediation-pvf/`三份TRX：

| 结果文件 | 结果 |
|---|---|
| `13_51_06`，Application | 210/210通过，0失败；两个新增实际发送回归均Passed |
| `13_51_29`，Protocol | 21/21通过，0失败；含既有Joust刷新与调度开关回归 |
| `13_51_39`，Host | 11/11通过，0失败 |

未自行运行构建/测试，避免和主执行者并行写bin；未改产品、运行PVF、数据库或Git状态。本轮仅更新此审核文件。

## 批准边界

结合前轮完整审核与本轮修复复审，当前HEAD的独立结果审核门禁为`approved`。该结论不替代主执行者对同HEAD的fresh Release构建及完整无PVF验证；这些完成验证尚需由其收口。

033F类别1任务映射仍为ClientStatic/High。原生入口/任务UI、特殊房间和四人联机仍属已声明的用户实机验收边界，不因本次自动化通过升级为WireCaptured或ClientVerified。
