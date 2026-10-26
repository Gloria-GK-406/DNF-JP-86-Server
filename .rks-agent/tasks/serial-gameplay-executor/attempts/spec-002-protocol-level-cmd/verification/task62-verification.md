# TASK-62 验证记录

最终证据采用复合来源：r5 的 fresh Release Rebuild（零警告、零错误）及 App／HttpApi／Host，r3 的 Protocol，以及 r4 的 Networking／Persistence／Infrastructure／Pvf。r3／r4 已完成八项后，最后只把 Register／Login／GmLogin／Logout／Audit 排除出统一业务 catch，保原无 Safe 路由的 CMD body 失败归属；因此重新构建并完整复跑受影响三项，其余五项沿用该修正前完整证据。不称全部来自同一 r5。所有正式项实际 require_escalated、默认运行时、原 xUnit 并行、5m、detailed、独立 DFO/TRX，严格串行并自然退出；起止、命令、权限及 exit 均在各 run.json。

| 项目 | 来源 | 通过 | 失败 | 跳过 |
|---|---|---:|---:|---:|
| Application | task62-final-r5 | 5980 | 2 | 16 |
| Protocol | task62-final-r3 | 1795 | 0 | 0 |
| Networking | task62-final-r4 | 203 | 0 | 0 |
| Persistence | task62-final-r4 | 484 | 0 | 0 |
| Infrastructure | task62-final-r4 | 974 | 0 | 2 |
| Host | task62-final-r5 | 208 | 0 | 0 |
| HttpApi | task62-final-r5 | 59 | 0 | 0 |
| Pvf | task62-final-r4 | 80 | 0 | 0 |

共 9803＝9783 通过＋2 原失败＋18 跳过。

两个失败仍为 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`、`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`，完整 message 与 task61 基线相同：`System.IO.InvalidDataException : Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`。十八条跳过的逐名、原因与重数不变；未调用 PVF 偶发例外。逐 display/outcome/重数库存、原 DFO parser 的 67 负例与七程序集正对照、outside/exe/orphan 归零见 `task62-final-combined-executor-*` 本机输出。

相对 task61 的三项 r3／其余五项 r2 基线，新增二十五、零删除：App 十七个固定审计／来源／只读取消场景及一个精确注入正例，Host 四个请求取消持久审计及一个实际 HTTP 搬运边界，HttpApi 两个 parser 失败仍入队的原 cause 场景。原事务、权限、并发幂等、receipt、提醒、body、HTTP 输出及真实网络断言保留。

四十二来源为三十九 group 与三 self，同一 `HttpRouteCommand`、固定 operation 和封闭纯输入。framework next 只在执行器外解析，失败／取消也携原 cause 按原入队边界进入；CMD 在 fresh scope 中显式取得 request、domain 与工作单元，不传 HttpContext／IResult／业务 delegate，不嵌套 dispatch。原查询除四个 Events/Joust ReadOnly 外均保持 Transactional；Cera Normalize、Mail Prepare 没有改为 ReadOnly。

Cera／Mail 保准备、业务写及独立失败审计阶段，失败审计仍有 Rejected／RolledBack／实际 Unknown。健康领域拒绝只在真实 action 内结果化并 RequireRollback；Mailbox 即时 Abort 算法不改，以真实内部 AbortAction 事实识别，未扩大 Self。真正失败保持 sticky，固定审计只消费本次合法 operation/input 与实际阶段事实；普通工作、跨 job、重复 audit、guard 和独立 rollback／scope／dispose 错误不获恢复。最终异常在完整收口后绑定 command 引用和 job/chain 身份，外侧只映射本次签发的数据；旧 carrier 连同一 command 的新请求也不能消费。普通已处理的审计／提醒错误仍记一条原类别 Error，已由 executor 记录的 carrier 不重复记。

请求取消的两条参数化业务表征先在主控创建的旧 b284ad0f 隔离树跑四例全绿，再在新树同断言四例全绿：`task62-cancel-oldgreen`／`task62-cancel-newgreen`。纯测试 patch 为本机 `/private/tmp/task62-cancel-puretest.patch`，仅 `GmRequestedCancellationAuditTests.cs` 与 GmTestHost 的 test-only feature wrapper。这是实际 Kestrel 和 SQLite audit；装饰器在指定领域入口取消同一 RequestAborted，不能冒称真实客户端断网验收。另 `task62-carrier-boundary` 五例通过，第二轮 ReplayBus 只证实际 HttpGameplay 的最终搬运守卫，不冒称第二轮真实网络请求。

执行期 r1/r2 暴露部分测试容器缺原宿主 readiness/catalog 的装配事实。GameLogin 和四个轻量 socket fixture 补明确测试 readiness；纯 Persistence 与 Redemption 的前置凭据采用真实 domain 事务 driver，不作为 HTTP 入口证据，测试主体实际 GameLogin／网络断言未绕过。GM prepared 阶段的旧强事实同样用真实 domain 事务 driver；默认 TestCredentials 仍走生产 typed HTTP CMD。r2 后一次 focused 生命周期用例及 r3 完整 Networking 的 GameExit 用例均在 AddListen 报 SocketException(48) 地址占用后连接被拒；前者原用例独立复跑通过，最终采用 r4 替代完整 Networking 的 203 项全绿。未证明具体占用者，也没有修改端口分配器、超时、并行或允许失败清单。

迁移账只关闭四十二 HTTP/GM source 与三十三个失去最后生产消费者的 fine，共七十五行；ID、original、顺序与非目标行不变。严格检查和八个坏例均通过。来源 46 closed／27 pending／12 retained，fine 185 closed／19 pending；其他二十七来源及保护／过渡项不提前闭合。AGENTS 只增名字。能力裁定由主控记录 phase2-backlog；没有 A21 实机或专项性能完成声明，未推送、PR、合并、tag 或清理工作区。
