# 阶段 3：可运行 PE 重建（策略 C）

更新时间：2026-08-14

承接阶段 2（`Docs/entry-recovery-phase2.md`）。本阶段把 analysis-only 的
`rebuild_analysis_pe.py` 扩展为可运行 rebuilder，按 handoff 第 10 节产出分级候选。

工具：`ClientHack/tools/rebuild_runnable_pe.py`
产物目录：`DNFClient-ClientHackLab-20260812\rebuilt\`（107 MB，不入 Git）

## 进度

| 候选 | 内容 | 结果 |
| --- | --- | --- |
| **B0** `DNF.unpacked-b0-loader.exe` | 物化全部节；`.idat2` 仅含 kernel32!ExitProcess；`.chstub` = `push 0xB000; call [ExitProcess]`；入口指向桩 | **通过**：退出码 0xB000 |
| **B0.5** `DNF.unpacked-b05-crt.exe` | 重建 406 项导入（4 段外部 FirstThunk）+ TLS(dir9→0x06230018) + 桩解析 9 孤立槽 + msvcp60 数据指针 + `call __security_init_cookie` + `ExitProcess(0xB005)` | **通过**：退出码 0xB005 |
| **B1** `DNF.unpacked-b1-debug.exe` | B0.5 基础上：合并原标准导入(40 项)、`edi=AEP → call cookie → jmp 0x04ED6E75` | 进行中 |

## B0 / B0.5 已验证的事实

- 重建 PE 能被 Windows Loader 映射到固定基址 0x00400000（重定位已剥离），17 个节全部映射，
  107 MB 映像可加载；**不执行 Themida AEP**。
- 用新建的 `.idat2` 导入目录，Loader 成功解析 **405 项客户端导入**（kernel32/ws2_32/msvcr71）
  写回既有固定槽（0x023D30F0、0x04312050、0x04312348…），无一失败。
- TLS 目录指向原 `0x06230018`（回调数组为空，已核实）即可，Loader 分配槽、拷贝零模板、写索引，
  不触发保护代码。
- 入口桩用 LoadLibrary/GetProcAddress 回填 9 个孤立槽与 msvcp60 数据指针（module_base+0x60F0）。
- 桩调用真实 `__security_init_cookie`(0x0253BBBF) 正常返回——即 cookie 自身经 IAT 的
  kernel32 调用也解析正确。

结论：**可运行重建的全部基础设施（映射/导入/TLS/孤立槽/cookie 执行）已验证可用。**

运行注意：候选放在 `rebuilt\` 子目录，需要把 app-local 运行库（至少 `msvcr71.dll`）拷到同目录，
否则 Loader 弹“系统错误”缺 DLL 对话框。

## 原标准导入目录（B1 需要）

原 PE 的标准 Import 目录（RVA 0x06522254，40 描述项/46 函数，FT 在 0x04E83000）负责加载客户端
其余 DLL：d3d9/d3dx9_43/fmodex/ddraw/dinput8/gdi32/imm32/ole32/oleaut32/shell32/wininet/urlmon/
dbghelp/tbbmalloc/comctl32/comdlg32/iphlpapi/netapi32/psapi/version 等，以及 **az.dll!PutRSInfo**
与 **ijl15.dll!ijlFree**。

B1 计划：把这 40 项合并进 `.idat2`（各自外部 FirstThunk 指向 0x04E83000+ 的既有槽），
但**排除 az.dll 与 ijl15.dll**——否则会把 ClientHack（Az via ijl15）注入到已解包的重建进程里，
与不执行保护层的目标冲突。若 B1 续跑成功后在 PutRSInfo/ijlFree 处崩，再提供 no-op 桩 DLL。

## B1 的核心待验证风险

策略 C 在 `0x04ED6E75` 续跑变形 CRT，需 `edi=0x06576731`（AEP，固定映像地址，已复现）。
真正未知：变形 CRT 是否还依赖 AEP 现场建立的**新鲜运行期状态**（堆/`ebx=0x0034C000`）。
B1 首跑的失败位置将回答此问题：
- 崩在 `0x04Exxxxx`（`.,_W` 续点区）→ 依赖新鲜运行期状态 → 策略 C 不成立，转策略 A；
- 越过续点、跑到 ctor/WinMain 或缺失导入处 → 续跑可行，补齐依赖即可。
因 B1 不执行 AEP，进程应可被调试器 attach，用于定位崩溃点。
