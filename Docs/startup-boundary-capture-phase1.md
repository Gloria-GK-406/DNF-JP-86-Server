# 阶段 1：启动边界栈采集结果

更新时间：2026-08-14

本文件记录 handoff（`client-unpacked-debug-exe-handoff.md`）第 9/10 节“阶段 1：启动边界采集”的
实现、运行结果与结论。这是决定阶段 2 入口策略的关键证据。

## 1. 已实现的低扰动采集

在 startup gate handler 覆盖临时 `CALL` 返回槽之前，快照被拦截线程的原始调用栈：

- `ClientHack/src/host/readiness.c`
  - `ch_startup_gate_snapshot()`：在 handler 中先用内联汇编取 EFLAGS/ESP/EBP，再从返回槽
    向高地址复制 256 个栈 DWORD（含 `return_address[0]`、`return_address[1]`）。仅寄存器读取 +
    memcpy，无 I/O、无 `VirtualQuery`、无 CRT 格式化，避开敏感时点。
  - `ch_startup_gate_capture_get()`：向工作线程暴露快照缓冲。
- `ClientHack/src/host/startup_trace.c`（新增）
  - `ch_section_index_for_rva()`：纯函数，按节表把 RVA 归类到节（含单元测试）。
  - `ch_startup_trace_write()`：工作线程侧序列化器。逐个栈 DWORD 做运行时归类（所属模块 /
    模块基址 / 偏移、是否落在 DNF 映像、DNF RVA + 节、页面信息），写出 JSON 报告。
- `ClientHack/src/host/host_main.c`
  - 在 `ch_startup_gate_wait` 成功后、任何后续步骤之前调用 `ch_startup_trace_write()`。
- 环境变量门控：仅当 `CLIENTHACK_STARTUP_TRACE=1` 时写文件。普通启动（`启动DNF.bat`）不受影响，
  不产生任何采集文件。采集用 `DNFClient-ClientHackLab-20260812\采集启动栈.bat`。

构建与测试（`out/build-trace`，因旧 `out/win32-release` 缓存源路径已迁移不可复用）：

```text
ctest: 8/8 通过（含新增 clienthack_host_tests 的 ch_section_index_for_rva 用例）
```

部署到隔离客户端的 Az.dll：

```text
旧 Az.dll SHA-256  52ADEF6954942F3E0FED0FB6852B68BE917A1631FA5C3F21EF1C83F58438A0D9  (备份为 Az.dll.bak-20260814-103900)
新 Az.dll SHA-256  BCF4E1CA0EFAC4F2E033A61D2E3C59D27FA7B0721F9DE1F3D6CBF6CDE308F644
```

## 2. 采集运行

- 启动：`DNF.exe` 加 `CLIENTHACK_STARTUP_TRACE=1`，参数与 `启动DNF.bat` 一致。
- 无调试器、无断点。
- 命中 startup gate 约 4 秒后写出 trace；客户端随后正常继续（gate 释放），进程存活，未触发提前退出。

产物：

```text
DNFClient-ClientHackLab-20260812\dumps\startup-stack-20260814-104246-653-13264.json
size 42,194  SHA-256 42ADA36ADA1A13D7EF3D8E4798B7089223F61CC91487320AA5AE14BBD1339CE0
```

关键头字段：

```text
tid                 13264
dnf_base            0x00400000
return_slot_va      0x001AFF70
xcookie_return      0x0253BBC4   (= XCookie+5，临时 CALL 的返回)
client_return       0x04ED6E75   (= 原客户端调用 XCookie 的返回地址)
```

## 3. 真实启动调用链

栈基约在 `0x001B0000`；有效栈帧集中在 i=0..~35，i≥36 已越过本线程 StackBase，落入相邻内存
（其中 i=157+ 是 UTF-16 路径字符串，数值恰好落在 `.text` RVA 区间，属误报，非代码指针）。

有意义的 DNF 代码返回地址：

