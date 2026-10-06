---
kind: rks-verification
decision: pass
---

# 活动管理与累计周签到开发交付验证

日期：2026-10-05。判断范围为TaskContract revision 1要求的服务端、Web及可供A21人工验收的实现交付，不包含用户实际游戏客户端验收。工作区为`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`，分支`task/warehouse-storage`，接受前HEAD为`af0715492d9b9850756afdd908f8a93614795ea6`。

独立[最终审查](../reviews/change-review.md)为approved；33个Server/Web/protocol改动路径最终指纹`321D8CBB2734F886B08503560573FB7B3C6D03A5695A126E60AE39BD34A1BDB7`与控制器新鲜计算一致。仅在暂存检查清理了新测试文件末尾多余空行，末尾说明及独立格式复核记录保留；最终状态文件与结果文档不改变该产品目标。

## 契约条件

| 条件 | 决定性证据及结果 |
|---|---|
| SC-1 注册及双接口 | 实际DI解析同一周签到对象，manager从注册实例获取列表/日历；管理始终可列，未生效不投影。真实Host集成及最终审查支持该边界。 |
| SC-2 A21消息及操作路径 | 同镜像日历reader、代码页、450B状态、14B请求与4B响应静态证据已落盘；中文独立GBK向量及decoder→dispatcher→命令→邮件/响应/刷新验证通过。关闭时空006C和零状态经真实在线通知路径验证。协议/选角/清关回归176/176。 |
| SC-3 推荐通关与资格 | 使用真实GrantClearExperienceCommand接入既有奖励事务，Run去重和结算前等级；非推荐/无效时段不推进，未达标拒绝。Host检查真实记录、边界及原副本流程，协议相关回归通过。 |
| SC-4 资产与持久化 | 真实SQLite、8个并发请求单邮件、同Run去重、邮箱失败回滚可重试、跨业务周/累计阶段、重建容器及启停、新季隔离均通过；当前A21归档首周奖励490003187实际投递验证通过。 |
| SC-5 管理页面 | All/CSRF/strict/revision/原子审计、不支持代码页字符拒绝且配置/审计不变，最终相关Host20/20；前端21项及相关回归共44/44。Chrome独立数据库实际配置、启停、新期次、重载、dirty取消通过，GM-01..04有记录。 |
| SC-6 可复核及回归 | 当前编码修正后176协议+20Host+16架构通过；存储13项通过且随后未修改存储源码。TypeScript/Vite通过；最终Host Release含新dist构建0警告0错误。说明、人工清单、协议人工区与目录一致，git diff --check通过。 |

## 实际检查及原始结果

所有.NET检查串行，`DOTNET_PROCESSOR_COUNT=2`、`-m:1 -nr:false --no-restore`。最终受影响的协议/Host检查在F-1修正后运行；前端源码此后没有变动。存储schema/事务检查之后仅有编码边界与文档修正，不影响已验证存储目标。

| 检查 | 最新证据 |
|---|---|
| 协议WeeklyAttendance/DungeonClear/SelectCharacter，Release | [176/176、退出0](../evidence/backend-f1-protocol-green.log) |
| GM活动/每日补给/初始化HTTP，真实PVF，Release | [20/20、退出0](../evidence/backend-f1-http-green.log) |
| 处理器架构/事务串行化，最终Release | [16/16、退出0](../evidence/final-architecture.log) |
| schema/审计/跨仓储事务 | [13/13、退出0](../evidence/backend-persistence-regression.log) |
| Events/DailyRefill/Initialization/AdminApp前端 | [44/44、退出0](../evidence/ui-tests.log) |
| TypeScript及Vite构建 | [退出0](../evidence/ui-build.log)；现有大包提示保留，不据此扩改打包架构 |
| 最终Host Release构建 | [退出0、0警告0错误](../evidence/final-release-build.log) |
| 本机Chrome | [实际观察与截图](../evidence/chrome-verification.md) |

F-1两条真实RED先复现错误编码和不可编码输入获200，修正后GREEN：[完整修正证据](../evidence/backend-f1.md)。初次缺PVF、测试选择器/数组引用比较失败和沙箱子进程限制均已保留原始记录并对应后续通过结果，未把失败日志冒充通过。本控制器最终沙箱构建停滞只结束经核实的本任务构建进程12068，提升后同一构建退出0；[停滞输出](../evidence/final-release-build-sandbox.log)保留，不称为产品测试失败或通过。

## 验收边界与运行状态

普通每周签到及累计4/8/11奖励是首例范围；bonus/补签、0441内部选择字段和event9条件尾表不声称支持。CP936结论属于同镜像ClientStatic默认路径，未冒充运行时抓包。

[人工清单](../Human-Test-List.md)的全部A21项目、GM-05跨会话冲突/部分权限浏览器复核仍待验；后者HTTP/前端自动验证已覆盖。契约明确将真实GUI验收独立列出，因此上述待验不改变本次开发交付pass，也不建立ClientVerified。

隔离Chrome测试实例23360和新建标签页已关闭；测试数据库/日志留在ignored artifact路径。在线实例35408仍位于原`artifacts/gm-daily-refill-20261005-c0e23fd1/runtime`，未部署活动版本或改玩家数据库。没有push、PR、merge或工作区清理。

本地接受只更新任务分支，接受前main指针为`6ee658cdf14208d819360c40b3b47c4717ae3499`；提交后的HEAD/干净状态及main不变由控制器读取Git证明，结果提交ID在交付回复中记录，避免自引用提交。

最终暂存检查补充：普通未暂存`git diff --check`不检查尚未跟踪的新文件。暂存后默认检查将Windows CRLF和反汇编输出末尾空格识别为告警；没有为了格式改写原始证据。采用显式`core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol`检查完整暂存产品/说明，排除保留原始格式的`evidence/**`，退出0。唯一真实格式问题为新Host测试文件末尾多余空行，已删除；不改变测试内容或行为，并请独立审查更新目标指纹。
