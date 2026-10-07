# 主动深渊入场实现

## 交付边界

基准 `b596b06bcef121e6444127850980032115a1b0fb`，工作区 `.worktrees/abyss-research`，分支 `task/abyss-research`。本次只接主动深渊准入和入场/切房投影；没有部署，也没有修改客户端或 PVF。柱交互、怪物波次、深渊掉落/结算、随机深渊概率和华丽深渊均不在此次范围。

## 调用和事务

`SelectDungeonDecoder → SelectDungeonHandler / 入场投影边界 → EnterDungeonCommand → 判定领域服务 → CMD switch → 普通入场或主动深渊领域服务 → 规则服务 → PVF 内容源`。

- `DungeonEntryContext` 携带地下城、任务事实、难度、请求模式、练习开关、全队成员；不依赖网络连接或协议对象。
- offset5 **等于1**才是 `ManualHell`。其他模式仍走既有普通分支，随机判定留桩；不会把所有非零值都解释成深渊。
- offset6 是独立练习开关，主动深渊暂拒绝练习组合，不能用它指定 A/B 难度。
- 主动领域服务检查内容支持、有效封印房/演员组、成员等级、已完成的前置任务及每个人的门票。全部通过后复用公共入场，准备共享 run、扣疲劳、扣门票；命令总线使用多账号独占租约和一个领域事务，失败/异常不会留下半队扣除。
- 通行证按 PVF 顺序优先，足量才采用；普通票可跨随身多个堆栈累计，同一个成员不会混用两种替代门票。仓库、账号仓库、共享材料不参与。
- 入场期间的业务拒绝/数据库异常回滚与投影后的传输失败不同。既有网络投影失败路径会清理 run 和选图状态，但不新增传输失败退款策略。

## 规则与冻结方案

WDM 提供 `[hell dungeon]`、`[hell quest]`、`[hell freepass item]`、`[item condition]`；解析带 `[in progress]` 标记的地下城/任务成对流，包含未列入 UI 清单的顶层 WDM。

DGN 每个 maze 的 `[seal door pos]` / `[seal door map index]` 指定封印房；MAP 的 `[special passive object]` 内 `[hellparty]` 三元组为组ID、权重、order。按 order 对满足本次难度的有效组抽一次。当前 `hellparty.etc` 手动难度权重 A60/B40，A→1、B→2；APC 等级读取 `.aic` 的 `[minimum info]`，普通怪物等级使用 dungeon basis。

普通邀请函数量沿用 **UpstreamImplementation**：最低等级≤44不提供普通准入成本方案，否则 `floor((40×最低等级−1800)/100)+10`，例如70级20张、85级26张。不是 PVF 自带数量，也不称为 A21 实机确认。

`HellEntryPlan` 保存难度、坐标、MAP、order/组/演员，在 shared run 的 Layout 中保留。首次进入该 cell 替换为封印 MAP；切房和重访不重抽。总演员数量超过线上的 u8 上限会在扣票前拒绝。

## 协议与证据等级

客户端绑定 b1 SHA256 `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`；PVF SHA256 `03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543`。

| 字段/行为 | 本次处理 | 证据边界 |
| --- | --- | --- |
| CMD0010 offset5/6 | 请求模式/练习开关 | ClientStatic，见协议人工补充与既有研究；无主动深渊 WireCaptured |
| NOTI001C offset8/9 | 冻结深渊 cell X/Y；普通维持0/0 | 原生读两独立字节，坐标语义为 UpstreamImplementation |
| NOTI001D offset7 | 冻结 A/B 值；普通维持2 | 原生读取宽度 Confirmed，模式语义按上游，待实机 |
| NOTI001D hidden actors | order、隐藏1/FF、fog1 | UpstreamImplementation，未称为 ClientVerified |
| NOTI02A7 | u32 count + APC code/level 的 u32 对；只在首次进入深渊房发送 | ClientStatic：handler RVA00D483C0、存表0134B4A0，消费者0134A7E0调用创建器01D18F60，创建器日志 `AiCharacter Create Index :%d`；见 [原始指令](verification/client-02a7-consumer.txt) |

hidden actors 不生成普通掉落，也不冒充已完成深渊战斗；其后续活动态和奖励需要独立功能接线。普通编码向量维持原样。

## 检查和实机待验收

测试覆盖类型判定、独立模式字节、真实 PVF 样本、权重/演员冻结、通行证优先、分堆扣票、全队缺票拒绝、全队当前前置任务、事务放弃/扣票异常回滚、实际 CMD 总线提交、切房重访和普通副本回归。塔旧入口只处理非主动请求，主动请求必须经过 CMD 后按不支持拒绝。日志和汇总保存在 `verification/`，原始 TRX 仅本地留存。

最初 RED 测试命令因 MSBuild 多进程问题没有形成有效行为结果；不能宣称整个实现完成严格 RED→GREEN。后续使用单节点构建，新增测试和回归均实际运行，以当前领域状态和编码向量作为 oracle。独立审查发现的塔入口绕过先由 `verification/tower-red.log` 复现，修复后 `tower-green.log` 记录副本/塔协议103项通过。

首次全量测试被沙箱 Windows EventLog 写入拒绝阻断：既有启动规则的 warning 触发 `Cannot open log for source '.NET Runtime'` / Win32Exception5。进程环境禁用未生效，未修改生产日志。相同4项启动检查在沙箱外全部通过（`host-environment-check.log`），确认环境边界；中断了命令行已核实的本任务测试根PID35040及其子进程，然后在沙箱外串行重新执行全量检查。

下一次部署后的实机验收：主动勾选能进入；不足票/未完成前置任务不能进入且资源不变；成功按本区成本扣一次；切房能到冻结封印房；普通副本入场/切房维持原表现。柱激活和奖励暂不列为通过项。
