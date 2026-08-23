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
| **B1** `DNF.unpacked-b1-debug.exe` | B0.5 基础上：合并原标准导入(40 项，排除 az/ijl15)、`edi=AEP → call cookie → jmp 0x04ED6E75` | **续跑成功**；卡在陈旧内联导入 |

## B1 结果：策略 C 续跑成功，但卡在陈旧内联导入

用 cdb 运行 B1（不执行 AEP，可正常被调试器 attach）。调用栈证明**策略 C 续跑成功**：

```text
ntdll!RtlUserThreadStart -> KERNEL32!BaseThreadInitThunk
  -> 变形 CRT (重建映像 0x04D13AD8/0x04D45B40)
  -> msvcr71!_initterm            <-- 正在跑 C++ 静态构造函数
  -> [某 ctor] -> CRASH 调用 0x607CB6C0
```

即：`edi=AEP → call cookie(0x0253BBBF) → jmp 0x04ED6E75` 顺利跑过 cookie、变形 CRT，进入
`_initterm` 执行 C++ 全局构造。**阶段 2 担心的 Themida 运行期状态（edi/ebx）耦合并未阻断续跑。**

崩溃根因是**陈旧内联导入**，不是续跑失败：
- 崩在某 C++ ctor 调用 `0x607CB6C0` = 0x607A0000(dump 时 msvcp60 基址) + 0x2B6C0。
- msvcp60 是非 KnownDLL，本机 ASLR 把它加载到 0x5EBD0000（dump 时是 0x607A0000）。
- 保护器的 IAT 修复把大量 DLL 绝对地址**内联**写进了客户端代码/数据（按 dump 时基址）。
  这些内联引用在新进程里陈旧。扫描：msvcp60 内联引用 .text=148/.code=34/.,_W=47…（近 900 处）。
- KnownDLL（kernel32/ntdll/user32/gdi32…）按 boot 一致基址加载，同 boot 内内联引用仍有效——
  所以 B1 能跑过 cookie 与 msvcr71（msvcr71 固定基址 0x7C340000，同 dump）。问题只在
  **非 KnownDLL 且非固定基址**的 DLL（首个是 msvcp60）。

把 app-local `msvcp60.dll` 用 `editbin /REBASE:BASE=0x607A0000 /DYNAMICBASE:NO` 重定基后，
msvcp60 确实加载到 0x607A0000，iostream ctor 前进，但**下一处崩在 0x60832410**——已越过当前
msvcp60 末尾(0x60816000)，说明 dump 时要么是更大/不同版本的 msvcp60，要么紧邻其后还有另一个
非 KnownDLL。陈旧内联导入在非 KnownDLL 间**级联**。

### 结论与待决策

- **策略 C 的入口机制已验证可行**：重建 PE 能加载、不执行 AEP、续跑真实变形 CRT 到 C++ ctor。
- 新的根本障碍：保护器把非 KnownDLL 的导入**内联绝对化**，dump 快照把它们钉在了 dump 时的
  基址/版本上；新进程要让所有这类 DLL 落到相同基址且相同版本，脆弱且可能是长尾（d3d9/d3dx9/
  fmodex/ddraw/dinput8/msvcp60…）。这对**任何**基于此 dump 的可运行重建都成立（策略 A 也躲不开，
  因为客户端代码本身带这些内联引用）。

可选方向（需决策）：
1. 逐个把涉事非 KnownDLL 重定基到 dump 基址并匹配版本（脆弱、需精确版本、长尾）；
2. 采集保护器/ClientHack IAT 修复时解析的**完整内联导入映射**（site→dll!func），在新进程重解析
   （改造 ClientHack 采集，较系统但工作量大）；
3. 重新采集一份 dump：系统级关闭 ASLR/bottom-up，使非 KnownDLL 基址在采集与运行间一致；
4. 接受部分子系统（iostream 等）需要精确 DLL，先让不依赖它们的路径跑起来。

## 陈旧内联导入：实测工作量远小于预期，已用 6-DLL 重定基走通

对 dump 时标准 IAT 与 B1 运行时解析值逐项对比：整个客户端里**只有 6 个 DLL 因 ASLR 移位**：
`msvcp60、tbbmalloc、fmodex、d3dx9_43、dinput8、netapi32`。其余 ~20 个都是 KnownDLL/固定基址，
同 boot 内基址一致，内联引用本就有效（所以 B1 能跑过 cookie 与全部 C++ ctor）。

