# A21 客户端 `0x001A DISJOINT_ITEM` offset 5 语义调查（rev 3，反证驱动）

## 2026-09-20 实机补证

便携模式的 H1/H2 已由本地 A21 实机样本裁决为 **H1（Confirmed）**：普通、坚固、优质三档
单件分解分别上报工具槽 89、90、91，随后普通档批量分解对三个目标逐件上报并始终复用工具槽 89；
全部六帧的 offset 5 均为 `0`。因此 offset 5 在便携模式下不是工具 item id，便携工具身份只由
offset 3 槽位中的服务端物品决定。下文保留 rev 3 当时的静态不确定性，作为裁决前证据链记录。

- 日期：2026-09-19（rev 3；撤回 rev 2 的 `DisjointToolItemId` 结论）
- 目标问题（rev 3 重定义）：offset 5 是否是**提供分解服务的 NPC index**，或 NPC/便携工具共用的
  **tagged provider context**——而不是 rev 2 假设的"分解工具物品 ID"。
- 静态镜像：`DNFClient-A21-debug\DNF.unpacked-b1-debug.exe`
  （SHA-256 `DC322D8F…306E9`，基址 0x00400000；壳 `1F8244…B00DD`；两镜像 .text 固定 +0x400000）
- 证据分三类标注：**[PVF]**（本次用 `PvfLib` 直接读 A21 `Script.pvf` 亲证，
  复现入口 `Tool/PvfProbe`）、**[静态]**（递归下降反汇编 + 全模式交叉扫描，
  工件在 `Document/outputs/disjoint-provider/`）、**[动态]**（待实机，脚本见 §8）。
- 未修改任何服务端生产代码与客户端二进制。

---

## 0. 最终裁决（先答一句话）

**offset 5 最准确的现名是 `DisjointProviderId`（分解服务提供者 id，按窗口模式带标签的上下文值）：
在 NPC/勇者装备分解机模式下它是提供服务的 NPC 的 index（德利拉=1000、勇者装备分解机=20064，
均与 PVF 闭合），在便携分解机模式下它取 0 还是便携道具物品 ID（2600468/69/70/548）静态无法裁决
（H1/H2 并立）。NPC-mode 语义置信度 High（差一次实机取值即 Confirmed）；便携模式语义 Hypothesis。**
它**不是** rev 2 所称的"分解工具物品 ID"——该结论被 PVF 硬证据推翻（物品 1000=野草莓），
正式撤回。

---

## 1. rev 2 结论逐条撤回/保留清单

