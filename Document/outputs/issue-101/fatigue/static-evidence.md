# GH #101 — A21 疲劳协议静态证据

## 范围与结论

本页只记录 A21 客户端静态证据，不启动客户端，不把客户端显示行为解释成原版服务端的疲劳消费政策。

- 疲劳状态更新的协议身份是 **S→C NOTI `0x0024` `FATIGUE`**。NOTI `0x0035` 是 `CERA`，NOTI `0x01EB` 是 `USER_FATIGUE_ACCELERATION_STATE`，均不是普通疲劳更新。
- `0x0024` body 是闭合的 **10 字节：`5 × u16`**。五次读取后，handler 内直到 `ret` 不再读取网络流。
- 前三个字段与 `SELECT_CHARACTER` 成功响应 body `+11/+13/+15` 进入完全相同的三个消费者。因此当前服务端写在 `+13/+15` 的 `Total/Used`，在增量通知中对应 `0x0024 +2/+4`；`+0` 对应选角响应里另一个兼容 `u16`（`+11`）。
- 客户端自己的日志格式把选角响应三项依次命名为 `Fatigue`、`FatigueMax`、`usedFatigueMax`。这是客户端字段命名证据；它不证明原版服务端何时扣减，也不证明服务端业务规则。
- 未闭合“零疲劳时客户端是否在本地阻止入场”的调用链，也未闭合最终 UI 图片选择。已确认的零值分支位于 `0x0024` handler 内，表现为记录标志更新／条件通知构造，不能命名为入场门或选图门。

## 样本身份

| 工件 | SHA-256 / 长度 |
|---|---|
| `E:\Project\DNF\DNFClient-A21-protocol\DNF.exe` | `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` |
| `E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin` | `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`；`122085376` 字节 |

下文地址均为 RVA。旧文档 `Docs/a21-protocol/proto-*.md` 使用加载基址 `0x00400000` 后的 VA，例如 VA `0x01185070` 等于 RVA `0x00D85070`。

## 协议身份与长度

`protocol/patterns/data/probes/a21-identity-census.tsv:696` 给出：

```text
NOTI  0x0024  0x00D85070  0x00D887D3  batch  FATIGUE
```

`Docs/a21-protocol/proto-1.md:228` 独立记录 VA `0x01185070` 的布局为 `5*u16`。手工复核 handler 得到五次连续 `ReadU16`：

| body offset | 宽度 | 读取调用 RVA | handler 局部变量 |
|---:|---:|---:|---|
| `+0` | `u16` | `0x00D850A7` | `[ebp-0x1C]` |
| `+2` | `u16` | `0x00D850B0` | `[ebp-0x28]` |
| `+4` | `u16` | `0x00D850B9` | `[ebp-0x20]` |
| `+6` | `u16` | `0x00D8520A` | `[ebp-0x24]` |
| `+8` | `u16` | `0x00D852EF` | `[ebp-0x2C]` |

读取原语目标均为 `0x023C52A0`。第五次读取后到 `0x00D85494 ret` 之间没有新的流读取。因此现有自动页 `protocol/game/server-to-client/NOTI-0x0024.md` 的“至少 8 字节”只是走图下界，不能作为完整长度。

## 与选角初始化字段的同消费者映射

`SELECT_CHARACTER` 响应 handler 为 RVA `0x00D11070`。其顺序读取与既有协议页一致：body `+9/+11/+13/+15` 分别在 `0x00D111D3/0x00D111DC/0x00D111E5/0x00D111EE` 读取 `u16`。`Docs/a21-protocol/proto-5.md:457-485` 的客户端日志字面量为：

```text
createdTime %d MyServerId %d, Fatigue %d, FatigueMax %d, usedFatigueMax %d Cash %d
```

除 `MyServerId`（body `+9`）外，三项疲劳字段与 `0x0024` 的前三项进入相同消费者：

