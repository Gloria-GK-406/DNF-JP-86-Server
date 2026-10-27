# 马戏团气球显示门取证

当前结论维护于 [协议流程](../../../protocol/patterns/flow-circus.md#原生气球-npc-的可见门)。
本次用户报告气球不可见；没有据此更改 PVF，也没有启用入门关或假定天幕继续请求。

## 材料与交叉检查

| 材料 | 用途与边界 |
|---|---|
| `evidence/balloon-pvf.md` | protocol2 的实际 PVF 摘录：地图放置、气球 NPC 角色与动画 |
| `evidence/circus-event-script.md` | `Event/CircusEvent.evt` 的 `[control panel] 1 4103 6`；不擅自补完三值含义 |
| `evidence/balloon-visibility-static.md` | 原生 NPC 对 20057 的专有门，及另一条 NPC 检查分支 |
| `evidence/balloon-event-switch.md`、`balloon-enable-source.md` | 原生事件对象构造/重置与状态 getter |
| `evidence/balloon-schedule-handler.md`、`balloon-switch-chain.md` | 033F 对 4103 的专用分支与 setter，四字段对应关系 |
| `artifacts/circus-probe/wire.jsonl` | 原探查会话只记录了 EVENT_INFO 和七条 033F 的元数据，正文未开启；不能冒充逐字段实帧证据 |

私有探查库中角色8为70级，最近位置 `town=2, area=2, x=214, y=171`；服务端日志也记录了
`2:0 -> 2:2`。这说明已进入气球所在区域，但不证明当时镜头覆盖 `(774,361)`。
源码投影只有六个其他地下城与一条任务状态，没有 4103，与元数据计数一致。

## 可重复的诊断差分

`probe_proxy.py --circus-schedule-on-select` 在每个游戏连接首次收到原服务端 NOTI006C 后，
发送一次明确标记为 injected 的 NOTI033F `(0,1,4103,0)`，并开启后续非登录/USERINFO 帧记录。
后端 Host、原客户端和 PVF 保持此差分前的内容，天幕 0282 不自动发送。

控制端 `POST /schedule {session,state}` 允许 0/1/2，用于打开/关闭同一显示门；要求先 arm。
替身测试已覆盖默认不注入、显式选角后注入、分片原包保留、开关帧、错误状态拒绝与来源标记。
替身不是游戏客户端。用户现已确认重新登录后的气球显示/点击，以及正式副本可选并已进入；
C2S0024实际目标town2/area64与服务端区域日志对应，详情和原始样本只维护于协议流程。
尚未验证关闭气球显示门的反向对照。其后用户已确认凌凌实体显示、原生结算首按钮继续及4114通关，
证据和边界见协议流程；场景对象本身的直接点击未单独测试。

2026-10-27 本次启用差分时代理无活跃连接，仅重启任务代理，PID更新为42876；Host仍为3560。
这些 PID 属于采样快照。用户仍使用原启动器和原客户端，原主干按用户指示保持关停，不自动恢复。
