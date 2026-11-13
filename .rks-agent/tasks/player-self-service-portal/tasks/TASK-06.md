# TASK-06 — 前端：登录切换、玩家界面与管理员「玩家后台」页

## Task Unit Contract

- Revision: 1（标识本契约及其详细计划）
- Outcome and references: 交付 Plan T-006（SD-10、SD-8a 前端部分、SD-1a 的前端分支）：
  - 顶层壳：先查 `/api/player/v1/auth/status` 与已有会话，再渲染登录页、`PlayerApp` 或 `AdminApp`；
  - 登录页"玩家 / 管理员"切换，默认玩家，总开关关闭时不显示玩家选项；
  - 玩家界面三页（我的账号、发邮件、记录）、已关闭页，手机宽度 390px 用底部标签导航；
  - `ItemPicker`、图标与悬停提示组件改为接收接口前缀，物品检索抽成可内联的 `ItemSearch`，GM 侧行为不变；
  - 管理员「玩家后台」页（全权限可见）；审计页"玩家自助"筛选与玩家操作者显示；
  - 前端测试与构建。

  覆盖 RQ-040–RQ-044；验收 AC-040（AC-041 的浏览器实测属 T-007，本单元只保证其前提）。同时处理遗留：
  - TASK-03 审查：没有 `code` 的 5xx 按 `unknown` 处理；总开关关闭时登出被边界以 `portal_closed` 拒绝，前端仍清空本地玩家状态并进入已关闭页。
  - TASK-04：加点券响应 `{ receipt, replayed, reminder }`，`reminder: "failed"` 仍按成功处理；请求带 `requestId`，同一次提交的重试复用同一编号。
  - TASK-05：发信响应 `{ receipt, replayed }`，无 `reminder`；物品搜索 / 详情 / 图标 / 图标包 / 悬停提示走 `/api/player/v1/...`；邮件子开关关闭时不请求这五条接口。
- Owned boundary:
  - `Web/gm-admin/src/` 下的前端源码与测试：
    - 新建 `playerApi.ts`、`ItemSearch.tsx`、`Portal.tsx`、`PlayerApp.tsx`、`PlayerAccountView.tsx`、`PlayerMailView.tsx`、`PlayerRecordsView.tsx`、`PortalClosedView.tsx`、`PlayerPortalAdminView.tsx`，测试 `Portal.test.tsx`、`PlayerApp.test.tsx`、`PlayerPortalAdmin.test.tsx`；
    - 修改 `main.tsx`、`api.ts`（`ApiError` 增加可选 `code`，GM `api()` 行为不变）、`useGmRead.ts`（可选读取函数参数）、`itemInfo.tsx`、`ItemPicker.tsx`、`LoginView.tsx`、`AdminApp.tsx`、`AuditView.tsx`、`style.css`、`test-utils.tsx`，以及为审计筛选补用例的 `AdminApp.test.tsx`、为前缀补用例的 `ItemPicker.test.tsx`。
  - `Web/gm-admin/vite.config.ts`：开发代理加 `/api/player`。
  - `Document/gm-web-admin.md`：「构建与启动」的开发代理说明、「界面」一节（登录切换、玩家界面、管理员「玩家后台」页、审计筛选）。
  - 不改服务端代码与接口。实现中发现服务端缺能力或形状与 `Document/gm-web-admin.md`「玩家自助后台」不符时，停下记 `## Blocker`。
- Required checks:
  - `cd Web/gm-admin && npm ci`（`node_modules` 不存在时）；`npm test` 全部通过（既有 GM 测试原样通过，作为 RQ-051 的前端回归）；`npm run build`（`tsc --noEmit` + `vite build`）通过。
  - AC-040 各点在第 7 节的用例中逐条对应，结果里列出对照表。
  - 变异核对（第 7.4 节），核对后恢复原代码并在结果中记录。
  - 界面文字自查：新增与改动的界面不含页面导语、状态旁解释句、开关旁重复标签、"由谁何时操作"、编造的示例值或占位值（第 0 节第 9 项的清单逐项核对）。
  - 行尾：`git ls-files --eol Web/gm-admin Document/gm-web-admin.md` 核对改动的已跟踪文件仍为 `w/lf`；新建文件用 Python 逐字节统计 CR 为 0（本环境 Bash 的 `grep -c $'\r'` 不可靠）。
  - 原则上不改服务端；若确实改了，追加 `dotnet build Server/86JP.Server.slnx` 零警告零错误与相关 Host.Tests。
- Dependencies: TASK-03（玩家登录 / 登出 / 状态 / 会话 / `me`、管理员设置 / 会话 / 踢人接口、审计 `actor` 筛选与 `actorKind` / `accountId`）、TASK-04（`cera/grant`、`operations`）、TASK-05（`mail/send`、玩家物品目录五条接口）；均已提交（`d7bfa8144`、`7f0d58d9b` + `79f7cb4b5`、`db45667db`）。接口形状以 `Document/gm-web-admin.md`「访问与审计」「玩家自助后台」为准。
- Result review: required —
  - 改动横跨十余个文件，并改了 GM 共用的组件（`ItemPicker`、`itemInfo`、`useGmRead`、`LoginView`、`AdminApp`、`AuditView`）：改错会让 GM 写邮件、初始化、每日补给的物品选择或图标请求错到玩家前缀，或让管理员登录、导航失常。既有 GM 测试是防线，但只覆盖已写的情形。
  - SD-1a 的分支（`portal_closed`→已关闭页、`unauthenticated`→登录页、`feature_disabled`→不可提交、无 `code` 的 5xx→`unknown`）和"结果未确认时用原编号重试"决定玩家会不会重复入账或卡在错误页面；只有特意构造的失败响应用例才能暴露，"能加点券"的正常用例照样通过。
  - 390px 布局与"界面不放解释文字"无法由 jsdom 测试判定，需要审查者读界面代码；T-007 的浏览器实测建立在本单元之上。

## Detailed Plan

### 0. 本地决定

标 ★ 的项需要协调方确认后再执行；其余按此执行。

