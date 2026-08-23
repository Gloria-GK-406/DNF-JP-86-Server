# S4A21 移植状态

目标：把 S4A12 上验证过的「dump → 重建可调试 exe」流水线搬到 S4A21。
结论先行：**宿主侧已打通，A21 的第一份 dump 已采到。**

## 为什么客户端是编译期选择

`include/clienthack/client_profile.h` 用 `CLIENTHACK_CLIENT_A21` 在编译期选定一整组
客户端专属常量，两个客户端各产出一份 `Az.dll`，分别放进各自的目录。

不做运行时探测是有意的：**启动门要在一个硬编码地址上改 5 个字节**。地址选错会在任何
日志出现之前就把客户端写坏。这种错误应该是编译错误，而不是运行时惊喜。

受影响的构建目标：

| 目标 | 产物 | 说明 |
| --- | --- | --- |
| `Az` / `Az_a21` | `stage/Az.dll` / `stage-a21/Az.dll` | 宿主 |
| `clienthack_core` / `clienthack_core_a21` | 静态库 | 必需插件名单编译进了它，A21 不能链 A12 那份 |
| `plugin_memory_dump` / `plugin_memory_dump_a21` | `diagnostics*/plugins/0000_MemoryDump.dll` | dump 插件 |

## A21 常量与出处

| 项 | 值 | 出处 |
| --- | --- | --- |
| SizeOfImage | `0x0746E000` | PE 头 |
| AEP | `0x07213FBC` | PE 头（位于第 13 节，与 A12 同构） |
| TimeDateStamp | `0x58458972`（2016-12-05） | PE 头 |
| SectionCount | 15 | PE 头 |
| cookie RVA | `0x028865DE` | 上游 `S4A21ClientPatch/az/az.cpp` 的 `#define XCookie 0x02C865DE`（VA） |
| cookie 前缀字节 | `8B FF 55 8B EC` | 与 A12 **完全相同**，上游 patch 的也是这五个字节 |
| SHA-256 | `1F8244D3…B20B00DD` | 脚本生成并回读校验（手工拆字节曾拆错一位） |
| IAT 表 | 420 条 | 上游 `S4A21ClientPatch/az/iat_table.h`，结构与 A12 的 406 条一一对应 |

`protected_call_rva` 在 A21 上**未测量**，profile 里不定义，manifest 里报 0——
宁可承认缺口，也不能让 manifest 声称一个从未检查过的地址。

## 两处 A12 专属逻辑的处理

**必需插件名单**：A21 为空。插件都在硬编码地址上比对字节签名，把 A12 的插件加载到
A21 只会让它自检失败——要求它存在，等于把「尚未移植」变成一次无法解释的拒绝启动。
名单随每个插件移植完成、并在该客户端上验证后逐条添加。

**IAT 数据导出特例**：A12 的索引 341 是 msvcp60 数据导出，需按模块 RVA 归一化。
A21 的表只导入 kernel32 / msvcr71 / ws2_32，**没有 msvcp60 槽**，因此该 profile 不定义
`CH_IAT_GATE_DATA_EXPORT_INDEX`，读取它的代码必须用 `#if defined(...)` 保护。

## 首次 bring-up 结果

```
[gate]  DNF.exe SHA-256 calculated
[host]  startup gate installed
[host]  startup gate intercepted the client thread     <- cookie 地址正确
[iat]   IAT policy accepted: 420 repaired, 0 already resolved, 0 retained
[host]  feature runtime signatures verified
[MemoryDump] analysis-only memory dump COMPLETE marker written
[host]  client thread released after startup initialization
```

dump `dnf-dump-20260822-184128-028-16280`：

| | |
| --- | ---: |
| bytes_written | 122,085,376（= SizeOfImage） |
| hole_bytes / failed_blocks | 0 / 0 |
| iat_total / iat_matches | 420 / 420 |
| loaded_modules | 74 |

## B1 exe 已跑通（2026-08-22 23:44 重启后）

跨重启的 dump 对到手后，A21 的 B1 exe 一次成型：

```
[gate]  DNF.exe SHA-256 calculated
[host]  startup gate intercepted the client thread
[host]  feature runtime signatures verified
[loader] plugins directory is missing or empty
[host]  ordered plugin initialization completed
[host]  client thread released after startup initialization   ← 入口桩交接成功
```

没有任何 first-chance 记录，客户端一路跑到它**自己**的 `Run Error` 对话框——即打包版
A21 在同样没有服务端时显示的那一个。

### CRT 续点是量出来的，不是逆出来的

`CRT_CONTINUATION_VA = 0x05D06329`。启动门拦在 `__security_init_cookie` 上时，客户端栈顶
的返回地址就是 CRT 要续的地方，宿主的 startup trace 早就把它记成 `client_return`：

```
CLIENTHACK_STARTUP_TRACE=1 → dumps\startup-stack-*.json
   client_return  0x05D06329   （落在 .-wI，与 A12 的 0x04ED6E75 落在 .,_W 同为 11 号段）
   edi            0x07613FBC   （= AEP，证实 Strategy C 依赖的不变量在 A21 同样成立）
   esi            0x00000000   （与桩里的 xor esi,esi 一致）
```

### 重定位表：532 条，17 条未决

`tools/crossmodule-relocs-a21.csv`，由 18:41（重启前）× 22:54（重启后）建立，再用
22:54 × 23:04 的**同 boot** 对 `--refine`。歧义 115 → 17。

