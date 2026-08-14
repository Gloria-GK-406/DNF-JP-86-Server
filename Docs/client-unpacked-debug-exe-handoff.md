# DNF 可调试无壳 EXE 重建 Handoff

更新时间：2026-08-14

## 0. 阶段 1 已完成（2026-08-14 追加）

启动边界栈采集（第 9/10 节的阶段 1）已实现、构建、部署并采集完成，详见
`Docs/startup-boundary-capture-phase1.md`。关键结论：

- 真实启动链已钉住：`RtlUserThreadStart -> BaseThreadInitThunk -> AEP 0x06576731 (.j`-`)
  -> 变形启动 -> 变形 OEP (.,_W) -> 调用 __security_init_cookie`。
- `client_return = 0x04ED6E75`（RVA `0x04AD6E75`）落在**变形节 `.,_W`**，不是普通 `.text`；
  整段有效栈没有任何普通 `.text` 调用者。
- OEP 及其后启动流程已被 **Themida 变形为高熵原生代码**，且有进入寄存器依赖，无法直接跳入续点，
  也无法可靠定位变形 OEP 起点。`__security_init_cookie` 本身在 `.text` 完整无损。
- 因此按第 9.2 节规则转入“合成干净启动桩 + 定位 cookie 之后的真实客户端入口（`WinMain`）”。
  在定位真实 `WinMain`/首个干净 `.text` 客户端入口前，不要开始阶段 3 离线可运行 PE 重建。

阶段 1 引入的变更：`ClientHack` host 新增 `src/host/startup_trace.c` 与 gate 快照（env 门控
`CLIENTHACK_STARTUP_TRACE=1`，普通启动零影响）；隔离客户端新增 `采集启动栈.bat`；已部署新
Az.dll（见第 3 节身份记录）。

## 1. 目标与当前结论

当前工作的首要目标已经从“继续逐条对抗 Themida/SecureEngine 的反调试检测”切换为：

> 利用无调试器环境下取得的完整解包后内存镜像，重建一个不执行保护入口、可稳定 attach、可使用软件和硬件断点的 DNF EXE。

现有 dump 对该方向提供了实质性信心。问题已经从“真实代码是否拿到”收敛为：

1. 确定或合成真实客户端启动入口；
2. 重建 Loader 所需的 Import/TLS 状态；
3. 证明新入口不再启动保护层线程，同时保留客户端必要初始化；
4. 在登录前和登录后验证稳定调试。

当前工程概率评估：

| 目标 | 当前估计 |
| --- | ---: |
| 保留现有 DLL 和全部节、但不执行保护 AEP 的 B1 可运行重建 | 65%–80% |
| 能进入频道/登录并稳定使用调试器 | 60%–75% |
| 物理移除保护节并减少 Az/ClientHack 依赖的 B2 独立重建 | 35%–50% |

如果下一轮无调试器采集能钉住 startup gate 下方的原始调用者，B1 的信心可上升到约 80%–90%。

## 2. 工作区、分支与关键路径

Server/源码仓库：

```text
D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNF-JP-86-Server
```

隔离客户端：

```text
D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNFClient-ClientHackLab-20260812
```

客户端只能通过以下 BAT 启动：

```text
D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNFClient-ClientHackLab-20260812\启动DNF.bat
```

当前 Git 分支：

```text
codex/antidebug-isolated-root
```

本 handoff 编写前的 HEAD：

```text
ff9ec97 Add analysis memory dump tooling
```

该分支在编写 handoff 前比远端同名分支领先一个提交。未经用户另行授权，不要 push、merge 或开 PR。

建议接手后先阅读：

```text
Docs/client-unpacked-debug-exe-handoff.md
Docs/memory-dump-evaluation.md
Docs/client-antidebug-investigation.md
```

## 3. 已完成的 dump 与分析产物

成功 dump 目录：

```text
D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNFClient-ClientHackLab-20260812\dumps\dnf-dump-20260814-094007-880-20220
```

