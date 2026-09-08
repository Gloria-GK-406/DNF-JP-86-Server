---
kind: rks-verification
decision: pass
---

# 完成验收证据

2026-09-08，TaskContract revision2；任务分支 change-risk-is-measured-repeatably，提交前基线 addbeb52bffdd329a85f292f6784200d1146bb28。独立完整变更评审及最终证据复审均 approved，见 ../reviews/change-review.md。下述证据对应当前内容；repository-before-acceptance.json 验证793个源码/测试/配置/工具文件与成功运行结束快照仍完全一致。

## 真实运行

所有命令均在任务 worktree 根目录、新 PowerShell 中运行，PVF_ARCHIVE_PATH 为 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。被测SDK10.0.302，PVF SHA256见各清单。

| 运行 | 命令 | 结果与日志 |
|---|---|---|
| 21-10-51 | `dotnet run --project Tool/CrapMeter/CrapMeter.csproj -c Release --` | 移走38组bin/obj后从新shell构建，2392通过/2跳过/0失败，退出0，complete；fresh-run-1.log |
| 21-14-59 | 运行21-10-51生成的rerun.ps1 | 2391通过/2跳过/1失败，退出1，partial；fresh-run-2.log；失败原样保留 |
| 21-21-45 | 运行21-10-51生成的rerun.ps1 | 2392通过/2跳过/0失败，退出0，complete；fresh-run-3.log |
| 21-25-32 | 上述dotnet入口加 `--simulate-failure after-tests` | 人为异常按预期退出70、failed；之前真实测试2391/2/1，不称健康运行；failure-injection.log |
| 工具判据 | `dotnet test Tool/CrapMeter.Tests/CrapMeter.Tests.csproj --nologo` | 81/81通过，退出0；tool-tests.log |
| 原始服务端判据 | `dotnet test Server/86JP.Server.slnx --nologo --logger 'console;verbosity=detailed'` | 构建并运行7项目，不启用覆盖采集；2392通过/2跳过/0失败，退出0；server-regression.log |

运行目录完整名称均为 `.crap/2026-09-08_<上表时分秒>`。真实SQLite失败的两个不同栈根未归入AGENTS旧豁免；单例与随后的完整判据通过，原失败与尚未证实的清池竞争嫌疑见 sqlite-diagnostic.md。

## 成功条件逐项核验

| 条件 | 结果与证据 |
|---|---|
| SC-1 持久可复跑 | Tool/CrapMeter及钉住配置在仓内。38组缓存先校验边界后移走保留，原路径全部不存在，再成功构建运行；cache-move-targets.json、build-clean-targets.json、takeover-and-rework.md、fresh-run-1.log。未依赖移走缓存。 |
| SC-2 新目录与历史不变 | 两次成功运行21-10-51/21-21-45并存；history-validation.json核对旧105文件及首轮45文件，共150个SHA256全未变。 |
| SC-3 输出不追踪 | repository-before-acceptance.json：git ls-files .crap为空，check-ignore来源为随仓分发的 `.gitignore:42:.crap/`，git status不含.crap。 |
| SC-4 自描述与失败留证 | 两份成功清单的每件产物、格式、路径/存在标志均核验；故障注入的清册、TRX、前后快照、异常与复跑参数保留，未产生的明细Exists=false。verify_history.py逐TRX核对其真实失败计数，不伪造全绿。 |
| SC-5 状态完整 | 两次各2797=2269Computed+526Excluded+2Missing，0Ambiguous；首轮296实测零覆盖属于Computed子集；Host缺口明确Missing/CRAP=null。run-1-validation.json、run-3-validation.json。 |
| SC-6 同快照与公式 | 两次各全部函数独立复算；793输入文件前后及跨运行相同，PVF哈希一致；25449原始点=21795已归属+3654未映射，逐点Key/module/file/line/hits可回溯。下文列四组手算。 |
| SC-7 四层聚合 | verify_run.py独立复算1543个类/文件/项目/全域范围，计数、最近秩分位数、全部分档占比、四象限、TopFunctions均一致；包括零成员Utilities项目。 |
| SC-8 真实测试 | 原始日志/TRX及实际进程退出码齐全；两次成功采集各2392/2/0，失败采集保留partial或failed而不替换。 |
| SC-9 报告次序和局限 | report.md先证据边界，再分布、候选、已读源码优先项与改进选项，最后列局限；独立复审通过。 |
| SC-10 发现可核查 | 函数身份/复杂度/覆盖/CRAP/分档与分位来自当前运行；首批3个源码优先项有源码和测试内容哈希守卫，事实与推断区分，未修改生产热点。 |
| SC-11 服务端无退化 | 最终原始判据2392通过/2跳过/0失败、退出0；两处曾失败用例在server-regression.log第740、741行通过。git diff对Server/src及既有Server/tests源码为空；只新增测试覆盖配置。 |
| SC-12 维护说明 | Document/CrapMeasurement.md给出真实入口、参数、产物、状态、口径与限制；按该入口和生成的脚本均已成功复跑。 |

