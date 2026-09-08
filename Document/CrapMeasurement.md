# CRAP 变更风险测量

日期：2026-09-08。被测生产基准：`addbeb52`；每次结果以自身快照清单为准。

在仓库根用 PowerShell 运行：

```powershell
$env:PVF_ARCHIVE_PATH = 'E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet run --project Tool/CrapMeter/CrapMeter.csproj -c Release --
```

工具依次构建 Server、通过真实解决方案入口运行七个测试项目、采集覆盖率、计算方法级 CRAP，并在 `.crap/YYYY-MM-DD_HH-mm-ss/` 写入结果。该目录被仓库 `.gitignore` 排除；每次新建，同秒加后缀，已有运行不被覆盖或删除。历史结果仅留在本地。

**当前是部分测量。** Host 集成测试启动子进程，但其命中未进入覆盖成果；原因尚未进一步定位。Host 的源码方法标为 `Missing`，不计算 CRAP。子进程还调用其他生产模块，所以其他模块的覆盖率也可能偏低、CRAP 可能偏高。`Run.Status=complete` 仅表示声明的采集流程成功，`MeasurementStatus=partial` 表示覆盖证据存在上述边界；不能称为全域健康基线。

## 前置条件与参数

需要仓库要求的 .NET SDK、可还原的 NuGet 依赖、可读的 PVF 档案。现有 Host 进程可能锁住构建产物，入口检测到 `Dfo.Server.Host` 会停止。测试会启动临时数据库及本机 TCP 端点。

| 参数 | 含义 |
|---|---|
| `--repo <路径>` | 仓库根，默认从当前目录向上查找 `.git` |
| `--pvf-archive <路径>` | 替代 `PVF_ARCHIVE_PATH` |
| `--configuration <名称>` | 被测 Server 构建配置，默认 Debug；入口的 `-c Release` 是工具自身配置 |
| `--top <正整数>` | 全域及每个聚合范围的热点数量，默认 30 |
| `--simulate-failure after-tests` | 测试执行后注入失败，用于核验失败留证 |

退出码：`0` 为采集流程成功；`1` 为测试失败或对账不成立的部分采集；`2` 为构建/覆盖采集失败；`70` 为记录了异常的失败；其他非零为参数或前置条件失败。开始后的失败会保留运行目录、清单和已取得日志；非法参数、缺 PVF 等前置失败不启动采集。

每个运行中的 `rerun.ps1` 保留此次配置、PVF 路径和参数，可用 `pwsh -NoProfile -File '<运行目录>/rerun.ps1'` 复跑到一个新目录。它仍要求稳定路径上的工具实现与仓库存在；不依赖上一运行的临时产物。带失败注入参数的脚本会再次复现失败。

## 产物与证据

| 文件 | 用途 |
|---|---|
| `manifest.json` | 范围、版本、参数、测试项目及退出码、运行状态、测量限制、逐件产物索引及存在状态 |
| `snapshot-before.json` / `snapshot-after.json` | 提交号、脏路径、Server 源码/测试/配置及工具实现的逐文件 SHA256；内容变化时拒绝计算 |
| `raw/source-inventory.json` | 独立于覆盖率建立的源码函数清册及复杂度 |
| `raw/coverage/*.opencover.xml` | 原始覆盖证据；明细中的报告相对路径和点 Key 可回溯到此 |
| `raw/tests/` | 真实 TRX、测试附件与原始覆盖报告 |
| `function-details.json` | 主明细：函数 ID、完整签名、范围、复杂度、覆盖点身份/命中、状态、未舍入 CRAP |
| `function-details.csv` | 数值摘要，完整身份和原始证据引用以 JSON 为准 |
| `distributions.json` | 类/文件/项目/全域分布，每个范围含 TopFunctions、分档计数/占比、分位数和四象限 |
| `distribution-{whole,project,file,class}.md` | 可读分布表；各范围 Top N 的位置见 JSON |
| `report.md` | 先讲证据限制，再讲分布、机械候选、已核查的源码优先项和改进选项 |
| `coverlet.runsettings`、`rerun.ps1` | 覆盖配置与复跑入口 |
| `logs/` | 实际 SDK、构建、详细测试输出，以及失败时的异常日志 |

清单同时记录 PVF 的 SHA256。失败或尚未执行的产物以 `Exists=false` 呈现，不伪造空成果。两次运行比较前要核对清单、函数宇宙、工具、测试集合和输入档案。

## 计算口径

`CRAP(f) = C(f)^2 × (1 − Cov(f))^3 + C(f)`，只在函数级计算。类、文件和项目仅有函数分布；均值/总和只是对规模敏感的补充。

范围来自 `Server/86JP.Server.slnx` 中的生产项目，包含空 Utilities 项目。排除 `Server/tests`、`Tool`、`ClientHack`、`bin`、`obj`。当前按项目目录内源码扫描，不是通用 MSBuild 编译项求值器；本仓目前无自定义 Compile 项或条件编译。新增这些配置时需先调整采集口径。

- 方法、构造函数、运算符、终结器、访问器分别建立身份，签名保留参数修饰符；自动属性/字段式事件逐访问器记录。顶层语句形成一个入口函数。
- lambda、局部函数、async/迭代器状态机归入所在源码函数；字段/属性初始化器、record 合成成员、主构造函数等无法归入声明函数的点单独记账。
- 复杂度来自 Roslyn：`1 + 判定点数`。判定点集合写在清单，包含循环、条件、switch 分支、异常过滤、短路与空合并等。IL 复杂度只供交叉核对。
- 覆盖率使用序列点命中比例，不等于唯一源码行比例；分支覆盖另存，不参与 CRAP。
- 按模块、文件、源码区间关联覆盖点；同行访问器再按 IL 方法身份区分，不能唯一判断则标歧义。
- 分位数采用最近秩法。风险分档为 `≤5 / (5,15] / (15,30] / >30`，占比分母为该范围已算函数数。四象限以全域复杂度 P75 与 Cov=0.5 切分。

| 状态 | 含义 |
|---|---|
| `Computed` | 可信输入已关联，计算了 CRAP |
| `measuredZero=true` | Computed 的子集：报告有可执行点，测试宿主未命中；与未取得证据不同 |
| `Missing` | 缺报告、缺映射或已知子进程采集缺口；CRAP 为空 |
| `Ambiguous` | 多个竞争映射无法可靠区分；CRAP 为空 |
| `Excluded` | 无可执行体且无可执行点的声明/访问器；CRAP 为空 |

模块覆盖点集合冲突、源码/配置/工具内容变化或 PVF 变化会直接失败，保留原始证据而不计算跨快照 CRAP。

覆盖率证明执行过，不证明断言充分。报告的自动分类只用来选择检查对象。首批源码优先项保存在工具的 `Reporting/SourceFindings.cs`，仅当所审阅源码与测试 SHA256 匹配且方法仍越线时展示；不匹配时要求重新审阅，不沿用旧建议。本任务没有重构或补测生产热点。

## 工具维护

```powershell
dotnet test Tool/CrapMeter.Tests/CrapMeter.Tests.csproj --nologo
```

工具与测试独立于 Server 解决方案，使用自身局部中央包配置，避免影响其他 Tool 项目。新增输出先在清单注册；修改公式、函数粒度或关联规则后，不要直接比较旧口径分数。