因此修复**不需要枚举每个内联导入**（那才是原以为的“大”），只需让这 6 个 DLL 落到 dump 基址。做法：
从各 DLL 导出表算精确 dump 基址（`dump_base = dump_addr − func_rva`），`editbin /REBASE:BASE=<base>
/DYNAMICBASE:NO` 生成 app-local 副本放进 `rebuilt\`。签名 DLL（tbbmalloc/d3dx9_43）需先剥签名
（清 Security 目录 + 截断尾部证书）再 editbin。netapi32 当前版本与 dump 不一致（基址不对齐），
暂不处理（网络信息用，登录前用不到）。

各 DLL dump 基址：msvcp60=0x607A0000、fmodex=0x60390000、d3dx9_43=0x604E0000、
tbbmalloc=0x60820000、dinput8=0x608A0000（互不重叠，与 dump 布局一致）。

## B1 结果：跑通真实客户端启动，创建主窗口，止于 TenProxy 反作弊

6 个移位 DLL 全部重定基到位后，B1（从 `rebuilt\`，游戏数据用 junction/hardlink 挂进来）：

- 越过 C++ ctor、全部子系统/DLL 初始化；
- 加载游戏数据（`dstr.dat`、`Script.pvf`…）；
- **创建了 DNF 主窗口，窗口类 `地下城与勇士`**（工作集涨到 62MB）；
- 止于客户端自身弹出的 **“Run Error: TenProxy.dll load Failed”**（腾讯反作弊 DLL）。

`TenProxy.dll` 在整个隔离客户端里根本不存在——即真实客户端也不直接加载它，是由 ClientHack/Az
处理/桩掉。B1 排除了 az.dll/ijl15.dll（避免 ClientHack 注入），所以撞上原始检查。

**结论：可运行重建机制 + 6-DLL 重定基已完全验证——重建 EXE 不执行 AEP、可被调试器 attach、
能跑完真实客户端启动直到创建主窗口。** 到登录界面所差的是 ClientHack 集成（TenProxy/服务端重定向/
PutRSInfo 等由 Az 负责），属于 handoff 第 14 节“B1 保留 ClientHack 功能”的既定用法。

## 结果：B1 + 精简 ClientHack 进入选频道（已过登录）

B1 与 ClientHack 集成成功，重建 EXE（不执行 AEP、可 attach）跑出**可见 800×600 的
`地下城与勇士` 主窗口 + `DNF_WND_CHAT` 聊天窗**，并通过 ServerRedirect 连上
100.100.51.178:7001，进入**选频道界面**（普通/公共频道、频道列表如“ch11 厄运之城 顺畅”），
工作集约 613MB、持续渲染。即已越过登录界面到达频道选择（post-login）。

### 关键修复

1. **精简 ClientHack（`CLIENTHACK_B1_MODE=1`）**：host 跳过 early anti-debug（B1 无 AEP，
   无反 attach 可打，进程本就可调试）与 IAT 修复（Loader 已解析 B1 导入）。6 个插件照常加载，
   携带服务端重定向与客户端检查绕过。
2. **gate 在 DllMain 同步安装（竞态修复）**：原来 worker 线程安装 gate，会和 B1 无 AEP 的入口桩
   （加载后几乎立刻 `call XCookie`）抢跑——worker 还在算 SHA 身份时主线程已越过 XCookie，gate 没拦到，
   插件补丁没生效（现象：gate installed 但从不 intercepted，客户端撞 R6002）。B1 映像已解包，XCookie
   加载时即可打补丁，故在 DllMain 同步 arm gate，主线程必然阻塞在 gate、插件在其阻塞时初始化。
3. **导入重新包含 az.dll/ijl15.dll**：客户端静态导入 `az.dll!PutRSInfo`，加载 ClientHack 的 Az 正是
   B1 拿到插件的途径。

### 7 个移位 DLL 全部重定基到 dump 基址（app-local + `editbin /REBASE /DYNAMICBASE:NO`）

只有非 KnownDLL/非固定基址的 DLL 需要处理，逐个从导出表算精确 dump 基址后重定基：

| DLL | dump 基址 | 备注 |
| --- | --- | --- |
| msvcp60 | 0x607A0000 | 系统 DLL（签名，先剥） |
| tbbmalloc | 0x60820000 | app-local，签名，先剥 |
| fmodex | 0x60390000 | app-local |
| d3dx9_43 | 0x604E0000 | app-local，签名，先剥 |
| dinput8 | 0x608A0000 | 系统 DLL |
| az | 0x60860000 | ClientHack 自身；PutRSInfo 恰在 RVA 0x1000，与 dump 一致 |
| logoncli | 0x60350000 | netapi32 `#59` 转发到 `LOGONCLI.I_NetServerAuthenticate3`；签名，先剥 |

`netapi32` 本身不用重定基（其槽经 Loader 跟随转发解析正确）；卡点是转发目标 logoncli 的内联引用陈旧。
签名 DLL 先清 Security 目录 + 截断证书再 editbin。所有基址互不重叠（沿用 dump 布局）。

### 运行环境

exe 与全部重定基 DLL、Az.dll、ijl15.dll、6 个插件、DNF.exe(硬链接，供 Az 身份校验) 都在 `rebuilt\`；
游戏数据用 junction/硬链接从客户端目录挂入；`set CLIENTHACK_B1_MODE=1` 后从 `rebuilt\` 启动。

### 尚未做

- 实际登录（账号输入）需用户操作 + 服务端在线；本次到达选频道即证明登录前+服务端重定向链路通。
- 硬件/软件断点在登录后的稳定性验证（handoff 阶段 5 矩阵）。
- 把 DLL 重定基与目录准备脚本化，便于复现。

## 附：原下一阶段设想（已实现）——把 B1 与 ClientHack(Az) 集成

真实客户端经 `ijl15.dll` 劫持加载 Az，Az 在 XCookie 处装 startup gate。B1 保留 `.text` 里完整的
XCookie(0x0253BBBF)，入口桩正是 `call 0x0253BBBF`。若在 B1 导入里重新包含 ClientHack 的
`ijl15`（进而加载 Az），Az 的 DllMain 会在入口桩之前装好 gate，我方桩调用 cookie 时正好被 Az 拦截，
Az 完成 ClientHack 启动（含 TenProxy 处理、服务端重定向、IAT/插件），释放后桩再 `jmp 0x04ED6E75`
续跑——等于用 B1 入口替代 AEP 跑通完整 ClientHack 流程。需处理 Az 的 IAT 修复/就绪等待与已解包状态
的潜在重复/冲突。

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
