# `.vmp1` 个案（续）：协议崩溃的真正落点 —— 101 字节物品记录与 `rep movsd` 栈溢出

日期：2026-09-07。转储：`DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`
（固定基址 `0x00400000`）。承接 [`vmp1-8.md`](vmp1-8.md)。

`vmp1-8.md` 已证：虚拟化的 `NetworkProc`（`0x0163BBD0`）只是路由器，`0xAF000` 校验安全，
崩溃在下游**未虚拟化**的 NOTI 处理器。本篇把那个处理器挖到底，回答另一 agent 的两个问题：

1. 客户端在哪里读时装的珠宝(emblem)槽与颜色？→ **`rec+0x1A` 计数的子数组**。
2. 101 字节记录的字段布局，尤其"末尾补零的 17 字节"到底装什么？→ **不存在补零，记录被用满到 `rec+0x64`**。

并给出止崩点：**`0x0117D204` / `0x0117E04F` 的 `rep movsd`**。

---

## 一、崩溃处理器的身份（已核实）

另一 agent 观测到的 fastfail 返回点是 `0x0117D6AD`。它是**函数 `0x0117C670` 的 `/GS` 尾声**：

```
0117D6A3  pop ebx / 0117D6A6 xor ecx,ebp / 0117D6A8 call __security_check_cookie(0x2C627F3)
0117D6AD  mov esp,ebp / pop ebp / ret          ← 观测到的崩溃返回地址
```

`0x0117C670` 是 **NOTI 类型 `0x0D` 的处理器**，身份由两条硬证据坐实：

- **注册表**：`0x011886C0` 起是 `RegisterNoti(type, handler, flag)` 批量注册序列
  （registrar = `0x0163C4A0`，`ret 0xC`）。其中 `0118871C push 0xD; 01188717 push 0x117C670` →
  **`RegisterNoti(0x0D, 0x0117C670)`**。同序列还给出 `0x0E → 0x0117D720`、`0x0F → 0x0117F820`。
- **解密字面量**（`0x0117C670` 内，`literal_cipher.py` 方案）：
  `NOTIFUNC_ENUM_NOTIPACKET_ITEM_LIST`、`Item Info List(Count : %d)`、
  `D:\Work\1229\Source\DNFClient\RDAR\GameCoreNetwork_NOTI_Etc.cpp`，以及一整串**逐字段调试打印**
  （见第三节），等于官方字段名表。

> 结论：崩溃发生在 **`NOTI_ITEM_LIST`（type 0x0D）** 处理器。`vmp1-8.md` 里 728 字节帧的
> `0x0117D720` 是**同一段代码的孪生 bug**（type 0x0E，见第五节），但另一 agent 这次抓到的返回点
> `0x0117D6AD` 精确属于 `0x0117C670`（帧 `sub esp,0x240` = 576 字节）。

---

## 二、致命指令：无上界的 `rep movsd`（已核实字节）

`0x0117C670` 里唯一的 `rep movsd`（全函数扫描确认唯一）：

```
0117D1F0  movsx ecx, byte ptr [ebp-0x14d]   ; ecx = (有符号) rec+0x2F         ← 包里的字节，攻击者可控
0117D1F7  cmp   ecx, ebx                     ; ebx = 0
0117D1F9  jle   0117D206                     ; 只挡了 <= 0，【没有上界】
0117D1FB  lea   esi, [ebp-0x14c]             ; 源 = rec+0x30
0117D201  lea   edi, [ebp-0x38]              ; 目标 = 固定栈局部 [ebp-0x38]
0117D204  rep movsd                          ; 拷 ecx 个 dword
```

- **计数**：`rec+0x2F` 一个**有符号字节**，`movsx` 后范围决定循环次数。`jle` 只排除 `<=0`，
  于是 `1..127` 全部放行，最多 **127 dword = 508 字节**。
- **目标容量**：cookie 在 `[ebp-0x10]`，目标在 `[ebp-0x38]`，二者相距 `0x28 = 40 字节`。
  写入超过 **10 个 dword** 即覆盖 `/GS` cookie 与返回地址 → `__security_check_cookie` 在
  `0x0117D6AD` fastfail。**`0xC0000409` 由此而来。**
- **源**：`rec+0x30` 起，但整条记录只有 101 字节（`0x30..0x64` 仅 52 字节 = 13 dword）。
  `count=127` 时既**越读**记录尾部（信息泄露），又**越写**目标栈（/GS 崩溃）。

> 被检查的界（`NetworkProc` 的 `0xAF000`）保护的是重组缓冲；要命的界是这里的 **40 字节固定栈局部**，
> 二者无关——这正是 `vmp1-8.md` 的判断，现在有了确切指令与阈值。

---

## 三、101 字节物品记录的字段布局（已核实：来自逐字段调试打印）

