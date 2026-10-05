---
kind: rks-review
verdict: approved
---

# 活动管理与累计周签到完整结果审查

审查日期：2026-10-05。本次为独立于生产者的混合结果审查，适用 `reviewing-work/SKILL.md`、`references/code.md` 与 `references/deliverable.md`。依据当前工作区 AGENTS.md 及本任务 TaskContract revision 1、spec.md、plan.md；无须挑战集的额外门。

最终判定为 **approved**，两项缺陷均已独立复核修正。下文保留首轮冻结目标、证据与 `changes_required` 历史结论；当前目标和复核结果见末节。

## 目标与范围

目标为 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`、分支 `task/warehouse-storage`，HEAD/接受基线均为 `af0715492d9b9850756afdd908f8a93614795ea6` 的冻结未提交结果。已读取完整 `git diff`、`git ls-files --others --exclude-standard` 中新增生产代码、测试、说明、契约及证据，追踪现有结算、邮件、事务、通知和权限实现，而非只读生产报告。工作区不存在 `.codegraph/`。

`Server/`、`Web/`、`protocol/` 的 33 个已修改/新增路径内容指纹为 `2881134A20EA3360535CAAA8C3754F10895B28F353C24890004A70FFECAEC33D`。算法：Git 路径排序去重，每行 `UPPERCASE_SHA256  repo/path`，LF 连接并追加尾 LF，对 UTF-8 文本计算 SHA-256。此指纹不含任务文档与根控制器并行增加的只读取证/Chrome记录。

只写本报告，未改生产、契约或 YAML，未构建/运行 .NET 或 npm，未操作运行服、客户端或 Git 提交。已有日志被直接检查，不宣称本审查重新运行了这些测试。

## 必须修正

### F-1 [P2] EVENT_INFO 中文文案使用了错误字符集

位置：`Server/src/Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs:20`，影响开始/结束公告、标题、短名和说明。`EventPayloadCodec.Information` 直接用 `Encoding.UTF8.GetBytes` 编码所有 dstring。

同一 A21 镜像的新鲜原始证据 `evidence/calendar-text-encoding.txt:92`、`:166`、`:228` 和 `:283` 证明：日历读取器调用 VA0274FCB0，经 VA027BAE40 将全局03B89610作为 `MultiByteToWideChar` 的 CodePage；初始化 VA00426BE0 写入936。默认路径未发现65001，另一个临时949路径会恢复原值。这是 ClientStatic 默认路径证据，不是运行时抓取值。当前 UTF-8 中文字节按936读取会乱码，直接影响契约要求的真实日历展示；ASCII linkKey 不会暴露此错误。现有日历测试也用 UTF-8 解读写出的字节，因而未保护实际消费者。

最小修正：仅在本次006C日历边界按已证CP936编码文案，长度继续按实际编码字节计算；对不能表示的输入明确处理，避免保存后才无声替换或丢失文案。将日历向量验证改为覆盖中文的CP936实际字节/消费者解码，并同步协议证据说明。不要据此扩改所有既有协议或客户端。

### F-2 [P2] 使用说明与人工验收清单仍按22次目标判断

位置：`Document/WeeklyAttendanceEvents.md:22` 与 `Document/tasks/event-weekly-attendance/Human-Test-List.md:21`。前者写每周目标22次，后者要求在真实22次达标前后观察按钮。

实际客户端脚本 `evidence/pvf-total-attendance.txt` 的 `[attendance condition]` 为 `4 44`；当前内容源以第一个数作为普通签到目标。`GmEventsHttpTests.Actual_A21_archive_uses_client_script_reward_order_and_delivers_declared_first_reward` 明确断言4，Chrome证据与截图也显示每周4次。22会使验收者错误地把第4次已经解锁的按钮判断为提前解锁，并额外执行18次无必要副本。

最小修正：将这两处统一为当前归档的4次普通目标，或要求按当次后台/PVF的实际目标观察达标前后；44仍为未开放bonus目标，不修改客户端/PVF或业务条件。

## 已核对的其他关键边界

| 边界 | 实际检查结果 |
|---|---|
| 同实例与管理来源 | ApplicationModule 两接口解析同一 WeeklyAttendanceDomainService；manager汇总注册实例并拒绝重复ID。日历和玩法共同读取配置、期限和规则可用性；后台始终列出注册实例。 |
| 期次与日期 | 配置/启停保存revision与season；启停不换季，新季显式生成ID且停用。已有进度拒绝改期限；UTC+8业务日回溯周一，开始包含、结束排除。 |
| 真实通关与去重 | SetPlayResultHandler先验证完成并claim runtime settlement；GrantClearExperienceCommand采用账号独占门，在经验结算事务内传RunId及结算前等级。账号/活动/季/Run持久唯一消费，非推荐及关闭/区间外不计数。 |
| 资产原子与重启 | 周领取条件写、每周/累计奖励系统邮件共享同一SQLite ambient事务。领取唯一索引、账号独占门和写锁覆盖重复/并发；测试实际观察邮箱、故障回滚、重启和新季隔离。累计4/8/11奖从PVF表按次数匹配，不推算连续物品ID。 |
| 协议与刷新 | 新鲜calendar-client-disassembly.txt的主表/detail读取顺序与encoder相符；042D为450B，0441严格14B opaque、结果4B。入场、成功通关、签到及GM修改均有刷新；关闭为清空日历与450B零状态。未知请求字段未授予进度，未冒称ClientVerified。F-1的字符集例外必须修复。 |
| PVF与管理边界 | 实际客户端加载路径为event/chn_event/chn_totalattendance.evt；规则核对可投递物品，缺失/变化不fallback。本季保存规则声明。HTTP沿用All、CSRF、严格JSON、revision及配置/成功审计同事务；提交后刷新使用独立于HTTP取消的token。 |
| Web恢复 | 页面使用统一NotiBar；409、5xx、网络/unknown锁写，重读失败不解锁，成功重读使用新版本。保存成功重新GET；busy/ref保护重复提交，权限失效回交AdminApp。Chrome记录及两张截图支持实际编辑、启停、新期、重载及dirty取消。 |

已逐项核对最终日志：Host新增7/7、相关Host19/19、协议175/175、架构16/16、Persistence13/13，均无跳过；Release构建0警告0错误；前端44/44和生产构建通过。初次无PVF回归失败、UI沙箱EPERM及选择器失败保留为历史证据，不冒称最终失败已凭空消失。各实际测试源码与这些结果的保护范围一致。

## 保留的验收边界

A21真实GUI日历点击、窗口/按钮、邮件附件领取与重登仍未验收，是本任务明确保留的人工工作，不因静态或隔离测试升级为ClientVerified。bonus及14B内部选择语义仍在首例范围外。

原始006C tail reader还包含manager中event9存在时的前置 `u8 count + count×u16` 条件分支，随后才读extra表。其manager赋值时序未完全闭合；本次encoder是2208首例投影，协议说明应避免把单个尾count描述为所有活动组合的完整通用布局。这是范围/置信度说明，不要求本次实现其他活动或通用平台。

判定：**changes_required**。须修正F-1、F-2后对变动及直接回归进行独立复核；本报告不授权部署，不代替控制器的完成验证。

## 修正复核与最终判定

复核日期：2026-10-05。工作区、分支与基线不变，生产再次冻结。仅复核F-1、F-2修正及其直接回归，没有重新扩大研究或新增要求。当前 `Server/`、`Web/`、`protocol/` 的33个变更路径内容指纹为 `315F1F619F1CDAE19DA6CF16402AB3C9811ECA1E58B4E3DCC59561B8910A54B2`，采用首轮同一算法；`git diff --check`通过。

| 项目 | 当前源码与证据 | 结论 |
|---|---|---|
| F-1 编码与保存边界 | `WeeklyAttendanceSlice.cs:18`使用CodePagesEncodingProvider的936与异常fallback，dstring长度取实际编码字节数。`WeeklyAttendanceRuleService.cs:14`按同字符集校验；`WeeklyAttendanceDomainService.cs:48`在configure/new-season路径构造配置前校验五项文案，随后才保存与成功审计。其他协议未改字符集。 | 已修正 |
| F-1 独立回归 | 新协议测试以字面GBK向量D6 D0 CE C4保护“中文”，完整日历测试按A21默认936读取全部字段；HTTP测试分别提交五项不支持的emoji，要求400、完整配置投影不变且无新增成功审计。 | 保护实际缺陷与保存原子边界 |
| F-2 文档一致性 | `WeeklyAttendanceEvents.md:22`与`Human-Test-List.md:21`改为当前归档4次，并要求按当次后台/PVF目标验收；`implementation-ui.md`同步修正记录。GM-01至04依据真实Chrome记录标通过，GM-05浏览器及全部A21项继续待验，引言与正文一致。 | 已修正 |
| 006C范围说明 | `protocol/game/server-to-client/NOTI-0x006C.md:118`明确event9条件前置列表及当前首例未覆盖该组合；`:120`明确CP936默认路径的ClientStatic/High证据，未宣称运行时恒定值或全部活动通用布局。 | 与证据及当前实现一致 |

直接读取了 `evidence/backend-f1.md` 和五份实际RED/GREEN日志。协议RED确实显示已知GBK字节与UTF-8实际输出不一致，HTTP RED确实显示预期400而实际200。首次HTTP GREEN的19通过/1失败属于DTO数组引用相等断言问题；当前测试改为完整序列化投影比较，保留全部业务断言，最终相关Host **20/20**、协议 **176/176**，均无跳过。不是只依据生产摘要认定通过。

直接读取 `evidence/final-release-build.log`，新鲜Release构建实际包含GmWeb与Host输出，完成为 **0警告、0错误**；`final-architecture.log`中的修正后架构/事务串行回归 **16/16**，无跳过。`final-release-build-sandbox.log`只留中途输出，没有成功/失败结尾；控制器说明该次沙箱构建停滞后安全结束，本复核不把它计为成功构建或产品测试失败。F-2仅文档改动，既有前端代码验证范围未变，本轮未要求重复npm构建。

最终判定：**approved**。当前审查范围无剩余必须修正项。真实A21 GUI、GM-05浏览器待验及bonus范围外边界保持不变，不升级为ClientVerified；批准本冻结结果不代替控制器完成验证或授权部署。

本轮仍只写本审查文档，没有构建、运行服务或修改生产目标。审查写入已结束，交还控制器写入权。

### 暂存末尾空白复核

最终暂存复核：33个产品路径不变，`GmEventsHttpTests.cs`仅删除末尾多余的CRLF空行，文件现在以两个右花括号及各自LF结束。在内存中向当前该文件补回CRLF后重算全部33路径指纹，结果恰为上一轮 `315F1F619F1CDAE19DA6CF16402AB3C9811ECA1E58B4E3DCC59561B8910A54B2`，证明其余产品字节及所有测试语句均未变。当前最终内容指纹更新为 **`321D8CBB2734F886B08503560573FB7B3C6D03A5695A126E60AE39BD34A1BDB7`**；HEAD仍为上述基线，内容已暂存而未提交。

以 `core.whitespace=cr-at-eol` 并排除原始 `evidence/**` 复核产品及说明的暂存空白检查退出0；原始反汇编证据保留本身格式，不据其尾空格否定产品检查。复用已批准的全部行为证据，不重跑测试，最终 **approved** 不变。只更新本报告，立即交还控制器写入权。