| rev 2 结论 | rev 3 裁决 | 依据 |
|---|---|---|
| offset 5 = `DisjointToolItemId`（分解机道具物品 ID）；0x3E8/0x4E60 是"分解机道具" | **撤回** | [PVF] `stackable.lst` id 1000 = `food_strawberry.stk`（野草莓）；真实便携分解机 = 2600468/69/70/548（`professional/common/disjoint_portable1-4.stk`，名"破旧/普通/坚固/优质的便携式分解机"）；[PVF] 1000/20064 均为 `npc.lst` 中带分解服务的 NPC |
| `arg2==0x4E60` 时改发 `0x0455 CHRONICLE_DISJOINT`（19 字节，u8 槽位+u32 0x4E60+14×0）并关窗；门控=值判等+窗口状态 0x1A+事件系统含 id 2245 | **保留**（[静态] Confirmed），语义重释：20064=勇者装备分解机 NPC index，2245=该 NPC 的 `[open visible event]` | [PVF] `npc/chn_event_Machine_SaintHorn.npc`（勇者装备分解机，`[item shop] 1235`、`[chronicle disjoint item] 0`、`[open visible event] 2245`）+ `event/chn_event/chn_chronicle_disjoint.evt` 明写 `[chronicle disjoint npc index] 20064`、`[disjoint cost] 2000`、`[result items] …`。客户端两个常量与 PVF 两个数值**逐位闭合** |
| offset 3 与 offset 5 "同源成对（同一道具的槽位与 ID）" | **撤回** | [静态] 二者来源不同字段：offset 3 ← `ctl+0x3D0`（便携机 CItem* 在主背包的槽位），offset 5 ← `*(ctl+0x1C0)`（provider id 指针解引用）；NPC 模式下 +0x3D0 无物、offset3=-1，"成对"只在便携模式偶然成立 |
| 发送函数签名/守卫链/body 写序/唯一调用点/唯一 begin 点 | **保留**（[静态] Confirmed，本轮未推翻） | rev 2 §1、§7 全部扫描复跑通过 |
| "普通/便携共用 0x001A，无独立路径" | **保留 + 重大补充**：玩家分解机商店模式走**独立 tick + `0x00EB`**，rev 2 完全漏掉（见 §4） | [静态] 商店 tick `0x0235EA20`（状态 0xF）、builder `0x016F80A0` |
| ACK 不消费/不回显/不关联 offset 5 | **保留**（本轮未再触碰） | rev 2 §6 |
| "`*(ctl+0x1C0)==0x3E8` → ACK 超时 1000ms（道具特殊）" | **行为保留，意图重释**：1000=德利拉 NPC index → 疑为德利拉专属较快超时（Hypothesis） | [PVF] `npc/Delilah.npc`（德利拉，`[simple shop] 1000 … [disjoint item] 0`） |
| 服务端建议"offset5 与 offset3 槽位物品 ID 交叉校验" | **撤回**（会误报：NPC 模式 offset3=-1 且 offset5=NPC id；便携模式下 offset5 若为 0 也无 item 可校验） | 本轮重释 |

---

## 2. [PVF] 新硬证据（全部本次本地亲证，非转述）

| 查询 | 结果 |
|---|---|
| `npc/npc.lst` id 1000 | `Delilah.npc`——德利拉（修理商），`[role] [simple shop] 1000 [random option operate] 0 [disjoint item] 0` |
| `npc/npc.lst` id 20064 | `chn_event_Machine_SaintHorn.npc`——**勇者装备分解机**，`[role] [item shop] 1235 [chronicle disjoint item] 0`，`[open visible event] 2245` |
| `event/chn_event/chn_chronicle_disjoint.evt` | `[chronicle disjoint npc index] 20064`；`[disjoint cost] 2000`；`[result items] 490000097 …`（客户端分流两常量 20064/2245 与此**双闭合**） |
| `stackable/stackable.lst` id 1000 | `food_strawberry.stk`（野草莓）——**不是分解机** |
| 便携分解机真身 | 2600468 `disjoint_portable1.stk` 破旧 / 2600469 普通 Rustic / 2600470 坚固 / 2600548 优质 |
| `character/expertjob/disjointer.exj` | `[portable disjoint info] 0 30 1 50 2 70 3 90`（便携机**档位→可分解装备等级上限**：30/50/70/90，与上游"PortableDisjoint 校验装备等级"吻合）；`[disjoint self service] 80 2610045 1`；`[chronicle set disjoint result] …`（0x0455 产物表已存在） |
| 带 `[disjoint item]` 服务的 NPC 总数 | **16 个**：1000 德利拉、317 阿拉德分解修理机、319 天界、320 素喃、321 行船用、324 斗兽场冶炼炉、340/341/354/375/381 …——"普通城镇 NPC 分解"的 provider 集合 |

结论：**20064、1000 是 NPC index；2245 是勇者分解机的开放事件 id；真正的分解工具物品 id 在
2600468+ 段**。rev 2 的"工具物品 ID"读法在三个数值上全部失去载体。

---

## 3. [静态] 分解窗口控制器：一个对话框、四种服务模式

