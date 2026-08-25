# A21 协议面：747 个包类型、分派机制、逐字段线格式

更新时间：2026-08-25

## 结论摘要

客户端的收包分派**不是跳转表，也不是函数指针数组**——是启动时用代码建起来的
`std::map<u16 类型, {处理函数, flag}>`，两张，一张给 C→S（CMD）一张给 S→C（NOTI）。
纯静态扫指针数组永远找不到它。

| | |
| --- | ---: |
| 已还原的包处理函数 | **764** |
| 已钉死类型码 | **747** |
| C→S（CMD）类型 | 351，最大 `0x4EB` |
| S→C（NOTI）类型 | 393，最大 `0x4B3` |
| 方向内类型码重复 | **0** |

结果表：`ClientHack/tools/packet-handlers-a21.csv`
（`source_file, handler_va, packet_type, purpose, wire_format`），
各源文件的完整分析在 `Docs/a21-protocol/proto-*.md`。

## 帧格式

接收路径 `CNGameCore::NetworkProc = 0x0163BBD0`：

```
+0x00  u8    方向选择器：0 → NOTI 表，1 → CMD 表
+0x01  u16   包类型                (0x0163BD63  movzx edx, word ptr [edi+1])
+0x03  u32   总长度                (0x0163BC16  mov ecx, [edi+3])
+0x0F  body  正文起点              (0x0163BD72  lea eax, [edi+0xF])

set_remaining(total_len - 0x0F)     (0x0163BEB2)
```

**CMD 与 NOTI 在正文起点上不对称**：CMD 路径框架自己先吃掉 1~2 个正文字节
（`read_u8` @ `0x0163BF30`，第二次 @ `0x0163BF73`），NOTI 路径不吃。
所以 NOTI 侧所有线格式从 body 偏移 0 开始，CMD 侧不是。

正文**不作为参数传递**，处理函数从全局游标读：

```
游标   [0x03BAAE38]        剩余   [0x03BAAE3C]
read_u8  0x027C5230   read_u16 0x027C52A0   read_u32 0x027C5310
read_bytes(dst,len) 0x027C5380     set_remaining 0x027C4350
```

## C→S 帧格式

从 `DNFClient\RDAR\CNGameSocket.cpp` 还原（S→C 的那一半在上一节）：

```
+0x00  u8    类常量 = 1
+0x01  u16   包类型
+0x03  u32   总长度（含头 = 正文长 + 填充 + 14）
+0x07  u32   校验和
+0x0B  u8    flag
+0x0C  u16   序号（明文）
+0x0E  body  正文起点
```

**C→S 头是 14 字节，S→C 是 15 字节**，两个方向不对称，不要互相套用。

## 分派机制

```
注册原语   0x0163C4A0   thiscall Register(map, u16 type, void* fn, int flag)
                        new(8) → node[0]=fn, node[4]=flag，按 type 插入
                        全镜像 943 个直接调用点
查找       0x0163C510   Find(type)

CMD 表   singleton getter 0x0163C410   实例 [0x03A5BE68]   填充 0x0163C190
NOTI 表  singleton getter 0x0163C550   实例 [0x03A5BE74]   填充 0x0163C4F0

启动      0x0163A850 → 两个填充函数 → 各自 tail-chain 到每个源文件的注册器
```

每个源文件自带一个注册器函数，位于该 `.obj` 的末尾：

| 注册器 | 源文件 | 注册数 |
| --- | --- | ---: |
| `0x01144AF0` | `GameCoreNetwork_CMD_Town.cpp` | 269 |
| `0x0110C2C0` | `GameCoreNetwork_CMD_Dungeon.cpp` | 47 |
| `0x01118BC0` / `0x01118180` | `GameCoreNetwork_CMD_Etc.cpp` | 85 |
| `0x0119A780` | `GameCoreNetwork_NOTI_Town.cpp` | 156 |
| `0x0115F540` | `GameCoreNetwork_NOTI_Dungeon.cpp` | 156 |
| `0x011885F0` | `GameCoreNetwork_NOTI_Etc.cpp` | 228 |

注册点是固定的 5 指令形状，可以机器提取：

```
push 0                  ; flag
push <handler VA>
push <type code>
mov  ecx, esi           ; this = map
call 0x0163C4A0
```

步长 `0x10`（imm8 类型码）或 `0x13`（imm32）。

## 处理函数 ABI

```
NOTI  0x016377E0 分派: mov ecx,[eax+4] / mov edx,[eax] / push ecx / push esi / call edx
                 → void __cdecl f(int type, int flag)，add esp,8
                 156 个 NOTI_Dungeon 处理函数的 flag 全为 0
CMD   0x01637680 分派: f(type, a, b, flag)，add esp,0x10
未注册的类型被静默丢弃。
```

## 枚举名表

`0x03B51F20` 是一张 2047 项的 `ENUM_NOTIPACKET_*` 名称表，按类型码索引（`[code*4]`），
由 `push <密文>; call 0x27BACC0; mov [0x3B51F20+i*4], eax` 建起。
CMD 侧对应表在 `0x03B53230`。

**交叉验证过**：类型码只取自注册器，名字只取自 14 个处理函数自己打日志时压入的
`NOTIFUNC_ENUM_NOTIPACKET_*` 字面量——**14/14 与 `0x03B51F20[code]` 一致**。

枚举上界由 `CheckPacketEndIndex = 0x01160090` 给出：
`ENUM_CMDPACKET_END = 0x4F7`，`ENUM_NOTIPACKET_END = 0x4C2`。
实测最大值分别是 `0x4EB` 和 `0x4B3`，都在界内。

## 已知的空洞

- **27 个 NOTI 类型接到裸 `ret`（`0x02231720`）**——收下即丢弃，零正文：
  `0x019 0x043 0x06F 0x074 0x079 0x09A 0x0A7 0x0B2 0x0C1 0x0C6 0x0CC 0x0E5
  0x102 0x10E 0x10F 0x110 0x116 0x11E 0x11F 0x120 0x12C 0x13C 0x15A 0x1A4
  0x1A5 0x1CA 0x314`
- 另有 302 个**运行期动态**注册点（`RegisterNotiHandler = 0x01637720`），
  其中 125 个静态解不出来，所以那部分的类型码碰撞未被完全排除。
- `0x03B53230` / `0x03B51F20` 两张名表在本 dump 里是全零——运行期才填充。
  想拿到符号名需要一次运行期抓取，或者从别处解密。

## 一个我犯的错

我在给分析 agent 的提示里把读原语的**RVA 当成 VA** 写了（`0x023C5230` 而非
`0x027C5230`，差一个 image base）。多个 agent 独立发现并自行修正，还在结论里注明了。
`parsers.csv` 本身是对的（生成时加了 base），只有提示文字错。
教训：**给 agent 的地址常量要标明是 RVA 还是 VA**。

## 相关文档

- `Docs/a21-client-cartography.md` —— 字符串解密与源码树归属（本文锚点的来源）
- `Docs/themida-api-wrapper-recovery.md` —— API 包装层还原
- `Docs/a21-protocol/proto-*.md` —— 每个源文件的完整分析
