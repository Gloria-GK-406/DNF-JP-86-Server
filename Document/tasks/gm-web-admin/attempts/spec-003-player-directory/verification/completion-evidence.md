---
kind: rks-verification
decision: pass
---

# Spec003 提交前验证

2026-10-02，主控在独立完整审查 approved 后验证当前冻结产品；基线416f26c9919383b58b0bd0e7179a99734fe53b48，分支feature/gm-web-admin。27个Server/Web变更指纹保持 `98016BD0667F676E724FD252C1F2E27DF42FA0F3937539199E9AA127E736A124`，规格指纹与批准记录一致。

## Required checks

以下命令均实际运行、退出码0。详细日志保留于git忽略的 `artifacts/gm-tests/spec003/`。

| 检查 | 当前结果 | 日志 |
|---|---|---|
| `dotnet test Server/86JP.Server.slnx -c Release --no-build -m:1 --logger 'console;verbosity=minimal'`，显式设置PVF_ARCHIVE_PATH | 八个测试项目合计5317通过、0失败；仅2条要求“没有档案”的Infrastructure场景因当前存在PVF而有条件跳过。Application2663及Protocol1231均无PVF缺失跳过；Host54/Persistence396/HttpApi50/Networking86/Infrastructure757/Pvf80通过。默认opt-in browser入口不驻留，另行实际启动。 | fresh-solution-tests.log |
| `npm test -- --reporter=verbose` | 25通过，含原10条认证及15条目录交互判据 | fresh-frontend-tests.log |
| `npm run build` | TypeScript与生产构建通过；保留既有大于500kB的chunk提示，未隐藏警告 | fresh-frontend-build.log |
| `dotnet build Server/86JP.Server.slnx -c Release --no-restore` | 0警告、0错误 | fresh-build.log |
| `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-build --filter FullyQualifiedName~GmPlayerBrowserFixtureTests.ExplicitBrowserFixture`，DNF_GM_BROWSER_FIXTURE=1、VERIFY_ONLY=1、fresh artifacts子目录 | 实际生产页面GET200，45账号/2在线账号/1已入场角色正确；1通过、Host正常关闭、结束后18083监听数0 | fresh-browser-fixture.log |
| `dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release --no-build -o artifacts/gm-directory-publish/spec003-fresh` | 发布成功，index及2个JS/CSS资源与dist逐个SHA256相等；HTML为F6EC8E3E00E4BC1AB315AD4359EBCBCF6B3E0461EFEF1793E75383F568785286 | fresh-publish.log及终端哈希核对 |

只读PVF为 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，与现服配置一致，三客户端文件SHA256均为 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`；验证前后不变，没有复制或修改客户端/PVF。此前生产者工作区自动解析找不到档案的跳过记录保持历史事实，本轮显式指向现有档案补齐内容回归。

## Success conditions and constraints

| 合同条件 | 当前证据与判断 |
|---|---|
| SC-1 全服查询/详情/余额/特殊姓名 | 实际Kestrel目录HTTP与SQLite判据在fresh Host测试通过：筛选前分页、同次total/items、越页/无结果、ID及归属、三币边界、name_bytes严格诊断与字面搜索；实际页面完成关联账号、无角色账号及异常姓名查看。 |
| SC-2 运行态/位置/频道 | fresh Host的真实状态所有者、选角准备/发布/退出、generation、移动换区、旧exact清理、读取交错、local/public端口区别、未知频道及网络就绪判据通过；浏览器后页/仅在线结果与模拟领域事实一致，current/saved/unavailable保持区分。 |
| SC-3 页面/权限/架构及旧流程 | 五个GET权限/字段/输入及原认证审计、命令注册和架构回归通过；25条前端行为与实际页面操作通过；Release构建/发布成功，独立drift aligned、change-review approved。 |
| 约束 | 明确白名单、只读typed接缝及原运行态所有者、单Host/DI经独立完整源码审查；成功/无结果/失败数据库与运行态无修改判据通过。HTTP没有直接持久化或Protocol依赖；source checkout、现服与真实玩家资料未改。 |

AC-001..AC-009由上述fresh HTTP/运行态/SQLite/UI与旧流程验证分组覆盖，具体判据映射见implementation-report。AC-010实际浏览器操作及五张已查看截图见 [browser-verification.md](./browser-verification.md)；常驻runner因应用中断无可恢复退出码未冒称通过，本轮独立VERIFY_ONLY启动/结束判据已通过。不以mock截图代替服务端事实。

本结论只验收当前查询child，允许工作区控制器进行一个本地结果提交；邮件、点券及最终CI/验收表仍由后继child承担。页面验证不是ClientVerified；大型前端chunk是非阻断发布优化余项，交最终集成阶段评估。
