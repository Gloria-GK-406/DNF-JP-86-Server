# 接手续作与回归证据

2026-09-08，按所有者的接手指令复用原任务 worktree，未创建第二工作区。任务分支 change-risk-is-measured-repeatably，接受前基线 addbeb52bffdd329a85f292f6784200d1146bb28；源分支 main 同一提交。

接手时已有未提交 CRAP 工具与测试、.gitignore/AGENTS/README/Server 覆盖配置变更、任务工件和三次历史运行。变更全部对应原 TaskContract；Server/src 与既有测试源码未改。运行目录保留并以 history-before.json 记录105个既有文件哈希。

## 回归保护

Runtime 子任务先观察到三项失败（脏内容身份、空 TRX、同秒并发分配），修复后75项通过。最终工具集见 tool-tests.log，81项通过。

主执行者在固定访问器身份、顶层入口和 ref 签名之前运行：

`dotnet test Tool/CrapMeter.Tests/CrapMeter.Tests.csproj --nologo --filter FullyQualifiedName~SourceInventoryTests`

观察到5失败/12通过（期望2个访问器实际1个、顶层入口为空、ref签名碰撞等）；修复后17/17通过。

覆盖关联修复前运行：

`dotnet test Tool/CrapMeter.Tests/CrapMeter.Tests.csproj --nologo --filter 'FullyQualifiedName~Conflicting_modules|FullyQualifiedName~Host_child|FullyQualifiedName~Same_line_accessors'`

三项均失败：冲突模块仍有数值、Host为Computed、同行访问器无法区分。修复后与源码清册一起28/28通过。随后81项全量工具测试通过；聚合输出新增检查属于实现后验证，不声称TDD红绿。

原有失败清单测试曾假设损坏 slnx 也会产生源码清册；现在清册范围来自 slnx，该情形无法构建清册，测试改为验证实际可得的失败清单/异常/前后快照，并检查清册文件不存在。未伪造空清册。

## 无旧缓存复跑

自动审批策略拒绝了递归删除缓存（仅返回 blocked by policy，未提供更细理由），未执行删除。改用非破坏性方案：先核实38组绝对源/目标路径都在任务 worktree，再将 bin/obj 移到 verification/build-cache/，原38路径均不存在后启动 fresh-run-1。该备份被本任务 .gitignore 排除，保留在本地；映射见 cache-move-targets.json。工具不引用这些备份。

TaskContract revision2 将 Document/ 与这种等价的无缓存验证方式同步到验收表述，交付目标未改变。
