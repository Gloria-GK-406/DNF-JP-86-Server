# 阶段 2：入口恢复与等价入口设计

更新时间：2026-08-14

承接阶段 1（`Docs/startup-boundary-capture-phase1.md`）。阶段 1 证明原 OEP/`__tmainCRTStartup`
被 Themida 变形，`__security_init_cookie` 在 `.text` 完整无损。本阶段确定可运行重建的**入口目标
与所需机器状态**，并给出两条候选入口策略。

## 1. 关键新证据：cookie 拦截点的完整寄存器

阶段 1 的采集已扩展为记录 XCookie 拦截点的全部 GP 寄存器（`Az.dll` SHA-256
`84AFEB6E3BD7D0272F56F5AB3FC537478CD140CE7B7369C82C26C5106FAA1C8E`，env 门控）。
两次采集一致，`client_return` 稳定为 `0x04ED6E75`。寄存器：

```text
eax = 0x00000001
ecx = 0x001AFFCC   (栈内地址)
edx = 0x00000000
ebx = 0x0034C000   (低于 ImageBase 的堆/分配地址，非映像固定地址)
esi = 0x00000000
edi = 0x06576731   (== 原始 AEP VA / RVA 0x06176731，位于 .j`-`)
```

`__security_init_cookie` 保存 `ebx/esi/edi/ebp`，破坏 `eax/ecx/edx`。因此在续点 `0x04ED6E75`
的寄存器状态里，`edi=0x06576731` 保持不变——**变形 CRT 把 Themida AEP/上下文指针放在 EDI**，
续点后的变形代码以 `edi` 为基址读取 `.j`-` 内的 Themida 数据。

意义：
- `edi=AEP` 是固定映像地址，可在重建 PE 中复现（前提：保留 `.j`-` 且基址仍为 0x00400000）。
- 变形 CRT 与 `.j`-` 的**解包后状态耦合**；dump 已物化该状态，重建保留 `.j`-` 即可携带。
- `ebx=0x0034C000` 是非固定堆地址，若变形 CRT 依赖 `[ebx]` 指向的 Themida 堆数据，则单纯复现寄存器
  不足以让续点独立运行——这是 B1 的核心不确定性，只能靠分级候选实测收敛。

## 2. 干净应用代码地图（佐证：只有 CRT 被变形）

对 dump 静态反汇编（capstone）确认：DNF 应用本体是干净 `.text` 的 C++（thiscall，`this` in ecx），
仅 CRT 启动薄桩被变形。已定位：

| 地址 (VA) | RVA | 角色 | 依据 |
| --- | --- | --- | --- |
| `0x02199640` | `0x01D99640` | 主窗口 WndProc | 4 参 `ret 0x10`；`cmp msg,0x10` 跳表；作为 `lpfnWndProc` 存入 WNDCLASSEX |
| `0x02199780` | `0x01D99780` | UI/消息泵线程过程 `DWORD WINAPI(LPVOID)` | 建 WNDCLASSEX(cbSize 0x30)、LoadCursor(IDC_ARROW)、GetModuleHandleW(NULL)、RegisterClassExW、CreateWindowExW(0x320×0x258 = 800×600)，唯一 `DispatchMessageW` 消息泵，返回 `msg.wParam`，`ret 4` |
| `0x02199900` | `0x01D99900` | 启动 UI 线程的 thiscall 方法 | CreateThread 包装，`lpStartAddress=0x02199780`，线程句柄存全局 `0x42CB018` |
| 全局 `0x42CB020` | | 主窗口 HWND | WinMain 存入、WndProc 读取 |
| 全局 `0x42CB018` | | UI 线程句柄 | 由 `0x02199900` 存入 |

用到的孤立槽：`GetModuleHandleW@0x027D3128`、`DispatchMessageW@0x027D351C`。全映像仅一处
`DispatchMessageW` 调用，即上面这一处消息泵。真实 `WinMain` 在更上层（多跳 thiscall），最终由
变形 CRT 调用；干净 `.text` 内对这些启动函数**无 E8 直接调用**（变形桩运行时计算目标，无字面量引用），
与阶段 1 结论一致。

（静态跨层回溯到最顶层 `WinMain` 因跳表/填充导致函数边界识别脆弱，未强行钉死；对入口设计非必需，
见下。）

## 3. 两条候选入口策略

### 策略 C（推荐，对应 handoff B1）：复现寄存器 + 跳回变形 CRT 续点

重建 PE 保留全部节（含 `.j`-`、`.,_W`，均取 dump 解包后状态）。新 EntryPoint 指向一段极小启动桩：

```text
new_entry:
    ; Windows Loader 已完成映射、导入、TLS（见阶段 3）
    mov  edi, 0x06576731        ; 复现 Themida 上下文指针
    xor  esi, esi
    ; （eax/ecx/edx 由 cookie 重新决定，无需复现；ebx 视实测是否需要）
    call 0x0253BBBF             ; __security_init_cookie（干净）
    jmp  0x04ED6E75             ; 续跑真实（变形）CRT：ctor、WinMain…
```

优点：复用全部真实启动逻辑，无需考古 WinMain/CRT。风险：变形 CRT 若依赖 AEP 建立的**新鲜** Themida
运行期状态（堆、TLS index、`[ebx]`…）而非仅 `.j`-` 静态数据，则续点会失败。

### 策略 A（回退）：合成干净 CRT + 直接调用真实 WinMain

放弃变形代码，合成 `cookie → _initterm(ctor 表) → WinMain(...)`。需要：定位真实 `WinMain`（继续静态
回溯，含健壮反汇编器）与 C++ 构造表 `__xc_a/__xc_z`。工作量更大、保真度更低，但不依赖 Themida 运行期状态。

## 4. 建议的阶段 3 计划（分级候选实测）

现有 `ClientHack/tools/rebuild_analysis_pe.py`（180 行）仅物化节、可选写 anchor 入口，属 analysis-only，
不重建导入/TLS，不可运行。阶段 3 需扩展或另建可运行 rebuilder，并按 handoff 第 10 节产出分级候选：

- **B0**：Loader 成功映射 + 进入日志桩后安全退出（验证映射、基址 0x400000、节权限、导入目录可解析）。
- **B0.5**：完成导入重建（406 项 + 9 孤立槽 + msvcp60 数据指针）、TLS 搬迁、cookie，安全退出。
- **B1**：装载策略 C 入口桩（edi=AEP → cookie → jmp 0x04ED6E75），进入真实客户端启动，从不执行 AEP。

每级独立输出文件与独立日志。B1 能否稳定运行，将直接检验第 1 节的 Themida 运行期状态耦合风险；若
B1 失败且定位为 `[ebx]`/新鲜运行期状态依赖，则切换策略 A。

## 5. 复现

采集（含寄存器）：部署上文 `Az.dll`，`set CLIENTHACK_STARTUP_TRACE=1` 后按 `启动DNF.bat` 参数启动，
或运行 `采集启动栈.bat`；产物 `dumps\startup-stack-*.json` 含 `regs_at_xcookie`。

静态分析脚本保存在会话 scratchpad（capstone）；关键地址：入口续点 `0x04ED6E75`、cookie `0x0253BBBF`、
AEP `0x06576731`、UI 线程过程 `0x02199780`、WndProc `0x02199640`。
