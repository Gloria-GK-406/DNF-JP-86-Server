# AGENTS.md — 86JP 服务端

DNF 日服 Act.86 客户端的服务端仿真，使用 .NET 10。
本仓承载服务端、服务端工具和协议知识库；客户端补丁与调试研究属于
`DNF-JP-86-ClientHack`，Windows 登录器属于 `DNF-JP-86-Launcher`。
旧单体 `E:\Project\86JP` 只供行为对照，不作为结构参考。

## 项目结构

| 目录 | 内容 |
|---|---|
| `Server/` | 服务端解决方案 `86JP.Server.slnx`、共享构建配置与测试 |
| `Server/src/` | Data / Abstractions 契约，Application 领域编排，Infrastructure 内容源，Persistence 存储，Protocol 协议，Networking 传输；Host 组合 HTTP、GM 与游戏组件 |
| `Web/gm-admin/` | GM 管理页面源码 |
| `protocol/` | 协议逆向结论与证据的权威知识库 |
| `Tool/` | PVF 探针、打线工具、CRAP 采集及发布工具 |
| `Document/` | 按主题维护的开发指南、设计约束、专项说明与验收资料 |
| `Docs/` | 设计、研究与参照资料；标注 ServerS4A21 的文件描述参照服务端，不是本仓实现 |

请求主线：薄消息 handler → 协议 CMD → 领域服务 → 规则／存储；
CMD 在唯一玩法执行器中划分事务并登记投影，作业结束后由发送端交付。

## 按任务读取

开始相关工作前，读取对应文档；这些专项约束与本文件共同构成开发要求。
按任务选择，无需每次读取全部文档。跨主题改动读取涉及的各项。

| 任务 | 阅读入口 |
|---|---|
| 新建模块、调整职责／依赖／接口 | [架构与分层](Document/Architecture.md) |
| 判断补丁还是重构、消除重复事实或判定 | [重构判断](Document/RefactorPreferences.md) |
| 新增或修改协议、handler、CMD | [协议 CMD 开发指南](Document/ProtocolCommands.md)、[协议知识库规则](protocol/README.md) |
| 修改执行器、事务、调度、发件箱、发送或关闭 | [玩法执行与事务](Document/GameplayExecution.md) |
| 修改角色、会话、当前状态、疲劳或设置 | [角色与会话状态](Document/CurrentCharacterFacts.md) |
| 修改物品、容器、移动或生命周期 | [物品与容器](Document/ItemContainers.md) |
| 修改领域事件、通知或多人投影 | [事件与通知](Document/EventsAndNotifications.md) |
| 修改宿主、组件开关或启动顺序 | [组件组合](Document/host-component-composition.md) |
| 构建、运行、测试与失败排查 | [构建与测试](Document/BuildAndTest.md) |
| 提交、创建 PR、分支收尾或发布 | [开发与发布约定](Document/server-ci-release.md) |
| 编写或整理文档、注释、证据 | [文档维护规范](Document/DocumentationGuidelines.md) |

## Code Review Rules

- PR 审核只评估架构漂移，不重复功能正确性或实机验收。审核前必须读取
  [架构漂移审核规范](Document/ArchitectureReview.md)，并按变更范围读取相关专项约束。
- 先检查职责归属、既有能力复用和周边同类实现，再区分实现错位与架构本身的限制。
- 逻辑放置错位不通过；当前架构完整承接则通过；范围小且孤立的架构例外可通过但须说明；
  结构较大且与架构存在根本冲突时不通过，交由人工接手。具体证据与裁定条件以专项规范为准。

## 简单约定

- 注释说明代码表达不了的原因、来源、不变量和刻意保留的边界，不复述代码。
- 文档保留当前规则、设计考量、有效的坑及证据边界；不记录演化过程、评审轮次或修复流水账。
- 每种事实只在所属专项文件维护，其他位置引用；修改结论时直接更新正文。
- 区分实现、自动测试与 A21 实机验收；参照资料和设计意图不能当作本仓已实现的证据。
