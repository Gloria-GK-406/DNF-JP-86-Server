# A21 客户端地图：字符串是加密的，解开之后客户端自报源码树

更新时间：2026-08-25

## 结论摘要

用 153 个 agent 的集群给 46MB `.text` 分区命名，**这个做法失败了：55 个被对抗验证的标签里 49 个被推翻**。
但集群在干活过程中做出了一个远更有价值的东西：**它们逆出了这个客户端的字符串加密算法**。

解开之后：

| | |
| --- | ---: |
| 解密出的加密字面量 | **36,678** 条（此前明文扫描的 21,921 条大部分是密文被当噪声） |
| 携带字面量的函数 | 8,798 |
| 带 `__FILE__` 源码路径的函数 | 759，覆盖 **346 个不同源文件** |
| 由源文件归属的函数 | 37,050 / 67,074（55%） |

不需要猜子系统了——**客户端自己报出了它的源码树**，`D:\Work\1229\Source\DNFClient\RDAR\...`。

| 产出 | 位置 |
| --- | --- |
| 字面量解密器（集群逆出的 cipher） | `ClientHack/tools/literal_cipher.py` |
| 全镜像解密 + 源码归属 | `ClientHack/tools/decrypt_literals.py` |
| 函数 → 源文件 | `ClientHack/tools/func-source-file-a21.csv` |
| 解密字面量 | `ClientHack/tools/literals-a21.csv` |
| 帧解析函数全集 | `ClientHack/tools/frame-parsers-a21.csv` |

## 字符串加密算法

4 字节头编码长度与密钥种子，主体是 dword 链式 XOR，尾字节换第二个乘数：

```
s    = header[1] & 0xFE
w    = u16 at header+2
n    = ((s << 7) | s) ^ w          长度
seed = s | 0x9A714CA0              密钥种子

dword 逐个:  v = d ^ k ;  k = (k * 0x1003F + v) & 0xFFFFFFFF
尾字节:      c = b ^ (k & 0xFF) ;  k = (k * 0x107 + c) & 0xFFFFFFFF

mode 2 → UTF-16LE（占 74,474 次）   mode 1 → cp949（115 次）
另有明文变体，magic = 0x2CA1，直接 UTF-16LE
```

**它是自校验的。** 地址错了要么解码失败、要么可打印率过不了阈值，所以可以对
`.text` 里**每一个**指向数据段的立即数操作数无脑试解——回来的要么是真字面量，要么什么都没有。
258,706 个候选立即数，解出 36,678 条。

## 协议面：按源文件分好了

`.text` 里有 **459 个函数**调用入站流读原语（`read_u8/u16/u32/bytes`、`set_remaining`）。
由于客户端每一次 S→C 字段提取都必经这五个函数，这是**完整集合，不是采样**。
交叉源码归属之后：

| 源文件 | 解析函数数 |
| --- | ---: |
| `DNFClient\RDAR\CNRDUserInfoPacketHandler.cpp` | 32 |
| `DNFClient\RDAR\GameCoreNetwork_NOTI_Etc.cpp` | 19 |
| `DNFClient\RDAR\GameCoreNetwork_CMD_Town.cpp` | 16 |
| `DNFClient\RDAR\GameCoreNetwork_NOTI_Dungeon.cpp` | 11 |
| `DNFClient\RDAR\GameCoreNetwork_NOTI_Town.cpp` | 10 |
| `DNFClient\RDAR\GameCoreNetwork_CMD_Etc.cpp` | 9 |
| `DNFClient\RDAR\GameCoreNetwork_CMD_Dungeon.cpp` | 7 |
| `DNFClient\RDAR\CNEntryDungeonPartyManager.cpp` | 7 |

命名规律很清楚：**`CMD_*` 是 C→S 命令，`NOTI_*` 是 S→C 通知，按域（Town / Dungeon / Etc）分文件。**

读调用最密的几个（即最大的分派型处理器）：

```
0x01146000   287 次读调用   GameCoreNetwork_NOTI_Dungeon.cpp
0x01160280   195            GameCoreNetwork_NOTI_Etc.cpp
0x01120970   170            GameCoreNetwork_CMD_Town.cpp
0x01157A70   167            GameCoreNetwork_NOTI_Dungeon.cpp
0x0118C3A0   131            GameCoreNetwork_NOTI_Town.cpp
```

网络层的边界也定住了：`Library\Include\Nengine\ISocket.cpp`（8 个锚点）、
`DNFClient\RDAR\CNSelectChannelSocket.cpp`（8 个）、`DNFClient\RDAR\CNRDUdpModule.cpp`（7 个）。

## 翻译单元级切分

759 个锚点跨越 41MB，相邻锚点中位间距 10,640 字节。同名文件的连续锚点构成翻译单元段，
348 段中 84 段有 ≥3 个锚点。最大的几段：

```
0x01F7D4A0-0x02000B90   525 KB   DNFClient\RDAR\Character\CNMage.cpp
0x01837290-0x01898380   388 KB   DNFClient\RDAR\IRDCharacter.cpp
0x0169FB30-0x016DAD90   236 KB   DNFClient\RDAR\CNRDEquipment.cpp
0x01119280-0x01144220   171 KB   DNFClient\RDAR\GameCoreNetwork_CMD_Town.cpp
0x01160090-0x01187FF0   159 KB   DNFClient\RDAR\GameCoreNetwork_NOTI_Etc.cpp
0x01335D40-0x01367F70   200 KB   DNFClient\RDAR\CNRDBaseInterface.cpp
0x029663D0-0x02999F40   206 KB   DNFShared\GameScript\RDARScriptItem.cpp
```

