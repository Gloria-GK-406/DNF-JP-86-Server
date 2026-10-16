# GM Web 管理后台

完整功能已包含查询、官方邮件和三币发放。按 [最终验收表](gm-acceptance-list.md) 接手；标准候选构建与发布资源检查见 [server-ci-release.md](server-ci-release.md)。本机已部署邮件附件配置版本，运行与恢复信息见 [部署记录](deployments/gm-mail-attachments-20261002.md)；真实 A21 验收边界仍按验收表保留。

开发分支基于 0.2.0。当前提供独立 HTTP 模块、中文管理员登录、按权限显示的导航、分页审计、服务概览与全服账号/角色查询，并支持官方邮件和按实际账号发放三种点券。模块说明见 [官方邮件](gm-official-mail.md) 与 [点券发放](gm-cera-grants.md)。

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

## 界面

左侧深色导轨按「运营 / 游戏配置 / 审计」分组，只显示当前权限可用的页面，底部显示游戏服务状态、频道与游戏端口、PVF 摘要和版本（取自 `server-info`）。顶栏有全局搜索（Ctrl K，跳到账号与角色）、待确认发放提醒、外观（跟随系统 / 浅色 / 深色，青绿 / 靛蓝 / 琥珀铜 / 樱绯四套色系，只存本浏览器）和会话信息。宽度小于 900px 时导轨收进抽屉。

| 路径 | 页面 | 权限 |
|---|---|---|
| `/gm/` | 服务概览：关键数字；频道在线（按人数排序）、活动排期、需要处理（拒绝 / 回滚审计按操作与结果合并成组，可展开明细、单组或全部标为已读，已读只记在本浏览器）、最近操作。每块默认 5 行，可展开 / 收起并记住状态；结果未确认的发放以红色横幅固定在顶部 | QueryPlayers（排期需全部权限，审计部分需 ViewAudit） |
| `/gm/players` | 账号与角色：列表 + 右侧详情；输入纯数字按 ID 查，否则按姓名 / 登录名字面搜索；可勾选账号批量发点券 | QueryPlayers |
| `/gm/grants` | 发放：邮件记录、点券记录、邮件模板、草稿，以及待确认发放的「查询原编号 / 按原内容安全重试」 | SendMail 或 GrantCera |
| `/gm/grants/mail`、`/gm/grants/cera` | 写邮件 / 发点券三步页面；写邮件右侧是仿游戏「查看邮件」窗口的预览，核对步骤以服务端预览为准 | SendMail / GrantCera |
| `/gm/initialization`、`/gm/daily-refill`、`/gm/events` | 初始化配置、每日补给、活动管理 | 全部权限 |
| `/gm/operations` | 操作记录，按结果和操作类型筛选 | ViewAudit |

旧地址 `/gm/characters`、`/gm/mail`、`/gm/wallet` 自动跳转到新页面。待确认发放的请求编号与冻结内容仍按管理员保存在 sessionStorage，刷新或换页不丢失、不自动重发；配置类页面遇到版本冲突或结果未确认时锁定写入，重新读取后才能继续。

## 管理员首次初始化

管理员与玩家账号独立。首次启动从部署环境配置 `GmWeb:Bootstrap:LoginName`、`GmWeb:Bootstrap:Password`，环境变量对应 `GmWeb__Bootstrap__LoginName`、`GmWeb__Bootstrap__Password`。用户名为 1–64 个 ASCII 字母、数字、点、下划线或短横线；密码 12–256 字符。使用部署秘密注入，避免把密码放在命令行、仓库配置文件或终端历史中。

可选 `GmWeb:Bootstrap:Permissions` 支持逗号分隔 `QueryPlayers,SendMail,GrantCera,ViewAudit`，默认全部。权限分别控制玩家查询、官方邮件、点券发放、全局审计查看；邮件与点券各自提供窄目标选择和个人成功收据，无需附加玩家查询或全局审计权限。已有管理员时 bootstrap 不改变其密码、启用状态和权限。未配置、缺项或无效配置不会阻止 GM 启动，但不会创建管理员；页面显示初始化提示。