1. ★ **两个会话同时存在时玩家优先。** 顶层壳先查玩家会话：有效就进 `PlayerApp`；否则交给 `AdminApp`（由它照旧查 GM 会话，有则进管理台，无则登录页）。理由：只有同一浏览器既登录管理员又登录玩家时才会碰到，规则确定、实现最少；玩家退出后若 GM 会话仍有效会直接回到管理台。备选：本浏览器记住上次登录的身份，按它决定先后。
2. ★ **登录页按设计图收文字。** 登录页现由两种身份共用，按设计图：左侧品牌区只留标志与"86JP"，去掉现有的标语、"独立管理员账号 · 会话 8 小时 / 远程访问需要 HTTPS"两行和表单下的"会话固定 8 小时…"说明。管理员模式的字段标签保留"管理员账号"（既有测试以它定位），玩家模式为"账号"。"管理员尚未初始化"的状态提示保留，只在管理员模式出现。
3. **玩家选物品内联在附件面板里（协调方裁定，按设计图）。** 从 `ItemPicker` 抽出共用的检索组件 `ItemSearch`（筛选、结果列表、分页，搜索参数拼法不变），GM 的 `ItemPicker` 弹窗左栏改用它、外观与行为不变；玩家发邮件页把 `ItemSearch` 嵌在附件面板里，结果行"添加"直接把该物品（数量 1）合并进附件列表。搜索接口（`mail/items` 同一选择键）、件数规则（`mergeItems`、`usedSlots`：装备每件一格、共 10 格）与物品校验（服务端 GM 邮件规则）与 GM 邮件同一套。
4. ★ **管理员页开关即时生效，限额点保存。** 设计图开关面板没有保存按钮、限额面板有"保存"：切换任一开关即提交一次设置（三个开关取切换后的值，限额取**已保存**的值，不带未保存的限额草稿），"保存"提交三个开关的当前值与限额草稿；两者都带读到的 `revision`，成功后以响应替换本地设置。"全部踢下线"用只有标题的确认（"全部踢下线？"），关闭总开关不加确认。
5. 结果未确认的写操作（加点券、发邮件）只在页面内存里保留 `{ requestId, 请求内容 }`，不写 sessionStorage；保留期间表单锁定，横幅给"用原编号重试"和"放弃"。玩家只能给自己账号加、且服务端有限额与重放保护，不照搬 GM 的跨刷新待确认机制。
6. `PlayerApp` 每 60 秒重读一次 `me`（与管理台每 60 秒检查会话同一节奏），使管理员关闭总开关或踢人后，停在页面上的玩家无需操作也会进入已关闭页 / 登录页（AC-041 的前提），余额、在线状态、今日用量也随之刷新。
7. 玩家路由：`/gm/player`（我的账号）、`/gm/player/mail`、`/gm/player/records`。玩家登录后进 `/player`；`PlayerApp` 在其它路径上重定向到 `/player`；玩家退出、过期、被关后导航回 `/`，避免管理员登录后落在玩家路径上。
8. 审计页"管理员"列改名"操作者"：管理员记录照旧显示名字；玩家记录显示登录名、"玩家"标记与账号 ID。页头说明气泡的文字改为与现状相符（"管理员与玩家自助的写操作与登录，按时间倒序。"）。
9. 界面文字清单（新增界面只出现这些类别的文字）：页面与面板标题、字段标签、按钮、表头、状态标记（在线 / 离线、已停用、成功）、数据（余额、加后、今日 x / y、件数、编号前 8 位、时间）、服务端返回的拒绝消息、本地校验的数据型提示（"单次上限 1,000"、"今日剩余 500"、"请选择收件角色"之类，只写数据或该做的动作）。不写页面导语、说明气泡、空状态里的引导句、开关旁重复的标签、"由谁何时操作"。玩家发邮件页内嵌的 `ItemSearch` 不显示 GM 弹窗的提示句与引导性空状态。输入框不放示例占位值（`ItemPicker` 现有的"物品名称或精确 ID"属字段说明，保留）。

### 1. 玩家接口层（`playerApi.ts`、`api.ts`）

- `api.ts`：`ApiError` 构造参数追加可选的第 4 个参数 `code?: string`（公开字段）。GM `api()` 不读 `code`，行为不变。
- `playerApi.ts`：
  - 常量前缀 `/api/player/v1`。`playerRequest<T>(path, payload?)`：写请求先 `GET auth/csrf` 取令牌，带 `X-GM-CSRF` 与 `Content-Type: application/json`；30 秒超时；`credentials: 'same-origin'`；204 返回 `undefined`。与 GM `api()` 同一写法，只换前缀并解析 `code`。
  - 失败统一抛 `ApiError(status, message, outcome, code)`：
    - 响应体有字符串 `code` 时照用；
    - 没有 `code` 且状态 ≥ 500（含非 JSON 响应）→ `code = 'unknown'`（TASK-03 遗留：登录端点等的裸 500）；
    - 没有 `code` 的 4xx（例如 antiforgery 之外的意外形状）→ `code = 'invalid'`，消息取响应体 `message` 或通用文案；
    - `fetch` 本身抛错（断网、超时）→ `ApiError(0, failureMessage(error), 'unknown', 'unknown')`：写请求可能已经到达服务端，按结果未确认处理。
  - 类型：`PlayerIdentity { accountId, loginName, expiresAt }`；`PlayerMe { accountId, loginName, balances: { cera, tokenCera }, characters: { characterId, name, job, level, isOnline }[], portal: { ceraEnabled, mailEnabled, limits: { ceraPerGrant, ceraPerDay, tokenCeraPerGrant, tokenCeraPerDay, mailGoldPerMail, mailsPerDay }, today: { cera, tokenCera, mails } } }`；`PlayerReceipt`（公共五字段 `requestId, kind, committedAt, summary, result`，点券另有 `currency, amount, before, after`，邮件另有 `characterId, characterName, title, body, items, gold, expiresAt`）；`PlayerCeraResult { receipt, replayed, reminder }`；`PlayerMailResult { receipt, replayed }`；`PlayerOperationsPage { items, total, page, pageSize }`；管理员侧 `PlayerPortalSettings { enabled, ceraEnabled, mailEnabled, limits, revision }`、`PlayerPortalSessionEntry { accountId, loginName, createdAt, expiresAt }`。
  - `isRetainable(failure)`：`code === 'unknown'` 或 `outcome` 为 `unknown` / `rolledBack` → 保留原编号与内容（第 4.5 节）。

### 2. 物品组件接收接口前缀（SD-8a）

- `itemInfo.tsx` 新增 `ItemSource = { key: string; prefix: string; read<T>(path): Promise<T>; quiet<T>(path): Promise<T | undefined> }`：
  - `gmItems = { key: 'gm', prefix: '/api/gm/v1', read: path => api(path), quiet: path => quietApi(path) }`（调用时才引用 `api`，测试对 `./api` 的 mock 照样生效）；
  - `ItemSourceContext`（默认 `gmItems`）与 `useItemSource()`。
- 缓存键加来源：详情 `${source.key}:${purpose}:${id}`、提示 `${source.key}:${id}`、失败图标同理；`resetItemCaches` 清全部。`cachedDetails` / `rememberDetails` / `loadDetails` / `useItemDetails` 增加可选 `source` 参数，缺省 `gmItems`，GM 调用点不改。
- `IconFace` 的 `src` 改为 ``${source.prefix}/items/${itemId}/icon``；`loadTooltip` 走 `source.quiet`；`useIconPackStatus(source = gmItems, enabled = true)`，`enabled` 为 false 时不请求、返回 `false`。组件内部用 `useItemSource()` 取来源。
- `useGmRead(path, onExpired, onDenied, read = api)`：增加可选读取函数，缺省 `api`，GM 调用点不改。
- 新建 `ItemSearch.tsx`（第 0 节第 3 项）：把 `ItemPicker` 左栏（筛选、去抖搜索、参数拼法、结果行、分页）原样搬入，读取走 `useGmRead(\`${purpose}/items?…\`, …, source.read)`，来源取 `useItemSource()`。由调用方提供每行的标记与按钮（`renderAction(entry)`、`renderTags(entry)`）和计数行右侧文字，使 GM 弹窗的"已在列表 / 待加入 / 选它 / 添加"与提示句保持原样；`hints` 为 false 时不渲染提示句与引导性空状态。
- `ItemPicker`：左栏改用 `ItemSearch`，DOM 结构、文字、`aria-label` 与请求不变；增加可选 prop `source?: ItemSource`（缺省取上下文）并包在 `ItemSourceContext.Provider` 里。`mail` / `template` / `init` / `refill` 四种用途的行为与文字不变，不新增玩家用途。

### 3. 顶层壳与登录页

