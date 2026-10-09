---
kind: rks-verification
decision: pass
---

# 完成验证

日期：2026-10-08；验证目标：task/domain-event-dispatch 相对 ef00741dc3d4ee826c14b3d273042f38c3a1c31f 的完整结果。证明实现、架构边界和本仓自动回归，不声明客户端实机验收。

## 最终检查

环境：Windows，.NET SDK 10.0.302，DOTNET_PROCESSOR_COUNT=4；PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf。测试使用临时库/端口，未操作部署或运行数据库。所有共享输出构建/测试串行。

| 检查 | 结果 | 日志 |
|---|---|---|
| 全解决方案构建 | exit 0，0 警告、0 错误 | ../build-accepted.log |
| Application 完整回归 | 3240 通过，0 失败、0 跳过 | ../final-Application.log |
| Protocol 完整回归 | 1376 通过，0 失败、0 跳过 | ../final-Protocol.log |
| Persistence 完整回归 | 465 通过，0 失败、0 跳过 | ../final-Persistence.log |
| Host 完整回归 | 155 通过，0 失败、0 跳过 | ../final-Host.log |
| 修复后事件/邮件/Joust 集合 | 71 通过，0 失败、0 跳过；属于上述全集，不重复累加 | ../remediation-green.log |
| Host 活动刷新目标回归 | 1 通过，0 失败、0 跳过；属于 Host 全集 | ../host-remediation.log |
| 全量依赖扫描/独立审查 | 43 个基线文件无遗漏；生产 Bus 引用只剩允许的 4 文件 | ../migration-matrix.md、../reviews/change-review.md |
| 事务/调度独立审查 | approved，首轮组饥饿已复现和修复 | ../reviews/transaction-review.md |
| diff 空白检查 | git diff --check exit 0 | 接受提交前终端检查 |

完整套件小计：3240 + 1376 + 465 + 155 = 5236。最终 Host 测试修改只涉及确定性等待与线程安全抓帧，生产代码与其余三个完整套件通过时相同；修改后重新构建全解决方案、执行目标及 Host 全集。

构建命令：`dotnet build Server/86JP.Server.slnx --no-restore -m:1 -p:UseSharedCompilation=false -v:minimal`。
完整回归对 Application、Protocol、Persistence、Host 分别执行：`dotnet test Server/tests/Dfo.Server.<项目>.Tests/Dfo.Server.<项目>.Tests.csproj --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'`，每次检查退出码后才运行下一项。

## 契约条件与决定性证据

| 条件 | 已满足的证据 |
|---|---|
| SC-1 结构与职责 | spec.md 重构前后表及事实/运行时/投影接缝；AGENTS 权威边界；完整独立复审批准 |
| SC-2 强类型、多订阅、恢复 | DomainEventEntryTests 注册/能力拒绝，DomainEventDispatchTests 不同 payload 在同组恢复、多订阅独立状态；未知名称/版本/JSON 保留诊断 |
| SC-3 原子性、幂等消费 | 业务/event/delivery 同事务回滚；Reliable 更新与完成同事务；失败恢复不重做其它已成功订阅；MailboxCoreIntegrationTests 实际 MailId/Recipient、重开库的可靠消费 |
| SC-4 后台生命周期 | 持命令准入门提交可返回、提交唤醒、到期自主重试、记账失败退避恢复、取消后留 pending、重启恢复；DomainEventSchedulingTests 同步持续积压不阻止另一组启动 |
| SC-5 旧调用全部迁移 | EventNotificationBoundaryTests 反射、主动违规探针及全 Server/src 扫描；独立从基线提取 43 文件并对照矩阵，缺漏 0 |
| SC-6 邮件与受众 | 每封事实保留，同事务/同角色一次提醒，不同批分离；离线不影响 Reliable，旧实例/TTL 的 LiveOnly 跳过；Notification/Party/Town 精确会话回归保持 |
| SC-7 协议与马战 | Application/Protocol/Host 全集通过；邮件/Joust 查询与重复 tick 无公告重放；原 ACK/库存/选角/组队/城镇帧顺序及失败投递断言保留；GM 活动停用后的 native frames 验证 |

## 执行异常与修复证据

- 新工作树无恢复资产：使用本机缓存 restore，命令级关闭 NuGetAudit 并 ignore-failed-sources；没有改变项目包管理策略。共享编译器等待通过命令级 UseSharedCompilation=false 避开。
- 首轮沙箱拒绝 Windows EventLog 并遮蔽宿主异常；改为正常权限执行隔离测试，诊断和全部回归通过。未削弱日志/业务断言。
- 首轮 Application 四项失败中三项为邮件同步提示的旧时点假设，增加显式 drain 后保留原断言；另一项宿主日志权限问题在正常权限下通过。test-Application.log 是首轮历史，不能代替 final-Application.log。
- 调度饥饿先用同步积压测试复现 RED（scheduling-red.log），再修复组启动及每批 Yield，GREEN 71/71。其它核心实现采取实现后验证，不将整体记录虚称为全程 TDD。
- Host 首次完整回归 154/155；唯一旧同步刷新假设在 host-first-complete.log。等待消费后揭示测试 List 并发枚举（host-capture-race.log），改线程安全队列和确定性消费等待，目标和最终全集通过；两次独立复审确认原帧断言保留。
- 初始较大的 TRX 和被权限噪音淹没的 Host 日志保留在本工作树 ignored artifacts/domain-event-dispatch-validation；../initial-evidence-archive.json 记录原大小/散列/路径。最终通过证据与修复 RED/GREEN 小日志随结果保留。

## 验收边界

未部署、未修改客户端/PVF、未推送/PR/合并/升版。未实现完整称号/成就规则。
Reliable 每组串行但退避可被后续到期事实越过，不提供严格 FIFO；新增订阅不回填历史投递，无自动 schema upcast，完成记录暂不自动清理。LiveOnly 为先标记后尝试的提醒，崩溃窗口可能漏提醒；不会补播旧提示。这些是已说明的实现边界。

源 task/abyss-research 仍为 ef00741d，保留既有 0x0010 与两份深渊调查文档修改；main 仍为 81f4373f8e5d686257420080b3f1b0c7cc7fc83c，主仓状态未改。接受对象只包含本任务的 AGENTS.md、Server 生产/测试和 Document/tasks/domain-event-dispatch。