| 客户端静态名 | 选角 body | `0x0024` body | 相同消费者证据 | 当前服务端投影 |
|---|---:|---:|---|---|
| `Fatigue` | `+11 u16` | `+0 u16` | `0x00D11A3B` 与 `0x00D85228` 都调用 `0x015DDB80` | 兼容字段，需与抓包值一起解释 |
| `FatigueMax` | `+13 u16` | `+2 u16` | 两处都写入以 `0x03B3E1D0` 为锚的同一混淆全局单元 | `Total` |
| `usedFatigueMax` | `+15 u16` | `+4 u16` | `0x00D11AF1` 与 `0x00D852E3` 都调用 `0x015DC830`，写所选角色对象 `+0x318` | `Used` |

`0x015DDB80` 先由 `0x015DD160` 取得旧值，计算 `new-old`，触发消息 id `0x56`，再写入以 `0x03B3E1C8` 为锚的混淆全局单元。这证明 `Fatigue` 会更新客户端状态并产生本地差值通知，但仍不等于服务端消费规则。

后两个字段也可与选角响应建立**消费者对应关系**，但语义未知。选角响应在 body `+17` 有 `premium_count`，随后存在变长列表，因此不能把尾字段写成跨样本固定 wire offset。既有 261 字节、`premium_count=0` 分支把它们观察在 `+223/+226`；其他分支会随前置变长段移动。

| `0x0024` | 选角响应中的对应值 | 同消费者 |
|---:|---:|---|
| `+6 u16` | `premium_count=0` 的既有样本中为 `+223 u16`；通用 wire offset 不固定 | `0x015DD500`、`0x015DD5C0` |
| `+8 u16` | `premium_count=0` 的既有样本中为 `+226 u16`；通用 wire offset 不固定 | `0x015DC430` |

不得把 `+6/+8` 写成 Total/Used；当前没有字段名或实包差分支持这种解释。

## 零值与 UI 静态线索

`Docs/a21-units/unit-011.md` 把 `CNRDBaseInterface::drawMainControl` 定位到 RVA `0x01373B00`，其客户端字面量是 `drawMainControl#sp표시, 피로게이지`，说明该 HUD 路径拥有疲劳槽显示。

`0x0024` handler 自身存在两组可确认的零值门：

- `0x00D850C1..0x00D85129`：当 `+4 == 0 && +0 == 0` 时，遍历一个 12 字节记录数组并把记录 `+8` 的字节置 `1`。
- `0x00D8512B..0x00D85203`：在功能 id `0x823` 开启、`+4 == 100 && +0 == 0` 时，构造本地化 id `0x11B9E` 的客户端通知。

这些分支说明客户端确实对 `Fatigue == 0` 做额外处理，但调用链没有闭合到地下城选择/进入请求的本地放行判断，也没有闭合到图片资源或帧索引。当前只能确认“零值会触发客户端侧状态/UI 相关处理”。**零疲劳入场本地门、图片选择条件、图片 id 和最终视觉效果均为 Unknown**。服务端返回 `0x0010` 的不足疲劳错误只能证明客户端能显示拒绝，不能反推客户端一定存在本地入场门。

另有多处客户端比较 `Fatigue` 与 `FatigueMax`，例如 `0x0069CAA0` 内部在 `0x0069CB2D..0x0069CB38` 比较 `Fatigue` 与 `FatigueMax-8`，以及 `0x0233E3A0` 内部在 `0x0233E446..0x0233E44E` 比较两者。这些更像准入/提示或操作门，不能直接标成 HUD 选图逻辑。

## 地下城资源 `+0x1BC` 字段纠正

入场候选路径调用 RVA `0x00CCBAC0` 时，会经 `[VA 0x03A5C940]` 的 type `0x16` 资源管理器取得 DGN raw resource，并返回 `byte(raw+0x1BC) != 0`。该字段的 PVF 原名已经闭合为 **`[added required item]`**，不是 `[event dungeon]`，也不是 `[story mode]`：

