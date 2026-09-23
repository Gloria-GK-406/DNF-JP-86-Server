# A21 任务完成券 CMD 0x0204 客户端取证

2026-09-23，只读静态核对与现有实帧。结论限于 A21 `DNF.exe` SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；使用 `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，其 manifest 记载镜像基址 `0x00400000`、无 hole。下述地址均为 **RVA**，首选 VA = RVA + `0x00400000`；本次没有启动、attach 或改动客户端。`protocol/game/client-to-server/0x0204.md` 机器区地址经当前 dump 字节重新核对，不直接将旧地址当运行时 VA。

## 本次请求：19 字节

服务端现有日志 `artifacts/quest-clear-pvf-20260923/server-final.stdout.log:288`：CMD `0x0204`、seq 29、`450000000000004D0000000000000000000000`，当时 Unhandled。背包 slot 69 的物品 10006007；PVF `stackable/event/chn_event_20150813_sheepfarm/chn_questclear_10006007.stk` 明列 `[action type] [quest clear]`、`[use action packet] 0`。样本本身不包含物品 id，须在服务端由槽位查真源。

| body 偏移 | 长度 | 样本 | 当前客户端构包来源 | 判定 |
|---:|---:|---|---|---|
| 0 | u16 | `45 00` = 69 | RVA `0x01203C8F` 读请求对象 `+0x24`，`0x01203C9B` WriteU16 | 槽位；Confirmed（字节 + 构包） |
| 2 | u8 | `00` | RVA `0x01203CA0` 读 `+0x28`，`0x01203CAC` WriteU8 | 背包容器选择码 0；High（收包端 `0x01248AC0` 用该码选择容器，0 取默认容器） |
| 3 | u32 | `00 00 00 00` | RVA `0x01203CB1` 写常量 0 | 保留值；Confirmed |
| 7 | u32 | `4D 00 00 00` = 77 | RVA `0x01203CBF` 零扩展请求对象 byte `+0x29`，`0x01203CCB` WriteU32 | **物品动作码**；High。请求对象由 RVA `0x01205F7D` 读取物品对象 byte `+0x528`，`0x01205FA3` 存到 `+0x29`；与此物品的 PVF `quest clear` 和实帧 77 一致。不是固定的任务号、数量或物品 id |
| 11 | u32 | `00 00 00 00` | RVA `0x01203CD0` WriteU32 常量 0 | 保留值；Confirmed |
| 15 | u32 | `00 00 00 00` | RVA `0x01203CDE` WriteU32 常量 0 | 保留值；Confirmed |

本样本对应 RVA `0x01203C70`（VA `0x01603C70`）的 0204 构包路径，累计恰好 19B。该函数是物品请求对象的虚方法：RVA `0x02EC7A08` / `0x02EC7A44` 均指向它，实例化路径 RVA `0x01205F66` / `0x012069C0` 分别写入相应虚表。通用 CMD 0204 的另外 16 个构包点形状或来源不同，不能把本表扩展为所有 0204 请求。`0x4D` 的枚举语义由本券 PVF、物品对象来源及一次实帧交叉支持；还没有第二种 `quest clear` 物品的实测差分。

## ACK：客户端实际读取范围

CMD 0204 在 RVA `0x02B60260`（VA `0x02F60260`）注册客户端响应 handler RVA `0x012041C0`（VA `0x016041C0`）。和同镜像 CMD 0021 handler RVA `0x00D200A0` 对照，`[ebp+0x0C]` 是公共 CMD 结果位，`[ebp+0x10]` 是公共错误码；两者由公共层传参，0204 handler 不重复读这两个字节。0021 的现有已知 ACK 为 `01 + payload` / `00 + reason`，因此以下 0204 **候选完整 body** 含公共前缀；无此券 S2C 实帧，尚不能标记 ClientVerified。

| 分支 | 公共前缀 | 0204 handler 后续直读 | 最小完整体 | 用途 |
|---|---|---|---|---|
| 成功 | `01` | RVA `0x012041FF` ReadU16，`0x0120420D` ReadU8，`0x01204219` ReadU32 | 8B：`01 | u16 slot | u8 container | u32 action` | u8 调 RVA `0x01248AC0` 选择容器，u16 调 RVA `0x012EE6C0` 查该容器槽位。u32 仅在 RVA `0x012042A5` 作为 RVA `0x011FC3D0` 的首参参与动作条件判断；本券候选回显 77，无 S2C 实帧证明服务端原值 |
| 失败 | `00 | u8 reason` | RVA `0x01204876` ReadU16，`0x01204884` ReadU8，`0x01204892` ReadU8 | 6B：`00 | u8 reason | u16 slot | u8 container | u8 detail` | u8 container 同样选择容器；u16 查槽位；末 u8 在 RVA `0x01204917` 等多处分派提示/动作，公共 reason 在 RVA `0x012048F7` 跳表分派。两码取值表未闭合 |

读原语是同一 A21 镜像核过的 `ReadU16=0x023C52A0`、`ReadU8=0x023C5230`、`ReadU32=0x023C5310`。在 `0x012041C0..0x01204CAB` 的 handler 内只找到上表六次直接读；成功分支后没有第四个 u32 读取。参照服 `ServerS4A21/.../UseStackableAckBuilder.cs` 的 `BuildSuccess` 额外附带 `u32 itemCode`，它可能只是被此客户端忽略的尾部，**不得据此要求或依赖 12B 成功体**；其失败构造也与上表客户端失败读取量不吻合。此结论只针对客户端直接消费字节，公共层的精确截断处理及附加尾字节容忍性未用真实 ACK 验证。

成功 handler 的可见本地行为：按 ACK 容器/槽位找物品；从物品对象提取 id 并在容器中计数（RVA `0x012EF410`）。第三个 u32 唯一直接用途是传给 RVA `0x011FC3D0`：该函数先做 `value - 0x51` 并检验 `<=0x32`；**若 ACK 回显本券动作码 77 (`0x4D`)，必返回 false**。handler RVA `0x012042AD` 因 false 跳过 `0x012F1300(slot)` 与物品虚方法 `+0x50` 的局部处理，故这个 ACK 候选值本身不会沿该路径本地移除券。局部消耗数量由所查物品模板 id 在容器中的数量查询驱动，不由 0204 请求中的保留 u32 提供；对 77 此数量不触发局部消耗。此后按物品 id 查询定义，并按定义 `+0x398` 分派 UI 动作；当前镜像跳表 RVA `0x01204CF0` / `0x01204CAC` 查值 `0x4D` 落 RVA `0x012046CE` 默认路由，随后仅比较 `0x83` 与 `0x8D` 并退出。因此**若**此券定义 `+0x398=0x4D`，ACK 不显示成功提示或刷新任务；PVF 的 `[quest clear]` 和请求对象 `+0x528=77` 支持这个假设，但尚未独立运行时证实定义字段同值。静态路径**未见该 ACK 自身读取任务 id、任务列表或 30000B 完成位图**；它无法代替任务状态通知。失败 handler 走错误码/详情提示和部分槽位刷新，不进入上述成功分支。失败公共 reason=2 在 RVA `0x012048F7` 经减 3、上界比较后越界返回，不进入文字提示跳表，故可作为静默拒绝候选；仍缺真实失败帧验证。消耗一券的服务端持久化事实须由服务端事务处理，客户端槽位以权威库存投影校准。

## 完成状态及通知顺序

当前服务端有 `QuestVisibilityMessage`（NOTI `0x0164`，`u32 30000 + 30000B flags`）、`AcceptedQuestProgressMessage`（NOTI `0x023F`，进行中任务）、`AvailableQuestListMessage`（NOTI `0x0015`）。前者目前只在角色入场同步中使用，但 **A21 在线 handler 确实处理它**：RVA `0x00D641F0` 进入，`0x00D64223` 读取到本地 30000B 缓冲，`0x00D64239` 调 `0x015A59C0` 将 30000B 复制到全局任务 flag 对象，随后 `0x00D64240..0x00D64307` 遍历 30000 个索引并调用任务状态/UI 更新。`0x015A59C0` 的 `memcpy(0x7530)` 在当前镜像可直读；因此在线全量重投影本身为 **High / A21 static**。读取 helper `0x0234FBE0` 的入口仍跳入混淆区，目录页中 30004B 的完整线布局追踪来自 **A12**，未有本次 A21 独立线确认。`0x023F` A21 静态直读 `4+6n`；`0x0015` 有 A12 7B 样本。这些是可复用的编码/处理候选，不是此次必要通知集合的已证顺序。

本次 0204 被服务端 Unhandled，客户端没有收到成功或失败 ACK，也没有成功场景的 S2C 逐帧记录。因此 **无法从现有证据断言** ACK 前后必须按 `0x0164 → 0x023F → 0x0015`，或任何其他顺序发送；也无法证明只发 ACK 就能即时刷新任务窗口。当前服务端候选顺序为 ACK、权威库存清槽、`0x0164`、`0x023F`、`0x0015`；完成位图和进行中列表从事务提交后重新读取的 cleared/carried 生成，可接列表独立刷新。要用真实 A21 成功样本验证 0x0164 完成位图、已接列表和可接列表各自效果与顺序。这里明确保留该缺口，避免将参照服的通知顺序升格为客户端事实。

## 证据与未完成项

- 一手：本仓 `artifacts/quest-clear-pvf-20260923/server-final.stdout.log:288` 实帧；A21 `Script.pvf` 对 10006007 的原文；上述 SHA 镜像与 `dnf-dump-20260922-194214-883-23144` 的 RVA 反汇编。
- 对照：`protocol/game/client-to-server/0x0204.md` 机器区、`protocol/patterns/data/probes/a21-census-anchors.md` 读写原语、`protocol/game/server-to-client/NOTI-0x0164.md` 等目录页。本调查不把 `ServerS4A21` 参考实现当客户端权威。
- 尚缺：0204 成功/失败 S2C 真实 body 与公共层长度行为；成功第三个 u32 的真实服务端取值及本券定义 `+0x398` 的运行时值；本券成功时任务投影通知的必需集合及顺序。这些不能靠当前 Unhandled 会话补证。