**A21 的 app-local DLL 是每次启动重新随机化的**（A12 是每 boot），所以那 98 条 refine
过的行不是长远优化而是当下的正确性：不做 refine 的表里有 84 条归属错误，抽到不同排列
就会崩。判据用离线残差比对得出，不是「启动成功」——细节见
`differential-crossmodule-fixups.md`。

### 包装层已还原：749 个 API 包装命名到函数（2026-08-23）

`.text` 经由 `.-wI` 的一层 Themida 变形包装调用所有 Win32 API，每个调用点一个专属副本。
现在这 749 个包装器都知道自己在调哪个 API 了（245 个不同 API），方法是对包装器做
x86 语义求值，而不是识别生成规则。

对本文档的直接影响：**上面「17 条未决」里 `.-wI` 那侧的歧义不再需要等重启**——
求值结果已纠正 4 行。做法与证据见 `Docs/themida-api-wrapper-recovery.md`。

### 空插件目录现在是合法配置

`ch_plugins_load_all()` 原先把「目录为空」直接当失败。A21 的必需插件数是 0，「一个插件
都没有」正是新客户端移植期的预期状态，所以两条空路径改为落到
`ch_required_plugins_complete()`——同一条规则，只写一处。插件**溢出**仍是致命的：那意味
着有插件被悄悄丢掉了。

### 怎么用

```
_rebuild\rebuild_a21_b1.ps1     # 每次重启后跑一次（约 40 秒）
_rebuild\launch_a21_b1.ps1      # 启动；陈旧的 exe 会被拦下
```

## 游戏内已验证：跑完一个副本（2026-08-23 00:16 起）

用 `ServerS4A21` 起服务端后，A21 的 B1 exe 跑完了一整局副本并正常退出。

判据用的是与 A12 相同的那一条（完整游戏局，不是「进程还活着」）：

* exe 是 **94,199,808 字节的精简版，13 号段 `.#|U`（26.6 MB Themida 容器）已丢弃**；
* 抓包 452 条、约 330 KB，含 0x00AA/0x00BF、0x04DD、0x0252 等成对往返；
* 退出后没有新的 WER 崩溃事件。

**这一条同时结掉了三件之前只验到启动的事**：532 条重定位表、13 号段可丢、以及 19 处
静态补丁 + 4 个运行期代理的移植。

### 缺失的是服务端，不是协议

在此之前用主仓的 86JP 服务端跑 A21，客户端崩在 `0x027C5318`，栈上第三帧是
`NOTIFUNC_CHANNELINFO`——它按 A21 的布局解析 A12 的 48 字节频道记录，走过了缓冲区末尾。
打包版与 B1 崩在**同一地址、同一栈**，这一点排除了改造侧的嫌疑，但当时被误读成「服务端
要补 A21 布局」。真实原因只是服务端配错：`ServerS4A21` 启动即声明

```
A21WireLayout: 14B game receive header; A21 CHANNELINFO/login layout
```

两个服务端都监听 7001 + 10011，拿错不会立刻报错。见
`memory/which-server-pairs-with-which-client.md`。

### 一局里记录到的 first-chance，全部是保护壳的探针

`0007_FaultRecorder` 记了 5 条，没有一条导致退出：

| 次数 | 代码 | 现场 | 结论 |
| ---: | --- | --- | --- |
| 1,3 | `0xC000001D` 非法指令 | RVA `0x04C1BD06` | VirtualPC 探测 |
| 2,4 | `0xC0000096` 特权指令 | RVA `0x04C1BDB5`，`eax=0x564D5868`(`VMXh`)、`dx=0x5658` | VMware backdoor 探测 |
| 5 | `0xC0000005` 执行 | `abs:0x4A1E8405`，栈只到 wWinMain + ntdll 线程起点 | 同类探针，被自身 SEH 吞掉 |

**这些是壳故意执行非法/特权指令再用 SEH 接住来判断是否在虚拟机里**，不是缺陷。记录器
按 `(code & 0xC0000000) == 0xC0000000` 收，所以它们会一直出现在日志里；判断有没有真问题
看的是**进程是否因此退出**，不是日志里有没有记录。

## 已知尚未做的
* 重定位表剩 17 条 `same-delta`：`ntdll|kernel.appcore|sspicli`、`d3d9|netapi32|wkscli`、
  `iertutil|netutils|srvcli|urlmon`。三组同 boot 内都不动，只能由**下一次重启**的 dump
  对来 `--refine` 收尾。在那之前，跨 boot 的 exe 不可信，重建仪式不能省。
* `protected_call` 与 feature-ready 两个身份锚点未在 A21 上测量（当前只靠 SHA-256 门）
* 已移植并跑通一局：`0010_DisableGameGuard`(13 处)、`0020_ClientCompatibility`(6 处)、
  `0050_A21RuntimeFix`(4 个代理)、`0007_FaultRecorder`(诊断)。四个都还**没有**进 A21 的
  必需插件名单——名单要在一个插件被证明稳定之后才加，加早了等于把「插件缺失」变成
  「拒绝启动」。
* 未移植：`0100_NetworkHook`（抓包与读原语追踪，全是 A12 的 RVA）、`0110_ServerRedirect`、
  `0900_PreventMinimize`。抓包目前只能靠服务端侧的 `packet_log.txt`。
* 上游 `GameGaurd.cpp` 还有 6 个聊天页签过滤的 callhook 未移植（`InstallChatFilterOfficial`）。
  纯功能性，不影响启动与副本。
* A12 的启动器新鲜度检查**仍未收尾**：本次重启做的对照显示，无服务端时新旧 exe 都会
  死，只是签名不同（陈旧的 EIP 落在栈上，新鲜的在 DLL 里读 NULL），这个混淆项要等服务
  端起来才能排除
