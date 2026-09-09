# 普通学习整合实现与检查

2026-09-09，基线 da623c2d，执行控制采用会话清单。领域阶段结果见 domain-implementation.md；没有创建持久 Task 单元。

- T-001：领域学习与规则槽位、43项领域行为和78项相关回归完成。
- T-002：LearnSkill 切片及唯一 ProtocolModule 接线完成，命令采用在线用户与账号排他事务。handler 在事务提交后回复，并在发送前复核原选角会话。解码严格校验长度及折叠 MD5；非零模式/退点由领域拒绝。
- T-003：5份请求和历史成功应答片段已归档；协议及帧层记录说明算法、成功读取、失败格式和置信边界。Release 构建可供实测。

## 行为 RED / GREEN

保护结果是实际捕获的加点请求能够进入已注册消息；风险是新增业务完成却遗漏线级接线导致客户端继续无响应。注册后实际解码的测试可容忍切片内部重构，已有测试未包含0x001D。

命令：`dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --filter FullyQualifiedName~LearnSkillCodecTests --logger "trx;LogFileName=protocol-red.trx" --results-directory .rks-agent/tasks/ordinary-skill-learning/verification --verbosity quiet`。

RED退出1，5 Failed；Assert.Single没有找到selector=1/type=0x001D的解包器，构建成功。接线后同一测试5 Passed（--no-restore，输出protocol-first-green.trx），退出0。首次编译遗漏NetworkDispatchContext的命名空间已修正，不算行为RED。

## 整合检查

设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，执行：

`dotnet test Server/86JP.Server.slnx --filter "FullyQualifiedName~LearnSkillCodecTests|FullyQualifiedName~SkillLearning" --logger "trx;LogFilePrefix=learning-integrated-green" --results-directory .rks-agent/tasks/ordinary-skill-learning/verification --verbosity quiet`

退出0，Application 48 + Protocol 17 + Networking 1 = 66 Passed，无失败或跳过。其他4个项目没有匹配此筛选的用例。覆盖真实向量、正文399/403字节校验边界、optional-extra、真实SQLite命令提交后应答、存储失败整包回滚、并发增量、发送失败保留提交，以及真实TCP未选角拒绝后连接可用。

准备整合检查时 --no-restore 遇新工作树尚未恢复部分测试项目资产；随后正常恢复重跑。测试消息构造的目标类型推断错误也已改成显式LearnSkillRequest，两者不算产品行为失败。

`dotnet build Server/86JP.Server.slnx -c Release --no-restore --verbosity quiet` 退出0，0警告、0错误。日志release-build.log。

原始TRX与构建日志保留于本地verification目录并由.gitignore排除提交；最终核验记录保存汇总与摘要。当前运行Host、主库、客户端及main分支未变更；仍需配置的独立完整审查和最终回归。
