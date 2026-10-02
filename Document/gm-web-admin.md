# GM Web 管理后台

开发分支基于 0.2.0。当前提供独立 HTTP 模块、中文管理员登录、权限菜单、分页审计、服务概览与全服账号/角色只读查询；资产操作在后续阶段接入。

## 构建与启动

```powershell
cd Web/gm-admin
npm ci
npm run build
cd ../..
dotnet build Server/86JP.Server.slnx -c Release
dotnet run --project Server/src/Dfo.Server.Host -c Release --no-build -- --GmWeb:Enabled=true --GmWeb:ListenUrl=http://127.0.0.1:18081
```

访问 `http://127.0.0.1:18081/gm/`。前端开发模式 `npm run dev`，Vite 将 `/api/gm` 转发至本机 18081；开发者修改实际后端监听地址时同步修改代理目标。

`GmWeb:Enabled` 默认 false，`GmWeb:ListenUrl` 默认本机 18081。与 `LauncherAuth:*`、`Networking:*` 开关独立；全部 HTTP 关闭时不启动 Kestrel。启用多个 HTTP 功能时使用不同端口，功能路由只在各自端口提供。Host 与各模块共享同一容器、运行态和数据库。

前端先构建，再执行 dotnet build/publish；Host 项目把 `Web/gm-admin/dist` 复制到输出/发布目录的 `wwwroot/gm`。服务运行不需要 Node。没有前端资源时，页面返回 503；缺失 assets 返回 404，未知 API 不会返回 SPA。

## 管理员首次初始化

管理员与玩家账号独立。首次启动从部署环境配置 `GmWeb:Bootstrap:LoginName`、`GmWeb:Bootstrap:Password`，环境变量对应 `GmWeb__Bootstrap__LoginName`、`GmWeb__Bootstrap__Password`。用户名为 1–64 个 ASCII 字母、数字、点、下划线或短横线；密码 12–256 字符。使用部署秘密注入，避免把密码放在命令行、仓库配置文件或终端历史中。

可选 `GmWeb:Bootstrap:Permissions` 支持逗号分隔 `QueryPlayers,SendMail,GrantCera,ViewAudit`，默认全部。权限表示玩家查询、官方邮件、点券发放、审计查看；当前开放玩家查询与审计业务。已有管理员时 bootstrap 不改变其密码、启用状态和权限。未配置、缺项或无效配置不会阻止 GM 启动，但不会创建管理员；页面显示初始化提示。

会话保持最多 8 小时，服务重启后仍可恢复；退出会撤销旧 Cookie，禁用管理员或改变权限在下一请求生效。数据库只保留密码散列与会话秘密的 SHA-256 摘要。首次初始化后可移除 bootstrap 密码环境变量；后续多管理员或密码管理界面不在当前模块内。

## 访问与审计

默认本机 HTTP；远程管理使用直接 HTTPS 与有效服务证书（Kestrel 的标准证书配置）。服务器采用实际连接判断，不信任请求携带的 `X-Forwarded-*`/`Forwarded`。本阶段没有反向代理信任配置；远程明文管理请求被拒绝。不要用代理头替代 HTTPS。

登录和所有状态写入使用 `X-GM-CSRF` 防护令牌，前端自动取得并提交。令牌来自 `/api/gm/v1/auth/csrf`，与浏览器 Cookie 和当前管理身份匹配；切换身份或重启服务后重新取得。跨站来源及无效令牌返回 403。CSRF 使用进程临时保护密钥，不写操作系统用户默认密钥目录；重启后 GM 会话仍在，旧 CSRF 需刷新。

同来源、同登录名在 15 分钟失败 5 次后限流；同来源累计失败 20 次也限流，防止换登录名绕过。限流窗口与容量有边界，返回 429。登录失败提示不区分账号是否存在。

登录成功、凭据失败、正常退出均写审计，审计页显示时间、操作、管理员、对象、结果和操作编号。审计能力复用已有事务，后续发放操作可以与记录一起提交或回滚。匿名私有请求返回 401，权限不足返回 403，分页参数不合法返回 400。

运行的客户端、PVF 和现有游戏服务不受当前隔离构建影响。验证使用临时数据库；本文件的启动示例面向独立部署，验证时须显式传入独立 `Database:Path`。

## 服务概览与玩家查询

