# spec003 实施报告

基线：`416f26c9919383b58b0bd0e7179a99734fe53b48`，分支 `feature/gm-web-admin`；本轮实施未提交。规格、审查和根控制记录不属于本生产者的改动。

## Changed

- Data/Abstractions：玩家筛选、存储投影与 HTTP 投影；typed 全服只读 Persistence/领域接口；原在线所有者的窄账号快照/精确已入场归属查询；游戏就绪接缝新增 NetworkEnabled。
- Application/Persistence：五类查询命令（事务仅用于一致读取）、SQLite 字面搜索/稳定分页、原始姓名字节诊断、当次运行态采样、真实频道与位置投影。HTTP 有明确白名单、权限元数据与通用中文错误，异常留下服务器诊断。
- Web：中文概览、账号/角色查询、ID 关联详情、搜索/在线筛选/分页/刷新与各类反馈；共享只读请求 guard。403 清除整个查询视图及 QueryPlayers 权限，并推进身份 generation；401 返回登录。原10条认证 UI 测试保留。
- Tests：8条真实 Kestrel 目录/采样判据、15条新前端行为判据及 opt-in 实际页面夹具。更新 GmTestHost 的配置/Services 接缝；原 Protocol 命令注册图和 HttpApi ReadyGame 夹具补齐宿主就绪事实。
- `Document/gm-web-admin.md`：查询语义、HTTP 与隔离页面验收入口。

## Why

在线名单先采样并核验精确发布/归属/位置，再作为同次数据库筛选参数；total/items 使用同一名单与 SQLite 事务视图，不在分页后过滤，也不增加全服缓存。姓名直接读原始 name_bytes，查询不复用会合成缺失字节的花名册入口。频道解析复用现有 policy，保持本地监听与公网端口的部署语义。

生命周期测试中的同账号第二张票据会被既有后台维护立即移除旧连接，因此用于观察双连接去重的夹具暂停该后台维护并显式清理，仍使用真实认证、OnlineStateRegistry、选中会话、发布和位置服务。产品维护行为未改变。

## Checks

全部共享 .NET 构建/测试串行执行，以下最终命令退出码均为0；原始输出保留于工作区 `artifacts/gm-tests/spec003/`（git忽略）；以下所有未带目录的 .log 名也指此目录。项目引用静态检查及 `git diff --check` 通过；查询领域/持久化实现未出现写 SQL、GetOrCreate 或通知调用，GM Web 的项目引用未增加 Persistence/Networking/Protocol。源文件与新增文档为 UTF-8 无 BOM、LF。

| 检查 | 实际结果 | 原始日志 |
|---|---|---|
| `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --logger "console;verbosity=detailed"` | 54通过；含目录/采样8条，opt-in browser 常态分支不启动 | `artifacts/gm-tests/spec003/host-tests.log` |
| `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --logger "console;verbosity=detailed"` | 2445通过 / 186跳过（Script.pvf不可用） | `artifacts/gm-tests/spec003/application-tests.log` |
| `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj --logger "console;verbosity=detailed"` | 396通过 | `artifacts/gm-tests/spec003/persistence-tests.log` |
| `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --logger "console;verbosity=detailed"` | 1167通过 / 59跳过（PVF内容条件） | `artifacts/gm-tests/spec003/protocol-tests-final.log` |
| `dotnet test Server/tests/Dfo.Server.HttpApi.Tests/Dfo.Server.HttpApi.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed"` | 50通过 | `artifacts/gm-tests/spec003/http-api-tests.log` |
| `npm test -- --reporter=verbose`（Web/gm-admin） | 25通过（原10 + 新15） | `artifacts/gm-tests/spec003/frontend-tests-final.log` |
| `npm run build`（Web/gm-admin） | TypeScript检查、Vite生产构建成功；保留既有500 kB chunk提示 | `artifacts/gm-tests/spec003/frontend-build-final.log` |
| `dotnet build Server/86JP.Server.slnx -c Release` | 0警告 / 0错误 | `artifacts/gm-tests/spec003/release-build-final.log` |
| `dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release --no-build -o artifacts/gm-directory-publish/spec003` | 发布成功；wwwroot/gm/index.html 与 dist 的 SHA-256 同为 F6EC8E3E00E4BC1AB315AD4359EBCBCF6B3E0461EFEF1793E75383F568785286，JS/CSS随包 | `artifacts/gm-tests/spec003/publish.log`；校验值为终端自检 |
| 显式 browser fixture + VERIFY_ONLY（下述入口） | 独立18083、实际页面GET200、45账号/2在线账号/1已入场角色正确；已正常关闭 | `artifacts/gm-tests/spec003/browser-fixture-selfcheck.log` |

Protocol 初轮失败为稀疏 module-only 命令注册夹具缺少宿主提供的 IGameServerReadiness，见 `artifacts/gm-tests/spec003/protocol-tests.log`；Release 初轮失败为旧 ReadyGame fake 缺新增属性，见 `artifacts/gm-tests/spec003/release-build.log`。均为 fixture 修正，不计行为 RED。首轮生命周期预期3而实际2的后台清理证据在 `artifacts/gm-tests/spec003/directory-expanded-1.log`，按上述原因控制夹具后6条通过（`directory-expanded-2.log`）。

### TDD 行为证据

