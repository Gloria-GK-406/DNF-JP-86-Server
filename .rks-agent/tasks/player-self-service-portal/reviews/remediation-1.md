# 第 1 轮修正：最终审查（change-review）后的处置

- 依据：`reviews/change-review.md`（verdict `changes_required`）与 `reviews/change-challenges.md` 的 CH-1–CH-4。
- 起点：分支 `task/player-self-service-portal-329`，HEAD `0fd7b39d1`。本轮只改工作区文件，未 commit，未读取 `agent-credentials.env`，两份库副本未替换、未迁移、未手工改动。
- 处置项：F-1（CH-3，阻塞）、CH-1（主控决定修复）、F-2（CH-4，阻塞交付）、CH-2（文档）、重新发布测试服产物。

## F-1（CH-3）：GM 概览页只取管理员审计记录

**改了什么**

- `Web/gm-admin/src/OverviewView.tsx`：两次审计查询都加 `actor=gm`：
  - "需要处理"：`audit?page=1&pageSize=100&outcome=attention&actor=gm`；
  - "最近操作"：`audit?page=1&pageSize=20&actor=gm`。
- `Web/gm-admin/src/Overview.test.tsx`：新增用例"只取管理员的审计记录"。
  - 模拟服务端按 `actor` 真实筛选：120 条玩家 `PlayerLogin`/`Rejected` 排在一条管理员 `GrantCera`/`Unknown` 前面，"最近操作"另有一条玩家 `PlayerSendMail`；
  - 断言"需要处理"只有"发放点券结果未确认"一类一条，没有"玩家登录已拒绝"；
  - 断言"最近操作"有"发放点券"，没有"玩家登录""玩家发邮件"；
  - 断言所有 `audit?` 请求都带 `actor=gm`。
- `Document/gm-web-admin.md` 的 `/gm/` 页面说明补一句：这两块只取管理员记录，玩家记录在「操作记录」页查看。

**为什么**：不带 `actor` 时服务端返回全部操作者（本分支新增行为）。玩家登录失败、超限额等拒绝会进入 GM 首页"需要处理"，并占满 100 条窗口，把管理员自己的 `Unknown` 发放挤出去，削弱防重复发放的机制（SC-11 / RQ-051）。服务端早已支持 `actor=gm`，迁移前的记录也是 `gm`，前端修正即可，不需要改服务端。

**检查**

- 新用例在修正后通过。
- 变异核对：临时去掉两处 `&actor=gm`，新用例失败（`× 只取管理员的审计记录…`，其余 3 例通过）。随后从 scratchpad 备份逐字节还原，`actor=gm` 计数恢复为 2。
- 全量前端测试见文末「验证」。

## CH-1：玩家请求按账号限制在途数

**改了什么**

- 新增 `Server/src/Dfo.Server.GmWeb/PlayerRequestLimiter.cs`：进程内按账号计数的轻量计数器（锁 + 字典，风格同 `GmLoginLimiter`）。
  - `PerAccount = 2`；
  - `TryEnter` / `Exit`：计数归零时删除该账号的键，不会无限增长；重复 `Exit` 不会减到负数；
  - `InFlight` 只供测试读取。
- `GmWebModule`：注册为单例。
- `GmAccessBoundary`：玩家前缀的请求通过全部既有检查（总开关、会话、HTTPS、同源、防伪令牌）之后、调用 `next` 之前：
  - 只对带 `HttpGameplayOperation` 元数据的非公开玩家路由计数，也就是会在执行器上作为命令运行的 5 条：`auth/logout`、`me`、`cera/grant`、`mail/send`、`operations`；
  - 超出时直接返回 429 `{ message: "请求过于频繁，请稍后重试。", code: "rate_limited" }`，不调用 `next`，所以不入队、不解析请求体、不写审计；
  - 名额在 `finally` 里归还。
- 不计入的路由与理由：
  - 登录：公开路由，另有失败限流（`GmLoginLimiter` 的玩家实例），并且要求先有会话才能按账号计数；
  - `auth/status`、`auth/csrf`：公开，不碰执行器；
  - `auth/session`：只返回边界已识别的身份；
  - 物品目录、图标、物品说明：在执行器外只读，不占串行执行器。
