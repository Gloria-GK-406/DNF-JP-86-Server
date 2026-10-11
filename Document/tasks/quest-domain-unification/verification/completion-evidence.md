---
kind: rks-verification
decision: pass
---

# Quest 归拢交付验证

验证范围为服务端重构、自动化回归和本地备份部署。客户端实机验收由用户下一轮执行，不包含在本次已验证结论中。

## 结果身份

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/despair-tower`，分支 `task/despair-tower`。
- 接受基线：`424da80417f020d5e852e2807d3526dec54fc392`。代码审查集合42个Server文件，SHA256摘要 `75e605c3efad36415c930e3c19305152a10746758b2d96d7e1ea3282cd496e97`；完整回归后重新计算仍相同。
- 发布版本：`0.2.4-questrefactor.20261011`。为先验证部署再进行唯一结果提交，运行包使用已审查源码快照构建；2059个源文件清单位于下述artifact的`source-files.json`，SHA256 `D05A18E72C84C3D71F092150E8CC90ABE97103994B031156D21CF97D8A8E7BA4`。接受提交后manifest关联结果commit，不改运行二进制。
- Artifact：`E:/Project/DNF/DNF-JP-86-Server/artifacts/quest-domain-unification-20261011`。

## 成功条件

| 条件 | 当前证据 |
|---|---|
| SC-1 三查询只读，生命周期与可提交分开 | QuestQuery测试覆盖查询前后库存/金币/任务不变、已接优先、所有权及镜像虚拟槽；镜像目标进度归零但尚未通关时不可提交 |
| SC-2 同一状态计算 | 登录/Seeking/MaterialRequirement/Completion Effective调用统一QuestStateRuleService；共享为0且Main旧副本、足量/缺1、金币、多通道及Clear依赖回归通过；独立审查确认无重复公式 |
| SC-3 资格与特殊语义 | World/Cycle/Replay/QuestOffer通过同一Quest资格规则；既有等级/职业/前置/互斥和循环、镜像回归通过。普通Accept未增加新门禁 |
| SC-4 Prepare只读、完整结算、失败与重复安全 | 普通/镜像Prepare提交事务后仍无写入，cost/reward/任务effects显式；Settlement成功、重复、数据库故障回滚、满包邮寄、完成券免条件无普通奖励回归通过 |
| SC-5 协议/事件及职责 | 独立完整代码审查approved；无新增Effect种类，CMD receipt模式、注册、架构约束和生产DI解析通过；Quest无外部资产执行调用，ACK/NOTI仍在提交后发送 |
| SC-6 可识别runtime与数据保护 | 发布成功；核验旧PID43476及文件/端口归属后停服，SQLite备份quick_check通过；新PID30324，31个监听全部归属该进程，GM health/bootstrap均ready，/gm/mail返回200，stderr为空；manifest保存exe/程序集/配置/PVF哈希及旧runtime路径 |

## 自动化检查

Release解决方案构建和发布通过，零警告、零错误。完整回归共6884项通过、0失败：

| 项目 | 通过 |
|---|---:|
| Application | 3706 |
| Protocol | 1454 |
| Persistence | 468 |
| Infrastructure | 876 |
| Host | 159 |
| HttpApi | 50 |
| Networking | 91 |
| Pvf | 80 |

Infrastructure首次指定真实PVF时跳过两个缺档案用例；清除`PVF_ARCHIVE_PATH`后单独补跑2项均通过。因此完整用例均取得执行证据。TRX和原始日志在artifact的`checks/`目录。任务局部88/71专项日志位于任务`checks/`目录，为实施阶段补充证据，不重复计入总数。

`git -c core.whitespace=cr-at-eol diff --check`通过。保留原CRLF测试文件，不引入全文件格式变更。

## 备份与验收边界

- 数据库仍使用既有权威玩家数据库；备份：artifact下`database-before-20261011-223951`，包含SQLite快照及文件哈希，未重置数据。
- 保留旧runtime：`E:/Project/DNF/DNF-JP-86-Server/artifacts/login-quest-qinglong-20261011-424da804/runtime`。启动失败可回旧二进制；不自动恢复数据库覆盖玩家写入。
- 未修改client、PVF、schema或正式VersionPrefix。原有黄龙调查目录保留，不纳入本轮提交。
- 实机待验收：[manual-test.md](../manual-test.md)。本次不宣称ClientVerified，也未执行PR、合并、发布或工作区清理。