归属的置信度分三档，**不要混用**：

| 档 | 函数数 | 含义 |
| --- | ---: | --- |
| `anchored` | 759 | 函数自己引用了 `__FILE__`，确定 |
| `interpolated` | 36,291 | 落在同名文件的两个锚点之间，可信 |
| `between-files` | 28,403 | 落在两个**不同**文件的锚点之间，**未归属** |
| `before-first` | 1,621 | 在第一个锚点之前 |

## 为什么"按地址分区命名"失败了

49/55 的标签被推翻，理由高度集中在两条，都是我的设计缺陷：

**一、"连续地址段 = 子系统"这个前提是错的。** 60 个区各 1118 个函数，一刀切会**切穿翻译单元边界**，
所以每个区都是"上一个模块的尾 + 下一个模块的头"。多个验证者独立给出同一句话：
*"link-order slice, not a module"*。正确的切分单位是**翻译单元**，而它只能由 `__FILE__` 锚点给出——
也就是必须先解密字符串。顺序反了。

**二、`DNFClient\RDAR` 没有基准率。** 全镜像 479 条源码路径全在这个前缀下，它是源码根目录，
不是任何区域的特征。验证者用我写进提示里的基准率纪律，打了标注者。

值得记的是**对抗验证起作用了**：验证者不是复读，它们**自己写工具重算**
（`decstr.py`、`bandec.py`、`r51_iat_resolve.py` 等），逐条复核引用地址，
其中一位就是在这个过程中逆出了 cipher。没有这一层，那 49 个错标签会进文档。

## 集群运行的实际代价

```
第一次   140 个 agent 中 67 个死于会话中断（[Request interrupted by user]）
第二次   140 个中 93 个死于服务端错误（529 Overloaded / 证书校验失败）
合计     6,250,368 subagent tokens，2,223 次工具调用，约 2 小时
实得     60/60 标注（全部落盘）、55 份验证结论、754 个帧解析函数描述、6/16 定向问题
```

**"返回前先写盘"这条指令救了整个阶段 A**：13 个 agent 的返回值被 API 错误吞掉，但它们
在死之前已经写了 `label-NN.md`，所以磁盘上是 60/60 完整的，而工作流返回值里只有 47。
以后任何大规模 agent 运行都应该带这条。

## 后续测量推翻的三条（2026-08-25）

**一、`functions.csv` 的 `span_bytes` 不是函数长度。** 它一路吃到下一行开头，包含尾部
`int3` 填充——**0/67,073 例外**。任何"某函数有 N 次读调用"式的结论都建立在这个 blob 上，
包括本文和协议文档里的那些。要精确到函数体，得先在 span 内切到第一段 `ÌÌÌÌ`。

**二、`.text` 里没有保护壳运行时区间。** 壳在 46.2MB 的 `.text` 里只占约 20KB（0.04%），
形态是散落在函数间 `int3` 填充里的 VMProtect VM-entry stub，彼此从不相邻，给不出"区间"。
壳的本体全在 `0x04F69000` 以上的追加节。我原先提的三个抓手全被基率击穿：

- "只被 `.tls`/`.-wI`/`.mackt` 触达的函数" = **空集**。而且 `.-wI` 根本不是调用方，
  它是 1654 条 API 导入包装——"调用 `.-wI`"只等于"调用 Windows API"。
- "无 API 可达且无字面量"的基率是 **74.1%**（49,727/67,074），是多数派，不是判据。
- 反向证据更强：29 个调用 `.tls` 的 `.text` 函数里有 11 个带游戏源码 `__FILE__`。

**三、`.tls` 不是被搬走的游戏代码。** 两种独立方法（立即数扫描 + 全对齐 dword 扫描）测下来
`__FILE__` 字面量为 **0**，`.bss`/`.rsrc`/`.vmp0` 同样为 0。它是打包进来的第三方保护/指纹运行时。
本文上一版说"不能推出它不是游戏代码"——方向是对的（不能从扫描失败反推），但结论反了。

## 未决

- 42% 的函数处于 `between-files`，未归属。**补法已经找到，而且不需要 agent**：
  x86 MSVC 的 C++ 异常数据不在 PE 异常目录里（`DataDirectory[3].rva = 0` 不代表没有），
  镜像里实测有 **22,880 个 `FuncInfo` 结构、37,510 个可验证函数入口**——是 759 个
  `__FILE__` 锚点的 **49 倍**。详见 `Docs/a21-next-measurements.md`。
- 类名、vtable RTTI、`.xui` 脚本名、断言消息这几类锚点也都还没用。
- ~~`.vmp1` 没有共享分派器~~ —— **已被推翻**，它确实是 VMProtect 虚拟机，
  只是 direct-threaded 所以本来就不该汇聚。见 `Docs/a21-vmp1-virtualization.md`。
- 146 个翻译单元只描述了 57 个（额度耗尽时停下）；40 份落在 `Docs/a21-units/`。
- 单元描述**没有经过对抗验证**（`UnitVerify` 阶段一个都没跑成），
  而上一轮 49/55 的推翻率说明这一层不能省。引用单元结论时要自己复核。

## 相关文档

- `Docs/themida-api-wrapper-recovery.md` —— 1654 个 API 包装的还原（本文的锚点来源之一）
- `Docs/differential-crossmodule-fixups.md` —— 重定位表
- `Docs/s4a21-port-status.md` —— A21 移植总体进度