对象与单例获取同 rev 2（`[0x3A5C9B8]`→`0x2306CD0`=按 UI id 0x1AF 取对话框；ctor `0x02358B90`
带 `push 0x1AF` 注册；工厂 `0x023C5DD0` 区域 `new`+ctor）。本轮补全：

### 3.1 状态机全图（dispatcher `0x02360871`，跳转表 `0x02360AE4`/索引表 `0x02360B08`，27 态）

| 状态 | 每 tick handler | 含义 |
|---|---|---|
| **0x1, 0x10, 0x1A** | `0x0235E480` | **NPC/便携/勇者分解机 batch tick**（发 0x001A，或分流 0x0455）——三个状态共用同一 tick，状态间差异（单件/便携/批量界面分支）未逐一分辨 |
| **0xF** | `0x0235EA20` | **玩家分解机商店 batch tick（0x00EB）**——rev 2 漏掉的模式 |
| 0x11,0x12,0x13 | `0x0235EF40` | （未还原） |
| 0xD | `0x02359A80` | （未还原） |
| 0xC,0xE | `0x023560D0` | （未还原） |
| 0x14 | `0x02359BB0` | （未还原） |
| 0x16 | `0x02359330` | （未还原） |
| 0x18 | `0x02359D00` | （未还原） |
| 0x0,0x2–0xA,0xB,0x15,0x17,0x19 | `0x023572E0` | 公共/空闲界面态 |

关闭流将 `+0x1B0` 写为 **0x1B**（`0x0235C60B`，`mov dword [esi+0x1B0], 0x1B`）并置 `+0x1B4=-1`。

### 3.2 控制器字段表（rev 3 修订版）

| 偏移 | 含义 | 证据 |
|---|---|---|
| `+0x180` | **OwnerUserId（商店模式）**：所进入的玩家分解机摊主 UserId | 商店 tick `0235ECC6`：`mov ecx,[esi+0x180]; push ecx; push edi(槽位); call 0x16F80A0` |
| `+0x184` | 商店模式标志（byte） | `0235ECB9`：为 0 → 走错误分支 `0235EE47` |
| `+0x18C` | 商店相关计数（发送前与主背包列表虚调用结果比较） | `0235ECA0`–`0235ECB7`；写入者待定位（§7 扫描已列候选） |
| `+0x1B0/+0x1B4/+0x1B8` | 状态 / 子状态-1 / 发送子状态 | 同 rev 2 |
| `+0x1C0` | **指针 → provider id（NPC index；NPC/勇者模式）**；便携模式语义未决（H1 空指针 / H2 指向便携道具 id） | 唯一写点 = opener `0x0235AD99`（存 arg1）；唯二清点 = 关闭重置 `0x02355857`；读者 = tick×3 |
| `+0x1D0` | ACK 定时器：商店 tick 固定 2000ms（`0235EC81`）；NPC tick 按 `*(+0x1C0)==1000` 取 1000/2000ms | `0235EA64`、`0235E4B2`–`0235E4DB` |
| `+0x1EC` | 在途请求标志 | 同 rev 2 |
| `+0x3B8…+0x3D0` | 选中队列；**`+0x3D0` = 便携分解机 CItem\*（商店模式不用）** | 注册：UI 消息 handler `0x0232EF40`（slot 80，vtable `0x035BC65C`）→ `Set_0x3D0(0x02350760)`；清除：道具被移除 `0x016F13BB`（比较 `Get_0x3D0=0x263FFB0`）与关闭重置 |

### 3.3 两条 batch tick 的对照（0x001A vs 0x00EB）

| | NPC/便携 tick `0x0235E480` | 商店 tick `0x0235EA20` |
|---|---|---|
| 状态 | 0x1 / 0x10 / 0x1A | 0xF |
| 队列 | `+0x3B8` | `+0x3B8`（同一队列复用） |
| 超时 | `*(+0x1C0)==1000 ? 1s : 2s` | 固定 2000ms |
| 发送 | `0x16F7B80(槽位, *(+0x1C0)?:0)`，`ecx`=物品所在列表（`0x1648AC0`） | `0x16F80A0(槽位, +0x180)`，`ecx`=主背包列表 |
| offset 3 绑定 | `+0x3D0`→主背包槽位→`list+0x98`，发后复位 -1 | 无此绑定 |
| 恢复调用 | 超时后 `0x235CF80/0x2352F80`（勇者模式跳过恢复） | 超时后同样调用 |