- `main.tsx` 渲染 `<Portal />`（在原有 `LookProvider` / `App` / `BrowserRouter basename="/gm"` 之内），不再直接渲染 `AdminApp`。
- `Portal.tsx` 状态：`probing` → `{ phase: 'player', identity }` | `{ phase: 'entry', enabled }` | `{ phase: 'closed' }`。
  - 启动：`playerRequest('auth/status')` 取 `{ enabled }`；`enabled` 时再 `GET auth/session`：200 → `player`；401 → `entry`；403 `portal_closed` → `entry` 且 `enabled = false`。状态读取失败 → `entry` 且 `enabled = false`（不凭空显示玩家选项）。探测期间渲染 `.boot` 里的 `Spin`，不带文字。
  - `player`：渲染 `PlayerApp`，传入 `identity` 与三个回调：`onClosed`（→ `closed`）、`onExpired`（→ `entry`，并 `noti.warning('会话已失效，请重新登录。', { key: 'player-session-expired', persistent: true })`）、`onSignedOut`（→ `entry`）。三者都 `noti.clear()`（过期提示除外）并 `navigate('/')`。
  - `entry`：渲染 `<AdminApp player={{ enabled, busy, onLogin, onShown }} />`。
  - `closed`：渲染 `PortalClosedView`（锁形图标、"玩家后台已关闭"、"返回登录"按钮）；"返回登录"重新探测状态后进 `entry`。
  - 玩家登录 `onLogin(values)`：`playerRequest('auth/login', values)`；成功 → `noti.clear()`、`player` 阶段、`navigate('/player')`；`unauthenticated`（凭据错误，**不当作会话过期**）、`rate_limited`、`invalid` 等 → `noti.failure` 显示服务端消息，留在登录页；`portal_closed` → `closed`。
  - `onShown()`：登录页挂载、以及管理员模式点"重新连接"时调用，重读 `auth/status` 更新 `enabled`（管理员退出回到登录页时玩家选项随之更新）。
- `AdminApp`：增加可选 prop `player`，原样传给 `LoginView`；不传时（既有测试）行为不变。其余登录、会话、退出逻辑不动。
- `LoginView`：
  - `player?.enabled` 为真时显示 `Segmented`（`block`，`aria-label="登录身份"`，选项"玩家 / 管理员"），默认"玩家"；为假或未传时不显示切换，即现有管理员登录。
  - 玩家模式：字段"账号"（`id="player-login-name"`，maxLength 256）、"密码"（maxLength 256），空值校验同管理员模式（"请输入账号" / "请输入密码"），提交调用 `player.onLogin`，按钮 loading 取 `player.busy`；不受 GM `initialized` 与 GM 探测状态影响。
  - 管理员模式：保持现有分支（未初始化提示、连不上时"重新连接"、管理员表单）。
  - 第 0 节第 2 项的文字调整。

### 4. 玩家界面

#### 4.1 外壳（`PlayerApp.tsx`）

- 布局沿用 `.app` / `.rail` / `.main` / `.topbar` / `.content`：左侧导轨品牌"86JP"与三项（我的账号 / 发邮件 / 记录，图标 `UserOutlined` / `MailOutlined` / `UnorderedListOutlined`，`aria-label="玩家导航"`）；顶栏面包屑为当前页名，右侧头像首字母、登录名（窄屏隐藏）与"退出"按钮；`.content` 下方是 `nav.tabbar`（`aria-label="底部导航"`，三项"账号 / 发邮件 / 记录"），宽屏由 CSS 隐藏。
- 上下文 `PlayerContext`：`{ me, refreshMe, call, items }`：
  - `call<T>(path, payload?)` = `playerRequest`，失败时先按 `code` 分流：`portal_closed` → `onClosed()`，`unauthenticated` → `onExpired()`，然后照常抛出；各页面的 catch 对这两种 `code` 不再提示。
  - `items: ItemSource = { key: 'player', prefix: '/api/player/v1', read: call, quiet: 吞掉错误的 call }`，`PlayerApp` 整体包在 `ItemSourceContext.Provider value={items}` 与 `IconPackContext.Provider value={useIconPackStatus(items, me?.portal.mailEnabled === true)}` 里，邮件子开关关闭时不请求 `items/icon-pack`。
- 启动读 `me`；之后每 60 秒重读（第 0 节第 6 项）；写操作成功后重读。`me` 首次未就绪时页面显示 `Spin`。
- 退出：`call('auth/logout', {})` 之外单独处理：成功或 `unauthenticated` → `onSignedOut()`；`portal_closed` → `onClosed()`（会话行已被服务端删除，前端清空本地状态，TASK-03 遗留）；其它失败 → 提示并留在原页。
- 每个页面组件以 `key={identity.accountId}` 渲染，切换身份时状态不残留。

#### 4.2 我的账号（`PlayerAccountView.tsx`，`/player`）

- 余额面板 `.wallet`：点券、代币券两格，数值 `nf()`。
- 角色面板：标题"角色"、计数；表格列 角色（职业首字 + 名字）、职业（`jobName(job)`，接口没有转职，窄屏隐藏）、等级（窄屏隐藏）、状态（`Pill` 在线 / 离线）、操作"发邮件"（`navigate('/player/mail', { state: { characterId } })`；邮件子开关关闭时禁用）。无角色时表格空状态为"无角色"。
- 加点券面板：
  - `Segmented` 点券 / 代币券（值 `Cera` / `TokenCera`）；"数量" `InputNumber`（`numberFormat`、`precision 0`、1..2147483647）；快捷 "+1 万 / +10 万 / +100 万"（累加，封顶 2147483647）；"加后"= 所选币种余额 + 数量（无数量时 "—"）。
  - 所选币种每日上限 > 0 时显示 `.quota`："今日" 与 `已用 / 上限` 及进度条。
  - 本地校验（只拦明显不通过的，服务端仍判）：单次上限 > 0 且数量超过 → "单次上限 {n}"；每日上限 > 0 且已用 + 数量超过 → "今日剩余 {n}"；超过 int 上限 → "数量超出范围"。
  - 点券子开关关闭：面板标题旁 `Pill kind="idle"` "已停用"，币种、数量、快捷、确认全部禁用（RQ-042）。
  - 确认：按 4.5 节提交 `cera/grant`；成功 → 面板下方 `Banner`（ok）"点券 +10,000"（按回执 `currency` / `amount`），余额用回执 `after` 立即更新并 `refreshMe()`；`replayed` 与 `reminder`（含 `failed`）都按成功处理，不另行提示。

#### 4.3 发邮件（`PlayerMailView.tsx`，`/player/mail`）

- 左列：
  - 收件角色面板 `.rcpt`：只列 `me.characters`（职业首字、名字、`Lv.x`、在线时"· 在线"），单选；路由 state 带来的 `characterId` 若在列表里则预选。
  - 附件面板：计数 `已用格 / 10`（`usedSlots`，详情经 `useItemDetails('mail', ids, access, items)` 读取）；内嵌 `ItemSearch purpose="mail"`（`hints=false`），结果行"添加"把物品数量 1 经 `mergeItems` 并入附件（已满 10 格或合并越界时按钮禁用 / 给出数据型提示）；附件行 = `ItemIcon` + `ItemName` + 数量 `InputNumber`（装备 1..10、其它 1..2147483647）+ 移除；"金币" `InputNumber`（0..2147483647）。单封金币上限 > 0 且超过时字段错误"单封上限 {n}"。
  - 内容面板：标题、正文（`Input` / `TextArea`），长度由服务端 GM 规则判定，前端不另设上限。
- 右列：
  - 信件预览 `.letter`：收件人（未选时"收件人未选"）、标题（空时"（无标题）"）、正文（空白时以弱色显示 `DEFAULT_MAIL_BODY`，即实际会投递的正文）、附件与金币 `chip`；
  - 每日封数上限 > 0 时显示 `.quota`（今日已发 / 上限）；
  - "发送"按钮；成功后 `Banner`（ok）"已送达"与"→ 角色名"，清空附件、金币、标题、正文（保留收件人），`refreshMe()`。
- 提交前本地校验：未选收件角色 → "请选择收件角色"；正文空白、无附件且金币为 0 → "正文、附件、金币至少填一项"；附件详情未就绪 → 不可提交。
- 请求体严格为 `{ requestId, characterId, title, body, items: [{ itemId, count }], gold }`，不带任何其它字段。
- 邮件子开关关闭：面板标题旁"已停用"，所有输入与"发送"禁用；**不渲染 `ItemSearch`、不读物品详情、不请求图标包**（`useItemDetails` 收到空 ID 列表，`useIconPackStatus` 的 `enabled` 为 false）。