- 名额占用多久：`HttpGameplay.RunAsync` 以 `BodyOnly` 方式入队，入队令牌为 `CancellationToken.None`，处理程序一直等到命令执行完（`CommandBus.SendInternalAsync` 的 `CompleteAsync` 等待 `queued`）。客户端中途断开时，名额要等命令真正执行完才归还，断开后马上重发绕不过上限。这一点写在 `PlayerRequestLimiter` 的 remarks 里。
- 前端：
  - `playerApi.ts`：新增 `RATE_LIMITED = '请求过于频繁，请稍后重试。'`。除 `auth/login` 外，`rate_limited` 一律显示这句；登录保留服务端原有的"尝试次数过多，请稍后重试。"，因为那是失败次数限流，不是在途上限。新增 `isUnexecuted`。
  - `PlayerApp.tsx` 的 `submit`：遇到 `rate_limited` 时只提示，不改动该路径的待确认状态，`finally` 照常解除 busy 与 running。
    - 原来没有待确认项：表单立即解锁，下一次提交生成新编号。请求从未执行，所以安全。
    - 原来有"结果未确认"的待确认项（这次是用原编号重试被限流）：保留原编号与锁定，下次重试仍用原编号。
    - 任务说明写的是"同一编号或新编号都安全"，这里有一处偏差：当之前有一次结果未确认的提交时，换新编号并不安全。原提交可能已经入账，被限流的请求对它的结果什么也没说明。所以这种情况保留原编号，不清空。
- `Document/gm-web-admin.md`「玩家自助后台」补一条"在途上限"，写明计入与不计入的路由、429 `rate_limited`、不执行不写审计、前端的提示与解锁行为。

**为什么**：玩家的执行器路由没有任何在途或频率限制。执行器是无界 FIFO，被放弃的请求照常执行。一个持有会话的玩家可以并发灌命令，拖慢全服游戏逻辑（CH-1）。主控决定在 PR 前修复，不登记为 Accepted Risk。

**检查**

- 新增 `Server/tests/Dfo.Server.Host.Tests/PlayerPortalRequestLimitHttpTests.cs`，共 2 例：
  1. 真实并发：
     - 用 `IGameplayQueue` 放一个等门闩的作业占住执行器；
     - alice 发 2 个 `GET me`，等 `InFlight(1) == 2`（确认两条都已过边界、在排队）；
     - 第 3 个请求（`cera/grant`，带固定编号）得到 429 `rate_limited`，且没有 `outcome`；再发 `me` 也是 429；
     - 同时 alice 的 `items/icon-pack`、`auth/session` 仍是 200（不计入）；bob 的 `me` 被接受，占 bob 自己的名额；
     - 打开门闩后，三个排队请求都是 200，alice 与 bob 的计数都归零；
     - 被拒的那次加点券没有执行：余额仍是 `10:20`，`PlayerGrantCera` 审计 0 条；
     - 用同一编号再提交得到 200、`replayed=false`，余额变为 `15:20`，说明该编号此前从未使用。
  2. 计数器单元：上限、按账号隔离、归还后可再进入、多次 `Exit` 不会减到负数。
- 构建时 xUnit 分析器 `xUnit1031` 拒绝了 `finally` 中的 `.Result`，已改为 `await`；改后零警告零错误。
- 前端 `PlayerApp.test.tsx` 新增 2 例：
  - "请求过于频繁被拒（未执行）"：显示"请求过于频繁，请稍后重试。"（服务端原文被替换），不出现"结果未确认"，确认按钮与数量框可用，再提交成功；
  - "结果未确认后重试遇到请求过于频繁"：504 → 用原编号重试得到 429 → 仍显示"结果未确认"、确认按钮锁定 → 再重试成功；三次请求体完全相同（同一编号）。

## F-2（CH-4）：用户测试流程分玩家窗口与管理员窗口

**改了什么**（`Document/tasks/player-self-service-portal/用户测试流程.md`）

- 「前置条件」首条新增"两个窗口"：
  - 玩家窗口用常用浏览器的普通窗口，管理员窗口用另一个浏览器或无痕窗口；
  - 两边都用 `http://localhost:43081/gm/`，不要用 `127.0.0.1`，以免与主干后台的 Cookie 互相覆盖；
  - 账号 B 需要第三个独立浏览器环境。同一浏览器的多个无痕窗口共用登录状态，不能一个给管理员、一个给 B。