`0x00EB` builder `0x016F80A0` 写序（[静态] 亲核）：`begin(0x00EB)`（`016F82C6/016F82D2`）→
WriteU16(arg2=OwnerUserId) → WriteU16(arg1=槽位) → 对齐混淆读 itemSpace → WriteU8 → 发送。
与上游 `DisjointMachineRequest`（u16 OwnerUserId + i16 TargetSlotIndex + u8 Main）一致。

---

## 4. [静态] 0x00EB 与 0x001A 的关系（任务问题 4 的回答）

1. **0x00EB = 玩家分解机商店的逐件执行请求**（不是报价、不是开门）：上游
   `ExpertJobStoreHandler` 校验 `OwnerUserId == store.OwnerUserId` 后直接
   `DisjointMachineService.TryDisjoint`（执行分解并扣道具/产出）。开门是
   `0x00F1 ENTER_DISJOINT_STORE`，摆摊是 `0x00EA/0x00CE CREATE`。
2. **0x00EB 不经过 0x001A**：商店 tick（状态 0xF）只调 `0x016F80A0`；`0x16F80A0` 全镜像唯一
   调用者即 `0x0235ECD0`；而 `0x001A` builder `0x016F7B80` 全镜像唯一调用者是 `0x0235E7E2`
   （另一 tick）。两条执行协议**互不嵌套**。
3. **玩家分解师是独立的 provider 类型**：其标识（摊主 **UserId**，u16）存于 `ctl+0x180`，
   与 NPC 模式的 `ctl+0x1C0`（NPC index）平行，互不混用。
4. **id 的分工**：NPC provider id → `0x001A` offset 5（NPC 模式）；便携工具 → `0x001A`
   offset 3（槽位）+（待实机）可能的 offset 5；商店摊主 UserId → `0x00EB` offset 0；商店/货物
   编号（如勇者机的 `[item shop] 1235`）只在 PVF/UI 层，不上报。

---

## 5. ctl+0x1C0 生产者穷举（任务问题 1）与负结果记录

**静态已证**：`+0x1C0` 的写点全镜像恰两处——opener `0x0235AC90`（`0235AD99`，存 arg1 指针）与
关闭重置 `0x02355857`（清零）；读点四处分写（tick×3 + 一处异类 UI 已排除）。**arg1 的实参来源
（谁调用 opener、指针指向 NPC 对象字段还是栈/全局）静态未闭合**，扫描记录如下（负面结论的
范围与方式）：