#### 4.4 记录（`PlayerRecordsView.tsx`，`/player/records`）

- `Segmented` 全部 / 点券 / 邮件 → `operations?page=&pageSize=20[&kind=cera|mail]`，切换时回到第 1 页；经 `useGmRead(path, noop, noop, call)` 读取（401 / 403 已由 `call` 分流）。
- 表格列：时间（`bjTime(committedAt)`）、类型（`chip` 点券 / 邮件）、内容（`summary`）、结果（`result` 为"成功"时 `Pill ok`，其它值原样 `Pill idle`）；分页；空状态"无记录"。

#### 4.5 写操作的编号与失败处理（加点券、发邮件共用的小钩子）

- 状态 `pending: { requestId, body } | null`。提交时若无 `pending` 用 `crypto.randomUUID()` 生成新编号，否则复用 `pending` 的编号与内容（此时表单锁定，内容不会变）。
- 结果：
  - 成功 → `pending = null`；
  - `isRetainable(failure)`（`code: unknown`、无 `code` 的 5xx、网络错误、超时、`outcome: unknown | rolledBack`）→ 保留 `pending`，表单锁定，显示 `Banner kind="crit"`：标题"结果未确认"，正文为编号前 8 位，操作"用原编号重试""放弃"；"放弃"清空 `pending` 并 `refreshMe()`；
  - `portal_closed` / `unauthenticated` → 已由 `call` 分流，不提示；
  - `feature_disabled` → `pending = null`，`refreshMe()`（表单随即变为已停用），提示服务端消息；
  - 其它（`invalid`、`limit_exceeded`、`request_conflict`、`not_found`）→ `pending = null`，提示服务端消息，下次提交生成新编号。
- 同一时刻只允许一次提交（按钮 loading，重复点击忽略）；组件卸载后的迟到结果丢弃。

### 5. 管理员「玩家后台」页与审计

- `AdminApp`：`pages` 在"游戏配置"组追加 `{ key: '/player-portal', label: '玩家后台', icon: <UserSwitchOutlined />, need: 'All' }`；路由 `content` 渲染 `<PlayerPortalAdminView key={key} {...access} onDenied={configDenied} />`（403 时与每日补给同样隐藏全部"全部权限"页面）。
- `PlayerPortalAdminView.tsx`（GM `api()`，路径 `player-portal/...`）：
  - 开关面板：三行"玩家后台"（总开关）/"加点券"/"发邮件"（子开关缩进，总开关关时弱色），antd `Switch`，`aria-label` 同行名；行内不再写重复的状态文字。切换按第 0 节第 4 项提交 `POST player-portal/settings`；提交期间三个开关禁用；关闭总开关成功后重读会话列表。
  - 限额面板：标题"限额"、计数位"0 = 不限"（设计图原文）；六个 `InputNumber`（`numberFormat`，0..2147483647，`precision 0`）：点券单次、点券每日、代币券单次、代币券每日、邮件金币单封、邮件每日封数；"保存"在草稿与已保存值不同且全部合法时可用；保存成功 `noti.success('已保存')` 并以响应替换本地值。
  - 失败：`401` → `onExpired`；`403` → `onDenied`；`409` → 锁定写入，显示 `Banner kind="crit"` 标题"设置已被修改"、操作"重新读取"；`≥ 500`、网络错误或超时 → 锁定，`Banner kind="crit"` 标题"结果未确认"、操作"重新读取"；`400` → 提示服务端消息，不锁定。"重新读取"重读设置与会话并解锁、丢弃草稿。
  - 在线会话面板：标题"在线会话"、计数、工具区"刷新"（图标按钮）与"全部踢下线"（danger，`Popconfirm` 标题"全部踢下线？"）；表格列 账号（登录名 + 账号 ID）、登录于、到期（`bjTime`）、操作"踢下线"（`POST player-portal/sessions/kick { accountId }`）；成功提示 `已踢下线 {kicked} 个会话` 后重读列表；空状态"无"。
  - 布局 `.grid.g-2`：左列开关 + 限额，右列会话（设计图）；≤1100px 单列（现有断点）。
- `AuditView.tsx`：
  - `AuditRecord` 类型（`api.ts`）增加 `actorKind: 'gm' | 'player'`、`accountId: number | null`。
  - 工具栏加 `Select`（`aria-label="操作者"`，`allowClear`，占位"全部操作者"，选项"管理员"=`gm`、"玩家自助"=`player`），选中时请求带 `actor=`，切换回到第 1 页；参数顺序 `page, pageSize, outcome, action, actor`（既有用例断言的 URL 不变）。
  - `actionLabels` 增加 `PlayerLogin` 玩家登录、`PlayerLogout` 玩家退出、`PlayerGrantCera` 玩家加点券、`PlayerSendMail` 玩家发邮件、`ChangePlayerPortal` 修改玩家后台设置、`KickPlayerSessions` 踢玩家下线（概览页共用同一表）。
  - 操作者列见第 0 节第 8 项。

### 6. 样式与 390px（`style.css` 末尾"玩家自助后台"段）

- 从设计图移入并改用现有令牌：`.tabbar`、`.wallet`、`.quick`、`.after`、`.quota`、`.rcpt`、`.letter`、`.closed`、`.sw-row`（`master` / `sub` / `muted`）、`.login-card .ant-segmented`。不引入设计图的 mock 工具栏与自绘开关。
- `@media (max-width: 900px)`：`.app.player` 的 `.main` 行为 `48px minmax(0, 1fr) 56px`，`.tabbar` 显示为三等分网格；`.player .rail` 隐藏（现有规则已隐藏 `.app > .rail`）；`.g-main-side` 单列。
- `@media (max-width: 520px)`：`.col-job`、`.col-lv` 隐藏；`.wallet .v` 22px；`.rcpt` 最小列宽 140px；附件行沿用现有 `.att` 窄屏规则或为玩家附件行写同类规则。
- 页面不出现横向滚动：表格放 `.tbl-wrap`（`overflow-x: auto`）内；内嵌检索结果行在窄屏收为图标 + 名称 + 按钮。390px 的实际效果由 T-007 浏览器截图验证。

### 7. 测试（vitest）

#### 7.1 测试支撑（`test-utils.tsx`）

- `renderPortal(path)`：与 `renderApp` 同一套 Provider，渲染 `<Portal />`。
- `stubPlayerFetch(handler)`：`vi.stubGlobal('fetch', ...)`，只处理 `/api/player/v1/` 开头的 URL，按 `(method, path, body)` 返回 `{ status, body }`；`auth/csrf` 默认返回令牌；记录全部调用供断言（含请求头 `X-GM-CSRF` 与请求体）。用例结束 `vi.unstubAllGlobals()`。
- GM 侧继续 `vi.mock('./api')` 只替换 `api`，`playerRequest` 走被替换的 `fetch`。

#### 7.2 用例与 AC-040 对照

