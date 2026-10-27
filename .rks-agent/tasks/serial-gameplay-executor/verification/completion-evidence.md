---
kind: rks-verification
decision: pass
target: spec-001
product_commit: 0ecb9af351cfddad5f636872e734c2830236aa78
---

# spec-001 最终核验（2026-10-24）

独立核验a_final_verifier，按verification-before-completion只读核对，主控复算相同。A在已披露附条件和残余下可封存；不代表B、根任务、Astra或A21实机完成。

## 对象和前置评审

根契约r4、A子契约r2、SpecR4，产品冻结0ecb9af351cfddad5f636872e734c2830236aa78。核验HEAD一致，Server/Tool相对冻结diff为空。三维Round2、TASK35快审、drift和聚合完整审查approved，drift aligned。证据见reviews/spec-001-final-review-code.md、-tests.md、-spec.md、spec-001-drift-review.md、spec-001-change-review.md和task-35-result-review.md（均相对任务根）。产品、断言、超时、xUnit类并行配置未改。

## 新鲜构建和完整测试

依据logs/spec-001-native-raw.txt、-commands.json、-violations.txt，原始取证evidence/spec-001-native-ev/。完整Release --no-incremental构建exit0，0警告0错误，21.09s。测试--no-build、console;verbosity=detailed、--blame-hang-timeout 5m、-m:1；只串行项目，程序集内并行保持。test exit1只因两原基线，不伪称exit0。commands退出依次0/0/0/1/0（HEAD、冻结、build、test、取证）。

| 项目 | 通过 | 失败 | 跳过 | 总计 |
|---|---:|---:|---:|---:|
| Pvf | 80 | 0 | 0 | 80 |
| Application | 4608 | 2 | 16 | 4626 |
| Host | 197 | 0 | 0 | 197 |
| HttpApi | 57 | 0 | 0 | 57 |
| Infrastructure | 968 | 0 | 2 | 970 |
| Networking | 190 | 0 | 0 | 190 |
| Persistence | 477 | 0 | 0 | 477 |
| Protocol | 1753 | 0 | 0 | 1753 |
| 合计 | 8330 | 2 | 18 | 8350 |

80＋4626＋197＋57＋970＋190＋477＋1753＝8350；8330＋2＋18＝8350。失败与baseline.md原名、异常类型和消息一致：

- DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource
- DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot

均InvalidDataException：Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'. 无新增失败、无SQLitePCL.sqlite3释放异常。数量7872＋(596＋35)－153＝8350，新增631/删除153/净增478。完整日志结束04:04:12 UTC。

## 表征、金样本与取证

表征10通过/0跳过；Characterization/Golden自f01dc226引入至冻结diff为空，表征源码仅两处取消Skip，原断言不变。完整选角SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript通过。

独立重放violations.py exit0，73＝69 write-off-executor＋2 bus-leaked-flow＋1 queue-leaked-flow＋1 network-wait。七程序集各一正向对照齐，全部预期反例计数正确；反例/对照外0、无测试帧0、exe0。exe0仅表示未看到打印异常，不证明吞掉未打印的异常不存在。

## Success Conditions

| 条件 | 决定性核验 |
|---|---|
| SC-1 | 唯一GameplayJobHost派生GameplayExecutor；分流键选同一执行器；dispatcher作业内建保护/帧。顺序/自我等待/入口判据本轮通过；入口与外部清单见t009-docs-and-appendices和t009-criteria，AC006附条件保留 |
| SC-3、SC-7 | 完整Networking/Protocol/表征通过，已提交条目刷出、网络等待拒绝、怪物死亡发布失败封锁等本轮通过。t006-closure/汇报§3/§9/附录E/G逐项保护仍按原组合证据和适用范围，不升级 |
| SC-4 | 42删除名规则生产0命中，SemaphoreSlim只获准5文件6处；删锁判据/反例通过，短锁/PVF缓存边界保留 |
| SC-5、SC-6 | 事件、链、深度上限及处置测试通过；深度极限/超限日志抽核通过。RQ050精度/F131附条件未消除 |
| SC-8 | 构建0/0，8350仅两原基线、18skip；表征10/0、金样本不变、完整选角成立。数量/断言变化历史分类与TASK35增量分述 |
| SC-9 | measurement/m3-summary.md有18次合成测量：作业max269.3ms、冷启停顿1231.1ms、吞吐1163包/s，均按用户2秒线接受。TASK35未改Executor/Job/Outbox/Outbound热路径，TickAsync生产者等待端、XO1等价，支持沿用M3；不声称重测，不把测试/套件耗时算CMD指标 |
| SC-10、SC-11 | 汇报三默示决定/VC1～10/26外审重点/18B交接/附条件/CH1-2齐。当前测试修改363/新增114/删除7＝484路径；C#356/99/7，另新增15Golden hex。旧分类标TASK34冻结，TASK35七文件35例另列 |
| SC-12 | T1～T6位置/理由/B删除方向齐；AsyncLocal8处＝T4六处＋作业/事务各一；生产Obsolete属性0。细粒度管线/handler编排明确是A过渡由B删除，不冒充B删净 |

## 约束和保留边界

七根约束按t009-closure§7.1及最终汇报已明示裁定/附条件，未扩大授权或放宽断言。F88/F93由B迁移前补；F127/F130 B首单元；F131/F132未修。CH1/CH2主控接受低风险后继处理。合成测量不是实机。保持本地分支，不推送/PR/收尾，等用户Astra通过与明确收尾。核验时Server/Tool冻结成立。

## 被排除的诊断运行

spec-001-final-*跨低电量休眠5516s（直接Sleep/Wake证据）；spec-001-serial-*有FSEvents挂起，被中断，均不算通过。本机单例探针只作环境鉴别。native完整运行保留原断言/期限/类并行；不声称每个早期超时的具体因果都逐项证明。环境恢复及脚本均已保存，见spec-001-environment-recovery.md。最终汇报§6/§11已同步本轮实际数字及环境边界。