| 扫描 | 方式 | 结果 |
|---|---|---|
| 直接调用 | 全 .text/.code + **全部 17 节中其余可执行节** 的 E8/E9 rel32 反解 | 0 |
| 数据引用 | 全 17 节 dword `0x0235AC90` | 仅 vtable `0x035BD464` 一处 |
| 虚调用（直接形态） | 全镜像 `FF 50–57 18`（call [reg+0x18]）→ 89 处，逐一看归属/上下文 | 全部为其他类/通用二分回调模板（0x017012D0、0x02312480、0x00EC9FF0 三处簇均为 `call [ebp+0x18]` 参数化模板） |
| 虚调用（两步形态，紧邻） | `8B modrm disp8=18` + 8 字节内 `FF D0–D7/E0–E7` → 222 处（`Document/outputs/disjoint-provider/_vslot18_calls.txt`） | InterfaceManager 4 处（`02335EDD/023378AF/02337F3E/02340E87`）均为 `[esi+0x130]` 对话框的**无参** slot-6 调用——与 opener 的 `ret 8` 双参约定不符，属其他对话框流，非本对话框开启点 |
| 虚调用（两步形态，宽窗） | 同上但 call 距 load ≤26 字节（参数压栈间隔）→ 926 处（`Document/outputs/disjoint-provider/_vslot18_calls_wide.txt`） | 分解簇内新增 0x02345B7C/0x02367D0F/0x0236E2AB/0x02370099/0x0237088F/0x02370C7F/0x02370DE8，逐个 CFG 检查均非 opener（例：`02367D0F` 是对全局 `0x3A5AE4C` 对象 +0x18 **原始函数指针**的调用，非 vtable） |
| RTTI | vtable `0x035BD44C` 的 COL `0x037590CC` → type descriptor | 类名被抹除（`.?A` 无命中）——该客户端剥离了 RTTI 名 |
| 事件/脚本绑定 | `GetDisjointDialog`（0x2306CD0/0x2306D00）137 个调用点向前 0x400 字节两种虚调用形态扫描 | 0 命中 |

**推断（Hypothesis）**：opener 的调用方在 XUI/脚本绑定层或经运行时才解析的函数指针（该客户端
UI 由 xui 驱动，`UI/ExpertJob/DisjointerWindow.xui` 等字符串在 literals 中可见），静态镜像中
不存在可直接反解的调用点。**一次实机断点（§8 B1）即可拿到返回地址与实参，终结此问题。**

opener 自身行为补充（[静态]）：`push 0x1AF` 的 ctor 与工厂链已确认对话框身份；opener 开头以
`0x2301820([0x3A5C9B8], id)` 检查 0x1F/0xF7/0x145/0x1AA/0x146 等其它 UI 打开态互斥；arg2 经
`0x23B7FE0`/`0x2355BD0` 传递但两函数均未读栈参（arg2 的实际消费点未定位）。

---

## 6. +0x1C0 与 +0x3D0 的拆分裁决（任务问题 2）

| | `ctl+0x1C0` | `ctl+0x3D0` |
|---|---|---|
| 类型 | 指针，解引用得 int id | CItem 指针 |
| 写入者 | opener（NPC/勇者/便携？待实机） | UI 道具消息 handler `0x0232EF40`（读道具名显示到分解窗，再注册） |
| 语义 | **分解服务提供者 id（NPC index）**——NPC/勇者模式 | **便携分解机道具实例** |
| 对报文 | offset 5 | offset 3（经主背包槽位查询绑定 `list+0x98`） |

- `+0x3D0` 的 CItem：由道具触发的 UI 消息注册（handler 在 InterfaceManager TU，读道具名、
  `0x23B9200` 填窗、注册 CItem）——行为上即"使用便携分解机道具"。能否从中取实际 item id：
  动态可证（§8 B3 直接 dump）；PVF 侧真实 id 必为 2600468/69/70/548（档位与
  `[portable disjoint info]` 的 30/50/70/90 一一对应）。
- **offset 3 命名**：建议 `DisjointToolSlotIndex`（便携分解机道具在主背包的槽位；-1=无）。
  rev 2 的"与 offset 5 同道具"捆绑解释作废。上游按 `offset3 >= 0 → PortableDisjoint 路径`
  处理与此自洽。
- 便携模式下 offset 5 的两种假设（静态不可裁决）：
  - **H1**：便携流不调 opener，`+0x1C0` 保持空 → offset 5 = 0（offset 5 纯 NPC 语义）；
  - **H2**：便携流也调 opener，arg1=&便携道具 id 变量 → offset 5 = 2600468 段（tagged union）。
  判据：§8 B1/B2 一次即分。

---

## 7. 场景矩阵（任务问题 3；每格独立标注，禁止跨格外推）

图例：C=Confirmed [静态] / P=PVF 亲证 [PVF] / H=High / Hy=Hypothesis / ? =未决（动态项）

