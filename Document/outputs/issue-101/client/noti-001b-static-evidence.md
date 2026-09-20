# A21 `NOTI 0x001B` 解析与选图模式链

日期：2026-09-20。范围仅限 A21 静态客户端、T01 已归档 wire，以及一次由主线程记录的只读 `ReadProcessMemory` 快照。没有启动或操作客户端，没有修改游戏、数据库、生产代码或共享协议文档。

## 结论

T01 的 37 字节 `NOTI 0x001B` 正文前 10 字节不是“region/mode 全为 0”：

- offset 0、4 是两个独立 `u32`，写入两组受保护的全局状态；业务名仍未知。
- offset 8、9 是两个独立 `u8` 计数，各自控制一段 `count × u16` 列表。T01 的两个 0 只表示这两段列表为空。
- offset 10 才是外层/member 数。每个 member 为 `u16 id + u8 nestedCount + nestedCount × (u32,u32,u32)`；生产 encoder 的 `u8 0` 只是“嵌套列表为空”的一种实例，不是固定保留字。

客户端进入选图 UI 时，`setCurrentModeMap(1)` 的 1 来自客户端自身的常量分支，并非从 `NOTI 0x001B` 前 10 字节解码而来。当前证据因此不支持“服务端把 001B 的 region/mode 写成 0，导致漂流洞穴列表为空”。它也不证明服务端当前 37 字节各字段的业务值都正确；多数尾部字段仍只有宽度，没有可靠语义名。

## 输入身份

| 输入 | SHA-256 / 身份 |
| --- | --- |
| A21 `DNF.exe` | `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` |
| `DNF-memory-rva.bin` | `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507` |
| dump manifest | `DFE449F82311AF3FF320F56AAA0A9311DFD6108CE6F19F405641049EFAD99D1A`；preferred image base `0x00400000`；holes=0 |
| T01 body | `00000000000000000000010100000000000001000000000000000000000000000000000000`，37 字节 |

RVA/VA 均绑定上述映像；`VA = RVA + 0x00400000`。

## A21 parser 的 37 字节布局

handler 入口为 VA `0x01190190` / RVA `0x00D90190`。下表按 T01 中所有可变长计数均为 0、且只有一个 member 的实际形状列偏移。

| offset | 宽度 | A21 读取点（RVA） | 已确认结构 |
| ---: | ---: | --- | --- |
| 0 | 4 | `0x00D90202` | `u32`；随后写受保护全局对 VA `0x03A5CEC0/0x03A5CEC4` |
| 4 | 4 | `0x00D90358` | `u32`；随后写受保护全局对 VA `0x03A5CEC8/0x03A5CECC` |
| 8 | 1 | `0x00D90470` | `u8 countA` |
| 9 | 1 | `0x00D904EF` | `u8 countB`（仅当 countA=0 时位于 offset 9） |
| 10 | 1 | `0x00D9097F` | `u8 memberCount` |
| 11 | 2 | `0x00D909B7` | member 的 `u16 id` |
| 13 | 1 | `0x00D909C5` | member 的 `u8 nestedCount` |
| 14 | 4 | `0x00D90B61` | 尾部 `u32`（nestedCount=0 时） |
| 18 | 2 | `0x00D90B90` | 尾部 `u16` |
| 20 | 2 | `0x00D90BC0` | 尾部 `u16` |
| 22 | 4 | `0x00D90BF0` | 尾部 `u32` |
| 26 | 1 | `0x00D90C34` | `u8 optionalFlag`；非 0 时再读 `u8 count` 与 `count × (u32,u16,u16)` |
| 27 | 1 | `0x00D90CD0` | 尾部 `u8` |
| 28 | 4 | `0x00D90DD5` | 尾部 `u32` |
| 32 | 4 | `0x00D90E04` | 尾部 `u32` |
| 36 | 1 | `0x00D90E35` | 尾部 `u8` |

两段和 member 内部的可变长结构由直接循环证明：

```text
RVA 00D90470  call ReadU8    ; countA
RVA 00D90488  call ReadU16   ; repeat countA
RVA 00D904EF  call ReadU8    ; countB
RVA 00D90528  call ReadU16   ; repeat countB

RVA 00D9097F  call ReadU8    ; memberCount
RVA 00D909B7  call ReadU16   ; member.id
RVA 00D909C5  call ReadU8    ; member.nestedCount
RVA 00D90A49  call ReadU32   ; nested.a
RVA 00D90A55  call ReadU32   ; nested.b
RVA 00D90A61  call ReadU32   ; nested.c
```