```text
RVA 0x026AC5B9  push 0x03659C78       ; 加密 tag descriptor
RVA 0x026AC5DF  mov  [ebp-0x1520],0x30D
RVA 0x026AC5F5  jne  0x02AACC87       ; 已初始化后的 tag handler

RVA 0x026ACC87  push 1
RVA 0x026ACC89  call 0x028AB5A0
RVA 0x026ACC91  test eax,eax
RVA 0x026ACC99  setne dl
RVA 0x026ACC9C  mov  byte ptr [eax+0x1BC],dl
```

这里最后一条中的 `eax` 是 parser 保存的 DGN raw resource 指针。只读遍历客户端解密字符串缓存 `[VA 0x03B8960C]` 得到 descriptor `VA 0x03659C78 -> UTF-16 "[added required item]"`；作为排歧，独立 descriptor `VA 0x03659C50` 解密为 `[event dungeon]`。

`RVA 0x0243B390` 的已加载资源缓存可以完全用 `ReadProcessMemory` 枚举，不需要调用客户端函数：

1. `manager = [VA 0x03A5C940]`；缓存对象内嵌在 `manager+0x208`，其 `+0/+4/+8` 分别是 bucket array、bucket count、item count。
2. 对 bucket `i`，先取 `head = [bucketArray+4*i]`，再取 `link = [head]`；`node = link-0x0C`。这一层额外解引用不能省略，因为 bucket 保存的是 predecessor 的 link 地址。
3. `node+0` 是规范化 UTF-16 DGN path，`node+8` 是 wrapper，`node+0x0C` 是下一 link，`node+0x10` 是 hash；`raw = [[node+8]]`。
4. hash 为逐 UTF-16 code unit 的 `h = h*0x1003F + ch`（32 位回绕）。当前节点只有在 `hash % bucketCount == i` 时仍属于该 bucket。

2026-09-20 的只读快照中，manager=`0x3A2E4680`、type=`0x16`、bucket array=`0x486A1580`、bucket count=`79`、item count=`53`，按上述结构恰好枚举出 53 个唯一节点。DriftCave 六个已缓存 DGN 的 `byte(raw+0x1BC)` 均为 `0`；`tau_kingdom/tau_kingdom.dgn` 与 `southerndale/grimseeker.dgn` 为 `1`。后二者与 `[event dungeon]` 的共现只是一项样本相关性，不能覆盖 parser 已确认的字段原名。

因此，这一字段可以从“未知特殊标志”纠正为 `[added required item]` 布尔投影；它本身不能解释 150..155 被过滤而 8007 留存。8007 的 DGN 当时未出现在这 53 项已加载缓存中，不能用该次 RPM 结果声称其运行时值。

## WDM `[dungeon]` 解析布局

真正的 WDM 资源管理器是 `[VA 0x03A5C934]`，2026-09-20 的只读快照为 manager=`0x3A2E4E00`、type=`0x0D`。type `0x0D` 的资源工厂分支位于 RVA `0x02358DAA`：它构造 `0x150` 字节对象并调用 RVA `0x02646DC0` 解析 WDM。公共缓存中 `driftcave.wdm` 的 wrapper=`0x33C72DF8`，raw=`0x4045D600`。

解析器在 RVA `0x02646F53` 比较 descriptor `VA 0x03122F00`，其解密明文为 `[dungeon]`。列表容器对象起于 raw `+0x68`；begin/end/capacity 指针分别位于 `+0x6C/+0x70/+0x74`。每项固定 12 字节：

| entry offset | 类型 | 解析含义 |
|---:|---:|---|
| `+0` | `i32` | dungeonId |
| `+4` | `i32` | qualifier questId；缺省初始化为 `-1` |
| `+8` | `i32` | 源语法是否出现 `[in progress]`；`0/1` |

第三字段的含义由 parser 直接证明：每项先在 RVA `0x02646FCB..0x02646FDF` 初始化为 `(dungeonId, -1, 0)`；当下一 token 匹配 descriptor `VA 0x0364D978`（解密明文 `[in progress]`）时，RVA `0x02647039` 把第三字段写为 `1`，随后读取的整数写入第二字段；裸整数则直接写入第二字段，第三字段保持 `0`。RVA `0x0264704D..0x02647062` 把三字段整体追加到 vector。

