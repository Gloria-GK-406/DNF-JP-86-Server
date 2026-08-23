# DNF 客户端反调试调查记录

更新时间：2026-08-14

## 目标与边界

目标是为隔离客户端建立稳定的调试环境，同时保留客户端正常初始化、频道、登录和服务器重定向行为。

- 只操作 `D:\project\DNF-AntiDebug-Isolated-Root-20260813` 下的副本。
- 游戏始终通过 `DNFClient-ClientHackLab-20260812\启动DNF.bat` 启动。
- 不修改或启动原始客户端。
- 当前重点是反调试框架，不再以 `00AB` 字段为主线。

## 当前结论

### 1. 被隐藏的是 DNF 主线程

外部 `NtQueryInformationThread(ThreadHideFromDebugger)` 查询表明，启动后只有 DNF 主线程的隐藏状态由 `0` 变为 `1`。没有证据支持存在一个单独的“幽灵检测线程”。

### 2. 隐藏动作绕过常规 ntdll 导出

已经覆盖或观测过以下路径：

- 32 位 `ntdll!NtSetInformationThread` 导出
- WOW64 标准 syscall gateway / `FS:[0xC0]`
- 64 位 `ntdll!NtSetInformationThread` syscall stub
- `NtCreateThreadEx` 的 hide flag
- Process Instrumentation Callback

主线程仍会隐藏。ETW 内核 syscall 栈显示真正的 hide 调用只有内核帧，没有 ntdll 用户栈，因此保护层使用了私有 native/WOW64 路径。

### 3. 精确时序与调用边界

ETW 文件：

- `trace\dnf-hide-20260813-230523.etl`
- `trace\dnf-hide-20260813-231901.etl`

第二份 trace 的关键时序（相对 trace 起点）：

- `920824 us`：标准 WOW64 `NtSetInformationProcess` 返回，DNF 返回地址 `0x052CD544`
- `920974 / 921130 / 921300 us`：三次 native `NtQueryInformationProcess`
- `921446 us`：native `NtSetInformationThread(ThreadHideFromDebugger)`
- 监控线程约 `206 us` 后观察到主线程 hidden

运行时代码：

```text
052CD53E  mov eax,[ebp]
052CD542  call eax
052CD544  mov ebp,[ebp-4]
```

因此 `0x052CD544` 是动态 `call eax` 的返回地址。三次 native 查询和 hide 都发生在这个调用内部。

### 4. 整体跳过动态调用不可行

把 `0x052CD542: FF D0` 临时替换为 `31 C0` 后，DNF 在 ClientHack 插件初始化前退出。该调用除了反调试，还承担必要的保护层或客户端初始化，不能整体 NOP。

这个失败假设已经回滚；普通启动基线已恢复。

### 5. 硬件断点有独立检测

未触发的硬件断点也会在约 9 秒后导致客户端退出。这个检测不依赖登录。软件断点在相同窗口内不一定触发该检测。

精确 serializer 执行断点产生的 `STATUS_SINGLE_STEP` 会沿最终未处理异常路径进入 `NtTerminateProcess`。进程内 VEH 对精确 serializer 单步异常进行消费并设置 RF 后，登录可以成功。

### 6. 保护器归因

当前高概率属于 Oreans Themida/SecureEngine 家族，而不是 DNF 完全自研的反调试库。可信度约 80%，尚无直接品牌字符串，不能视为最终确证。

支持证据：

- PE 入口位于后置保护区，节区名称随机化，多段区间为 RWX。
- 导入表被压缩为每个 DLL 基本只保留一个 API，符合 API wrapping/import protection。
- 动态 VM/dispatcher 调用同时包含保护初始化、调试查询和线程隐藏。
- 异常驱动控制流、硬件断点检测、WOW64/native 混合路径与 SecureEngine 能力吻合。
- Oreans 官方说明 Themida 提供代码虚拟化、API wrapping、全局 Anti-Debugger Detection 和 `CHECK_DEBUGGER` 宏。

客户端时间戳为 2015 年，若归因正确，版本可能接近 Themida 2.3.x 世代；精确产品与版本仍待签名或对照样本确认。

## 已实现的防护层

当前 ClientHack 隔离分支包含：