读取原语已用多个调用点的直接指令形状交叉确认：ReadU8 RVA `0x023C5230`、ReadU16 RVA `0x023C52A0`、ReadU32 RVA `0x023C5310`。证据级别：`Confirmed / A21 static`。

offset 0/4 的两个全局对也由其他 handler 使用；至少 offset 0 对还在 `NOTI 0x0023` 路径中被写。因此只能确认“共享受保护状态”，不能把它们命名为区域或模式。证据级别：结构为 `Confirmed`，业务名为 `Unknown`。

## 与进入选图 UI 的关系

001B handler 一开始就取得选图模块并调用 RVA `0x023184A0` 重置/准备它：

```text
RVA 00D901A8  call 0x00D02C30
RVA 00D901AF  call 0x024D6D00
RVA 00D901B6  call 0x023184A0
```

这证明 001B 与选图模块生命周期相连，但没有证明 offset 0/4 是该模块的 region/mode。

用于只读定位模块的完整链为：

```text
root         = RPM32(VA 0x03A5B348)
selectModule = RPM32(root + 0xB4)
```

依据：单例 getter RVA `0x01237C60` 在 RVA `0x01237CE3` 执行 `mov eax,[0x03A5B348]`；accessor RVA `0x024D6D00` 只有 `mov eax,[ecx+0xB4]; ret`。

进入 UI 的相关函数入口为 RVA `0x02338990`。T01 活跃快照命中 `[selectModule+0x8C] != 1` 的分支；该分支从状态对象的单元素列表取得 23，并在 RVA `0x02338B56` 传给 VMProtect 入口 RVA `0x053558DF`。PVF `worldmap/worldmap.lst` 给出 `23 = DriftCave.wdm`。随后未虚拟化的普通路径在 RVA `0x02339D06` 重新读取 `[selectModule+0x44C]=23`，通过真实 WDM manager 绝对 VA `0x03A5C934` 取得 `driftcave.wdm`，并在 RVA `0x02339D21` 调用普通列表构建器 RVA `0x0231D490`。

这里纠正先前记录：绝对 VA `0x03A5C96C` 运行时 type=40，缓存的唯一资源为 `advancealtar.stm`。它及 RVA `0x02330A50` 属于特殊祭坛列表路径，不能拿来说明普通 DriftCave WDM 构建。`setCurrentModeMap(1)` 也只在 `[selectModule+0x8C]==1` 的另一分支中出现，不是 T01 当前普通分支。

T01 空列表的直接原因已经在 [`ordinary-dungeon-filter-evidence.md`](ordinary-dungeon-filter-evidence.md) 闭合：真实 WDM 十项均已加载；普通构建器遍历 definition `+0x168` 的 quest list，并同时查询当前任务容器与已完成任务树。当前任务只有 2489，已完成只有 101、1016、1776..1782，都不命中 150/151/152/153/155 的漂流洞穴主线任务，故这些地下城在 RVA `0x0231D748` 跳到下一条 WDM 记录。该结论与 `NOTI 0x001B` 前十字节无关。

证据级别：001B 布局与普通路径控制流为 `Confirmed / A21 static`；T01 容器值为 `Observed / read-only RPM`。

## 对当前生产 encoder 的约束

生产 `DungeonSelectionSlice.cs:448-495` 生成的单人 37 字节实例与 T01 正文长度、偏移完全吻合，但现有注释中的部分业务语义超过证据：

- offset 8 可确认是 `u8 + count×u16`，但“hell-quest blocked slots”尚未由本次 A21 consumer 证明。
- offset 9 不是普通固定 0；它是第二个 `u8 + count×u16` 的 count。
- 每个 member 后的 `u8 0` 是 nestedCount=0；非零时每项为 12 字节。
- offset 18 的 `u16 1` 被 A21 读取，但本次没有闭合它的业务含义。

这属于协议注释/可扩展编码能力的差异，不足以说明 T01 的零值导致空列表。按任务边界，本报告不修改生产代码。

## 剩余未知

- offset 0、4 和尾部 offset 14..36 的可靠业务名。
- `selectModule+0x8C` 的完整枚举名和所有写入来源。
- 8007 已通过任务门但没有形成用户可见普通按钮的后续展示规则。

这些未知不再阻塞 T01 中 150/151/152/153/155 被任务门剔除的结论。
