# 活动管理前端实施结果

工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`，分支 `task/warehouse-storage`，基线 `af0715492d9b9850756afdd908f8a93614795ea6`。仅修改 Web 前端与本次说明/验收文档；未提交，未修改后端、契约或 YAML，未操作真实在线服。

## 已交付

- `EventsView.tsx` 消费实际 `ManagedEventList` 与 `EventChange`，`AdminApp` 新增 All 权限“活动管理”导航 `/events`（外部入口 `/gm/events`），审计动作 `ChangeEvent` 显示中文名称。
- 当前注册活动列表含标题、类型、季、版本、状态、北京时间期限、PVF 来源及两类奖励概要；展开查看全部奖励。规则不可用保留原因，未配置期限禁止启用。
- 配置独立 Modal 仅编辑全部展示字段及有效期，字段长度、非空标题/短名、NUL、时间范围及顺序验证沿用后端约束。未修改期限保留原秒/毫秒，避免只改展示字段误改进度期限。
- 启停独立确认，使用原 revision；新期次经独立编辑及再次确认，明确旧记录保留、新进度从头开始和默认停用。
- `App.useApp().modal`、统一 `NotiBar`、`useGmRead`、dirty取消确认和 ref提交防重；401/403回交AdminApp。409/5xx/网络故障或明确unknown结果锁定写入，重读失败仍锁定，仅成功重读后解锁。
- 更新[使用说明](../../WeeklyAttendanceEvents.md)和[人工清单](Human-Test-List.md)；bonus/补签明确不在首例范围。

## 验证

| 检查 | 结果 / 日志 |
|---|---|
| 新活动交互21项 + DailyRefill/Initialization/AdminApp相关回归 | **44/44通过**；`npm test -- src/Events.test.tsx src/DailyRefill.test.tsx src/Initialization.test.tsx src/AdminApp.test.tsx`；[ui-tests.log](evidence/ui-tests.log) |
| TypeScript与Vite生产构建 | **退出0**；`npm run build`；[ui-build.log](evidence/ui-build.log) |

初次测试沙箱因Vite子进程 `spawn EPERM` 未启动（[日志](evidence/ui-tests-sandbox.log)），按已授权验证范围提升执行。首轮测试中AntD确认框同一标题出现两处，测试改为明确确认标题选择器后通过（[首轮日志](evidence/ui-tests-first.log)）。构建仅有现有单包体积提示，未据此增加无关重构。

根控制器Chrome观察到原生日期自动化填写未进入控件值，补充“从空期限填写两端并保存”测试通过，未据未证实的自动化差异修改生产代码；真实Chrome结果由根控制器记录。

[Chrome隔离记录](evidence/chrome-verification.md)已证明GM-01至GM-04的列表、配置、启停、新期次及dirty取消，清单据此标记Chrome通过。GM-05未做浏览器复核，A21游戏客户端各项仍待验。

独立审查F-2指出使用说明与A21-03误写普通目标22次；现依据实际PVF、HTTP与Chrome证据修正为当前归档4次，并要求验收以当次后台/PVF实际目标为准。此次只修正文档，未改代码或重跑npm验证。

构建产物：`Web/gm-admin/dist/index.html` 与 `assets/index-DypoZnQ2.js`、`index-CAOtslb0.css`。未由本子任务打开浏览器或部署产物；Chrome 隔离环境验证由根控制器进行。A21 客户端GUI项目仍为待验，自动交互测试不提升其证据等级。
