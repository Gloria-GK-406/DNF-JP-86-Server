# GM 点券发放

点券页面 `/gm/wallet` 需要独立管理员的 `GrantCera` 权限。仅有该权限也能选择账号、发放、查询个人成功收据；`QueryPlayers`、`SendMail`、`ViewAudit` 不作为附加条件。全局操作审计仍要求 `ViewAudit`。

按账号 ID 或登录名字面子串查找，分页勾选明确账号。选择跨页保留，取消项始终可见；同账号多角色只增款一次，离线及没有角色的账号均支持。每次选择点券（Cera）、代币点券（TokenCera）、欢乐代币点券（HappyTokenCera）之一，输入每账号增量和原因，再预览、核对并确认整批发放。

增量为 1..2147483647 的整数；原始账号数组最多 100 项，服务器去重排序，每个实际账号得到相同增量。三币当前及结果余额均在 0..2147483647；任一账号缺失、钱包异常或超过上限，整批不提交。原因归一换行并去首尾空白后为 1..256 个 UTF-16 单元。API 必要字段缺失、数值字符串/小数、未知字段、非法币种和超过 32 KiB 请求均拒绝。

预览展示当前与预计三币余额、每账号增量及总增量，尚未到账，也不保留余额。提交按事务内当前余额结算，结果里的前后余额是实际持久化事实；预览后正常消费或其它发放发生时，以成功收据为准。整批余额、成功收据、成功审计同事务提交，任何一处失败全部回滚。

确认后冻结账号/币种/增量/原因及操作 UUID。重复点击、进程重启、并发及同编号同内容重试恢复原结果，不重复增款；同编号改内容返回冲突。账号 ID 顺序或重复项差异、UUID 大小写及标准格式差异归一后仍是同一请求。成功后新发放必须重新预览并显式确认新编号。

网络超时或“结果暂未确认”时保留原编号与原内容，在页面选择“查询原编号结果”或“按原内容安全重试”。同浏览器刷新可恢复待确认状态，但不会自动发放。查询 404 仅表示尚未找到成功收据，不能据此改编号重发；原编号重试用于确定/恢复结局。浏览器无法保存待确认请求时会在发送前停止。切换管理员只显示该管理员的记录及待确认状态；401 返回登录，403 清除页面私有资料。

“已到账，在线提醒未确认”表示持久款项已提交，不能再次补发。提醒只在提交后尽力发送给当前真实选中会话，读取当前钱包；离线/无角色不需提醒，会话切换或传输失败不撤销到账。服务器发送不证明客户端收到，玩家重新登录仍从持久钱包读取。

接口前缀 `/api/gm/v1/cera`：GET `/accounts`、POST `/preview`、POST `/grant`、GET `/operations`、GET `/operations/{requestId}`。POST 输入为 `{accountIds,currency,amount,reason}`，grant 另需非空 requestId。账号列表参数 page 默认1（1..1000000）、pageSize 默认20（1..100）、可选正整数 accountId/q（最多128字符）；个人收据同分页范围。列表按账号 ID 升序，记录按提交时间及编号降序；错误 message 为中文，拒绝/回滚/unknown 分开表达。

## 隔离浏览器夹具

先完成 `npm run build` 和 Host.Tests 构建。夹具仅在工作区 ignored `artifacts/gm-cera-browser/spec005` 的 fresh 子目录新建 SQLite；已有 test.db 或 reparse 祖先时拒绝。测试管理员 `operator` / `gm-isolated-test-password` 仅有 GrantCera，25 个测试账号：账号1无角色，账号2有两个角色，可跨页选择。游戏网络关闭，无预先发放。

```powershell
$env:PVF_ARCHIVE_PATH = 'E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
$env:DNF_GM_CERA_BROWSER_FIXTURE = '1'
$env:DNF_GM_CERA_BROWSER_FIXTURE_DIRECTORY = '<任务工作区绝对路径>/artifacts/gm-cera-browser/spec005/<fresh目录>'
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-build --filter FullyQualifiedName~GmCeraBrowserFixtureTests.ExplicitCeraBrowserFixture --logger 'console;verbosity=detailed'
```

夹具就绪输出 `http://127.0.0.1:18084/gm/wallet`，使用本机 Chrome。创建同目录 stop.txt 后正常停止，最长15分钟自动停止。`DNF_GM_CERA_BROWSER_FIXTURE_VERIFY_ONLY=1` 仅自检并立即退出；结束后移除这些 fixture 环境变量。夹具不重置、删除或操作现服数据库。

## 玩家待验项

- 分别发放三币，核对客户端在线余额展示；同账号多角色及重新登录仍是同一账号余额。
- 离线或无角色账号发放后，后续正常选角/登录核对实际余额。
- 通知未确认时先核对后台成功收据和玩家重登余额，不能凭提醒失败再补发。

以上仍由用户在真实 A21 客户端验收；自动 HTTP/SQLite、协议编码及后台页面证据不标记 ClientVerified。