关键文件：

```text
DNF-memory-rva.bin       107,556,864 bytes
DNF.memory.exe           107,545,600 bytes
DNF.analysis-anchor.exe  107,545,600 bytes
DNF-iat.csv              406 rows
DNF-pages.csv
private-exec-regions.csv
private-exec\
manifest.json
COMPLETE
```

重要：`DNF.memory.exe` 和 `DNF.analysis-anchor.exe` 都是 analysis-only 文件。不要把它们当作可运行客户端直接启动。

SHA-256：

```text
DNF.exe
F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C

DNF-memory-rva.bin
932B288B18A5EDDC720EC296661A757B67BB987A3089F7007D208D731E2F4F53

DNF.memory.exe
27163CAA41F77736871AAE5933D8B25D54B03FCF8AFC37C023D2B61D4B176AFF

DNF.analysis-anchor.exe
844C8640743EE40040C55536DEC63A0455C143FB3BBF5320D0FA173AD3125E59
```

Dump 完整性：

```text
bytes_written    = 107,556,864
hole_bytes       = 0
failed_blocks    = 0
skipped_regions  = 0
IAT total        = 406
exact matches    = 405
known exception  = 1
```

诊断插件当前已经从正式插件目录移到：

```text
DNFClient-ClientHackLab-20260812\plugins-disabled\0000_MemoryDump.dll
```

因此普通启动不会再次写入约 100 MiB 的 dump。需要重新采集时才临时恢复，并在采集完成后再次禁用。

阶段 1 部署的 Az.dll 身份（2026-08-14）：

```text
旧 Az.dll  52ADEF6954942F3E0FED0FB6852B68BE917A1631FA5C3F21EF1C83F58438A0D9  (备份 Az.dll.bak-20260814-103900)
新 Az.dll  BCF4E1CA0EFAC4F2E033A61D2E3C59D27FA7B0721F9DE1F3D6CBF6CDE308F644  (含 startup_trace，env 门控)
```

新 Az.dll 仅在 `CLIENTHACK_STARTUP_TRACE=1` 时写出 `dumps\startup-stack-*.json`，普通启动零影响。

## 4. 最关键的时序认识

`0x0253BBBF` 确实是标准 MSVC `__security_init_cookie`：

```asm
0253BBBF  mov edi,edi
0253BBC1  push ebp
0253BBC2  mov ebp,esp
...
0253BBD6  mov edi,0BB40E64Eh
```

安全 cookie 全局变量：

```text
VA 0x02FE20C0 = 0xBB40E64E
VA 0x02FE20C4 = 0x44BF19B1
```

它在 dump 中仍保持默认值。这里不能得出“函数不是启动链的一部分”的结论。

真正原因是现有 startup gate 的执行顺序：

```text
保护器完成主要展开
  -> XCookie 字节恢复为 8B FF 55 8B EC
  -> ClientHack 在 XCookie 入口临时写入 CALL gate_handler
  -> 原客户端调用 XCookie
  -> gate_handler 恢复 XCookie 原始序言
  -> gate_handler 阻塞原客户端线程
  -> 工作线程修复 IAT、初始化 required 插件并完成 dump
  -> gate release
  -> 原客户端线程重新从 XCookie 开始执行
```

相关实现：

```text
ClientHack/src/host/readiness.c
```

因此当前 dump 的捕获点非常有价值：

- 主要客户端代码已经展开；
- 406 项 IAT 已修复；
- 原始 `__security_init_cookie` 尚未执行；
- 大量 CRT、堆对象、游戏全局状态尚未污染快照；
- feature plugins 尚未初始化；
- early anti-debug 仍处于激活状态，但主要保护调用 `0x052CD542` 未被修改。

同时要避免一个关键错误：

> `0x0253BBBF` 是可靠的解包/启动地标，但不能直接作为新 PE 的 EntryPoint。直接进入该函数后，其 `ret` 没有合法调用者，客户端会崩溃。