| AC-040 / 遗留 | 文件 · 用例要点 |
|---|---|
| 登录页切换、默认玩家 | `Portal.test.tsx`：`enabled: true` 时出现"登录身份"切换且选中"玩家"、字段为"账号"；切到"管理员"后出现"管理员账号"，提交走 GM `auth/login` |
| 总开关关闭时隐藏玩家选项 | `Portal.test.tsx`：`enabled: false`（及状态读取失败）时没有"玩家"选项，只有管理员表单 |
| 玩家登录后进入玩家界面而非管理员界面 | `Portal.test.tsx`：玩家登录 → 出现"玩家导航"与"底部导航"，没有"主导航"；GM `auth/session` 之外不发任何 GM 私有请求；已有玩家会话时启动直接进入玩家界面 |
| 凭据错误不当作过期 | `Portal.test.tsx`：玩家登录 401 `unauthenticated` → 显示"账号或密码错误。"，仍在登录页，没有"会话已失效"提示 |
| 加点券提交与加后预览 | `PlayerApp.test.tsx`：快捷 +1 万、+10 万后"加后"= 余额 + 110,000；切换币种后按代币券余额计算；提交体为 `{ requestId: UUID, currency, amount }`；成功后余额按回执更新并出现"点券 +110,000"；`reminder: "failed"` 与 `replayed: true` 同样按成功显示 |
| 原编号重试 | `PlayerApp.test.tsx`：第一次返回 500 无 `code`（及网络错误）→ "结果未确认"、表单锁定；"用原编号重试"的请求体与第一次逐字相同；`invalid` 拒绝后再次提交生成新编号 |
| 发邮件只列自己角色 | `PlayerApp.test.tsx`：收件角色只等于 `me.characters`；角色行"发邮件"进入发邮件页并预选该角色；请求体只有六个字段、物品只有 `itemId` / `count` |
| 玩家物品目录走玩家前缀 | `PlayerApp.test.tsx`：附件面板内嵌检索：搜索、详情请求 URL 以 `/api/player/v1/mail/items` 开头，图标 `src` 以 `/api/player/v1/items/` 开头，没有任何 `/api/gm/v1/` 请求；"添加"把物品并入附件、同物品再加合并数量；不出现装备配置 |
| 收到"已关闭"切到已关闭页 | `PlayerApp.test.tsx` / `Portal.test.tsx`：`me`、加点券、记录任一请求返回 403 `portal_closed` → "玩家后台已关闭"；"返回登录"后登录页不显示"玩家"选项（状态已为 false）；60 秒重读 `me` 得到 `portal_closed` 也切换 |
| 会话失效回登录页 | `PlayerApp.test.tsx`：401 `unauthenticated` → 登录页 + "会话已失效，请重新登录。" |
| 关闭时登出（TASK-03 遗留） | `Portal.test.tsx`：`auth/logout` 返回 403 `portal_closed` → 已关闭页，玩家界面卸载 |
| 子开关关闭时表单不可提交 | `PlayerApp.test.tsx`：`ceraEnabled: false` → 确认与快捷按钮禁用；`mailEnabled: false` → 发送禁用、不出现检索框，且整个用例中没有 `mail/items`、`items/icon-pack`、`items/{id}/icon`、`items/{id}/tooltip` 请求；加点券返回 403 `feature_disabled` → 重读 `me` 后表单变为已停用 |
| 记录按类型筛选 | `PlayerApp.test.tsx`：切"邮件"请求 `operations?page=1&pageSize=20&kind=mail`，行显示回执 `summary` 与"成功" |
| 管理员「玩家后台」页：开关 | `PlayerPortalAdmin.test.tsx`：全权限可见、缺一项权限导航里没有；关总开关提交体为 `{ enabled: false, ceraEnabled, mailEnabled, limits: 已保存值, revision }` 且不含未保存的限额草稿 |
| 限额保存 | `PlayerPortalAdmin.test.tsx`：改"点券每日"后保存，提交体带草稿限额与读到的 `revision`；409 → "设置已被修改"，开关与保存禁用，"重新读取"后解锁并显示新 `revision` 的值；500 → "结果未确认" 同样锁定 |
| 踢会话 | `PlayerPortalAdmin.test.tsx`：单行"踢下线"提交 `{ accountId }`；"全部踢下线"确认后提交 `sessions/kick-all`；之后重读会话 |
| 审计"玩家自助"筛选 | `AdminApp.test.tsx`：选"玩家自助"请求 `audit?page=1&pageSize=20&actor=player`；玩家记录显示登录名、"玩家"与账号 ID，动作名显示为中文 |
| SD-8a GM 不变 | 既有 `ItemPicker.test.tsx`、`Mail.test.tsx`、`Initialization.test.tsx`、`DailyRefill.test.tsx` 原样通过；`ItemPicker.test.tsx` 追加断言 GM 用途的图标 `src` 仍为 `/api/gm/v1/items/` |

#### 7.3 运行

`cd Web/gm-admin && npm test`（全部，含既有 GM 用例）；`npm run build`。

#### 7.4 变异核对（逐项改回后重跑相关文件，再恢复）

1. `playerRequest` 不把无 `code` 的 5xx 归为 `unknown` → "原编号重试"用例失败。
2. 重试时重新生成 `requestId` → 同一用例失败。
3. `call` 不分流 `portal_closed` → 已关闭页用例失败。
4. 玩家 `ItemSource` 的前缀写成 GM → 玩家前缀用例失败。
5. 邮件子开关关闭时仍调用 `useIconPackStatus(items, true)` → 子开关用例的"无目录请求"断言失败。

### 8. 文档与开发代理

- `vite.config.ts`：`server.proxy` 增加 `'/api/player'`，目标同 `/api/gm`。
- `Document/gm-web-admin.md`：
  - 「构建与启动」：开发模式代理 `/api/gm` 与 `/api/player`。
  - 「界面」：登录页"玩家 / 管理员"切换（总开关关闭时只有管理员）；页面表追加 `/gm/player-portal`（玩家后台，全部权限）；新增玩家界面的路径表（`/gm/player`、`/gm/player/mail`、`/gm/player/records`、已关闭页）及其行为要点（≤900px 底部标签、已关闭 / 失效的切换、子开关关闭时不可提交、结果未确认用原编号重试）；审计页的操作者筛选。只写规则，不写开发过程。

### 9. 执行顺序

1. `npm ci`（无 `node_modules` 时）；先跑一次 `npm test` 记下基线。
2. 第 1、2 节（接口层与物品组件前缀）→ 跑全部既有测试，确认 GM 不变。
3. 第 3 节（顶层壳、登录页）+ `Portal.test.tsx`。
4. 第 4 节（玩家三页、已关闭页、写操作钩子）+ `PlayerApp.test.tsx`。
5. 第 5 节（管理员页、审计）+ `PlayerPortalAdmin.test.tsx`、`AdminApp.test.tsx` 追加用例。
6. 第 6 节样式；第 8 节代理与文档。
7. `npm test`、`npm run build`、变异核对、界面文字自查、行尾核对；在本文件末尾追加 `## Result`。

## Result

结论：按 Revision 1 完成。协调方的裁定（第 0 节第 1、2、4 项接受；第 3 项改为内联检索，已在计划正文落定，不升 revision）都已落实。没有改服务端代码。未提交，改动都留在工作区。

### 改动

- 新建
  - `playerApi.ts`：玩家接口层与类型。失败统一抛 `ApiError(status, message, outcome, code)`：
    - 没有 `code` 的 5xx 或非 JSON 响应归为 `unknown`；
    - 没有 `code` 的 4xx 归为 `invalid`；
    - 断网和超时得到 `ApiError(0, …, 'unknown', 'unknown')`。

    另有 `isRetainable` 判断失败是否保留原编号。
  - `ItemSearch.tsx`：从 `ItemPicker` 左栏原样抽出的检索组件，包含筛选、去抖、参数拼法、结果行和分页。`requireQuery` 打开时，没有关键词或筛选就不请求、不显示列表。
  - `Portal.tsx`：顶层壳。
  - `PlayerApp.tsx`：玩家外壳，包括 `call` 分流、`ItemSource`、60 秒重读 `me`、退出，以及 `useSubmission` 与 `PendingBanner`。
  - 玩家三页 `PlayerAccountView.tsx`、`PlayerMailView.tsx`、`PlayerRecordsView.tsx`，已关闭页 `PortalClosedView.tsx`。
  - 管理员页 `PlayerPortalAdminView.tsx`。
  - 测试 `Portal.test.tsx`（11 例）、`PlayerApp.test.tsx`（10 例）、`PlayerPortalAdmin.test.tsx`（6 例）。
