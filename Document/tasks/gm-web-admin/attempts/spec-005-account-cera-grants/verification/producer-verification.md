---
kind: rks-verification
decision: manual_required
target: ../spec.md
target_sha256: 958277A02E6AC3B67363D995E7728616939A57A752735B088D6EF2F08288EA0B
---

# Spec005 生产者验证

本报告支持“模块实现及下列自检通过、产品已交付审查”，不支持完整验收或 ClientVerified。独立规格 approved 只作为实施前提；主控 Chrome 证据见 [浏览器验证](browser-verification.md)，完整产出审查与主控新鲜验证仍由主控负责。

工作目录除 npm 外均为 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`；npm 工作目录为其 `Web/gm-admin`。所有 .NET 命令串行并明确设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`（只读）。以下日志相对工作区根；CeraHttp 是隔离 Host 的真实 HTTP 与 SQLite，不以 mock 代替资产原子性。前端是 API mock 行为测试。

## 最终命令及结果

| 检查 | 实际命令 | Exit / 结果 / 日志 |
|---|---|---|
| 点券模块 | `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --filter FullyQualifiedName~GmCeraHttpTests --logger 'console;verbosity=detailed'` | 0；17 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/cera-final.log` |
| Application 回归 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~CeraShop\|FullyQualifiedName~Mail\|FullyQualifiedName~Notification\|FullyQualifiedName~CommandHandlerArchitecture\|FullyQualifiedName~MultiAccountExecution\|FullyQualifiedName~AccountEntryState' --logger 'console;verbosity=detailed'` | 0；210 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/application-regression.log` |
| 旧 GM HTTP 回归 | `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-build --filter 'FullyQualifiedName~GmOfficialMailHttpTests\|FullyQualifiedName~GmPlayerDirectoryHttpTests\|FullyQualifiedName~GmAuthenticationHttpTests' --logger 'console;verbosity=detailed'` | 0；54 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/host-regression.log` |
| Protocol 回归 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --filter 'FullyQualifiedName~CeraShop\|FullyQualifiedName~Mailbox\|FullyQualifiedName~SelectCharacter' --logger 'console;verbosity=detailed'` | 0；80 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/protocol-regression.log` |
| Persistence 回归 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj --filter 'FullyQualifiedName~SqliteAccountRepository\|FullyQualifiedName~AccountEntry\|FullyQualifiedName~CeraShop\|FullyQualifiedName~GmAudit\|FullyQualifiedName~SqliteMailbox' --logger 'console;verbosity=detailed'` | 0；27 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/persistence-regression.log` |
| 全部前端 | `npm test` | 0；4 files / 54 pass，0 fail，0 skip（点券 13 项）；`artifacts/gm-tests/spec005/ui-full-final.log` |
| 前端构建 | `npm run build` | 0；TypeScript/Vite 成功，主 chunk 超 500 kB 的建议警告；`artifacts/gm-tests/spec005/ui-build-final.log` |
| 解决方案构建 | `dotnet build Server/86JP.Server.slnx --no-restore` | 0；0 warning / 0 error；`artifacts/gm-tests/spec005/server-build.log` |
| fresh 夹具自检 | `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-build --filter FullyQualifiedName~GmCeraBrowserFixtureTests.ExplicitCeraBrowserFixture --logger 'console;verbosity=detailed'` | 0；1 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/browser-fixture-verify-only.log` |
| Schema 窄修复 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj --filter FullyQualifiedName~SchemaOwnershipTests --logger 'console;verbosity=detailed'` | 0；3 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/producer-schema-fix.log` |
| 完整 Persistence | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj --no-build --logger 'console;verbosity=detailed'` | 0；396 pass，0 fail，0 skip；`artifacts/gm-tests/spec005/producer-persistence-full.log` |

表内 filter 的 `\|` 是 Markdown 表格转义；实际 PowerShell 参数是单引号内的普通 `|`，不是反斜杠。fixture 命令额外设置 `DNF_GM_CERA_BROWSER_FIXTURE=1`、`DNF_GM_CERA_BROWSER_FIXTURE_VERIFY_ONLY=1`，`DNF_GM_CERA_BROWSER_FIXTURE_DIRECTORY` 为工作区 `artifacts/gm-cera-browser/spec005/producer-verify-37952d64a03441deb24ed069752af1d4` 的绝对路径。

该 fresh 库实际验证 `/gm/wallet` 静态入口 200、登录与预览 200、初始成功操作数为 0，Host 正常 shutdown，命令进程退出；交付前再次确认 18084 无监听。未以未启用的 opt-in runner 当作浏览器证据。主控之后以另一 fresh 目录完成实际 Chrome 单/批、正常刷新/history和跨页选择，并正常停止夹具（exit0）；2收据/2成功审计与余额的只读核验见 `artifacts/gm-tests/spec005/main-chrome-db-proof.json`，日志 `main-chrome-fixture.log`。未制造实际浏览器断网，不能把正常刷新称为断网恢复。

## 已覆盖范围

点券17项真实HTTP/SQLite检查覆盖 GrantCera-only、零角色/多角色账号、三币边界、只读预览与提交当前余额、账号去重、CAS0/收据/审计故障整批回滚、重叠批次、持久并发/重启防重与个人隔离、撤权、未知提交恢复及当前钱包13B协议投影。既有真实商城三次消费与三次增款竞争结果为 `100000 + 150 - 2340 = 97810`；100账号最大增量的总数为long `214748364700`。测试源码 `Server/tests/Dfo.Server.Host.Tests/GmCeraHttpTests.cs` 保存各项断言。