会话保持最多 8 小时，服务重启后仍可恢复；退出会撤销旧 Cookie，禁用管理员或改变权限在下一请求生效。数据库只保留密码散列与会话秘密的 SHA-256 摘要。首次初始化后可移除 bootstrap 密码环境变量；后续多管理员或密码管理界面不在当前模块内。

## 访问与审计

默认本机 HTTP；远程管理使用直接 HTTPS 与有效服务证书（Kestrel 的标准证书配置）。服务器采用实际连接判断，不信任请求携带的 `X-Forwarded-*`/`Forwarded`。本阶段没有反向代理信任配置；远程明文管理请求被拒绝。不要用代理头替代 HTTPS。

登录和所有状态写入使用 `X-GM-CSRF` 防护令牌，前端自动取得并提交。令牌来自 `/api/gm/v1/auth/csrf`，与浏览器 Cookie 和当前管理身份匹配；切换身份或重启服务后重新取得。跨站来源及无效令牌返回 403。CSRF 使用进程临时保护密钥，不写操作系统用户默认密钥目录；重启后 GM 会话仍在，旧 CSRF 需刷新。

同来源、同登录名在 15 分钟失败 5 次后限流；同来源累计失败 20 次也限流，防止换登录名绕过。限流窗口与容量有边界，返回 429。登录失败提示不区分账号是否存在。

登录成功、凭据失败、正常退出均写审计，审计页显示时间、操作、管理员、对象、结果和操作编号。`GET /api/gm/v1/audit` 除 `page`/`pageSize` 外还接受 `outcome`（`attention` = 除 Succeeded 以外的全部，或 `Succeeded|Rejected|RolledBack|Unknown` 之一）与 `action`（精确操作键：ChangeDailyRefill、ChangeEvent、ChangeJoustParameters、EditMailTemplate、GrantCera、Login、Logout、SaveInitialization、SaveMailDraft、SendOfficialMail、SetJoustChampion）；筛选在 SQLite 查询内完成，`total` 为筛选后的总数。未知取值或重复参数返回 400 `{ message }`。官方邮件与点券的资产变更、成功收据和成功审计共同提交或回滚，拒绝、已确认回滚和未知状态分开记录。匿名私有请求返回 401，权限不足返回 403，分页参数不合法返回 400。

运行的客户端、PVF 和现有游戏服务不受当前隔离构建影响。验证使用临时数据库；本文件的启动示例面向独立部署，验证时须显式传入独立 `Database:Path`。

## 服务信息与物品展示

以下接口只要求已登录（任意权限）：

- `GET /api/gm/v1/server-info` → `{ version, networkEnabled, channelPort, gamePorts, gmPort, pvfSha256 }`。`version` 为程序集版本（去掉 `+提交` 后缀）；端口按 `Networking:*` 配置与网络模块相同的默认值读取，网络关闭时也照配置报告；`pvfSha256` 为服务端实际会打开的 PVF 文件 SHA-256（首次请求时计算并缓存，找不到 PVF 为 null）。
- `GET /api/gm/v1/items/{itemId}/icon`、`GET /api/gm/v1/items/icon-pack`：物品图标，见 [GM 物品图标包](gm-item-icons.md)；配置 `GmWeb:IconPack`，默认 `Data/gm-icons.zip`。
- `GET /api/gm/v1/items/{itemId}/tooltip`：游戏风格 tooltip 所需的静态 PVF 资料，未知 ID 返回 404 `{ message }`。字段：`itemId`、`name`、`rarity`、`category`（equipment/stackable）、`typeName`（如 太刀、布甲 上衣、项链、材料）、`minimumLevel`、`maximumLevel`、`jobs`（null = 全职业；职业名取自 `character/*.chr [growtype name]` 首词，同名按性别加（男）/（女））、`attach`（`[attach type]` 原始键，如 sealing、trade delete）、`periodDays`（`[usable period]`）、`dailyDelete`（`[daily delete item]`）、`stats`（按 PVF 声明顺序的 `{label, value}`，值已格式化：品级区间 `186 ~ 201`、四维 `+15`、速度 `+8.0%`）、`explain`、`flavor`、`durability`、`weight`（kg）、`price`（`[value]`，即出售价）、`stackLimit`。只读模板根部段落，不计算品级/强化/增幅/锻造加成，不含技能与套装；未识别的属性标签直接忽略。`[expiration date]` 绝对期限暂不输出。

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