- 修改
  - `api.ts`：`ApiError` 增加可选 `code`；`AuditRecord` 增加可选的 `actorKind`、`accountId`。
  - `useGmRead.ts`：可选读取函数，用 ref 保存，避免函数换实例时重复请求。
  - `itemInfo.tsx`：新增 `ItemSource`、`gmItems`、`ItemSourceContext`；详情、提示、失败图标三类缓存按来源分开存；图标 `src` 改用来源前缀；`useIconPackStatus(source, enabled)`。
  - `ItemPicker.tsx`：左栏改用 `ItemSearch`，增加可选 prop `source`，GM 四种用途的 DOM、文字与请求不变。
  - `LoginView.tsx`：增加"玩家 / 管理员"切换，并按设计图收掉文字。
  - `AdminApp.tsx`：可选 prop `player`，"游戏配置"组加「玩家后台」。
  - `AuditView.tsx`：增加"操作者"筛选、"操作者"列、六个新动作名；页头说明改成与现状相符。
  - `main.tsx`：渲染 `Portal`。
  - `style.css`：末尾追加"玩家自助后台"段。
  - `test-utils.tsx`：新增 `renderPortal`、`stubPlayerFetch`、`refuse`。
  - `vite.config.ts`：开发代理加 `/api/player`。
  - `Document/gm-web-admin.md`：开发代理说明；「界面」页面表加 `/gm/player-portal` 与审计操作者筛选；新增「登录与玩家界面」一节。
  - 测试 `AdminApp.test.tsx`（+1 例，审计"玩家自助"筛选）、`ItemPicker.test.tsx`（+1 例，GM 前缀不变）。

### 与计划的出入

- `AuditRecord` 的 `actorKind`、`accountId` 定为可选字段，以兼容既有测试里的记录构造。服务端总会返回这两个字段。
- 管理员页的限额面板按设计图保留了"0 = 不限"。另外在页头加了一个"重新读取"按钮，冲突横幅上也有一个同样的按钮。
- 发邮件页的物品检索在邮件子开关关闭时不渲染；结果未确认、表单锁定期间也不渲染。

### 验收对照（AC-040 与遗留）

| 要点 | 用例 |
|---|---|
| 登录页切换、默认玩家 | `Portal.test.tsx` "玩家后台开启时登录页默认玩家，可切到管理员并走管理员登录" |
| 总开关关闭时隐藏玩家选项 | `Portal.test.tsx` "玩家后台%s时登录页不显示玩家选项"，覆盖关闭与状态读取失败两种情形 |
| 玩家登录后进入玩家界面 | `Portal.test.tsx`：<br>• "玩家登录后进入玩家界面而不是管理员界面"：登录请求带防伪令牌；没有 GM 私有请求<br>• "已有玩家会话时直接进入玩家界面，即使同时有管理员会话"（裁定 1） |
| 凭据错误不当作过期 | `Portal.test.tsx` "玩家凭据错误留在登录页显示服务端消息，不当作会话失效" |
| 加点券提交与加后预览 | `PlayerApp.test.tsx` "快捷加数与币种切换更新加后预览，提交带新编号，提醒失败仍按成功显示"：请求体恰为三个字段；`reminder: failed` 按成功显示 |
| 原编号重试（TASK-04 / TASK-03 遗留） | `PlayerApp.test.tsx` "结果未确认时锁定表单，用原编号重试；明确被拒后下一次换新编号"：<br>• 非 JSON 的 500、网络错误两次都保留原编号与内容<br>• `invalid` 被拒之后下一次换新编号<br>• 重放结果按成功显示 |
| 发邮件只列自己角色、玩家前缀 | `PlayerApp.test.tsx` "收件人只列自己的角色；物品检索与图标走玩家前缀；请求只带六个字段"：<br>• 角色行"发邮件"会预选该角色<br>• 内联检索，"添加"两次合并为数量 2<br>• 图标 `src` 为 `/api/player/v1/items/3037/icon`；不出现装备配置<br>• 请求体恰为六个字段<br>• GM `api` 只被调用过 `auth/status` 和 `auth/session` |
| 本地校验 | `PlayerApp.test.tsx` "未选收件人或内容为空时不提交"、"设置了每日上限时显示今日用量，并按单次与剩余额度拦截" |
| 收到"已关闭"切到已关闭页 | `Portal.test.tsx`：<br>• "读取得到已关闭时切到已关闭页，返回登录后不再显示玩家选项"<br>• "登录时玩家后台刚被关闭则进入已关闭页"<br><br>`PlayerApp.test.tsx`：<br>• "每 60 秒重读账号，玩家后台被关闭后停在页面上的玩家进入已关闭页" |
| 会话失效回登录页 | `Portal.test.tsx` "会话失效时回到登录页并提示" |
| 关闭时登出（TASK-03 遗留） | `Portal.test.tsx` "总开关关闭时登出被拒，仍清空玩家界面进入已关闭页" |
| 玩家退出（裁定 1） | `Portal.test.tsx` "玩家退出后回到登录页，管理员会话仍有效时直接回管理台" |
| 子开关关闭时表单不可提交 | `PlayerApp.test.tsx`：<br>• "点券子开关关闭时表单不可提交"<br>• "提交时点券子开关刚被关闭：提示服务端消息并重读为已停用"<br>• "邮件子开关关闭时表单不可提交，也不请求物品目录、图标与提示"：整个用例中没有 `mail/items*` 与 `items/*` 请求（TASK-05 遗留） |
| 记录按类型筛选 | `PlayerApp.test.tsx` "按类型筛选并显示回执摘要与结果" |
| 管理员页：可见性与开关 | `PlayerPortalAdmin.test.tsx`：<br>• "只对全部权限管理员可见"<br>• "切换总开关即提交：限额取已保存的值，不带未保存的草稿"（裁定 4） |
| 限额保存与冲突 | `PlayerPortalAdmin.test.tsx`：<br>• "保存限额带草稿与读到的版本"<br>• "保存返回 409 / 500 时锁定写入，重新读取后解锁" |
| 踢会话 | `PlayerPortalAdmin.test.tsx` "踢单个账号与全部踢下线"：全部踢下线有确认，确认前不发请求 |
| 审计"玩家自助"筛选 | `AdminApp.test.tsx` "审计按"玩家自助"筛选并显示玩家操作者" |
| SD-8a GM 不变 | 既有 160 例原样通过；`ItemPicker.test.tsx` "GM 用途的检索与图标仍走管理员前缀" |

### Checks

- `npm ci`：成功（工作区原先没有 `node_modules`）。
- 基线：改动前 `npx vitest run` 13 个文件、160 例全部通过。抽出 `ItemSearch` 与改来源之后、加新测试之前再跑一次，仍是 160 例全部通过，GM 回归。
- `npm test`：最终 16 个文件、189 例全部通过，连续多轮如此。
  - 偶发：全量并行运行时，既有 GM 用例 `DailyRefill.test.tsx` "新增通过补给专用目录…" 有一轮超过 3000 ms 的等待上限而失败。
  - 该用例单独运行时，改动前后都约 310–340 ms；`ItemPicker` 的同类用例改动前后也几乎一样（848 / 869 ms）。
  - 我按 `HEAD` 用 `git archive` 导出的基线副本连跑 5 轮全部通过，当前代码连跑 5 轮也全部通过。
  - 判断是新增测试文件加重了并行负载，使这条既有用例偶发超时，不是行为变化。
  - 自己写的用例里，"用原编号重试"曾因 antd 按钮在 loading 时把图标名并入可访问名而偶发找不到按钮。已改成按子串匹配，并等按钮空闲再点（`clickWhenIdle`）。之后连跑 5 + 4 轮全部通过。