前端点券13项检查单/批、冻结预览、跨页取消、存储失败先停止、超时刷新不自动POST、原UUID重试、404保留、401/403清私有数据和迟到响应。取消账号租约、身份/端口、邮件、消费、选角及handler/notification架构复用表中本次已跑的相关回归。规则接facts，handler仅领域服务，持久internal且建表归Schema；资产领域无socket lookup，当前会话与钱包由Protocol投影核验。自动投影不等于客户端已收。

## TDD 与完整失败记录

价值门：重复款及错误资产目标会直接造成损失，现有邮件测试未覆盖钱包，真实 HTTP/SQLite 的余额、收据和成功审计是忠实 oracle，允许内部重构。选择最小零角色账号发放及原编号重放作为 RED/GREEN；其余为扩展回归，不回填 TDD 声称。

早期日志均保留在本目录：

| 阶段 / 命令 | 实际结果 / 修复 |
|---|---|
| 首次尝试重定向 RED 输出 | verification 目录尚未存在，Out-File 前置错误；dotnet 未运行，wrapper exit 0 不计通过、不计 RED。创建目录后重跑。 |
| RED：`dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --filter FullyQualifiedName~GrantOnlyAdministratorCreditsAccountWithoutCharacterAndReplaysOnce --logger 'console;verbosity=detailed'` | exit 1；1 failed，预期 200 实际 403。缺端点元数据被 GmAccessBoundary 拒绝，是实际缺行为；`producer-red.log`。没有将其改成举例中的 404。 |
| 最小实现，同 RED 命令 | exit 1，CS1503：RequireId 返回 void 被当 Guid，尚未执行测试；`producer-green.log`。修复为返回 Guid；重跑 exit 0、1 pass：`producer-green-2.log`。编译错误不计 RED。 |
| 扩展模块，同最终点券 filter | exit 1，13 pass / 2 fail：数值字符串被 Web JSON 默认允许并实际增款；N 格式大写 UUID 被默认读取拒绝。`producer-cera-1.log`。 |
| 数值字符串诊断，filter `FullyQualifiedName~BoundariesMissingFieldsUnknownTypesAndCorruptWalletNeverCredit` | exit 1，1 fail；输出 amount:"1" 得到真实成功收据与错误增款，证明默认 AllowReadingFromString 根因；`producer-parse-diagnosis.log`。端点局部 Strict 数字和 Guid.TryParse converter 后，模块重跑 exit 0、15 pass：`producer-cera-2.log`。 |
| 商城竞争新增，filter `FullyQualifiedName~RealShopPurchasesCompeteWithGrantsWithoutLostSpendingOrRoleMultiplication` | exit 1，xUnit1031（Task.Result 阻塞），未执行测试；`producer-shop-race.log`。改 await Task.WhenAll，重跑 exit 0、1 pass：`producer-shop-race-2.log`。 |
| 前端早期 `npm test -- CeraGrant.test.tsx` / `npm run build` | 均 exit 0；当时点券 10 pass、build 成功；`producer-ui-tests-1.log`、`producer-ui-build-1.log`。补3项页面测试后全量最终54 pass。 |
| 前端首次全量 `npm test` | exit 0、51 pass；`artifacts/gm-tests/spec005/ui-full.log`。后续追加测试后的最终结果见上表，不把较旧51项当当前54项。 |
| 主控 SchemaOwnershipTests 回归 | exit 1；2 pass / 1 fail，旧全表计数 expected54 / actual55；`artifacts/gm-tests/spec005/main-schema-regression.log`。仅更新既有断言为55，并追加 gm_cera_operations 精确五列 shape，目标3项及完整Persistence396项重跑通过。不另造行为RED，不放宽或删除全表计数。 |

上述生产者早期 dotnet 诊断/扩展同样带 `--logger 'console;verbosity=detailed'`，使用 Host.Tests 项目；最后 Schema/完整回归使用 Persistence.Tests 项目。其它曾出现的工具前置错误：两次在 Web 工作目录误用根相对路径读文件，随后从正确根重读；一次说明文档 patch 上下文不匹配被拒，核对原行后重写；一次无监听的端口查询返回非零，最终已用显式“无监听”查询确认。这些均未作为测试通过或行为 RED；失败日志没有删除。

修复仅新增 `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs` 的产品 ownership；先保存原22文件 `product-owned-manifest-r1.md`（SHA256 `3D960ED0FDCC900F7AB8AF1B59ADDC25DD22B1442C630E7805A2C359A9C47EFB`），再生成当前23文件清单。其它22项产品哈希必须与r1一致，Spec不变；本轮不重跑其它.NET、fixture或前端。

## Unverified Conditions

- 实际浏览器断网/超时未制造，未知刷新及原UUID恢复的证据为前端行为测试；主控正常刷新证据不升级为真实断网验证。
- 生产者产品冻结后的独立漂移/完整结果审查及主控新鲜验证；本报告不自行推进工作流或宣布接受。
- A21 客户端在线三币展示、接收提醒、离线/零角色后续选角及重登余额，见 Document/gm-cera-grants.md 玩家待验项；未声称 ClientVerified。

最终 CI、整体发布与整体验收表属于 Spec006，不在本报告扩展。当前证据范围内无未修复测试失败；Vite 大 chunk 建议提示保留。