- anti-attach stub 修复与守护
- `ProcessDebugPort` / `ProcessDebugObjectHandle` / `ProcessDebugFlags` 伪造
- `NtGetContextThread` / `NtSetContextThread` 调试寄存器净化
- `NtCreateThreadEx` hide flag 清理
- 标准 WOW64 `ThreadHideFromDebugger` 过滤
- exit、异常与 VEH 诊断
- serializer 单步异常消费
- 早期 instrumentation callback 与主线程隐藏状态监控
- 仅由诊断环境变量启用的 late-attach 握手设施

上述措施能够改善 attach 和断点行为，但尚未屏蔽私有 native hide 调用。

## 已证伪或不完整的路径

| 假设 | 结果 |
| --- | --- |
| hide 由独立隐藏线程执行 | 否；是 DNF 主线程隐藏自身 |
| hook 32 位 ntdll 导出即可 | 否 |
| hook 标准 WOW64 gateway / FS:C0 即可 | 否 |
| patch 64 位 ntdll syscall stub 即可 | 否 |
| hide 使用标准 0x77AF7000 gate，参数 class=0x11 | 未观察到 |
| 从进程创建开始挂 cdb | 会触发早期 debugger detected 分支 |
| 在第二次 SetInformationProcess 后全程 TF | 调试器改变调用次序，且单步风暴导致 readiness 超时 |
| 整体跳过 0x052CD542 的 call eax | 客户端提前退出，不可用 |

## 当前稳定基线

- 分支：`codex/antidebug-isolated-root`
- 普通 BAT 启动时 DNF 可存活并完成所有 ClientHack 插件初始化。
- 服务端重定向到 `100.100.51.178` 生效。
- 自动测试：7/7 通过。
- 普通启动仍会把主线程设为 hidden，说明目标绕过尚未完成。

## 下一步

1. 在不跳过 `call eax` 的前提下，捕获其真实 EAX 目标和 VM 入口。
2. 识别该调用内部的私有 Heaven's Gate/native dispatcher。
3. 找到统一 debugger-result 或 failure dispatcher；优先伪造检测结果，而不是逐个 hook API。
4. 仅屏蔽三次 native debug 查询及 `ThreadHideFromDebugger` 分支，保留必要初始化。
5. 依次验证普通启动、主线程 visible、调试器 attach、软件/硬件断点和登录稳定性。

## 参考

- Oreans Themida protection options: <https://www.oreans.com/help/tm/hm_protection-options.htm>
- Oreans CHECK_DEBUGGER macro: <https://www.oreans.com/help/tm/hm_checkdebugger-macro.htm>
- Oreans Themida product/VM description: <https://www.oreans.com/index.php/Themida.php>
- Oreans Themida history: <https://www.oreans.com/ThemidaAllWhatsNew.php>

## 2026-08-14 补充：两条入口的完整性保护判据

### `0x052CD542` 代码字节在执行前即受校验

为区分异常处理器中的目标内存读取与断点恢复流程，探针先后缩减为：

1. 命中后读取 EAX 指向的 64 字节；
2. 命中后只记录 EAX，不读取 EAX 指向的任何内存；
3. 通过共享内存暂停，再由进程外读取目标。

前两种均在插件初始化前退出；第三种甚至在到达共享内存地标前退出。日志只到
`Az worker started` 或身份散列阶段。由此可排除固定长度 `memcpy` 是唯一原因，
并支持更强结论：保护层在执行该调用前检查 `0x052CD542` 所在代码，单字节
`FF -> CC` 改写本身即可触发退出。因此不能把该地址当作软件断点入口。

### 从 instrumentation callback 直接置 TF 同样不可用

新增的无调试器 native trace 在第二次 `NtSetInformationProcess` 返回时设置 TF，
并把每次 VEH 单步的 EIP 写入共享内存。该运行在插件初始化前退出，而共享内存
最终为：

```text
COUNT=0 CAPTURED=False TRANSITION=0x00000000
```

即进程在第一条可由 VEH 记录的单步之前已经退出。当前证据表明保护层或 WOW64
返回机制不允许从 Process Instrumentation Callback 直接开启 TF；这条路线也不能
作为稳定剥离入口。

### 对下一步的约束

- 不再修改 `0x052CD542` 及其所在保护代码页。
- 不再从 Process Instrumentation Callback 直接设置 TF。
- 下一轮应保持 DNF 代码页和 EFLAGS 原样，优先使用外部只读内存枚举，缩小
  `MEM_PRIVATE + executable` 区域里的私有 WOW64/native gateway 候选；随后在
  ClientHack 自有代码或标准转换入口处做结果级过滤。
