---
kind: rks-verification
decision: pass
---

# 根任务最终验证

验证对象：根 TaskContract revision 1，accepted baseline `0476c922`，分支 `every-frame-draws-the-current-character`。
独立 `root-review.md` 已 approved；其后核对产品与 accepted commit 无差异，工作区仅有完成游标与根证据。

新鲜根验证：在无 Dfo.Server.Host 进程、加载实际 PVF 的环境下执行
`dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`，退出 0，原始输出 `root-full-suite.txt`。
Pvf 60 + Networking 52 + Protocol 631 + Persistence 333 + Infrastructure 259 + Application 1076 + Host 6 = **2417 通过**，Infrastructure **2 跳过**，**0 失败**。
全部七个程序集有运行和成功统计，全文无 [FAIL]、error CS 或 MSB数字标记。日志中预期 Host 启动失败来自 NotificationRegistrationTests 的拒绝断言，InvalidOperationException 栈根 NotificationHandlerRegistry，已由根 review 核对，非失败判据。

| 条件 | 最终证据 |
|---|---|
| SC-1, SC-2 | spec-001 的 coverage-record 和独立 review 保留具体 decoder 变异/覆盖来历；六个当前直接 decoder 判据本次通过，后续未改 decoder 行为。旧 scratchpad 原始变异日志不在当前工作树，本次未冒称重跑。 |
| SC-3 | spec-002 的同源路径和 M-A/M-B 独立历史验证；当前 CharacterRosterService → DrawnAppearance 同时服务选人及入场名单，生产路径判据再次通过。 |
| SC-4 | 字节锚及62项哈希断言未改，本次全部通过；入场名单属于 SupplementalSynchronizationMessage，原先“该帧在锚里”的假设已更正，没有移动的锚帧无需新值。 |
| SC-5 | 旧列仍有存储搬运、无名单解码消费；保留列/decoder及其判据作为未迁移单元的决定在 spec-002 D-1/D-2。迁移存档不纳入本批。 |
| SC-6 | 四条移动分派路径检查两端，正反复述与新拖动判据通过；byte-identical replay 前提与无法区分重新构包的发现保留，未声称实机普遍修复。 |
| SC-7 | spec-002/003/004 的双向行为表与根 review 逐项核对，包含拒绝变化、读取变化、代次失效外观抑制、整体覆盖风险。 |
| SC-8 | spec-004 reader-enumeration：16读点/12文件、驻留与入场边界及 PvP 其他玩家读取均有核查。 |
| SC-9 | 穿上→真实副本选择帧→脱下→再次选择帧的当前判据通过，P2删除写回使帧值断言失败；方案比较依据实际两位消费者。 |
| SC-10 | AGENTS七写点、名单/副本注释和协议历史更正等记录均由根 review 核对；未重写封存历史成果。 |
| SC-11 | 此 accepted result 的完整 root-full-suite 原始输出及七程序集统计保留。 |

约束最终均 aligned：Git仍为原任务 worktree，四成果按授权次序接受；main addbeb52 未变；未扩到#17/#18、CRAP、客户端或参照目录。无新增帧布局/注册、架构断言放宽或旧六个快照写点行为改动；CS9113/TreatWarningsAsErrors 构建通过。期望变更有授权对应记录。二进制I/O保存源文件/备份，变异逐次SHA-256复原。产品及Markdown空白检查通过；运行器原始 stdout 的CRLF原样保存，不作为代码空白缺陷重写。

根游标核对：四个唯一attempt均sealed/completed，active_attempt=null，phase=root_verification/in_progress；SC1..11有最终证据，准备原子封存。

批准的限制仍然存在：同代次快照整体覆盖、读取失败后的旧副本；未实机确认重复发送原字节/PvP画面，特定实例字段解释可能令新拖动误拒。旧三项原始变异日志未随当前工作树保留，历史依据是封存独立评审与具体结果，本次原始日志真实在场。上述限制与根review的接受边界一致。
