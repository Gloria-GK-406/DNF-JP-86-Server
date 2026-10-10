# 融合异界基础流程

日期：2026-10-10。分支 `task/monster-declaration-parser`；起点 `66cab317`。
用户授权实现并部署；本次没有改客户端或增加 patch，没有 PR/推送。

## 入场与次数

融合入口 DGN7100 走现有 CMD0010。CMD 的普通分支交给异界领域服务分类；
非异界继续普通入场，循环、镜像、主动深渊维持原有分支。
通过 PVF 的 `[dimension dungeon]` 识别异界。领域服务读取次数并检查请求模式和队伍，
再用共用结构规则、普通准备入场流程检查等级、难度、疲劳、材料并生成房间。

仅成功准备后发送 `DimensionEntryRecorded`，EventService.DirectAsync 同事务消费，
由 UsageQuota 写入每角色、每日 `dimension.entries:yyyy-MM-dd`；复用
`character_daily_counters`，未增加数据库迁移。整队先检查再扣，失败与回滚不扣。
提交后的事件通知读取当前快照，NOTI0326 两个 u32 分别为普通剩余、额外剩余。
查询不写重置；登录及在线跨日也刷新。当前沿用 UTC+8 06:00 每日重置，
PVF 配置普通 5 次、额外 0 次；普通次数用尽后才使用额外次数。

## 地图与奖励

7100 原 `[map specification]` 将只有右门的起始资源 53070 绑定到 x=1，
导致共用门方向校验拒绝从左侧抵达。`Tool/PvfDimensionLayoutPatch` 仅修改该文件的
五个坐标 token，将普通资源 53070～53074 绑定到 x=0～4，x=5 使用地图池中
明确标注 Boss 的 53075。未放宽结构解析校验，也未改客户端资源。
修复器校验输入 SHA、预期 token 和全部非目标文件索引、旧内容块。

PVF 原文件 SHA：`87599470F3F387B14DEAC355177DDF49B8FE70E9A8D521B3E496786CB2384714`。
新文件 SHA：`ADAC21543D89AE1A8228C6D6031757BB54A5A15F996AF062B331B09795EBF096`。
产物：`artifacts/dimension-dungeon-20261010/Script.dimension.pvf` 及其 `.json` 校验记录。

最初部署采用上游 `DimensionDropSystem` 的职业池策略（精英固定1件、Boss普通和套装各1件）。
后续代码已改成全职业去重池、类别权重、难度概率、Boss基础掉落与追加；碎片独立。
免费/付费翻牌也改用全职业普通/套装池，黑钻卡保持既有规则。
具体概率、归属与验证见 [融合异界掉落策略](DimensionDungeonDropPolicy-20261010.md)。
跨职业掉落版本 `fa7acdee` 已部署，用户接受基础掉落表现；下列原始回归数字保留为初版历史记录。最新运行身份与一期验收边界见 [收尾记录](DimensionDungeonCloseout-20261010.md)。

## 证据与验证边界

- UpstreamStatic：异界每日次数配置、0326 字段语义、职业装备池与精英/Boss 基础奖励策略。
- ClientStatic：0326 接收入口 `0x000C90A0` 可见两个 u32。字段文案和总长仍待本轮实帧闭合。
- CurrentImplementation：异界领域分流、UsageQuota 同事务更新、0326 快照及 PVF 五坐标修复。
- 自动化：真实 PVF 六房可达，六 Cell 完整创建，Boss61809及个人异界装备/碎片预生成；
  入场失败、整队拒绝、耗尽、重载持久化、事务回滚、日切与 CAS；协议 8B 编码；相关项目回归。
  运行日志与部署收据保存在 `artifacts/dimension-dungeon-20261010/`。
- 已知基线失败：Infrastructure 两项既有任务目录断言仍预期 2814，当前实际 3252；
  使用部署前 PVF 重跑同样失败，本次未改任务解析或对应断言。另有 2 项既有跳过。
- 本轮回归：Application 3569、Protocol 1439、Persistence 468、Networking 91、Host 159 全部通过；
  Infrastructure 870 通过、2 既有失败、2 跳过。最后补充真实入场检查后，异界 6 项再次全通过。
  Release 解决方案构建 0 警告、0 错误。
- ClientVerified（有限范围）：用户接受已测试的异界单人基础可玩一期，并确认跨职业掉落表现符合预期；不将整体接受扩展为每档概率统计、耗尽/日切/断线或全部地图逐项通过。
- ClientObserved：用户观察抗魔不足由客户端削属性，本期不重复实现服务端削属性；准确公式与多人交互未静态闭合。
- HumanPending / Unknown：复杂专属机关、全部教学/融合变体、多人完整流程，以及次数耗尽/日切/异常矩阵的逐项实机证明；续查 [#145](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/145)。

## 后续逐项补测清单

1. 重新登录 1123，进入异次元裂缝选图，核对普通剩余次数（当天新周期初始 5）。
2. 以普通难度进入 7100，确认入场后减 1；放弃后重登，剩余次数保持。
3. 按六房路线清怪切房，最后确认 Boss 出现且杀死后才通关；记录卡房或机关异常的房间。
4. 检查专属精英/Boss 掉落异界装备与 3311 碎片，拾取入背包；初版按职业池，后续版本改为跨职业概率掉落，验收按部署身份区分。
5. 通关测试免费、付费翻牌（金币充足），奖励只领取一次；普通图与镜像流程仍可进入。
6. 用尽当天次数后再入，确认拒绝且保持选图可操作；日切或次日登录恢复。

最新一期验收版本以 `artifacts/dimension-drop-20261010-2315/deployment.json` 为准（代码 `fa7acdee`）；
`artifacts/dimension-dungeon-20261010/deployment.json` 是初版部署历史。旧运行目录与数据库备份保留，可回滚。
本次收尾只更新文档与 issue，没有重启、部署或修改客户端/PVF。