- 第 6、10、11、12 步拆成逐条动作，每条标明在**玩家窗口**还是**管理员窗口**里做。第 12 步的踢人结果分别写明在哪个窗口观察。
- 第 1、2、3、13、15 步顺带标明窗口，避免第 2 步"退出后看登录页"让管理员窗口失去登录。第 2 步改为在玩家窗口看登录页，管理员窗口保持登录。
- 第 13 步补一条预期："服务概览"的"需要处理""最近操作"不出现玩家记录，对应 F-1。
- 测试服表"分支 / 提交"改为"提交见交付说明"，由主控最终填写。原来写的 `d6cd73cbf` 已过时。

**为什么**：同一浏览器里有效的玩家会话优先于管理员会话（`Portal.tsx`）。原流程只给一个地址，第 11 步"玩家保持登录、管理员关闭总开关"在单一浏览器里做不到。用户如果自己改用 `127.0.0.1`，又会与主干后台的 `Dfo.Gm.Session` 互相覆盖。

**检查**：逐条对照第 6、10、11、12 步，每个动作前都有窗口标注。文档仍为 LF、无 BOM，见行尾核对。

## CH-2：受信代理与 Origin 的部署前提

**改了什么**：`Document/gm-web-admin.md`「玩家自助后台」开头新增一段：

- 经反向代理对外开放前，必须先配置 `GmWeb:TrustedProxies`；
- 地址要与代理实际回源的地址一致：从 `::1` 回源写 `::1`，从 `127.0.0.1` 回源写 `127.0.0.1`；
- 未配置时全体玩家共用一个限流来源，会一起被锁（#305）；
- 部署前核对反向代理对 `/api/player/*` 的 `Origin` 处理：代理如果只对 `/api/gm*` 拦截并去掉 `Origin`，玩家接口的 `Origin` 会原样到达服务端，同源检查改用受信代理解析出的协议与 `Host` 比对，任一不符就拒为 `forbidden`。

**为什么**：行为本身符合 SC-9 / RQ-030（未配置时与现在相同），缺的是运维前提。线上 Caddy 的改动在 Scope Out，这里只写文档。

**检查**：通读该节，与「访问与审计」中 `TrustedProxies` 的语义一致（单个 IP、`::ffff:a.b.c.d` 与 `a.b.c.d` 等同、只读一层）。

## 重新发布测试服产物

做法沿用 TASK-07 Result 第 3 节。日志写在 `artifacts/player-portal-329/logs/`，文件名带 `-r1` 后缀，旧日志不覆盖。

- 发布前核对：43001/43010/43011/43100/43080/43081 都没有监听，也没有从 `artifacts/player-portal-329/host` 运行的 Host 进程。
- `cd Web/gm-admin && npm run build`：退出码 0（`logs/npm-build-r1.log`）。输出只有原有的"chunk 超过 500 kB"提示；入口变为 `assets/index-BsCROQG6.js`。
- `dotnet publish Server/src/Dfo.Server.Host -c Release -o artifacts/player-portal-329/host`：退出码 0，无警告（`logs/publish-r1.log`）。
- 一致性核对：
  - `diff -r Web/gm-admin/dist artifacts/player-portal-329/host/wwwroot/gm` 无差异；
  - 两边 `index.html` 的 SHA-256 都是 `9b5d023f673630d0…`；
  - `assets/` 里只有 `index-BsCROQG6.js` 与 `index-C3lW4IzP.css`，旧入口 `index-BxbXS_V_.js` 已不在。
- 内容抽查：
  - 发布的 `Dfo.Server.GmWeb.dll` 含 `PlayerRequestLimiter`；
  - 前端包含 `actor=gm` 与"请求过于频繁"。
- `host/appsettings.json` 中 `GmWeb:Enabled` 与 `Networking:Enabled` 仍为 `false`，由脚本用环境变量打开。
- 两份库副本都没有被替换、迁移或手工修改。
  - agent 副本：冒烟时 agent 脚本按原设计打开了它，与 TASK-07 的冒烟相同；
  - 用户副本：没有被打开。

**agent 冒烟（短暂）**

- 在后台运行 `bash artifacts/player-portal-329/run-agent-server.sh`，日志写在 `logs/agent-host-r1.log`。
- 6 个 43xxx 端口全部由 PID 47696 监听：
  - 路径：`C:\Program Files\dotnet\dotnet.exe`；
  - 命令行：`dotnet Dfo.Server.Host.dll`；
  - 创建时间：2026-11-14 04:00:32。
