---
kind: rks-verification
decision: pass
---

# Completion Evidence

本结论是“服务端实现、自动化验证和实机交接已就绪”，不是完整 A21 实机验收完成。

2026-09-21，工作区 `feat/dungeon-weakness-recovery`，基线 `88741631`，当前完整改动经独立 [review](../reviews/change-review.md) 批准。规则与来源边界按 TaskContract revision 1。

| 条件 | 当前证据 |
|---|---|
| SC-1 | WeaknessTests 等级19/20/85、失败/通关、重复退出、旧run、组队全灭断言通过；生产所有结束调用经异步入口，断线清理沿用它。 |
| SC-2 | 十周期到100、配置夹取、暂停/恢复幂等与满值移除断言通过。 |
| SC-3 | 纯服务无需在线会话仍推进，新服务无旧值；读等级失败保留补偿，入新run后补偿仍暂停，Recover和新失败不会被旧异步结果覆盖。 |
| SC-4 | WeaknessProjectionTests 4项验证真实帧0021单字节和旧会话拒绝；登录和回城接线经源码检查、Protocol和Host回归。用户逐步实测说明位于 Document/地下城虚弱与自然恢复.md。 |

## Fresh checks

环境：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-debug/Script.pvf`。

- `dotnet build Server/86JP.Server.slnx -c Release -v quiet`：exit 0，0 warning、0 error。
- `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed"`：exit 0，1669/1669。
- 同样命令运行 Protocol.Tests：exit 0，1007/1007；Host.Tests：exit 0，17/17。
- 评审后再次构建运行 Release 的 `--filter FullyQualifiedName~WeaknessTests`：exit 0，18/18。
- `git diff --check`：exit 0；contract/spec ready、revision 1 的 YAML 读取验证通过。

## 已有失败的范围说明

Networking.Tests：exit 1，52通过、14失败。未修改的 main `88741631` 上独立构建Debug全量对照也是52通过、同名14失败，全部为缺 ICharacterEntryTable 的既有fixture启动依赖问题。失败集合经脚本比较完全相同，独立评审判定不属于本次回归；不报告网络全量通过。

完整本地输出在任务目录 application-tests.log、protocol-tests.log、host-tests.log、networking-tests.log、baseline-networking.log、release-build.log。关键结论和范围已在本文件保存，原始日志不作为发布工件。

实机完整生命周期尚待用户操作。金币恢复、免虚弱资格与疲劳不在本次完成声明内。
