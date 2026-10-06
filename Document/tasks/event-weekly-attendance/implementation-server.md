# 周签到后端实施结果

状态修正（2026-10-05）：本文CP936选择是历史实现，已被实际客户端代码页65001的运行时证据推翻；当前代码统一UTF-8，详见[006C记录](../../../protocol/game/server-to-client/NOTI-0x006C.md)。下文保留当时测试与判断，不作为当前编码依据。

基线 `af0715492d9b9850756afdd908f8a93614795ea6`，工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`。未提交 Git，未写 Web 源码，未启停真实运行服；测试使用临时数据库/端口。本报告覆盖 T-002/T-003 后端，整体任务的前端、独立审查和客户端 GUI 验收由控制器收口。

## 已交付

- `Data/Events` 和 `Abstractions/Events`：领域信息、管理/玩法/规则/持久化接口；`WeeklyAttendanceDomainService` 在 DI 中为单一对象，管理接口注册集合与玩法接口复用同实例。`EventManagementDomainService` 以注册 ID 汇总和路由，支持后续活动实例，不把2208硬编码进 manager。
- `Application/Events`：启停、有效期、稳定季、新期次和普通周签到；业务周复用 `IDailyResetRuleService` 的业务日，再回溯周一。当前格使用上游 `min(duration,max(1,count))`；下一次奖品使用 `count+1`。额外签到/补签未开放。
- `Infrastructure/Content/PvfWeeklyAttendanceDefinitionSource`：读取客户端实际脚本 `event/chn_event/chn_totalattendance.evt`，按表读取12组周奖励及4/8/11累计奖励，禁止连续ID推算或 fallback 物品。规则服务核对实际 placement，未知/缺失/不可投递内容使活动 unavailable，拒绝开启而不阻止 Host 启动。首次启用保存规则声明，本季归档规则变化时停用日历/玩法并显示原因，需恢复规则或显式新季。
- `Persistence/Events` 与唯一 schema：`event_configuration`、`event_attendance_week`、`event_consumed_runs`；账号/活动/季/Run 去重，周签到与系统奖励邮件加入同一 ambient SQLite 事务，审计与配置保存同事务。
- `GrantClearExperienceCommand`：兼容的 optional RunId/SettlementLevel/SettledAt，账号独占门；成功清关经验事务内推进活动，传递结算前等级。`SetPlayResultHandler` 只在持久提交和 runtime settlement 接受后发送活动刷新。普通推荐资格沿用等级±5和PVF等级范围重叠，未添加次元特殊例外。
- 选角准备结果新增独立 Events 快照；协议 factory 同步006C与042D，旧纯fixture保持空基线兼容。042D固定450B，0441严格14B opaque，ACK固定4B；所有资格依据服务端状态。签领奖响应和刷新经精确会话通知，领奖后邮件提醒复用既有 MailboxChanged。
- `/api/gm/v1/events` GET/POST：All 权限、CSRF、严格 JSON、revision guard、原子审计；提交后使用全部在线精确会话快照广播日历和个人状态，含副本中的在线角色。

## API 契约

GET/POST 返回 `ManagedEventList { entries: ManagedEventEntry[] }`。每项字段为 `eventId,type,revision,seasonId,enabled,status,title,shortName,description,startNotice,endNotice,startsAt,endsAt,sourcePath,available,unavailableReason,requiredClears,totalWeeks,weeklyRewards,cumulativeRewards,updatedAt,updatedBy`。两个奖励列表均 `{attendanceNo,itemId,count}`。时间为ISO日期或null；未配置初始有效期为null。

POST `EventChange` 必填 `eventId,expectedRevision,action`。action 为 `configure|enable|disable|new-season`；configure/new-season 还必填全部展示字段和有效期。enable/disable 不得携带这些配置字段。title trim后1..80，shortName 1..40，description 0..1000，startNotice/endNotice 0..200，拒绝NUL；有效期完整且 start<end，Unix秒0..uint.MaxValue。启停保留同季进度；存在进度的季拒绝改有效期；new-season 生成新 seasonId、默认 disabled，旧季数据和邮件保留。有效区间 `[startsAt,endsAt)`。

status 为 `active|disabled|scheduled|expired|unavailable`。规则不可用仍可以列出且明确原因，开启返回400。409版本冲突，其他错误沿用 `{message,outcome:'rejected|unknown'}`；客户端收到冲突/未知结果后重读。

## 检查

RED 与保存限制见 [backend-red.md](evidence/backend-red.md)。新测试直接观察真实SQLite记录、系统邮箱资产和编码帧；没有用 mock call count 代替奖励结果。

| 检查 | 结果/证据 |
|---|---|
| 新增 Host 集成 | 7/7，`evidence/backend-green.log`；重复Run、8并发领取单邮件、启停/重启、新季、故障回滚、周界、累计奖、真实A21奖励、清关命令与decoder→dispatcher、在线关闭清空 |
| 新增协议+既有选角/清关 | 175/175，`evidence/backend-protocol-regression.log` |
| 处理器架构与事务串行化 | 16/16，`evidence/backend-architecture.log` |
| Schema/审计/跨仓储事务 | 13/13，`evidence/backend-persistence-regression.log`；表计数58→61 |
| 相关GM回归 | 19/19，`evidence/backend-host-regression-pvf.log`；PVF输入为当前A21归档 |
| Release构建 | 退出0、0警告0错误，`evidence/backend-release-build.log` |

旧 GM 初始化测试首次因没有 `PVF_ARCHIVE_PATH` 出现两个500（`backend-host-regression.log`）；补齐归档后相关18项全部通过，不修改旧初始化行为。测试时钟跨周导致管理员session过期，测试按真实鉴权规则重新登录后通过。协议测试使用完整选角fixture修正后通过。

构建统一 `DOTNET_PROCESSOR_COUNT=2`、`-m:1 -nr:false --no-restore`，测试归档输入 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。Release输出 `Server/src/Dfo.Server.Host/bin/Release/net10.0/`。日志包含完整命令结果，待控制器独立审查和最终复核。

## 验收边界

客户端日历 detail flags0/5及 linkKey 候选遵循上游seed与A21静态消费，真实点击、窗口显示、按钮、奖励包裹领取、重登仍需人工验收。未知14B、bonus=44/bonusattendancecount1没有被解释成补签/额外签到协议，不声称支持该模式。已更新协议人工区与catalog，未覆盖自动生成机器区，未提升为ClientVerified。

## F-1 审查修正（2026-10-05）

仅006C日历文案改为严格CP936/GBK；长度按编码后的字节数计算。周签到规则服务新增文案可编码性校验，配置/新期次保存前对标题、短名、说明、开始/结束公告调用；无法表示的字符返回HTTP400，配置、revision和成功审计保持不变。Application没有引用Protocol，未扩改其他文本协议。

独立GBK汉字向量与同CP936消费者解码，以及真实HTTP拒绝测试，均先在原UTF8/无校验实现上观察RED再修正。证据、命令和直接回归见 [backend-f1.md](evidence/backend-f1.md)。006C记录补充了静态默认CodePage936证据和event9前置尾分支未覆盖的范围；本次仍是2208首例投影，不是全活动通用布局，也不是客户端GUI验收。

修正后协议/选角/清关直接回归176/176，Host活动/相关GM回归20/20，均零跳过、退出0；`git diff --check`通过。最终包含前端dist的Release构建交由根控制器执行，本次没有另跑重复构建。