| 场景 | ctl+0x1C0 | ctl+0x3D0 | offset 3 | offset 5 | opcode |
|---|---|---|---|---|---|
| 城镇分解修理机 NPC（16 个：317/319/320/321/324/340…） | &NPC id → 具体值 H | 空 C | -1 C（机制）/值 H | **NPC id** H | 0x001A C |
| 德利拉（1000） | &1000（=NPC index）H | 空 C | -1 | 1000 H（并触发 1s 超时 Hy） | 0x001A C |
| 便携分解机（2600468/69/70/548） | **?（H1 空 / H2 &道具id）** | CItem* C（实例 P 可对应） | **便携机主背包槽位** C（机制）/具体值 ? | **?（H1: 0 / H2: 2600468 段）** | 0x001A C |
| 玩家分解师商店 | 不适用（tick 不读）C | 不适用 C | 不入 0x001A C | 不入 0x001A C | 0x00F1 进入 + 逐件 **0x00EB(OwnerUserId, 槽位, Main)** C |
| 勇者装备分解机（20064，事件 2245） | &20064 H | 空 C | -1 H | （0x0455 body：u8 槽位 + u32 20064）C | **0x0455** C；门控不满足时退回 0x001A(offset5=20064) H |

注：
- NPC 模式"offset5=NPC id"的 H 级依据 = 数值三方闭合（客户端常量 1000/20064 ↔ PVF npc.lst ↔
  事件文件"npc index"字样）+ 数据流闭合；缺的只是实机取一次值。
- "16 个 NPC 都走同一窗口/NPC-mode"为 Hy（UI 入口未逐一追）；每 NPC 的 offset5 值即其 npc.lst
  id，未见任何映射表插入其它数值。
- 0x0455 的 19 字节 body 中 `buf[1..5]` 是**立即数 0x4E60**（`0235EEC`），与 arg2 的来源无关，
  即使 arg2 经其它路径赋值，勇者包内恒为 20064。

---

## 8. 动态验证脚本（任务"动态验证准备"；cdb/win32，attach `DNF.unpacked-b1-debug.exe` 后执行）

```text
$$ B0. 帧级眼睛：ClientHook [FrameCapture] 全量双向照开（比对所有断点结论）

$$ B1. opener 入口：抓返回地址/实参/调用栈 —— 一次命中即终结"谁开窗、arg1 指向什么"
bp 0235AC90 ".printf \"OPEN ret=%p arg1=%p *arg1=%d arg2=%d ctl=%p\n\", poi(esp), poi(esp+4), poi(poi(esp+4)), poi(esp+8), ecx; k 6; dd poi(esp+4) L2"

$$ B2. +0x1C0 写入点：ctl、pointee、*pointee
bp 0235AD99 ".printf \"SET1C0 ctl=%p ptr=%p *ptr=%d\n\", esi, ebx, poi(ebx)"

$$ B3. Set_0x3D0：注册的 CItem 及其前 0x40 字节（含 vtable 与可读出的 id 字段）
bp 02350760 ".printf \"SET3D0 ctl=%p item=%p\n\", ecx, poi(esp+4); dd poi(esp+4) L10"

$$ B4. 发送函数入口：arg1/arg2 与 this
bp 016F7B80 ".printf \"SEND this=%p slot=%d tool=%d\n\", ecx, poi(esp+4), poi(esp+8)"

$$ B5. 读 +0x1C0 的发送前一拍（tick 内）
bp 0235E7CE ".printf \"READ1C0 ctl=%p ptr=%p val=%d\n\", ecx, eax, poi(eax)"

$$ B6. 分流与商店证据
bp 016F7EFA ".printf \"CHRONICLE-BEGIN\n\""      $$ 命中=走了 0x0455
bp 016F82C6 ".printf \"STORE-00EB-BEGIN owner=%d slot=%d\n\", poi(esp+8), poi(esp+4)"
$$ 0x00EB 的 body 全文以帧捕获为准（u16 owner + i16 slot + u8 Main）

$$ 清理：bc *
```