| 保护结果 | RED 命令 / 退出码 / 判据 | GREEN |
|---|---|---|
| 全服分页/余额/字段白名单 | `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --filter FullyQualifiedName~GmPlayerDirectoryHttpTests --logger "console;verbosity=detailed"`；退出1；允许管理员实际GET /accounts预期200、返回403（路由尚未存在，被默认封闭边界拒绝）；`red-directory.log` | 同命令退出0，`green-directory.log`；扩展见 host-tests.log |
| 中文角色查询入口 | `npm test -- --reporter=verbose src/PlayerDirectory.test.tsx`；退出1；页面仍占位，缺“角色姓名搜索”；`red-ui.log` | `green-ui.log`，退出0（11通过） |
| 详情403清除所有查询资料 | `npm test -- --reporter=verbose src/PlayerDirectory.test.tsx -t "接口失权清除"`；退出1；详情失权后列表仍保留角色甲；`red-ui-permission-4.log` | `frontend-tests-final.log`，退出0 |
| 撤权后的旧身份轮询不能恢复权限 | `npm test -- --reporter=verbose src/PlayerDirectory.test.tsx -t "403后的旧身份轮询"`；退出1；旧poll成功后无权限提示消失；`red-permission-poll-race.log` | 同判据包含于 frontend-tests-final.log，退出0 |

`red-ui-permission.log`、`red-ui-permission-behavior.log`、`red-ui-permission-3.log` 中的按钮 selector/loading label 与一次错误 cwd 是测试设置错误，未当作行为 RED；修正后才记录上述有效失败。前端扩展中的中文按钮空格、抽屉中文关闭标签和 jsdom 动画修正同样不计行为 RED，最终测试关闭动画而生产界面保留原行为。

### AC 证据

| AC | 已有实现证据 |
|---|---|
| AC-001 | EveryQueryUsesCurrentPermission... 实际五类HTTP的匿名/玩家票据/允许/撤权/过期/远程明文；DirectoryPages... no-store；原Host监听隔离矩阵及原认证CSRF回归保持通过。 |
| AC-002 | DirectoryPages...、FiltersPagesBoundaries... 与 RealRuntimeLifecycle...：超过一页、组合/字面条件、边界/重复字段、空库/越末页、不存在、数据库前页之外的在线对象；SamplingHttp 同名单交错。 |
| AC-003 | NamesAreStrict... / DirectoryPages...：无角色账号、多角色、删除记录、身份/归属/等级、0/普通/int.MaxValue三币、HTTP禁止字段及成功/无结果/失败只读对比。 |
| AC-004 | NamesAreStrict...：原始字节与兼容文本冲突、中文/literal %/_/引号/HTML、NULL missing、非法FFFE；前端无效ID/HTML文本行为。 |
| AC-005 | RealRuntimeLifecycle...：匿名→登录未选角→准备→发布未Place→入场→退出，同账号去重及发布前后generation差异。 |
| AC-006 | RealRuntimeLifecycle...：步行/换区、两频道替换及旧清理、unavailable/current/saved；SessionInvalidatedWhileReadingPosition... 与 captured-set交错证明。 |
| AC-007 | RealRuntimeLifecycle...：静态99/88与实际1/1，公网31011/31012与本地21011/21012，unknown listener21999的null频道/单独计数；GM-only真实readiness及启用/就绪独立注入事实投影。 |
| AC-008 | SQLite/运行态前后比较、通用失败不改数据、无创建/写SQL/通知入口、Application命令注入判据及Host/认证/Protocol回归；宿主测试使用隔离数据库。 |
| AC-009 | PlayerDirectory.test.tsx 新15条：搜索重置页码、在线筛选、账号切换/ID关联/详情关闭与刷新，400/401/403/404/500/网络错误，乱序/退出/撤权与旧身份poll，中文概览及HTML文本。原10认证UI判据保留。 |
| AC-010 | 已构建/发布实际bundle，Host.Tests输出也传递wwwroot/gm；隔离fixture自检证明页面200和种子运行态。实际浏览器视觉与完整操作验收由主控后续执行，尚未声称完成。 |

## 浏览器交接

从该 worktree 运行以下命令，数据库位于 git 忽略的 artifacts。默认目录当前无 test.db；自检使用独立 `verify-*` 子目录，未占用默认路径。

```powershell
$env:DNF_GM_BROWSER_FIXTURE = '1'
Remove-Item Env:DNF_GM_BROWSER_FIXTURE_VERIFY_ONLY -ErrorAction SilentlyContinue
Remove-Item Env:DNF_GM_BROWSER_FIXTURE_DIRECTORY -ErrorAction SilentlyContinue
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-build --filter FullyQualifiedName~GmPlayerBrowserFixtureTests.ExplicitBrowserFixture --logger 'console;verbosity=detailed'
```

URL：`http://127.0.0.1:18083/gm/`；测试管理员 `operator` / `gm-isolated-test-password`。种子值见 `Document/gm-web-admin.md` 的隔离页面入口；配置始终游戏/Launcher关闭，但运行态种子使用真实领域服务。`artifacts/gm-directory-browser/spec003/ready.txt` 表示准备完成；同目录创建 stop.txt 正常退出，最长10分钟。退出后移除上述环境变量，保持普通测试不启动手工夹具。

本轮 VERIFY_ONLY 命令显式设置 `DNF_GM_BROWSER_FIXTURE=1`、`DNF_GM_BROWSER_FIXTURE_VERIFY_ONLY=1`、`DNF_GM_BROWSER_FIXTURE_DIRECTORY=<artifacts/gm-directory-browser/spec003/verify-UUID>`，执行相同 test filter（未用--no-build），在 finally 恢复原环境值。未打开浏览器。

## Remaining

主控实际浏览器验收 AC-010、独立挑战/完整审查及最终验证仍待执行；PVF条件跳过未升级为通过或客户端验证。无生产实现待补项。所有本生产者启动的 exec/test/Host session 已结束，18083无监听；未触碰现服/客户端/source checkout/真实玩家库，未改根cursor、规格或历史sealed证据，未commit。