## 手算与映射样本

公式 `C²×(1−Cov)³+C`，未先聚合复杂度/覆盖率。

| 样本 | 手算 | 输出 |
|---|---|---:|
| ChunkStore.ChunkCount.get（低） | 1²×(1−1)³+1 | 1 |
| ChunkStore.Decode（中） | 6²×(1−17/18)³+6 | 6.006172839506172 |
| PvfTutorialRewardDefinitionSource.Read（高） | 13²×(1−0)³+13 | 182 |
| PvfMetrics.ChunkHits.get（实测零） | 1²×(1−0)³+1 | 2 |

mapping-samples.json补充AC-007的7个实测声明：OpenScript(int/string)重载；StateSynchronizationPayloadCodec两个分部文件；AddGameMessageDecoder泛型方法；IGoldDropTableSource.Load显式接口；CommandAsync异步状态机。每项含源码摘录、路径/行号、函数ID、原始点Key和数值；覆盖点由verify_run.py全量回溯核对。相应工具回归测试也已通过。

## 复跑差异与保留风险

两份成功运行使用同793输入文件、同2797函数身份/复杂度/状态，23个CRAP>30候选不变。唯一数值差异是ChunkStore.Get：Cov从22/27变为26/27，CRAP从4.101610526850582变为4.000812884214804。原始点107–110行在第二次由0命中变为2次，正是锁外解码后的锁内缓存竞争分支。执行时序造成差异是有源码支持的推断，未记录具体线程交错；不宣称覆盖率跨运行完全确定。

Host子进程命中未进入覆盖成果，2个Host成员Missing，其他模块也可能漏计子进程执行；Run.Status=complete仅表示采集流程成功，MeasurementStatus=partial，不能称全域健康基线。生成成员/初始化器未映射点单独记账。覆盖命中不证明断言充分。SQLite两个新增失败栈根原因未证实，已原样留证及实际复跑，未改动热点或既有测试来消除红灯。

verify_history.py最初错误要求故障注入运行的真实测试必须全绿，随后改为直接核验原TRX真实结果；TRX的notExecuted汇总字段为0但逐结果有2个NotExecuted，因此按逐结果并与total-executed独立对账。最终history-validation.json为pass。上述是验收脚本修正，没有改动已评审工具。

## 接受边界

暂存后默认git空白检查将Windows原始证据的CRLF与日志尾空格报错。未重写原始日志或已测源码来消除这些字节；以 `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --cached --check -- . ':(exclude).rks-agent/tasks/change-risk-is-measured-repeatably/verification/*.log'` 验证其余全部变更，退出0。仅原始日志免空白风格检查，源码/配置/说明/证据脚本均仍检查行尾空白、文件尾空行和缩进；CRLF被识别为合法行结束。

当前验证通过，支持在本任务分支进行一次已授权的本地成果提交，完整包含实现、工具测试、说明、工作流与持久证据。提交前HEAD及main均为addbeb52；.crap成果与缓存备份不提交。不合并、不push、不建PR、不删除分支/worktree。提交后从Git读取实际提交号并证明worktree干净、父提交为原基线、main未动；不为写入自身提交号再修改本文件。