全映像静态扫描没有发现普通 `E8/E9` 或绝对地址引用直接指向 `0x0253BBBF`。保护层可能通过计算地址、VM dispatcher 或间接调用进入它。

## 5. Dump 为什么值得用于可运行重建

原始磁盘 `DNF.exe` 大小只有 25,228,288 字节。15 个节中，前 12 个节在磁盘上的 `SizeOfRawData` 全是 0；真实内容只在运行时出现。

Dump 已经将这些节物化：

| 节 | VirtualSize | 非零比例 | 熵 |
| --- | ---: | ---: | ---: |
| `.text` | 37,560,320 | 90.5466% | 6.7231 |
| `.code` | 8,224,768 | 80.4263% | 6.4565 |
| `.data` | 21,602,304 | 0.3379% | 0.0531 |
| `.rdata` | 208,896 | 0% | 0 |
| `.tls` | 2,125,824 | 72.2651% | 5.8567 |

静态检查发现：

- `.text` 中存在大量正常 MSVC 函数和调用链；
- 可找到约 70,000 个常见 `55 8B EC` 序言字节形态和 736 个 `8B FF 55 8B EC` 热补丁型序言；
- 已知地址 `0x0118999B`、`0x02011360`、`0x0253BBBF` 均形成合理 x86 代码；
- `0x052CD53E` 仍为 `mov eax,[ebp]`，随后 `call eax`，未因 dump 被改写；
- 低地址私有可执行页只有 9 个 4 KiB 小页，形态符合 ClientHack/MinHook trampoline，没有遗漏明显的大型 32 位 VM/dispatcher 区域。

`.data` 虽有约 21 MiB VirtualSize，但非零数据只集中在 59 个页面。静态归类为“外部地址范围”的大量 DWORD 实际是 ASCII/C++ 修饰名片段，不是数千个已污染的进程指针。

## 6. PE 结构审计结果

基本头：

```text
PE32 / x86
ImageBase           0x00400000
SizeOfImage         0x06693000
Original AEP RVA    0x06176731
Original AEP VA     0x06576731
Section count       15
Relocations         stripped
```