| i | slot VA | value (VA) | RVA | 节 | 含义 |
| ---: | --- | --- | --- | --- | --- |
| 0 | 0x001AFF70 | 0x0253BBC4 | 0x0213BBC4 | `.text` | 我方补丁 `CALL handler` 的返回（XCookie+5） |
| 1 | 0x001AFF74 | 0x04ED6E75 | 0x04AD6E75 | `.,_W` | **调用 XCookie 的返回点（OEP 续点）** |
| 25 | 0x001AFFD4 | 0x0521E7F3 | 0x04E1E7F3 | `.,_W` | 更深的变形代码/残留值 |
| 33 | 0x001AFFF4 | 0x06576731 | 0x06176731 | `.j`-` | **原始 AEP（Themida OEP 桩），位于栈基，是线程入口点参数** |

夹在中间的模块地址与线程启动链一致：

```text
i=2  kernel32!...+0x15D49
i=4  kernel32!...+0x15D30      (BaseThreadInitThunk 区域)
i=6  ntdll!...+0x6E12B         (RtlUserThreadStart 区域)
```

即：`RtlUserThreadStart -> BaseThreadInitThunk -> AEP 0x06576731 (.j`-`) -> [解包/变形启动] ->
变形 OEP (.,_W) -> 调用 __security_init_cookie`。

## 4. 决定性结论：OEP 已被 Themida 变形

对 dump（同一采集点镜像）静态反汇编：

- `__security_init_cookie`（`0x0253BBBF`，`.text`）为完整无损的 VC7.1 代码：
  `mov edi,edi; push ebp; mov ebp,esp; sub esp,0x10; mov eax,[0x02FE20C0]; ... mov edi,0xBB40E64E`。
- `client_return` 续点 `0x04ED6E75`（`.,_W`）为纯 Themida 变形原生代码：
  `push 0x77DC5B5A; pushfd; and [esp+4],..; jbe; add [esp+4],..; neg [esp+4]; xor byte[esp+4],..`，
  用栈上算术运算构造跳转目标。随后 `0x04ED6EBC: mov ecx,[edi]; mov eax,[edi+4]; mov fs:[ecx],eax`
  依赖进入前由更早变形代码设置好的 `EDI`，因此**不能从中间地址直接跳入**。
- 续点前一条指令是 `jmp`（非 `call`）。对 XCookie 的“调用”是变形引擎用 `push 计算返回值; jmp/ret`
  完成的，所以 `0x04ED6E75` 是真实续点却不被 `call` 直接前导。
- 全映像搜索：不存在字面量 `push 0x04ED6E75`，也不存在字面量立即数 `0x0253BBBF`。印证 handoff
  第 4 节“无静态 E8/E9/绝对引用直指 XCookie”——XCookie 地址由运行时计算得到。

节特征（dump 全节量化）进一步佐证：

| 节 | RVA | 非零 | 熵 | `55 8B EC` | 热补丁序言 | 判定 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `.text` | 0x00001000 | 90.6% | 6.72 | 70,731 | 736 | 干净 MSVC 代码 |
| `.,_W` | 0x04ACE000 | 97.2% | **7.83** | 229 | 174 | **Themida 变形原生代码** |
| `.j`-` | 0x04EB7000 | 99.4% | **8.00** | 3 | 0 | 保护/加密 blob |

`0x04AD6E75` 附近 ±0x2000 无任何热补丁序言，且遍布 `CC` 填充孤岛——典型变形代码布局。

## 5. 对阶段 2 的影响与建议

按 handoff 第 9.2 节判定规则：`return_address[1]` 未落在普通 `.text`，而落在变形节 `.,_W`；
遍历整段有效栈也**没有任何普通 `.text` 调用者**。因此对应规则第三条：

> 不要继续盲猜历史 OEP，改为合成干净启动桩并寻找 cookie 之后的 CRT/internal startup 函数。

含义与风险：

- 原 OEP 与其后的启动流程被 Themida 变形为高熵原生代码，且存在进入寄存器依赖，
  **无法直接 `jmp 0x04ED6E75` 续跑，也无法可靠定位变形 OEP 的真实起点**。
- 好消息：DNF 通过标准 IAT 导入 `msvcr71.dll`（64 项），CRT 主要在该 DLL 中，
  变形的只是薄启动桩。若能定位真实 `WinMain`（或变形启动最终调用的第一个干净 `.text` 客户端函数），
  即可合成入口：cookie → 用 msvcr71 完成 `__wgetmainargs`/`_initterm`/命令行/启动信息 → 调用 `WinMain`。
- 这会把 B1 的可行性重新落到“能否定位真实 WinMain / 首个干净 `.text` 客户端入口”。

推荐的阶段 2 下一步（二选一或组合）：

1. **更深一层运行时采集（推荐先做）**：在同一 gate 释放后，捕获变形启动**首次返回/调用进入干净
   `.text`** 的地址，即真实客户端入口（`WinMain` 或 CRT 对它的调用点）。这是低风险、可自主的
   增量采集，但需要设计不触发反调试的插桩（不能用普通断点/单步）。
2. **静态定位 `WinMain`**：在 `.text` 中依据 `WinMain(HINSTANCE,HINSTANCE,LPSTR,int)` 调用约定、
   对 `GetModuleHandle`/命令行/窗口初始化的引用等特征收敛候选，再与采集结果交叉验证。

在确定真实客户端入口前，不应开始离线可运行 PE 重建（阶段 3），否则入口目标不成立。

## 6. 复现方式

```powershell
# 构建（cmake 位于 VS Enterprise 内）
$env:PATH = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;" + $env:PATH
cd D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNF-JP-86-Server\ClientHack
cmake -S . -B out/build-trace -G "Visual Studio 18 2026" -A Win32
cmake --build out/build-trace --config Release
ctest --test-dir out/build-trace -C Release --output-on-failure

# 部署新 Az.dll 后采集（隔离客户端）
#   set CLIENTHACK_STARTUP_TRACE=1 并按 启动DNF.bat 相同参数启动 DNF.exe
#   或直接运行 采集启动栈.bat
# 结果：dumps\startup-stack-*.json
```
