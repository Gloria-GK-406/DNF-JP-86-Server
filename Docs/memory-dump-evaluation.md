# DNF 无调试器内存转储首轮评估

更新时间：2026-08-14

## 结论

阶段 A 已通过真实客户端验证。当前 ClientHack 可以在无调试器、无断点、
不修改 `0x052CD542` 的前提下，在解包地标恢复、IAT 修复完成且功能插件尚未
初始化时，得到完整的 RVA 布局内存镜像，并离线重建为可由 PE 工具解析和
反汇编的分析级 PE。

该结果不是可运行脱壳 EXE，也不证明 Themida VM 已被反虚拟化。它证明的是：

- 解包后的主要 DNF 代码和数据已经能够稳定落盘；
- 后续可以在静态映像中搜索、交叉引用和反编译真实客户端代码；
- 阶段 B 是否值得继续，可以基于分析尸体而不是继续盲追保护层决定。

## 当前实现

- `ClientHack/plugins/0000_memory_dump/memory_dump.c`
  - 当前 ClientHack optional 插件 ABI；
  - 目标名 `0000_MemoryDump.dll`；
  - 只读主映像，不调用 `VirtualProtect`，不暂停或枚举线程；
  - 输出主映像、页面清单、406 项 IAT 和私有可执行区清单；
  - 仅在所有输出刷新完成后创建 `COMPLETE`。
- `ClientHack/tools/rebuild_analysis_pe.py`
  - 使用原始 DNF.exe 的结构头；
  - 使用 RVA 内存镜像物化 15 个节；
  - 为原 Raw Size 为零的节分配对齐 raw data；
  - 清零 Checksum 和 Security Directory；
  - 可选把分析锚点写入 AddressOfEntryPoint，但明确不可运行。
- `ClientHack/tests/test_rebuild_analysis_pe.py`
  - 验证零 Raw Size 节物化、RVA 字节保持、raw 对齐、证书目录和校验和清理。

MemoryDump 是显式诊断目标 `clienthack_memory_dump`，输出到构建目录的
`diagnostics/plugins`，不属于正式 `clienthack_stage`，避免普通部署时重复生成
约 100 MiB 转储。

## 首轮运行

启动方式：隔离副本中的 `启动DNF.bat`。

制品目录：

```text
D:\project\DNF-AntiDebug-Isolated-Root-20260813\
  DNFClient-ClientHackLab-20260812\dumps\
  dnf-dump-20260814-094007-880-20220
```

运行状态：

- `COMPLETE` 成功写入；
- DNF 在转储完成后继续初始化所有 required 插件；
- 客户端线程正常释放；
- 服务器 loopback 重定向实际命中；
- 转储后客户端持续存活，未触发 debugger detected 或提前退出。

## 转储质量

```text
DNF-memory-rva.bin size = 107,556,864
bytes_written            = 107,556,864
hole_bytes               = 0
failed_blocks            = 0
skipped_regions          = 0
page rows                = 21
```

关键运行时字节：

```text
RVA 0x0213BBBF = 8B FF 55 8B EC
RVA 0x04ECD542 = FF D0
```

IAT：

```text
total                     = 406
read failures             = 0
resolved export matches   = 405
expected unavailable      = 1
```

唯一不能通过 `GetProcAddress` 解析的槽仍是已知的 MSVCP60 数据导出：

```text
slot VA  0x04712340
slot RVA 0x04312340
runtime  0x607A60F0
```

主要节内容：

| 节 | VirtualSize | 非零比例 | 熵 |
| --- | ---: | ---: | ---: |
| `.text` | 37,560,320 | 90.5466% | 6.7231 |
| `.code` | 8,224,768 | 80.4263% | 6.4565 |
| `.data` | 21,602,304 | 0.3379% | 0.0531 |
| `.rdata` | 208,896 | 0% | 0 |
| `.tls` | 2,125,824 | 72.2651% | 5.8567 |

`.rdata` 全零来自无空洞的完整读取结果，应视为该运行时节的实际状态，而不是
转储失败。

## 私有可执行页

32 位低地址空间发现 9 个 `MEM_PRIVATE + executable` 区域，每个为 4 KiB。
外部只读 sidecar 采集显示：

- 每页仅有 7–30 个非零字节；
- 内容是函数序言、相对跳转和绝对跳转槽；
- 没有 `0F 05` syscall 指令；
- 形态符合 ClientHack/MinHook trampoline 页面，不像 Themida VM 或大块 native
  dispatcher。

这不能排除 WOW64 高地址 64 位空间中的私有代码，但说明当前 32 位可见空间没有
漏掉明显的大型保护代码区。

## 分析 PE 验证

生成：

```text
DNF.memory.exe
DNF.analysis-anchor.exe
```

两者大小均为 `107,545,600` 字节。`dumpbin` 与 `pefile` 均成功解析：

- PE32/x86；
- 15 个节；
- ImageBase `0x00400000`；
- SizeOfImage `0x06693000`；
- 所有 raw 区间对齐且互不重叠；
- Security Directory 为零；
- XCookie 和受保护动态调用字节与内存镜像一致。

实际反汇编已确认：

- `0x0253BBBF` 形成正常函数序言和 API 间接调用；
- `0x0118999B` 附近形成正常调用链；
- `0x02011360` 形成完整函数；
- `0x052CD53E` 仍解码为 `mov eax,[ebp]`，随后 `call eax`。

## SHA-256

```text
DNF.exe
F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C

Az.dll
52ADEF6954942F3E0FED0FB6852B68BE917A1631FA5C3F21EF1C83F58438A0D9

0000_MemoryDump.dll
805746B1D05145364C8349ED2EE50EBC02E739853573616202B736D70C9D53CA

DNF-memory-rva.bin
932B288B18A5EDDC720EC296661A757B67BB987A3089F7007D208D731E2F4F53

DNF.memory.exe
27163CAA41F77736871AAE5933D8B25D54B03FCF8AFC37C023D2B61D4B176AFF

DNF.analysis-anchor.exe
844C8640743EE40040C55536DEC63A0455C143FB3BBF5320D0FA173AD3125E59
```

## 当前停止点

阶段 A 已达到预期停止点。下一步不应立即尝试运行 `DNF.memory.exe`，而应先：

1. 在 IDA/Ghidra 中加载 `DNF.memory.exe`；
2. 用 `DNF-iat.csv` 为 406 个固定槽位添加名称；
3. 静态定位 `0x052CD542` 的动态目标构造、真实 CRT 启动链和 TLS Directory；
4. 再决定阶段 B 是仅构建依赖现有 Az 的 B1，还是继续追求独立 B2。

大型 `.bin/.exe` 制品保留在隔离客户端 `dumps` 目录，不提交 Git。