记录由 `read_bytes(buf, 0x65)`（`call 0x27C5380`，`push 0x65`）**整块读入** `[ebp-0x17c]`，
随后按固定偏移取字段。`0x0117C670` 里的调试打印按顺序拼出字段名（格式串已解密），
把打印源操作数回绑到记录偏移，得到权威字段表：

| 偏移 | 宽度 | 字段名（解密自调试串） | 证据指令 |
| --- | --- | --- | --- |
| `+0x00` | i16 | **SlotIndex** | `movsx …,word[ebp-0x17c]` @0117CC9D；`SlotIndex(%d)` |
| `+0x02` | u32 | 物品表索引 itemIndex | `mov …,[ebp-0x17a]` 多处 |
| `+0x06` | i32 | **Data** | `, Data(%d)` @0117CD0F |
| `+0x0A` | u8 | **ext_data1** | `, ext_data1(%d)` @0117CD70 |
| `+0x0B` | u16 | **Durability** | `, Durability(%d)` @0117CDD0 |
| `+0x0D` | u8 | **isSealed** | `, isSealed(%d)` @0117CE30 |
| `+0x0E` | i32 | **enchantIndex** | `, enchantIndex(%d)` @0117CE8F |
| `+0x12` | u8 | **enchantUpgrade** | `, enchantUpgrade(%d)` @0117CEF0 |
| `+0x13` | u8 | **amplify_type** | `, amplify_type(%d)` @0117CF50 |
| `+0x14` | u16 | **amplify_value** | `, amplify_value(%d)` @0117CFB0 |
| `+0x1A` | i8 | **珠宝/纹章(emblem)条目数** | 见第四节（`movsx …,[ebp-0x162]`）|
| `+0x1B` | — | 珠宝条目数组区起点 | 见第四节 |
| `+0x2B` | i32 | **period** | `, period(%d)` @0117D18E |
| `+0x2F` | i8 | **附加选项条目数**（← 致命计数）| 第二节 |
| `+0x30` | — | 附加选项 dword 数组区起点 | 第二节 |
| `+0x4A` | i8 | **genuineUpgrade** | `, genuineUpgrade(%d)` @0117D234 |
| `+0x4B` | i8 | **emancipateEquipmentLevel** | `, emancipateEquipmentLevel(%d)` @0117D291 |
| `+0x4C` | i8 | **tradeRestriction** | `, tradeRestriction(%d)` @0117D2F0 |
| `…+0x64` | — | 记录被用满至 `rec+0x64` | 字段访问扫描（`rec+0x61` dword）|

> **"末尾补零 17 字节"的假设不成立**：静态扫 `0x0117D720` 对记录的访问，偏移一直用到
> `rec+0x61`（dword，占 `0x61..0x64`）。会话里那些字节恒为零，只是因为该会话没有带这些值的物品，
> 不是补零。字段实际是 `genuineUpgrade / emancipateEquipmentLevel / tradeRestriction / …` 等。

字段随后喂给 **`ItemFactory::createItem`**（`0x1A85220`，解密串 `createItem` /
`fail create item! item index : %d` / `ItemFactory.cpp`）构造 `CItem`。

---

## 四、时装珠宝(emblem)槽与颜色在哪里读（回答问题 1，已核实）

记录里 **`rec+0x1A` 计数**驱动的子数组就是珠宝/纹章条目。以 `0x0117D720`（type 0x0E，
偏移读得更干净）为例，循环 `0x0117DE20`：

```
count = (i8) rec+0x1A                              ; 0117DE0B cmp [ebp-0x1fe],al ; jle
for (ebx = 0; ebx < count; ebx++) {
   dword  v  = *(u32*)(rec+0x1B + ebx*4);          ; 0117DE30 mov edi,[ebp+ebx*4-0x1fd]   ← 珠宝/物品索引
   byte   a  = rec+0x23 + ebx;                      ; 0117DE20 movzx edx,[ebp+ebx-0x1f5]
   byte   b  = rec+0x25 + ebx;                      ; 0117DE28 movzx ecx,[ebp+ebx-0x1f3]
   byte   c  = rec+0x27 + ebx;                      ; 0117DE47 movzx edx,[ebp+ebx-0x1f1]
   byte   d  = rec+0x29 + ebx;                      ; 0117DE55 movzx ecx,[ebp+ebx-0x1ef]
   entry8 = { v, a, b, c, d… };                     ; 组装 8 字节条目 [ebp-0x264]
   vector.push_back(entry8);                        ; 0117DED6/0117DF4A call 0x160dd40（增长）
}
```

- 这是**珠宝(emblem)槽列表**：每条目 = 一个 dword 索引（`rec+0x1B` 起，stride 4）+ 若干属性字节
  （`rec+0x23/0x25/0x27/0x29` 起，即"颜色/类型/槽位"等）。`rec+0x1A..0x37` 这 30 字节正是另一 agent
  说的"30 字节消费方"。