官方邮件编辑、内容模板、明确收件人、原子发放和安全重试见 [GM 官方邮件](gm-official-mail.md)。

`QueryPlayers` 控制概览、账号/角色列表及详情。账号和角色可分别按 ID、登录名/姓名字面子串搜索，角色还可按归属账号筛选；中文、`%`、`_`、引号作为普通文本，不是 SQL 通配符。页面支持仅在线、分页、手动刷新，以及按 ID 打开角色归属账号/账号角色列表。无角色账号可正常显示三币余额。软删除角色保留记录并明确标注。

所有查询使用 `GET /api/gm/v1`：`/overview`、`/accounts`、`/accounts/{accountId}`、`/characters`、`/characters/{characterId}`。列表参数 `page` 默认1（1..1,000,000）、`pageSize` 默认20（1..100）、`q` 最多128字符、`onlineOnly` 为 true/false；账号支持 `accountId`，角色支持 `accountId` 和 `characterId`。条件按 AND 组合；结果按 ID 升序，先筛选再分页，返回 items/total/page/pageSize/sampledAt。详情包含相同事实与采样时间，角色详情是平铺字段并附账号 balances。非法参数400、不存在404、失效401、无权限403；内部失败返回通用中文500并在服务器记录诊断。

在线账号包括已登录但未选角的真实游戏连接，按账号去重；在线角色要求当前有效且已发布的精确入场会话。准备中的角色不计在线。角色当前位置来自位置领域，移动/换区/会话替换后刷新可观察变化；保存位置仅用于离线资料，在线暂时无位置时显示“当前位置不可用”。频道从实际本地监听映射到声明目录；概览频道人数不采用静态 CurrentUsers。无法映射者单独计数。网络启用和游戏就绪分别显示，GM-only 游戏关闭仍可查询资料。

角色姓名以原始 name_bytes 严格 UTF-8 解码；字节缺失或异常显示诊断，异常保留 hex。后台不把历史兼容姓名当成权威姓名，不以显示姓名寻址；实际对象以账号/角色 ID 为准。接口不返回密码散列、玩家票据、连接 ID、IP 或会话秘密。页面把姓名作为文本渲染。查询接口失权时清除整页私有资料，显示无权限；401 才退出管理会话，旧请求不能恢复被清除的资料或权限。

采样反映当前进程的某次观察，不是所有存储/运行态字段的全局原子快照；跨页刷新不冻结名单。界面显示采样时间。本模块仅查询，不创建玩家、不执行选角恢复、不保存位置、不发送通知。

## 隔离页面验收入口

自动测试 `GmPlayerBrowserFixtureTests.ExplicitBrowserFixture` 可显式启动实际构建页面的隔离 Kestrel。只允许在工作区 `artifacts/gm-directory-browser/spec003` 及其 fresh 子目录创建库；已有 test.db 时拒绝启动，不覆盖或删除。默认端口18083，游戏/Launcher均关闭。先完成前端构建及 Host.Tests 构建，再由验收者运行：

```powershell
$env:DNF_GM_BROWSER_FIXTURE = '1'
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-build --filter FullyQualifiedName~GmPlayerBrowserFixtureTests.ExplicitBrowserFixture --logger 'console;verbosity=detailed'
```

访问 `http://127.0.0.1:18083/gm/`，测试管理员 `operator` / `gm-isolated-test-password`（仅此隔离夹具）。种子有45账号、47角色，账号45无角色且点券为 int.MaxValue；角色46姓名为 `中文%_' <b>` 且有删除标记；47姓名字节缺失，48字节无效。在线账号40/41，只有角色40入场，频道11、位置(123,234)；账号41登录未选角。可验收搜索、多页、在线过滤、ID关联、余额、诊断及刷新。

`ready.txt` 表示种子及页面就绪，创建同目录 `stop.txt` 可正常结束，最长10分钟后自动结束。运行结束后移除 DNF_GM_BROWSER_FIXTURE 环境变量，防止普通测试误开夹具；数据库位于 git 忽略的 artifacts，页面截图/观察证据保留在任务验证目录。`DNF_GM_BROWSER_FIXTURE_DIRECTORY` 可指定上述边界内另一 fresh 子目录，`DNF_GM_BROWSER_FIXTURE_VERIFY_ONLY=1` 只启动自检并立即结束。