- 检查结果：
  - `http://localhost:43081/gm/` 与 `http://127.0.0.1:43081/gm/` 都返回 200，正文引用 `index-BsCROQG6.js`；
  - `GET /api/player/v1/auth/status` 返回 200 `{"enabled":true}`。agent 副本的总开关是主控此前实测时打开的，本次没有改动；
  - `GET /api/gm/v1/auth/status` 返回 200；
  - 日志没有异常，内容预热 78 项、0 失败。
- 结束方式：
  - 重新核对 PID、路径、命令行与创建时间，四项都一致后，只结束 PID 47696；
  - 之后 43xxx 端口全部释放，父 bash 也已退出。后台任务报告的退出码 127 来自这个被结束的进程；
  - 没有碰 41xxx/42xxx 端口，也没有结束其它进程。

## 验证

| 检查 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx` | 0 警告 0 错误。第一次构建失败：测试里的 `.Result` 触发 xUnit1031，改为 `await` 后通过 |
| `dotnet test Server/tests/Dfo.Server.Host.Tests --no-build --filter "FullyQualifiedName~PlayerPortal\|FullyQualifiedName~Tests.Gm\|FullyQualifiedName~TrustedProxy\|FullyQualifiedName~GateHttpHost"`（`PVF_ARCHIVE_PATH` 只读指向 protocol 客户端的 PVF） | 240/240 通过，耗时 3 分 10 秒，含新增 2 例 |
| `Web/gm-admin` `npm test` 第 1 轮 | 16 个文件，195/195 通过，17.97 秒 |
| `Web/gm-admin` `npm test` 第 2 轮 | 16 个文件，195/195 通过，19.69 秒 |
| `npm run build` | 通过 |
| `git diff --check` | 无空白问题 |

- 前端测试从 192 例增加到 195 例：概览 1 例，玩家请求过于频繁 2 例。
- 两轮前端测试都没有与 dotnet 测试同时运行，没有出现 change-review 记录的负载下钩子超时。
- 测试日志在 scratchpad 的 `C:\Users\Gloria\AppData\Local\Temp\claude\ee4ff4290ff1f592\95d2e4ea-7241-4368-9cd2-277ea6a22dc3\scratchpad\logs\` 下：`build.log`、`limit-test.log`、`host-tests.log`、`npm-test-1.log`、`npm-test-2.log`。
- 没有重跑全量 `dotnet test`。改动只在 GmWeb 边界与新增测试里，回归范围由上面的过滤条件覆盖；全量结果沿用 TASK-07 的记录。

## 行尾核对

以下 12 个文件逐字节检查过：没有 CR、没有 UTF-8 BOM、以换行结尾。`git ls-files --eol` 显示已跟踪的文件为 `i/lf w/lf`。

- 新文件：
  - `Server/src/Dfo.Server.GmWeb/PlayerRequestLimiter.cs`
  - `Server/tests/Dfo.Server.Host.Tests/PlayerPortalRequestLimitHttpTests.cs`
  - 本文件
- 修改的文件：
  - `GmAccessBoundary.cs`、`GmWebModule.cs`
  - `OverviewView.tsx`、`Overview.test.tsx`
  - `PlayerApp.tsx`、`PlayerApp.test.tsx`、`playerApi.ts`
  - `Document/gm-web-admin.md`
  - `Document/tasks/player-self-service-portal/用户测试流程.md`

## 留给主控

- `用户测试流程.md`：
  - "分支 / 提交"已改为"提交见交付说明"。本轮没有 commit，提交后请把提交号填进交付说明；
  - "就绪情况"仍由主控填写。
- change-review 列出的以下计划内产出仍未完成：
  - `verification/completion-evidence.md`；
  - 用户测试服的启动与交付（需要先裁定 UDP 端口）；
  - 在线收信等用户实机验证。
- 写 completion evidence 时请补上本轮新增的三项行为：
  - 概览页只查 `actor=gm` 的审计记录；
  - 玩家执行器路由每个账号同时最多 2 个在途请求；
  - `rate_limited` 在玩家接口里有了新含义：登录以外的路由用它表示超出在途上限。
