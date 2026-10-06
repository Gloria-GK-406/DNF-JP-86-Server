# T-004 GM 马战配置 UI 结果

基准：`b4bf8d0d01113c39962c885b44002453dcb740c6`，工作区 `warehouse-storage`。消费当前 `JoustModels.cs` 与 `backend-result.md` 的设置接口；变更未提交、未部署。

## Changed / Why

- 注册 `joust` 的“设置活动”分派，新增专属弹窗，只通过 `events/joust/settings` GET/POST 读取或保存。保存请求仅包含独立 `expectedRevision` 与六字段 `schedule`；日期和启停保留在通用管理。
- 编辑每日开始时间、期数、每期间隔、下注/封盘时长、三个比赛阶段的单阶段秒数。前端拒绝非整数、空值、越界、跨午夜和阶段总长超出每期间隔，后端仍为权威校验者。
- PVF 骑士、两种下注材料与参与/中奖奖励展示名称和 ID，缺失物品名称保留 ID；无额外物品目录请求。骑士的攻击类型及动画向量不进入 GM 展示。
- 409、500、网络及 `outcome=unknown` 后保留全部提交草稿并锁写。重读失败继续锁写；成功后展示当前生效赛程，用户核对确认后使用新 revision 保存。普通 400 保留草稿且可继续编辑。
- 正常刷新时，未编辑的草稿同步服务器新值；已编辑的草稿继续保留，若服务器赛程变化则展示新生效值并要求确认，避免借新 revision 无提示覆盖其他管理员修改。
- 保存期间以同步 ref 防止重复写，禁用编辑和关闭；放弃修改需确认。初始读取、不可用与权限失败均有对应行为；通知沿用 Noti，401/403 沿用 AdminApp 权限边界。
- 马战停用确认明确“禁止新下注；已有投注的轮次仍会继续结算并发放奖励”。签到原有设置和启停提示由回归保护。

## Checks

全部命令在 `Web/gm-admin` 串行执行；测试结束后才编辑或启动下一个命令，没有文件级并行测试。

| 最终命令 | 结果 |
|---|---|
| `npm test -- --no-file-parallelism src/events/JoustSettings.test.tsx src/Events.test.tsx` | 2 文件、48/48 通过；最新最终源码运行耗时 15.83 秒。 |
| `npm test -- --no-file-parallelism` | 10 文件、137/137 通过；耗时 57.22 秒。 |
| `npm run build` | `tsc --noEmit` 与 Vite 构建通过；1515 modules，构建 4.09 秒。Vite 提示主包超过 500 kB，未阻断构建。 |
| `git diff --check -- Web/gm-admin` | 通过。 |

新弹窗 21 项交互用例覆盖完整请求、服务端返回版本、合法性、PVF 名称与 ID、loading/不可用、重复写、四类不确定结果、重读失败、不确定提交意图、普通 400、两类正常刷新并发修改、关闭和权限回调。Events 回归额外覆盖马战分派及停用结算提示。

首次 sandbox 测试在 Vite 配置加载处遇到 `spawn EPERM`，改用已授权的 `require_escalated` 本机隔离执行。首轮新增测试有一处姓名查询未匹配包含 ID 括号的文本，修正查询后最终检查全部通过；不作为生产行为缺陷。上表只列最终源码的运行结果。

## Remaining / Handoff

RTL 加完整 UI 回归及 build 已充分验证本阶段，不额外启动浏览器或 HTTP 服务。未调用 .NET，未操作 live 18081 或真实数据库；构建输出仅位于隔离工作区的忽略目录 `Web/gm-admin/dist`。

本阶段没有服务进程；全部 npm 测试和构建 session 已以 exit 0 结束，核对无本工作区遗留 Node 进程。T-004 前端实现与本阶段检查完成，交回唯一生产写入权。主控继续 T-003 原生接线、综合审查与交付；原生客户端实机验收仍待后续阶段，不声明 ClientVerified。
