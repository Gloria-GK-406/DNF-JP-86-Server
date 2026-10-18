# 0.2.6 发布记录

2026-10-18 用户授权：把 GM 后台改版并入 next-round，出一个版本。目标基线是 main `314bd463`（0.2.5），本次 PATCH 加一，升到 0.2.6。
`task/gm-ui-refactor` 以 merge commit 并入 `task/next-round-20261013`，再由同一个 PR 合入 main，原分支提交全部保留。

## 变更范围

- **GM 后台改版**：控制台按批准的演示重做，总览压缩到一屏并支持展开。官方邮件可以给装备选锻造等级、开孔数和品级。新增按物品 ID 的图标包（附重建工具 `Tool/GmIconPack`）、PVF 物品提示接口、审计日志按结果和动作筛选，以及服务器信息页（监听端口与 PVF 身份）。见 [GM 后台](gm-web-admin.md)、[官方邮件](gm-official-mail.md)、[物品图标](gm-item-icons.md)。
- **婚姻系统**：
  - 求婚与答复、订婚恢复与婚前任务；普通婚礼的预约、教堂入场、准备、仪式与结算，包括礼炮、祝词簿和站位同步；
  - 婚姻状态合并为单一实体并持久化阶段；
  - 婚房家具与情侣仓库、爱情树、戒指升级、成长胶囊，以及爱之摩伽陀的准入；
  - 修正爱相随守护券的 PVF 重复声明。
  - 见 [婚礼流程](MarriageWeddingFlow-20261016.md)、[聚合设计](MarriageAggregateDesign-20261017.md)、[婚房实现](MarriageHousing-Implementation-20261018.md)、[胶囊与摩伽陀](MarriageCapsules-LoveMagatha-20261018.md)、[守护券修正](WeddingTicket-PvfFix-20261018.md)。
- **新内容**：
  - 贺拉斯研究所 3×3 的入场次数与兑换券结算（[实现](Heras3x3Implementation-20261016.md)）；
  - 陶王国入场、每日门票与经验书（[陶王国](TauKingdom-20261015.md)）；
  - SAO 塔的共享进度、首通奖励、入口与汉化（[塔核心](SaoTowerCore-20261015.md)）；
  - 无尽／极限祭坛；
  - 黑龙大会与二次觉醒（[黑龙与二觉](BlackDragonAndSecondAwakening-20261013.md)）。
- **物品与系统**：
  - 黄金蜡装备重新封印（[重封](EquipmentReseal-20261013.md)）、权重祝福强化券；
  - 新物品期限初始化，以及每日在线过期物品清理（[期限](ItemExpiration-20261014.md)、[在线清理](OnlineExpiredItemCleanup-20261014.md)）；
  - 注册式异步定时任务（[定时任务](ScheduledTasks-20261014.md)）；
  - A21 客户端设置 blob 的保存与回放；
  - 角色外观与登录可见性统一（[外观](CharacterAppearance-20261014.md)）。
- **修复**：
  - 队伍申请与副本选择主机槽位、循环副本队员任务同步（[队伍修复](PartyCycleEntryFix-20261018.md)）；
  - NPC 堆叠购买的获得量与限购计数；
  - 大会 APC 身份与黑龙次数；
  - 恶魔契约礼包与开罐槽位复用；
  - 首次觉醒不重置已学技能。

## 验收边界

上述功能大多只有自动化测试和静态协议证据。双端婚房、戒指、树、胶囊与爱之摩伽陀的实机验收仍待完成，步骤见 [婚房测试步骤](MarriageHousing-TestProcedure-20261018.md)。普通婚礼的若干修正也待重测。高级／豪华婚礼和离婚没有开放。发布不把静态分析或自动测试升级为 ClientVerified，也不自动关闭未完成的 issue。

## 验证与发布

- **构建**：Release 构建通过，0 警告、0 错误。
- **.NET 回归**：用当前部署的服务端 PVF `artifacts/wedding-ticket-pvf-fix-20261018/Script.server.pvf` 跑完整回归，结果如下。
  - 共 7,552 条：7,549 通过、1 失败、2 跳过。各项目总数为 Pvf 80、Application 4,133、Protocol 1,616、Infrastructure 941、Persistence 473、Host 165、Networking 94、HttpApi 50，相加正好 7,552。
  - 唯一的失败是 `SelectCharacterLifecycleTests.ObserverQueuedBeforeRollbackClose_CompletesAfterGateRelease`：等待 2 秒后抛的是 `TimeoutException`，测试期望的是 `IOException`。同一用例在无 PVF 的全量回归中通过，单独连跑 5 次全部通过，每次约 78 毫秒，所在测试类 30 条也全部通过。判断为满载下的时序抖动，没有修改生产代码或跳过该用例。
  - 无 PVF（与 CI 同条件）的全量回归里，有 3 条结构判据在合并前的 next-round 顶端 `31dfc2db` 上同样失败。原因是婚房、戒指、胶囊新增了 3 个命令、2 个结算请求、11 个 CMD wire key 和 12 张婚姻表，测试里的清单没有跟着更新。已在测试中逐项列出新成员和新表，修正后这三条判据通过。新命令全部按 Transactional 注册，生产代码没有改动。
- **其他测试**：GM 前端 158 项测试及生产构建、发布保护 11 项、WireSmoke 5 项、GmIconPack 工具 32 项全部通过。前端构建仍有既有的大 chunk 警告。
- **协议资料同步**：本次处理前有 39 项缺口，处理后为 0，检查器自测通过。补了 12 行 wire type 映射：`CapsuleAck` 3 行、`HousingAck` 8 行、`AltarDifficultyResponse` 1 行。并重新生成了 catalog 的服务端覆盖区：C2S 173 个身份，S2C 267 个身份，catalog 共 446 个身份。
- **发布流程**：PR 双平台 CI 全部通过后合并；主干 CI 通过后，从合并提交创建 `v0.2.6`。Server Release 工作流会重新执行 Windows x64／Linux x64 测试、打包、启动冒烟和 SHA-256 检查后发布附件，实际结果以对应 Actions 为准。

发布包不包含 PVF、角色数据库或客户端二进制。本次发布不更换正在运行的部署。升级前应备份并保留现有数据库、对应的服务端 PVF 和部署配置；新版本启动时会建立婚房相关的新表。