- `npm run build`（`tsc --noEmit` + `vite build`）：通过，没有类型错误。Vite 报了 chunk 大于 500 kB 的提示，属于警告：主包约 1.27 MB，主要是 antd。
- 变异核对：每项改完重跑相关测试文件，再恢复原代码，并核对恢复后与改前的 SHA-256 一致。
  1. 无 `code` 的 5xx 不归为 `unknown`：重试用例失败。
  2. 重试时重新生成 `requestId`：重试用例失败。
  3. `call` 不分流 `portal_closed`：已关闭页的两个用例失败。
  4. 玩家 `ItemSource` 的前缀写成 GM：玩家前缀用例失败。
  5. 邮件子开关关闭时仍请求图标包：子开关用例失败。

  前三项在修正重试用例之后又跑了一次，结果相同。
- 界面文字自查：用脚本抽出新增界面文件里的全部中文字符串，逐条核对，只有以下几类：
  - 标题、标签、按钮、表头；
  - 状态（在线 / 离线、已停用、结果未确认、设置已被修改、玩家后台已关闭）；
  - 数据型提示（单次上限 n、今日剩余 n、超过 10 格、请选择收件角色、正文、附件、金币至少填一项）；
  - 设计图原文"0 = 不限""收件人未选""（无标题）"。

  没有导语、说明气泡或编造的示例值。管理员模式保留了原有的"未初始化 / 连不上"状态提示。
- 行尾：
  - `git ls-files --eol Web/gm-admin Document/gm-web-admin.md` 共 60 个文件，全部 `w/lf`。
  - `git diff --stat` 与 `--ignore-cr-at-eol --stat` 一致（14 files, +264/−134）。
  - 13 个新建文件（含本文件）用 Python 逐字节统计，CR 均为 0。
- 服务端：`git diff --stat -- Server` 为空，没有改动，因此不需要跑 `dotnet build`。

### 留给 T-007

- 390px 与电脑宽度的实际布局、底部标签、内联检索的窄屏排布、已关闭页、审计页，都还没在浏览器里看过，jsdom 不做布局。按 AC-041 截图验证。
- 玩家角色只有基础职业名，接口没有转职字段。
- 发邮件页物品图标的悬停提示与 GM 同一组件，只在浏览器里能看到。

### 第 2 轮（task-rework）

按协调方要求，在提交前修复快审残余风险 1、3、4。残余风险 2（页头"重新读取"按钮）保留；DailyRefill 偶发超时本轮不处理。

- **风险 1：结果未确认的原编号切页会丢。**
  - 改法：写操作状态 `{ pending, busy }` 从组件内存上移到 `PlayerApp` 层（`slots`，按接口路径分开存）。提交、重放判定和失败分流也一并移到 `PlayerApp.submit`。`useSubmission` 只剩薄封装，"一次只提交一次"的判断也在这一层。
  - 效果：切页、组件重新挂载都不会丢；回到该页仍然锁定，仍提供"用原编号重试 / 放弃"。请求还没返回就切走的，结果照样记到 `PlayerApp` 层。仍只存在内存里，不写 sessionStorage（计划第 0 节第 5 项的取舍不变）。
  - 发邮件页重新挂载后，物品详情按锁定内容里的物品读取，所以名称和件数显示正确。
- **风险 3：首次读取 `me` 失败一直转圈。**
  - 改法：首次读取遇到非分流失败（5xx、网络错误等）时，在页面位置显示错误消息、`HTTP 状态`（有状态码时）和"重新读取"按钮，读到之后恢复。已经读到过之后再失败，仍只弹提示。
  - 顺带：玩家侧断网时的消息改为"无法连接服务，请检查网络后重试。"（原先沿用的是 GM 的"无法连接管理服务…"）。
- **风险 4：管理员模式连接中，转圈盖住了身份切换。**
  - 改法：转圈只替换管理员表单区域，"登录""玩家 / 管理员"切换保留，能切回玩家登录。
  - 对纯管理员登录页（玩家后台关闭）的影响：连接中时卡片标题"登录"也会显示，其余不变。
- **文档**：`Document/gm-web-admin.md`「登录与玩家界面」补充了两点：锁定状态切页保留；首次读取失败的显示。

新增用例：

| 风险 | 用例 |
|---|---|
| 1 | `PlayerApp.test.tsx` "结果未确认后切到别的页再回来仍锁定，重试用的仍是原编号"：502 后切到"记录"再回"我的账号"，横幅与锁定仍在，数量仍为 7；重试请求体与第一次逐字相同，之后成功 |
| 3 | `PlayerApp.test.tsx` "首次读取账号失败时显示错误与"重新读取"，不一直转圈"：显示"服务暂不可用，请稍后重试。"与"HTTP 500"，点"重新读取"后显示余额 |
| 4 | `Portal.test.tsx` "管理员模式下管理服务还在连接时只转圈管理员表单，保留身份切换"：GM `auth/status` 一直不返回时切到"管理员"，出现"正在连接管理服务…"且"玩家"选项仍在，切回后出现玩家登录表单 |

Checks（第 2 轮）：

- `npx tsc --noEmit` 通过。
- `npm test` 全量连跑 3 轮，每轮 16 个文件、192 例全部通过（第 1 轮是 189 例，本轮新增 3 例）。
- `npm run build` 通过，仅有与第 1 轮相同的 chunk > 500 kB 警告。
- 变异核对：每项改完重跑相关测试文件再恢复，并核对恢复后与改前的 SHA-256 一致。
  1. 在 `useSubmission` 卸载时清掉锁定状态：风险 1 用例失败。
  2. 首次读取失败仍显示转圈：风险 3 用例失败。
  3. 连接中隐藏身份切换：风险 4 用例失败。
- 行尾：改动的已跟踪文件仍全部为 `w/lf`；新建文件与本文件 CR 为 0。
- 服务端：未改动。

## Result Review

### Independent Quick Review 1

```yaml
kind: rks-review
verdict: approved
```

