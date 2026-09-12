# A21 队伍设置静态证据（2026-09-12）

## 结论与边界

当前 A21 客户端静态代码足以支持后续实现一个**严格的双形状 decoder**，并把两种请求归一化为
同一份 12 字节设置块：

- 无内嵌标题：恰好 12 字节；
- 内嵌标题：`2 + i32-le length + title[length] + 10`，总长严格为 `16 + length`。

设置块中，`[2]` 是容量，`[9]` 是队伍类别，`[10..11]` 是 little-endian 类别目标。
类别映射为 `Story=0`、`Cycle=1`、`Normal=2`。其余字节只记录构包宽度或局部用途，不在本轮
强行命名。`NOTI 0x0009 type=1` 消费同一 12 字节信息块，不含名册块，但在末尾仍有
`type<=2` 共用的 `hasExtra` 字节。

这些是 **current-client static / High** 和上游已捕获样本的交叉结论，本节本身不是实机抓包。
静态阶段三个当前 A21 客户端目录均没有可用的 NetworkHook 帧日志，因此当时不把 `0x000C` 或
`NOTI 0x0009 type=1` 标为 `ClientVerified`；随后取得的 direct/type-1 实帧另见
[`a21-party-settings-live-20260912.md`](a21-party-settings-live-20260912.md)，不反向改变本页静态证据等级。

## 固定输入

| 输入 | 大小 | 时间 | SHA-256 |
|---|---:|---|---|
| `E:/Project/DNF/DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe` | 94,199,808 | `2026-09-06T22:48:26.3116609+08:00` | `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14` |
| `E:/Project/DNF/DNFClient-A21-protocol/dstr.dat` | 3,173,376 | `2026-02-04T14:51:25.4340524+08:00` | `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10` |
| `E:/Project/DNF/ServerS4A21 origin/master` | — | `2026-09-07T23:34:10Z` | commit `c58cd2b479b0357cb6ca78887c9d638b080f4638` |

当前 exe ImageBase 是 `0x00400000`。本页地址均为 VA；探针内部按 `RVA = VA - ImageBase`
映射 PE。可复现探针：

```powershell
python -X utf8 `
  .rks-agent/tasks/party-mode-rules/attempts/spec-001-party-settings-foundation/evidence/probe_a21_party_settings.py `
  E:/Project/DNF/DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe `
  E:/Project/DNF/DNFClient-A21-protocol/dstr.dat