测试步骤与判据（每场景重复 B1–B5+帧捕获）：

| 场景 | 步骤 | 判读 |
|---|---|---|
| A. 普通分解修理机 NPC（如 317 阿拉德分解修理机） | 对话 → 装备分解 → 分解 1 件 | B1 ret=?（记下调用者）、*arg1==317？B5 val==317？帧 offset5==317、offset3==FFFF？ |
| B. 德利拉（1000） | 对话 → 分解 1 件 | 同上且 *arg1==1000；观察 ACK 是否 ~1s 超时档 |
| C. 便携分解机（背包右键/拖入，按 UI 实际） | 用 2600468 分解 1 件 | **B1 是否命中**（H1: 不命中→offset5=0；H2: 命中且 *arg1∈2600468 段）；B3 的 CItem 与帧 offset3==该道具主背包槽位 |
| D. 玩家分解师商店 | 进入他人分解机摊位 → 分解 1 件 | B6 STORE 行 owner==摊主名 UserId；帧为 0x00EB；**0x001A/0x0455 断点不得命中** |
| E. 勇者装备分解机（事件 2245 开启时） | 用勇者机分解 1 件勇者装备 | B1 *arg1==20064；B6 CHRONICLE 命中；帧 0x0455 body[1..5]==0x4E60；服务端记 [disjoint cost] 2000 |
| F. 差分（可选） | B5 处 `r eax poi(eax)` 改值放行 | offset5 变更而服务端结果不变 → 非权威复证；改成 20064 → 应见 0x0455 分流 |

判别要点：NPC id 与物品 id 空间不重叠即可排除法（1000=野草莓/20064=勇者机 NPC）；"窗口 id
0x1AF""状态 0x1A""模式枚举"都不会出现在 offset 5。

---

## 9. 生产者—状态机—协议消费者 数据流图（rev 3 全图）

```text
                       ┌────────────────────────────────────────────────────────┐
                       │ 分解窗口对话框 (UI id 0x1AF, ctor 02358B90, 27 态)      │
                       └────────────────────────────────────────────────────────┘
 [PVF] NPC role          │ UI 入口(静态未闭合,XUI/脚本层?)                          │ [PVF] 便携道具使用
 npc.lst 16 个           ▼                                                        ▼
 [disjoint item]   ┌─────────────┐  arg1=&providerId   ┌──────────────────────────────┐
 [disjoint item]0  │ opener      │────────────────────▶│ ctl+0x1C0 = ptr→provider id  │
 [chronicle        │ 0235AC90    │                     │ ctl+0x3D0 = 便携机 CItem*     │◀── 0232EF40(UI msg)
   disjoint item]  └─────────────┘                     │ ctl+0x180 = OwnerUserId(商店) │◀── 进入商店流程(writer待定位)
 勇者机=20064               │                            │ ctl+0x1B0 = 状态(1/0x10/0x1A/ │    ctl+0x184/0x18C
 事件=2245                  │                            │   0xF=商店)                  │
                            ▼                            └──────────────┬───────────────┘
                 ┌────────────────────────┐                             │ dispatcher 02360871
                 │ 状态 1/0x10/0x1A       │              ┌──────────────┴───────────────┐
                 │ tick 0235E480          │              │ 状态 0xF                      │
                 │ 超时 *(1C0)==1000?1s:2s │              │ tick 0235EA20 (固定 2s)       │
                 │ +0x3D0→主背包槽→list+98 │              │ (不碰 1C0/3D0)                │
                 └───────────┬────────────┘              └──────────────┬───────────────┘
                             ▼                                          ▼
              016F7B80 SendDisjointItem(slot, providerId)      016F80A0 SendRequestDisjointItem
                             │                                          │
             ┌───────────────┴───────────────┐                          ▼
             ▼                               ▼                    0x00EB {u16 OwnerUserId,
     providerId==20064                providerId!=20064          i16 slot, u8 Main}
     且状态0x1A 且事件2245             （便携: 0 或 2600468 段？）        │
     且事件查找通过                        │                          ▼
             ▼                            ▼                    玩家分解机商店
     0x0455 CHRONICLE_DISJOINT      0x001A DISJOINT_ITEM       (上游 ExpertJobStoreHandler:
     {u8 槽位, u32 20064, 14×0}     {i16 槽位, u8 itemSpace,    OwnerUserId==store.Owner)
     +本地关窗                       i16 toolSlot(-1=无),
                                    i32 providerId}
```