live raw 的 begin=`0x3F6251C0`、end=`0x3F625238`、capacity=`0x3F62525C`，恰好 10 项，并与 PVF 原文逐项一致：

```text
(150,  -1, 0)  (151,  -1, 0)  (152,  -1, 0)  (153,  -1, 0)
(154,1895, 0)  (155,  -1, 0)  (239,1925, 1)  (239,1927, 1)
(8007, -1, 0)  (2011,13505,0)
```

因此 150..155 与 8007 都已经被 WDM parser 加载；当前候选差分发生在后续消费者。`154 1895` 是“裸 qualifier questId，marker=0”，而两条 `239 [in progress] questId` 的 marker=1。

### WDM qualifier 的消费语义

消费者 RVA `0x00A64AC0` 对上述 12 字节项的判断顺序如下：

1. `qualifierQuestId == -1` 时直接放行。
2. `marker == 1` 时只调用 RVA `0x00A51BF0(questId)`。
3. `marker == 0` 时先调用 RVA `0x00A505A0(questId)`；未命中再调用 RVA `0x00A61ED0(questId)`，两者是 OR 关系。
4. 上述带 qualifier 的判断失败后，还会调用 RVA `0x01363ED0`（VA `0x01763ED0`）作为全局兜底。该兜底的具体模式语义不在本次边界内，因此这里确认的是 quest 谓词，不把整个函数简化成只有这两个条件。

三个 quest 谓词可以进一步闭合：

- RVA `0x00A505A0` 扫描 `questManager+0x08..+0x0C` 的 12 字节记录数组，仅判断记录指向的 questId 是否存在。2026-09-20 只读快照 `Document/outputs/issue-101/live/t01-dungeon-quest-gate.json` 中该数组只有 quest `2489`，与当前角色唯一 active quest 一致。
- RVA `0x00A51BF0` 扫描同一数组，除 questId 命中外，还解码记录 `+4/+8` 的状态并要求结果非零。本次快照中 quest `2489` 的编码值解得 `1`。结合 WDM parser 的明文 token `[in progress]` 只选择这条分支，可将它定为“当前任务表中处于 in-progress 状态”，而非 completed 集合查询。
- RVA `0x00A61ED0` 查询 `questManager+0xB0` 的 keyed container。`NOTI 0x0164 CLEAR_QUEST_LIST` handler RVA `0x00D641F0` 在 clear bitmap 中逐个枚举已完成 questId，并于 RVA `0x00D64267` 调用 RVA `0x00A72C60`；后者在 RVA `0x00A72C6A` 对 `questManager+0xB0` 执行插入并写入该 questId。因此这个容器是 clear/completed quest 集合，不是 acceptable/available 集合。上述快照也直接枚举出 `+0xB0` 的 9 个 key：`101, 1016, 1776..1782`，与 `Document/outputs/issue-101/live/t01-client-quest-flags.json` 的 clear bitmap 一致。

所以，忽略独立的全局兜底条件时，裸 `questId` 的正常 quest 判定是 **active/current quest 表存在 OR completed/clear 集合存在**；带 `[in progress]` 的判定是 **当前任务表命中且记录状态非零**。这也解释了裸 `154 1895` 与 `239 [in progress] 1925/1927` 的语法差异，但当前角色既未 active 也未 clear `1895` 时，154 会被该 quest 谓词拒绝；这项结论不延伸到普通 DGN 的 `[story mode]` 入场门。

## 置信度与仍未知项