```

探针使用 Python 3.11.3、pefile 2024.8.26、capstone 5.0.7，只读输入并输出选定窗口。`dstr.dat`
取 PE `FILE/102`，校验 `SC01`，以初始 key `0x5819AF17` 做前一密文字链式 XOR 后按 UTF-8
读取；同时对 14 个关键 VA 做精确字节锚点校验，本轮结果为 `14/14 PASS`。关键文本为：
`48600>剧情地下城`、`48601>循环地下城`、
`48605>没有循环地下城区域。`、`48612>无法进入与现在组队模式不符的地下城。`、
`48613>在剧情地下城队伍中无法委任队长`、
`49275>组队进入剧情地下城， 可以共享队长的任务经验值。`。

## 抓包覆盖检查

`rg --files -uu` 对 `DNFClient-A21-protocol`、`DNFClient-A21-debug`、
`DNFClient-A21-protocol2` 的 frame-capture/write-trace 文件匹配数均为 0。

仓库内仅有的相近 JSONL 也没有本协议样本：

| 文件 | 性质 | JSON 对象数 | `type/arg_type/cmd == 12` |
|---|---|---:|---:|
| `ClientHack/runtime/captures/capture-1.jsonl` | ClientHack 目标字节诊断快照，不是网络帧 | 441 | 0 |
| `ClientHack/runtime/captures/capture-2.jsonl` | ClientHack 目标字节诊断快照，不是网络帧 | 441 | 0 |
| `frame-capture-20260817-town-walk.jsonl` | 旧版本城镇移动帧摘录 | 30 | 0 |
| `frame-capture-20260817-dungeon-entry.jsonl` | 旧版本副本进入帧摘录 | 240 | 0 |

因此本页不会把上游 self-test 中名为 `captured*` 的数组伪装成本仓当前实帧；它们只作为
`ServerS4A21` 的来源记录。

## `0x000C` 当前客户端构包

### 共享字段顺序

通用编辑构包窗口 `VA 0x009C6468..0x009C651A`：

1. `0x009C6469` 选择 type `0x000C`，`0x009C6472` 建 header；
2. `0x009C6477..0x009C6482` 写一个 `u8`；
3. `0x009C6487..0x009C6495` 写第二个 `u8`；
4. `0x009C649A..0x009C64AC` 仅在第二字节为 0 时写 raw-dstr；
5. `0x009C64B1..0x009C6515` 依次写 `u8, u32, u8, u8, u8, u16`；
6. `0x009C651A` Finalize。

宽度由同一客户端 parser 的读取函数和既有构包原语交叉确认。于是：

```text
第二字节非零：2 + 10 = 12
第二字节为零：2 + (4 + titleLength) + 10 = 16 + titleLength
```

Story 专用窗口 `0x019E6EF8..0x019E6F9A`、Normal 默认窗口
`0x00C24785..0x00C24833`、自动窗口 `0x0240BF41..0x0240C05F` 都使用同一顺序。
Normal 默认路径在 `0x00C2481D..0x00C24833` 写类别 `2` 和目标 `0`；Story 路径在
`0x019E6F74..0x019E6F95` 写类别 `0` 和当前目标。

自动路径 `0x0240C008..0x0240C05A` 只保留 `[0,3)` 的现有类别，否则写 `2`，同时在合法
现有队伍上保留对象 `+0x64` 的目标，否则写 `0`。这同时证明当前 UI 把三值域限定为
`0/1/2`，默认回落是 Normal。

### 归一化矩阵

| 规范 offset | 宽度 | C2S 来源 | 当前含义 | 证据等级 |
|---:|---:|---|---|---|
| 0 | 1 | 两形状的第 1 字节 | 信息/标题模式字节；确切业务枚举未命名 | High |
| 1 | 1 | 两形状的第 2 字节 | 客户端 title/raw-dstr gate；客户端 UI 也把它投影为回归玩家/赛丽亚祝福队伍标志。现行服务端按产品决策强制归一化为 0 | High semantics / Implemented policy |
| 2 | 1 | direct `[2]`；edit 标题后的第 1 字节 | 容量，当前服务端不变量为 1..4 | High（客户端构包 + 上游捕获/校验） |
| 3..6 | 4 | edit 尾部 `u32` | 未解释，必须无损保留 | Confirmed width / unknown meaning |
| 7 | 1 | edit 尾部下一 `u8` | 未解释，必须无损保留 | Confirmed width / unknown meaning |
| 8 | 1 | edit 尾部下一 `u8` | 未解释；部分路径写 0，自动路径写运行时布尔值 | Confirmed width / unknown meaning |
| 9 | 1 | edit 尾部倒数第 3 字节 | `PartyCategory` | Confirmed static |
| 10..11 | 2 | edit 尾部最后 `u16` | `ModeTargetRaw`，little-endian，按类别解释 | Confirmed width; High semantics |

内嵌标题不属于 12 字节块：edit 的规范块严格由“前导 2 字节 + 标题后的 10 字节”拼成。

### 上游来源与严格边界

固定 commit 的 `Network/Parsers/Party/SetPartyInfoRequest.cs:5-55` 明确保留两份已捕获形状：

- direct create：恰好 12 字节，`TitleIndex=body[1]`、`UserMax=body[2]`；
- named edit：最小 16 字节，`body[0] <= 2`、`body[1] == 0`，在 offset 2 读取有符号
  little-endian `i32` 标题长度，只接受 `body.Length == 16 + titleLength`；标题从 offset 6
  开始，规范块由前导 2 字节和尾部 10 字节组成。

`SelfTests/A21PartyProtocolSelfTest.cs:264-333` 还保存了：

- 12 字节 direct 样本：`00 03 02 00 00 00 00 05 00 02 00 00`；
- 28 字节 edit 样本：`01 00 0C 00 00 00 <“无队伍名”的 12B UTF-8> 04 00 00 00 00 05 00 02 01 00`；
- edit 归一化结果：`01 00 04 00 00 00 00 05 00 02 01 00`。

这与当前客户端静态长度公式和字段顺序逐字节一致。上游 parser 对 direct 形状的“create”命名
来自其捕获场景；当前客户端静态代码只证明 wire 分支，不单凭函数地址断言 UI 动作。

### 请求语义与响应边界

同一上游 commit 的 `PartyHandler.cs:532-633` 以“当前无队/已有队”决定创建或更新，而不是用
另一个 opcode；创建成功投影 `NOTI 0x0009 type=0` 再投影 `0x0099`，已有队更新广播
`NOTI 0x0009 type=1`。`PartyManager.cs:107-153` 要求：当前会话仍是队长、容量合法且不小于
现有人数、规范块恰为 12 字节并满足 `[2] == capacity`；保存时克隆标题和整块原始字节。

当前客户端响应 handler `VA 0x01119990` 不直接读取 frame buffer：它只比较 dispatcher 已传入的
`[ebp+0x0C]` 和 `[ebp+0x10]` 状态/原因参数（例如 `0x08`、`0x9F`、`0x15`）并触发 UI。
因此本轮**没有**恢复足以安全实现的成功/失败响应 body；后续 handler 在没有当前帧或更上游
dispatcher 证据前不得复制上游的 `00 04` 错误响应。

## `NOTI 0x0009 type=1` 当前客户端消费

parser 在 `VA 0x01171FD0`。`0x01171FF8..0x01172066` 先读 `u16 blockCount`、每块
`u16 partyId` 和 `u8 type`；`0x0117210B..0x01172111` 只让 type 0/1 进入信息块。

信息块窗口 `0x01172117..0x011722C7`：

- 先读 `info0`；当它为 0，`0x01172152..0x01172174` 进入 raw-dstr 读取分支；
- 随后机械读取 `info1..info11`：`u8, u8, u32, u8, u8, u8, u16`；
- `0x0117229C..0x011722A2` 把第 10 个信息字节（规范 `[9]`）保存到队伍对象 `+0x60`；
- `0x011722B1..0x011722C7` 以 `u16` 读取规范 `[10..11]` 并保存到对象 `+0x64`；
- type 1 跳过 type 0/2 的八槽名册块；type `<=2` 仍需末尾 `hasExtra`，0 表示没有后续
  `u32` 对扩展记录。

上游 `PartyInfoNotiBuilder.cs:8-13,60-108` 与该 parser 一致：type 0/1 写信息块，
`info0==0` 时当前写一个 4 字节长度 0 的空 raw-dstr，type 0/2 才写名册，最后对 type `<=2`
写 `hasExtra=0`。所以当前无标题扩展时 type-1 body 为：

```text
u16 blockCount=1
u16 partyId
u8  type=1
u8  info0
[if info0==0: u32 rawDstrLength=0]
bytes info1..info11
u8  hasExtra=0
```

`info0==0` 时总长 22；`info0!=0` 时总长 18。后续实帧已确认 18 字节分支；`info0==0` 的 22 字节分支
以及标题内容究竟应在 S2C raw-dstr 中回显还是保持上游当前空串选择，仍缺当前 S2C 实帧。

## 类别与目标语义

- `0x02534ED2..0x02534F11`：对象 `+0x60 == 0` 取 dstr 48600（Story），`==1` 取
  48601（Cycle），其他值走标题解析；结合自动路径只接受 0..2 且默认 2，确定 `2=Normal`。
- `0x0240E0E1..0x0240E17C`：Story 分支多次把对象 `+0x64` 交给剧情/进度查找和显示。
- `0x0240E20B..0x0240E27C`：Cycle 分支把对象 `+0x64` 交给循环区域条目查找并显示其标签。
- `0x02595B62..0x02595B8E`：类别 0 时使用 48613 拒绝委任队长。
- `0x02720E04..0x02720E40`：进入失败时，类别不是 2 使用 48612“模式不符”，类别 2
  使用普通失败文本。

据此把 `[10..11]` 命名为 `ModeTargetRaw` 是安全的；把它提前命名成具体 QuestId、AreaId 或
DungeonId 还不安全。Story/Cycle 的具体目标命名和规则属于后继规格。

## 冲突、可实施结论与剩余未知

旧 `0x000C.md` 机器区的“7 字节、titleIndex 在 offset 0、后跟 dstring”是旧上游注释与不完整
提取的混合推断，不能约束当前 A21。当前客户端四条构包路径、当前 NOTI parser 与固定上游捕获数组
一致，故人工区以本页的 12B / `16+n` 双形状为准；机器区历史内容保留，等待提取器另行修复。

后续实现可以安全地：

1. 只接受上述两个精确长度公式，先完成全部边界检查再构造请求；
2. 验证合法 UTF-8、容量 1..4、`raw[2] == capacity`、类别只为 0/1/2；
3. 把标题与 12 字节块分离；`[1]` 按当前产品决策归一化为 0，其余未知 `[0]`、`[3..8]` 原样保存；
4. 服务端默认构造 `Normal(2), target=0xFFFF`，并用 type-1 投影同一不可变规范块；这与上文客户端自动创建路径在无合法现有队伍时回落到 target `0` 的静态观察是两个不同层次。

仍未知、不得提前实现为事实：

- 无队 create、inline-title edit 的当前 A21 C2S body，以及 `info0==0` 的 S2C type-1 body；
- `raw[0]`、`raw[3..8]` 的完整业务枚举；
- Story/Cycle `ModeTargetRaw` 的精确资料表身份；
- type-1 非空标题回显、`hasExtra!=0` 的扩展形状；
- `0x000C` 成功/失败响应 body 与发送条件。