---

## 10. 字段命名裁决与服务端建模建议（任务问题 5）

**命名**：
- offset 5 → **`DisjointProviderId`**（描述性别名：NPC 模式下即 `DisjointNpcIndex`）。
  不叫 `DisjointContextValue`（无信息量）、不再叫 `DisjointToolItemId`（已被 PVF 推翻）。
  若动态证明便携模式也写入道具 id，则它是 **mode-tagged provider context**（NPC index ∥
  portable tool item id），命名保持 `DisjointProviderId` 并在 DTO 注释里写明两义。
- offset 3 → `DisjointToolSlotIndex`（便携分解机道具主背包槽位；-1=非便携）。
- 0x00EB offset 0 → `StoreOwnerUserId`（上游已有）。

**服务端建模边界**：
1. offset 5 维持"协议 DTO + 诊断日志"，**不参与资格/产物/扣除/回包/持久化**（[静态] 客户端不回读；
   [静态/上游] 上游无消费）。
2. **不要**按 rev 2 建议做"offset5==槽位物品 ID"交叉校验（已撤回）。便携模式 H1/H2 尚未裁决，当前也不启用全局 provider 白名单告警；动态闭合后，可仅在 `toolSlot == -1` 的 NPC 模式下，用服务端 PVF 生成的分解 NPC 集合做旁证检查。
3. `0x001A` 中 offset5==20064 在窗口状态不符等提前分支下可合法回落，建议按普通分解处理并记 warning、不硬拒；若状态已是 0x1A 但事件 2245 查找失败，客户端会拒发而不是回落为 `0x001A`。
4. **0x0455 若立项**：PVF 已给出门槛事件 2245、`[disjoint cost] 2000`、`[chronicle set disjoint result]` 产物候选和请求体 `{u8 槽位, u32 20064, 14×0}`，但产物选择、资格、ACK/NOTI 时序和事务失败规则仍需独立闭合；服务端应校验角色当前可交互的勇者机 NPC==20064（服务端自身权威状态），而非信任报文。
5. 便携分解（若实现）：资格校验以 offset 3 槽位上的道具为准（上游现状），等级上限用
   `disjointer.exj [portable disjoint info]`（30/50/70/90）；offset 5 不参与。
6. 权限问题：offset 5 一律视为**客户端上报的非权威旁证**——服务端的 provider 权威来自
   自身交互领域状态（是否在商店、是否与 NPC 交互、事件是否开启），报文值只用于诊断与 anomaly 检测。

---

## 11. 还差什么才能 Confirmed（任务最终问句的展开）

1. **便携模式 H1/H2 裁决**（一次实机：§8 场景 C，看 B1 是否命中与 offset 5 实值）——
   这是 `DisjointProviderId` 能否升级为"完整 tagged union"定论、以及 NPC-mode 语义能否
   上升到整体 Confirmed 的唯一缺口。
2. NPC 模式取一次实值（场景 A/B）证明 offset5 == 交互 NPC 的 npc.lst id（预期 317 或 1000）。
3. （非必须但顺路）opener 返回地址（B1 的 ret）——补上静态未闭合的调用方，把"XUI/脚本层调用"
   假设坐实或推翻。