- 消费者是 `NOTI_ITEM_LIST(0x0D)` 与 `NOTI_GET_ITEM` 等**同读 101 字节物品记录**的处理器
  （全量扫描：9 个 `read_bytes(buf,101)` 站点，见附录）。装配好的 8 字节条目向量经物品对象的
  虚表槽 `vtbl[0x114]`（`call [item][0x114]`，@0117D558 / @0117E364）交给 `CItem`。

> 判读（推断）：`rec+0x1A` 列表 = 装备/时装上的**镶嵌孔（emblem/jewel）**，dword 为镶嵌物索引，
> 属性字节为孔位与颜色。这一路走 `push_back`（容器自增长），**不是崩溃点**；崩溃在 `rec+0x2F` 那路。

---

## 五、孪生 bug 与止崩（已核实结构 + 可执行建议）

**同一记录、同一无界 `rep movsd`，出现在多个物品记录处理器**：

| NOTI | 处理器 | 计数字段 | 目标缓冲 | 目标→cookie 距离 | 溢出阈值 |
| --- | --- | --- | --- | --- | --- |
| `0x0D` ITEM_LIST | `0x0117C670` | `rec+0x2F`(i8) @0117D1F0 | `[ebp-0x38]` | `0x28`=40B | >10 dword |
| `0x0E` | `0x0117D720` | `rec+0x2F`(i8) @0117E038 | `[ebp-0xd4]` | `0xC4`=196B | >49 dword |

两者计数都是 `movsx` 有符号字节、都只 `jle` 挡 `<=0`、都无上界，`1..127` 均可打崩。
另一 agent 抓到的返回点 `0x0117D6AD` = `0x0D` 分支。

**止崩（治本）**：在两处 `rep movsd` 前，把计数夹到目标容量与记录余量的较小值。例如对 `0x0D`：

```
count = (i8) rec+0x2F;
if (count < 0) count = 0;
if (count > 8)  count = 8;         // 40B/4 = 10，留边界取 8；或按记录余量 (0x64-0x30)/4 = 13
rep movsd count;
```

同法处理 `rec+0x1A` 那路（虽走增长容器不崩，但会越读记录尾、灌入伪造珠宝数据），
按 `(0x37-0x1B)/…` 夹紧即可。

**定位是哪个 type / 多长打崩（治标 + 取证）**：`vmp1-8.md` 第五节的探针依然有效——
在 `NetworkProc` 的 `0x0163C11F`（`mov ecx,[eax*4+0x3B51F20]`，`eax = word[edi+1]`）记录 type 与
`messagesize-0xF`；本篇进一步指出，只要 type ∈ {读 101 字节记录的那些}（附录 9 个），
且记录内 `rec+0x2F`（净体第 0x2F 字节）> 目标阈值，即触发。最省事的活体断点直接下在
`0x0117D1F0` / `0x0117E038`，读 `ecx`（= 攻击者计数）与 `rep movsd` 目标即可。

---

## 附录：读 101 字节物品记录的全部站点（`read_bytes(buf,0x65)` → `0x27C5380`）

扫描全 `.text` 得 9 处，均是 `push 0x65; push reg; call 0x27C5380`：

| read 站点 | 所属处理器 / 上下文（解密串） |
| --- | --- |
| `0x0117CC51` | `NOTI_ITEM_LIST`(0x0D) `0x0117C670` |
| `0x0117D893` | NOTI 0x0E `0x0117D720`（孪生 bug）|
| `0x0117F85F` | `NOTI_DROP_ITEM`(0x0F) `0x0117F820` |
| `0x0117FEDC` | `NOTI_DROP_ITEM` 续 |
| `0x011869D6` | `NOTI_GET_ITEM` `0x01186600` 区 |
| `0x01198455` | `NOTI_AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM`（`GameCoreNetwork_NOTI_Town.cpp`）|
| `0x0113581B` | `CMD_BUY_ITEM`（`GameCoreNetwork_CMD_Town.cpp`）|
| `0x0113B0B6` | CMD 区（Town）|
| `0x01143852` | 注册/派发区 |

这些是同一 101 字节物品记录格式的其它消费方；`rec+0x2F` / `rec+0x1A` 的夹紧应在**共享的记录解析**
层面统一做，否则要逐个 patch。

## 方法备注

- 反汇编 `scratchpad/gs/xdis.py`；字段/计数/`rep` 扫描 `fields.py` `scan.py`；
  注册表抽取 `regtab.py`（按 `RegisterNoti(0x0163C4A0)` 调用点回溯三个 `push`，分块去重）；
  记录读点全扫 `find101.py`；字面量解密 `lit.py`（`literal_cipher.py` 方案，对本转储适用）。
- 全部字段名来自加密调试字符串解密，属**已核实**；珠宝条目的字节语义（颜色/槽位）为**推断**，
  待另一 agent 用活体数据核对。