- 审查对象：Contract Revision 1 及其详细计划（第 0 节 ★ 项按协调方裁定）；`## Result` 第一轮执行结果；工作区未提交改动（14 个已跟踪文件修改 + 12 个新建源码 / 测试文件，`git diff --stat -- Server` 为空）。
- 依据：TASK-06 契约与计划；plan.md SD-10、SD-8a、SD-1a；spec.md RQ-040–044、AC-040、RQ-051；`Document/gm-web-admin.md`「访问与审计」「玩家自助后台」；UI 设计图 `Document/玩家自助后台UI设计图-20261113.html`（抽取全部可见文字逐项对照）。适用 reviewing-work 的 Task Unit + Code 准则，范围限于本单元的局部缺陷。
- 独立核查与证据：
  - GM 零变化：`ItemSearch` 的参数拼法、筛选、分页与原 `ItemPicker` 左栏逐行一致，GM 弹窗的提示句、空状态、按钮与 `aria-label` 原样由调用方传入；`itemInfo` 的 `source` 参数全部缺省 `gmItems`（前缀 `/api/gm/v1`，经 `api` / `quietApi`），缓存键按来源分开；`useGmRead` 缺省 `api`；`AdminApp` 不传 `player` 时 `LoginView` 只走管理员分支。玩家侧源码（`Portal`、`PlayerApp`、三页、`playerApi`）不引用 `api` / `quietApi`，硬编码 `/api/gm` 只在 `gmItems`；`PlayerPortalAdminView` 只经 GM `api()`。
  - 顶层壳与分支：`playerRequest` 无 `code` 的 5xx / 非 JSON → `unknown`，无 `code` 的 4xx → `invalid`，`fetch` 抛错（含 30 秒超时）→ `ApiError(0, …, 'unknown', 'unknown')`；`call` 先分流 `portal_closed` / `unauthenticated` 再抛出；登录时的 `unauthenticated` 走表单提示而非过期；关闭时登出被拒仍进入已关闭页。`useSubmission` 在可保留失败时复用原 `{requestId, body}`、锁定表单，明确拒绝后清空并在下次生成新编号；`outcome` 判断与文档的小写取值（`unknown` / `rolledBack`）一致。邮件请求体由 `shown` 构造，严格六字段、附件只含 `{itemId, count}`。邮件子开关关闭时 `ItemSearch` 不渲染、`useItemDetails` 收空列表、`useIconPackStatus(items, false)` 不请求。
  - 管理员页：开关即时提交且限额取已保存值、保存带草稿与 `revision`；409 / ≥500 / 非 `ApiError`（GM `api()` 网络错误为裸异常）锁定，"重新读取"解锁并丢弃草稿；踢单个、全部踢下线（仅标题确认）后重读会话。审计"操作者"筛选参数顺序不改既有 URL，玩家行显示"玩家"标记、登录名与账号 ID。
  - 文字：新增界面文字与设计图抽取结果对应（登录、余额、角色、加点券、收件角色、附件、内容、预览、记录、已关闭、玩家后台设置各面板），未见页面导语、说明气泡、开关旁重复状态文字、操作者与时间或编造示例值；"0 = 不限"为设计图原文。
  - 测试有效性：重试用例构造了非 JSON 500、网络错误、`invalid` 拒绝与重放四种回复并逐字比对请求体；子开关用例断言整个用例无 `mail/items*` / `items/*` 请求；GM 前缀用例断言图标 `src`。这些用例能暴露计划第 7.4 节列出的错误。
  - 复跑：`npm test` 全量 8 轮（5 轮普通 + 3 轮 verbose）均 16 文件 189 例通过；`npm run build` 通过（仅 chunk > 500 kB 警告，与既有一致）。行尾：`git ls-files --eol` 所有已跟踪改动文件为 `w/lf`，12 个新建源码文件与本文件 Python 统计 CR 为 0。
- DailyRefill 偶发超时评估：该用例在全量并行下耗时 1.0–1.2 s（本工作区 3 轮），以 `git archive HEAD` 导出的基线副本（13 文件 160 例）全量运行为 0.84–0.86 s（3 轮），单独与 `ItemPicker.test` 同跑为 0.38–0.41 s（改动后）对比原报告 0.31–0.34 s；每步 `asyncUtilTimeout` 为 3000 ms。耗时增量来自新增三个测试文件带来的并行负载，单跑无回归，本审查 8 轮未复现。判定**不阻塞**、非本单元引入的行为不稳定；若 CI 上复现，再考虑为该用例单独加超时或限制 vitest 并发，而不是改业务代码。
- 非阻塞残余风险（交协调方 / T-007 留意，不要求本单元修改）：
  1. 结果未确认的 `pending` 只存在页面组件内存（计划第 0 节第 5 项的既定取舍）：玩家在锁定期间切到别的页面，或再次点"发邮件"标签（`PlayerMailView` 以 `location.key` 为 key 会重挂载），原编号即丢失，之后再提交会用新编号，存在重复入账 / 重复发信的可能；后果受服务端每日限额约束，且只涉及本人账号。如需收紧，可把 `pending` 提到 `PlayerApp` 层或锁定期间禁用导航。
  2. 管理员页页头多了一个"重新读取"按钮，设计图没有（Result 已披露）；冲突横幅上已有同名按钮。
  3. 首次读取 `me` 遇到非分流失败（如 5xx）时页面停留在 `Spin`，直到下一次 60 秒重读。
  4. 登录页切到"管理员"且管理服务探测中时，`Spin` 替换整个表单（含身份切换），探测结束前无法切回"玩家"。

### Independent Quick Review 2

```yaml
kind: rks-review
verdict: approved
```

- 审查对象：同一 Contract Revision 1；`## Result` 的「第 2 轮（task-rework）」；当前未提交改动（27 个文件，服务端无改动）。只复审第 1 轮遗留风险 1、3、4 的修复及其直接回归，第 1 轮其余结论引用不重述。
- 风险 1（锁定状态上移到 `PlayerApp`）：
  - 隔离：`slots` 按接口路径为键（`cera/grant`、`mail/send`），`useSubmission(path)` 只读自己那一格；`submit` 从 `pendingOf.current[path]` 取原编号与内容，`running` 也按路径记录。加点券与发邮件的编号、内容、busy 互不串用；一边处于未确认状态不会锁住另一边。`discard(path)` 只清对应一格。
  - 正确性：重试时忽略新传入的 body，复用该路径的 `{requestId, body}`；成功或明确被拒才清空；`isRouted` 失败不留 pending；`patchSlot` 用函数式更新，`pending` 与 `busy` 两次写入不会互相覆盖。请求在途时切走再回来，`busy` 仍为真，表单锁定，重复点击会被 `running` 拦下。发邮件页重挂载后由 `pending.body.items` 读详情，预览和件数用锁定内容。
  - 用例"结果未确认后切到别的页再回来仍锁定…"在 502 后切到"记录"再回来，断言横幅、锁定、数量 7 仍在，且重试请求体与第一次逐字相同，能暴露"卸载即清空"的回归（Result 中的变异核对也证明了这一点）。没有跨路径隔离的专门用例，但隔离由按路径取值的结构保证，不作为缺陷。
  - 已知取舍不变：只存内存，刷新页面会丢（计划第 0 节第 5 项）。视图卸载后才到达的成功结果仍会执行该视图的 `accept`（对已卸载组件 setState 无害；余额经 `setBalances` / `refreshMe` 在上层更新），"已送达 / 点券 +n"提示随视图卸载而不显示。可接受。
- 风险 3：首次 `me` 非分流失败时显示失败消息、`HTTP n`（有状态码时）与"重新读取"，没有引导或解释句；读到后恢复，之后的失败仍只弹提示。断网消息改为玩家侧专用的"无法连接服务…"，只影响 `fetch` 抛出 `TypeError` 的情形；超时仍走 `failureMessage`，`code` 仍为 `unknown`，重试分支不受影响。
- 风险 4：`Spin` 只替换管理员表单区域，身份切换保留。管理员提交登录时，条件仍是 `loading && initialized === null`，表单与按钮 loading 行为与改前相同。纯管理员登录页在连接中多显示卡片标题"登录"，属于 Result 已披露的可见差异，不影响功能。
- 复跑：`npm test` 共 8 轮（3 轮普通 + 5 轮 verbose），7 轮 192 例全过，**1 轮** `DailyRefill.test.tsx` "新增通过补给专用目录…"在 3353 ms 超时失败；`npm run build` 通过；行尾：已跟踪改动全 `w/lf`，27 个改动 / 新建文件的 CR 合计为 0。
- DailyRefill 再评估：第 2 轮的改动不触及 DailyRefill、ItemPicker、itemInfo 路径（`LoginView` 的改动只影响未登录时的渲染）。该用例单独连跑 8 次耗时 0.27–0.92 s 全过；以 `git archive HEAD` 导出的基线副本全量跑 5 轮全过，耗时 0.71–**1.82 s**，可见基线在全量并行下也有大幅抖动。判定这是既有用例在并行负载下的环境性抖动，新增测试文件加重了负载、提高了触发概率，不是本单元引入的行为回归，**不阻塞本单元**。但它已在两轮审查中都实际出现，"`npm test` 全部通过"这项检查因此不是每次都稳定可得。建议协调方另开一个小项处理测试基础设施（例如给该用例单独设更长超时，或限制 vitest 的 `maxWorkers`），不要改业务代码。
- 新回归：未发现。