| 结论 | 置信度 | 限制 |
|---|---|---|
| 更新 identity 是 NOTI `0x0024 FATIGUE` | Confirmed | 注册表与 handler 一致 |
| body 是 `5 × u16`、共 10 字节 | Confirmed（静态） | 尚无归档 A21 实包样本 |
| 前三项对应选角 `+11/+13/+15` | Confirmed（静态同消费者） | 客户端字段名不等于服务端领域语义 |
| `+2/+4` 对应当前服务端 `Total/Used` 投影 | Confirmed（针对当前编码位置） | 不证明当前固定 `(188,0)` 符合原版政策 |
| `+6/+8` 的消费者对应关系 | Confirmed（静态） | 含义 Unknown；选角侧 wire offset 受变长列表影响 |
| WDM 裸 qualifier 的 quest 谓词为 active/current OR completed/clear | Confirmed（静态调用链 + 通知写入链） | qualifier 失败后仍有独立全局兜底 RVA `0x01363ED0` |
| WDM `[in progress]` 只查当前任务表且要求记录状态非零 | Confirmed（parser token + 静态消费者 + 只读状态） | 未给解码状态的其他数值建立枚举名 |
| 零疲劳入场本地门 | Unknown | 已见零值处理，但未闭合到地下城入场请求放行判断 |
| 零疲劳图片选择门 | Unknown | 仅闭合到零值分支与 HUD 所有权，未闭合到图片资源 |
| 原版服务端扣减时机、扣减量、免疲劳例外 | Unknown | 客户端显示和错误码均不能证明服务端政策 |

## 最小实机取证建议

1. 先只做透明采集：保留 `SELECT_CHARACTER` 成功响应原始 body，并记录 `+11/+13/+15`；若连接参考服，再观察参考服是否自然发送 10 字节的 `0x0024`。
2. 当前服务端没有疲劳账本且 `Spend` 为 no-op，普通入场不会自然产生可信的扣减或 `0x0024`，所以“打一格后等待通知”不能作为当前服务端的验证方法；透明采集得不到 `0x0024` 也是预期可能结果。
3. 用受控、独立夹具向客户端投影一帧合成 `0x0024`，初值取本次选角的五项对应值；一次只改变 `+0`、`+2` 或 `+4` 中的一项，记录 HUD 与本地提示。夹具必须保留原始 10 字节和时间线。
4. `+6/+8` 先原样保持；只有在前三项映射验证后才分别做单字段差分。任何合成通知都只证明客户端消费/显示，不证明原版服务端扣减时机、扣减量或免疲劳政策。

## 复现命令

```powershell
cd E:\Project\DNF\DNF-JP-86-Server

python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xD85070 0xD854A0
python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xD11070 0xD11B60
python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xA64AC0 0xA64B21
python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xA505A0 0xA505E9
python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xA51BF0 0xA51CCF
python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xA61ED0 0xA61F29
python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xD641F0 0xD64308
python Document/outputs/issue-101/fatigue/fatigue_static_probe.py 0xA72C60 0xA72C9A
python Document/outputs/issue-101/fatigue/find_direct_xrefs.py 0x15DDB80 0x15DC830 0x15DC430 0x15DD500 0x15DD5C0
python Document/outputs/issue-101/fatigue/resource_cache_probe.py --pid <DNF-PID>

Get-FileHash E:\Project\DNF\DNFClient-A21-protocol\DNF.exe -Algorithm SHA256
Get-FileHash E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin -Algorithm SHA256
```

前两个脚本只读 RVA dump，不加载或启动游戏；第三个脚本只做进程内存读取：

- `Document/outputs/issue-101/fatigue/fatigue_static_probe.py`（SHA-256 `6F04C22753B873134E989F8F06E205990961FDF76EE3A45ADE9D0C6FF8CCADF8`）
- `Document/outputs/issue-101/fatigue/find_direct_xrefs.py`（SHA-256 `48E557BC1D4FAAC3D3C6C112EC0261791AA579044151FF7B7A72BC016FBD00C5`）
- `Document/outputs/issue-101/fatigue/resource_cache_probe.py`（SHA-256 `47CBB82C29300FB9B5A31559AB91092CCAD5D1110B4CE8EEBC960394FAF6B33A`；仅以 `PROCESS_VM_READ` 枚举已加载缓存，不调用客户端函数）