原 AEP 位于高熵保护节 ``.j`-`` 中。重建版不能继续使用该 AEP。

当前非零 Data Directory：

| Directory | RVA | Size | 所在节 |
| --- | ---: | ---: | --- |
| Import | `0x06522254` | `0x334` | ``.j`-`` |
| Resource | `0x0664D000` | `0x45F20` | 最后一个 `.rsrc` |
| TLS | `0x06230018` | `0x18` | ``.j`-`` |
| IAT | `0x04E83000` | `0x158` | `.lA0` |

当前标准 Import Directory 可正常解析，包含约 40 个描述项/函数。多数 DLL 只导入一个代表函数，这是保护后布局的一部分。第一版 B1 应保留这些导入，不要急于删除。

没有需要恢复的 Base Relocation、Load Config 或 x86 Exception Directory。固定基址简化了重建，但也意味着候选 EXE 必须成功映射到 `0x00400000`。

## 7. TLS 审计

TLS 字段：

```text
StartAddressOfRawData  0x05283984
EndAddressOfRawData    0x052B6604
AddressOfIndex         0x04442A2C
AddressOfCallBacks     0x00000000
SizeOfZeroFill         0
```

结论：

- TLS callback 数组为空，不存在加载即跳回保护代码的 TLS callback；
- TLS 模板长度为 208,000 字节；
- 模板逐字节检查为全零；
- TLS Directory 本身位于保护节，物理移除保护节前必须搬迁到新的普通数据节；
- B1 阶段也可以先保留保护节，仅让 Loader 读取 TLS 结构而不执行保护节代码。

## 8. IAT 重建审计

`DNF-iat.csv` 当前是 406 项，不是旧方案文档中的 401 项。

按模块和地址连续性分为 5 段：

| 模块 | RVA 起止 | 数量 | 段后 DWORD |
| --- | --- | ---: | ---: |
| kernel32 | `0x023D30F0-0x023D33C8` | 183 | `0` |
| ws2_32 | `0x023D3604-0x023D3674` | 29 | `0` |
| kernel32 | `0x04312050-0x04312250` | 129 | `0` |
| msvcp60 | `0x04312340` | 1 | `0` |
| msvcr71 | `0x04312348-0x04312444` | 64 | `0` |

这些段都存在安全零终止符，可以让新的 Import Descriptor 的 `FirstThunk` 原位指向现有固定槽。DLL 名、`IMAGE_IMPORT_BY_NAME`、OriginalFirstThunk 和 descriptors 应放入新的 `.idata2` 节。

### 8.1 九个额外孤立 API 槽

对 `.text` 中有效绝对间接调用做静态检查后，还发现 9 个不在 406 表中的外部 API 槽：

| 槽 VA | API |
| ---: | --- |
| `0x027D3000` | `advapi32!ControlService` |
| `0x027D3430` | `shlwapi!StrStrIW` |
| `0x027D3474` | `user32!SetWindowLongW` |
| `0x027D3490` | `user32!GetWindow` |
| `0x027D34CC` | `user32!GetKeyState` |
| `0x027D351C` | `user32!DispatchMessageW` |
| `0x027D3560` | `user32!SendMessageW` |
| `0x027D356C` | `user32!wsprintfW` |
| `0x027D35F4` | `winmm!timeGetTime` |

这些槽嵌在随机/保护数据中，相邻 DWORD 不是零，不能直接作为标准 `FirstThunk`，否则 Loader 会继续错误解析相邻数据。

可选解决方式：

1. 推荐：新入口桩通过现有 `LoadLibrary/GetProcAddress` 写回这 9 个固定 VA；
2. 或者为它们新建 IAT，再修改所有对应 `FF 15` 调用操作数；
3. 其中 `ControlService`、`StrStrIW`、`timeGetTime` 已经存在于原标准 IAT，可直接复制对应已解析函数地址。

此前字节扫描还显示过一个 `0x04C48300 -> 0x72E722CD` 的“外部指针”，但反汇编证明 `FF 15` 只是另一个 `E8 rel32` 位移中的字节重叠，是假阳性，不需要处理。

### 8.2 msvcp60 特殊数据指针

唯一不能用 `GetProcAddress` 验证的槽：

```text
slot VA    0x04712340
symbol     ??_7?$basic_string@GU?$char_traits@G@std@@V?$allocator@G@2@@std@@6B@
runtime VA 0x607A60F0
```

该值对应当前 `msvcp60.dll` 基址 `0x607A0000 + RVA 0x60F0`，更像未按名称导出的内部 vftable/data 地址。标准命名导入不能可靠恢复它。

推荐在入口桩中：

1. 取得已加载的 `msvcp60.dll` 基址；
2. 校验 DLL 身份/版本；
3. 写入 `module_base + 0x60F0`；
4. 不要把当前进程的绝对地址 `0x607A60F0` 固化到新 EXE。

## 9. 下一位 agent 的最高优先级任务

不要立刻尝试运行 `DNF.memory.exe`，也不要先删除保护节。第一步应采集 startup gate 的原始调用栈。

### 9.1 推荐的安全采集设计

在 `ch_startup_gate_handler()` 中，`_AddressOfReturnAddress()` 返回的是临时 `CALL handler` 的返回槽。在覆盖该槽之前：

```text
return_address[0] = XCookie + 5（临时 CALL 的返回地址）
return_address[1] = 原客户端调用 XCookie 后的返回地址
return_address[2...] = 原调用者栈内容/参数
```

应该采集：

- `return_address[1]`；
- 至少 128 个后续栈 DWORD；
- ESP、EBP、EFLAGS；
- 当前 TID；
- 每个疑似代码地址所属的模块、节和页面属性；
- 必要时记录栈上下各 512 字节。

不要在被阻塞的客户端主线程中直接执行复杂文件 I/O、CRT 格式化或加载 DLL。推荐：

1. handler 只把寄存器和栈字节复制到预分配全局缓冲；
2. 设置事件；
3. 由现有工作线程写 JSON/二进制日志；
4. 写完后再正常 release gate。

这能避免在保护器刚完成展开的敏感时点引入 Loader Lock、复杂 CRT 或额外线程等待。

### 9.2 如何解释采集结果

- 如果 `return_address[1]` 落在普通 `.text`：从该返回点向前恢复调用指令和所在函数，优先把它视为 CRT/OEP 候选；
- 如果落在 ``.j`-``：说明保护 dispatcher 直接调用了 XCookie；继续分析更深的栈地址，寻找普通 `.text` 返回点；
- 如果栈中完全没有普通 `.text` 调用者：不要继续盲猜历史 OEP，改为合成干净启动桩并寻找 cookie 之后的 CRT/internal startup 函数；
- 每个候选都应先做静态反汇编和调用约定验证，不能仅凭地址所属节决定。

## 10. 推荐的六阶段执行顺序

### 阶段 1：启动边界采集

- 修改 startup gate，只增加低扰动栈快照；
- 构建并部署到隔离客户端；
- 仅通过 `启动DNF.bat` 启动；
- 无调试器、无断点；
- 采集完成后恢复正常插件状态。

完成标准：取得真实调用者/返回链，或证明需要合成入口。

### 阶段 2：真实入口恢复或等价入口设计

- 普通 `.text` CRT 调用者存在时，恢复其函数边界和初始化顺序；
- 否则设计新入口桩：cookie、孤立 API 槽、msvcp 数据指针、必要 CRT/TLS，然后跳入真实客户端启动函数；
- 不要求考古式恢复原厂 OEP，只要求行为等价且不执行保护 AEP。

完成标准：形成经过静态调用约定验证的入口设计和目标地址。

### 阶段 3：离线 PE 重建

- 扩展 `ClientHack/tools/rebuild_analysis_pe.py`，或新建独立 runnable rebuilder；
- 新增 `.idata2` 和必要启动桩节；
- 重建 406 项连续导入；
- 处理 9 个孤立槽和 msvcp 数据指针；
- 搬迁 TLS Directory；
- 保留全部原节；
- 暂不物理删除 ``.j`-``。

完成标准：`dumpbin`、`pefile` 等多种工具解析一致，raw/RVA 对齐、目录边界、thunk 终止符和入口地址均通过自动化测试。

### 阶段 4：分级运行候选

建议分别输出：

```text
DNF.unpacked-b0-loader.exe
DNF.unpacked-b05-crt.exe
DNF.unpacked-b1-debug.exe
```

- B0：Loader 成功映射并进入日志桩，然后安全退出；
- B0.5：完成导入、cookie、TLS/最小 CRT 后安全退出；
- B1：进入真实客户端启动逻辑，但从不执行原保护 AEP。

不要把多级改动混在第一次运行中。每一级都必须使用独立输出文件和独立日志。

### 阶段 5：健康调试环境验证

建议测试矩阵：

| 场景 | 目标 |
| --- | --- |
| 启动后无断点 attach | 持续存活 |
| 挂未命中软件断点 | 持续存活 |
| 挂未命中硬件断点 | 超过旧检测的约 9 秒窗口 |
| 命中普通 `.text` 软件断点 | 能继续执行 |
| 命中普通 `.text` 硬件断点 | 能继续执行 |
| 读取所有线程上下文/DRx | 不触发退出 |
| 登录前保持 attach | 稳定 |
| 登录后保持 attach | 稳定 |

代码构建、启动、日志和登录前调试可由 agent 自主完成。账号输入和游戏界面登录通常仍需用户操作一次。

### 阶段 6：物理移除保护层

只有 B1-debug 稳定后才进行：

- 确认没有线程入口、调用目标或必要数据落在保护节；
- 搬出仍位于保护节的 Import/TLS 结构；
- 逐步移除保护节，而不是一次删完；
- 将异常 RWX 节权限收紧为最小需要；
- 再减少 Az/ClientHack 依赖；
- 每一步重复启动、频道、登录和调试矩阵。

优先交付：

```text
DNF.unpacked-debug.exe
```

它允许保留“不执行但仍存在”的保护节和当前客户端 DLL。环境稳定后再追求：

```text
DNF.unpacked-clean.exe
```

## 11. 构建和验证入口

ClientHack 构建：

```powershell
cd D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNF-JP-86-Server\ClientHack
cmake --preset win32-release
cmake --build --preset win32-release --target clienthack_stage
ctest --preset win32-release --output-on-failure
```

MemoryDump 是显式诊断目标，不属于正式 stage：

```powershell
cmake --build --preset win32-release --target clienthack_memory_dump
```

输出位于：

```text
ClientHack\out\win32-release\diagnostics\plugins\0000_MemoryDump.dll
```

离线分析 PE 重建脚本：

```text
ClientHack/tools/rebuild_analysis_pe.py
```

对应测试：

```text
ClientHack/tests/test_rebuild_analysis_pe.py
```

现有分析重建测试在提交 `ff9ec97` 时为 8/8 通过。

## 12. 操作边界和不要重复的路线

必须遵守：

- 只操作隔离客户端副本；
- 客户端只通过 `启动DNF.bat` 启动；
- 不直接运行 analysis-only PE；
- 大型 dump/EXE 不提交 Git；
- 部署诊断插件前后记录文件身份和配置状态；
- 未经授权不 push、不改远端、不清理用户工作；
- 关键洞察或需要用户登录时停下报告。

不要重复投入的路线：

- 不要继续把主要精力放在逐条 hook Themida 的 native/Heaven's Gate 反调试路径；
- 不要把 `NtGetContextThread` 导出 hook 当成硬件断点检测的完整解决方案；旧证据表明检测可能绕过 ntdll 导出；
- 不要直接 NOP 保护初始化调用，保护路径还承担其他初始化；
- 不要把 `XCookie` 本身直接设为 runnable EXE 的 AEP；
- 不要因为保护节仍在文件里就急于物理删除；第一目标是让它永远不执行；
- 不要继续扩大 dump 到数 GB。主映像已经完整、无空洞，当前缺口是启动调用链，不是数据量；
- 不要把字节级 `FF 15` 搜索结果直接认定为 IAT 调用，必须先按指令边界反汇编验证。

## 13. 自主完成边界

下一位 agent 可以自主完成：

- startup gate 栈采集设计、实现、构建和部署；
- 通过 BAT 启动和采集；
- 静态恢复启动链；
- runnable PE rebuilder、入口桩和自动化测试；
- B0/B0.5/B1 候选生成；
- Loader/进程存活/登录前 attach/软硬件断点验证；
- 文档和本地提交。

可能需要用户的节点：

- 输入账号并执行登录；
- 确认游戏界面是否达到频道/角色画面；
- 如果系统实际弹出 UAC，进行一次确认。

同用户调试自己启动的普通 DNF 进程通常不要求管理员权限，应优先设计为无需 UAC。

## 14. 建议的第一个交付点

下一位 agent 不应一开始承诺“完全干净 B2”。第一个可验收交付应是：

```text
DNF.unpacked-debug.exe
```

验收条件：

1. Windows Loader 能独立加载；
2. AEP 不在原 ``.j`-`` 保护入口；
3. 客户端能进入登录前界面；
4. attach 后持续存活；
5. 未命中和命中的硬件断点均不触发旧的约 9 秒退出；
6. 没有线程在原保护节内运行；
7. 当前服务端重定向和必要 ClientHack 功能仍可工作；
8. 用户登录后客户端仍存活。

达到该点后，后续静态分析和逻辑重建就已经拥有健康环境；物理删除保护节属于第二阶段优化，而不是环境可用性的前置条件。
