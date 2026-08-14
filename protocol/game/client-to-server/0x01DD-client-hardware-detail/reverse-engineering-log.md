# `0x01DD` 逆向工作日志

对象客户端：DNF SHA-256 `F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`。
所有地址均为 **RVA**（image base `0x00400000`）。内存 dump：
`DNFClient-debug/dumps/dnf-dump-20260814-184532-552-3888/DNF-memory-rva.bin`（RVA 索引，2026-08-14）。

本条协议**没有动用调试器**：全部结论来自一次全量抓包 + 该内存 dump 的静态反汇编，
再用 `0x00AB` 会话遗留的 cdb 日志做运行时数值对账。

## 1. 前置：把抓包引擎从单 type 改成全量

`0100_NetworkHook` 原先把过滤器硬编码为 `TARGET_PACKET_TYPE = 0x00AB`，观察任何新
type 都要重编译插件。改为 `ClientHack.ini` 的 `[FrameCapture]` 驱动（type 白名单 / `*`、
按 type 记调用栈、hex 上限、write-trace 开关），默认全量双向记录。
详见 `Docs/debug-play-split-runbook.md` 的 4d 节（`client-debug-lab` 分支）。

改造中修掉两个影响证据保真度的问题：

- **入站方向不再读 `input - 13`**。`Cipher::Decrypt` 的 `input` 前面没有帧头（服务端头 15 字节
  且不在此处），旧代码那次越界读之所以一直没出事，只是因为 `0x00AB` 从不入站、过滤器在
  入站路径上从未命中。现在入站只 dump body，身份取自引擎传入的 `packet_type`，并以
  `hdr:0` 标注证据强度较弱。
- **出站在回填 totalLength 之后才 dump**。此前先 dump 会记下 `MakePacket` 留下的陈旧值：
  `0x00AB` 的 2026-08-13 样本帧头是 `3D000000`（61），而实际上 wire 上是 `34000000`（52）。
  本协议样本的 `1D000000`（29 = 13+16）已是真实上线字节。

## 2. 抓包：登录握手的完整 transcript

2026-08-14 会话（本地服务端，客户端走到角色列表；因角色创建协议未做，未能进城）：

| # | dir | type | body | 备注 |
|---:|---|---|---:|---|
| 1 | send | `0x0001` | 73 | LOGIN，含 auth hash，不入样本 |
| 2 | recv | `0x0001` | 54 | LOGIN response |
| 3 | send | `0x00AB` | 39 | 与 2026-08-13 样本 body 逐字节一致 |
| 4 | send | `0x01DD` | 16 | **本协议**，seq=2 |
| 5 | send | `0x0008` | 3 | GET_USERINFO（`FFFF02`） |
| 6 | recv | `0x0002` | 18 | 角色列表（空名单） |
| 7 | send | `0x02A8` | 2 | `FFFF` |
| 8 | send | `0x0002` | 31 | 与 #6 同 type、反方向 |

#3 的 body 与既有 `0x00AB` 样本完全一致，这同时验证了新抓包路径本身没有引入偏差。

#7/#8 推翻了 `0x00AB` 记录里"`0x02A8`/`0x0002` 为 S→C"的内存扫描推断——它们至少也
作为 C→S 出现。

## 3. 用调用栈定位构包器（本次方法的关键）

抓包对每个出站帧记了 16 层调用栈（RVA）。用**已知答案的 `0x00AB` 做标定**：

```text
00AB: 01C9007C  01CD11B0  01CD12CA  00D8566E  00D89C36  01C90D3B  00C601BA  ...
                └ 序列化器 └ wrapper
                  0x1CD1120  0x1CD12C0（记录已确认）
01DD: 01C9007C  00C37683            00D8566E  00D89C36  01C90D3B  00C601BA  ...
                └ 构包器
```

- 第 0 帧两者相同（`0x01C9007C`，共享写入 helper 的返回地址）。
- `0x00AB` 有"序列化器 + wrapper"两层，`0x01DD` 只有一层 → 它把序列化内联了。
- 第 2 帧起完全相同（`0x00D8566E → 0x00D89C36 → …`）→ 两条协议由同一处上层派发点发出。

所以目标函数就是包含返回地址 `0x00C37683` 的那个函数。

定位函数入口时踩过一个坑：先按"向后找 `CC/90` 填充后的第一个 prologue"取到
`0x00C375B0`，但线性反汇编在 `0x00C375C9` 的 `ret` 处停下，看起来"到不了目标"，于是
误判入口错误。原因是**函数内部的提前 `ret`（early-out）被当成了函数结尾**——
`0x00C375B0` 本来就是正确入口，它开头那道门失败时直接 `ret`。修正判据（扫描时不因
内部 `ret` 停止，只要指令流精确落在目标地址即认定）后，入口确认为 `0x00C375B0`。

## 4. 构包器 `0x00C375B0`（`__thiscall`，`esi = this`）

```text
0x00C375B0  push esi
0x00C375B1  push 0
0x00C375B3  push 0x29c2d24          ; 描述符结构，不是字符串
0x00C375B8  mov  esi, ecx           ; this
0x00C375BA  call 0x21216d9          ; 门 1
0x00C375BF  add  esp, 8
0x00C375C2  test eax, eax
0x00C375C4  je   0xc375ca
0x00C375C6  xor  al, al             ; 不发送，返回 false
0x00C375C8  pop  esi
0x00C375C9  ret
0x00C375CA  mov  ecx, esi
0x00C375CC  call 0xc37490           ; 门 2 = 填充器，返回 bool
0x00C375D1  test al, al
0x00C375D3  je   0xc375c6
0x00C375D5  push 0x1dd
0x00C375DA  call 0x1c8f5b0          ; GetWriter
0x00C375DF  mov  ecx, eax
0x00C375E1  call 0x1c90460          ; MakeHeader(0x01DD)
;   以下每个字段都是 (取值 → push → GetWriter → mov ecx,eax → WriteXX)
0x00C375E6  movzx eax, byte  [esi+0x04]   → 0x1c90550 (u8)
0x00C375F7  movzx ecx, byte  [esi+0x05]   → 0x1c90550 (u8)
0x00C37608  mov   edx, dword [esi+0x08]   → 0x1c905b0 (u32)
0x00C37618  movzx eax, word  [esi+0x0c]   → 0x1c90580 (u16)
0x00C37629  movzx ecx, word  [esi+0x0e]   → 0x1c90580 (u16)
0x00C3763A  movzx edx, word  [esi+0x10]   → 0x1c90580 (u16)
0x00C3764B  movzx eax, word  [esi+0x12]   → 0x1c90580 (u16)
0x00C3765C  movzx ecx, byte  [esi+0x14]   → 0x1c90550 (u8)
0x00C3766D  movzx edx, byte  [esi+0x15]   → 0x1c90550 (u8)
0x00C3767E  call 0x1c8fdd0          ; Finalize
0x00C37683  mov  al, 1              ; ← 抓包记录的返回地址落在这里
0x00C37685  pop  esi
0x00C37686  ret
```

宽度合计 16 字节，与实测 body 长度闭合。

### 写入原语的宽度归属（`E-004`）

`0x1C90550/80/B0` 的宽度不是猜的，用 `0x00AB` 已确认的序列化器 `0x1CD1120` 交叉验证：
它 `push 0xab` → `GetWriter` → `0x1C90460`，随后三次 `0x1C90550` 写 3 个 u8
（trigger/reported/count），循环体内每槽 `0x1C90550` 写 1 个 u8 tag + 两次 `0x1C905B0`
写 2 个 u32，末尾 `0x1C8FDD0` 并 `mov byte [ebx+0x34],1`——与该记录的已确认布局逐项吻合。
`0x1C90580`（u16）由 `0x01DD` 首次确定：四次调用对应四个 u16，长度算术与实测
16 字节唯一自洽，且其中两个值是已知 u16 量（PCI `8086`/`A7A8`）。

| RVA | 作用 |
|---|---|
| `0x1C8F5B0` | `GetWriter()` → eax（每个字段前调一次） |
| `0x1C90460` | `MakeHeader(u16 type)` |
| `0x1C90550` | `WriteU8` |
| `0x1C90580` | `WriteU16` |
| `0x1C905B0` | `WriteU32` |
| `0x1C8FDD0` | `Finalize()` |

已提升到 [patterns/game-frame.md](../../../patterns/game-frame.md)。

## 5. 填充器 `0x00C37490`：字段语义来源

用"四个连续 u16 写入 `[reg+0x0C/0x0E/0x10/0x12]`"作指纹扫 `0x00C20000..0x00C50000`，
唯一命中 `0x00C374E3` 起的簇，且它恰好覆盖构包器读取的全部 9 个偏移。

```text
0x00C37490  push ebp; mov ebp,esp; sub esp,0x4c   ; /GS cookie
0x00C374A2  mov  esi, ecx                          ; this
0x00C374A6  cmp  [esi], 0        → je 0xc37591     ; 提供者指针为空 → false
0x00C374AE  cmp  [esi+0x18], 0   → je 0xc37591     ; IsWow64Process 指针为空 → false
0x00C374B7  lea  eax,[ebp-0x48]; call 0x1d985a0    ; 填 CPU 信息结构：
                                                   ;   [ebp-0x48] u32 = MHz
                                                   ;   [ebp-0x44] u8  = 逻辑处理器数
                                                   ;   [ebp-0x40]     = CPU 标识串
; --- 厂商桶三分支：strcmp 语义（返回 0 = 相等） ---
0x00C374C3  push 5; push 0x29c2d10; call 0x1cf50a0 ; 解密 5 字符 token
0x00C374D2  push eax; lea ecx,[ebp-0x40]; push ecx; call 0x211ffba
0x00C374DF  test eax,eax; jne 0xc374e8
0x00C374E3    mov [esi+0x04], bl                   ; 相等 → 桶 0
0x00C374E6    jmp 0xc3750e
0x00C374E8  push 3; push 0x29c2d00; call 0x1cf50a0 ; 解密 3 字符 token
0x00C374F7  push eax; lea edx,[ebp-0x40]; push edx; call 0x211ffba
0x00C37504  test eax,eax; setne al; inc al         ; 相等 → 1，否则 → 2
0x00C3750B  mov [esi+0x04], al
; --- 其余字段 ---
0x00C3750E  mov cl,[ebp-0x44];  mov [esi+0x05], cl        ; 逻辑处理器数
0x00C37511  mov edx,[ebp-0x48]; mov [esi+0x08], edx       ; MHz (u32)
0x00C37517  mov ecx,[esi]; call 0x1d69400                 ; → edx:eax = ullTotalPhys
0x00C37523  shrd eax,edx,0x14; shr edx,0x14               ; >>20 → MB
0x00C3752A  mov [esi+0x0c], ax
0x00C3752E  call 0x1d69440;   mov [esi+0x0e], ax          ; GPU vendor id
0x00C37539  call 0x1d69450;   mov [esi+0x10], ax          ; GPU device id
0x00C37544  call 0x7f710                                  ; = mov eax,[ecx+0xa0]; ret
0x00C37549  shr eax,0x14;     mov [esi+0x12], ax          ; >>20 → MB
; --- 位数 ---
0x00C37550  lea eax,[ebp-0x4c]; push eax; mov [ebp-0x4c],0
0x00C37557  call [0x27d3120]                              ; kernel32!GetCurrentProcess
0x00C3755D  mov ecx,[esi+0x18]; push eax; call ecx         ; IsWow64Process(hProc,&out)
0x00C37563  test eax,eax; je 0xc37572                      ; 失败 → 保持 0
0x00C37567  mov ebx,[ebp-0x4c]; dec ebx; neg ebx; sbb ebx,ebx; add ebx,2
                                                           ; out==1 → 2, out==0 → 1
0x00C37572  mov ecx,[esi]; call 0x1c11260
0x00C37579  mov [esi+0x14], al                             ; OS 分类枚举
0x00C3757C  mov [esi+0x15], bl                             ; 位数
0x00C37580  mov al,1  … ret                                ; 成功
0x00C37591  xor al,al … ret                                ; 两道空指针门的失败出口
```

`0x1D69400/40/50` 即 `0x00AB` 记录 `E-008` 中以 VA 记的 `0x2169400/40/50`
（VA − `0x400000`），也就是同一个 SystemInfo collector；这把本协议的数据来源直接
接到已确认证据上。

`[esi]` 指向 SystemInfo 全局（`0x00AB` 记录中的 VA `0x031FB498`），`0x7F710` 取的是
该结构 `+0xA0`。

## 6. 运行时数值对账（不需要新的调试会话）

`0x00AB` 那次 cdb 会话把 SystemInfo 全局整块 dump 过（`analysis/cdb-00ab-live3.log`，
`dd 0x31FB498 L34` 覆盖到 `+0xCC`），足以直接核对本协议的字段：

| SystemInfo | 值 | 换算 | 对应 01DD |
|---|---|---|---|
| `+0x00` | `0x0000000E` | — | off 14 = 14 ✔ |
| `+0x18/+0x1C` | `0x3EEBDE000` (u64) | `>>20` = 16107 | off 6 = `0x3EEB` ✔ |
| `+0x38` | `0x00000A33` | 2611 | off 2 = 2611 ✔ |
| `+0x3C` | `0x0000000C` | 12 | off 1 = 12 ✔ |
| `+0xA0` | `0xFFD00000` | `>>20` = 4093 | off 12 = `0x0FFD` ✔ |

`+0xA0` 恰好是 4093 MiB，且位置紧邻 `+0x7C/+0x7E`（PCI ID）、`+0x84`（显卡名）、
`+0xAC`（`0x00FF00FF`，像颜色掩码）——都在结构体的 GPU/D3D 区块内，所以按"显存/可用纹理内存"
理解是有支撑的，但写入方未定位，语义只标 High。

## 7. 未走的路 / 死胡同

- `0x29C2D24`（门 1 的参数）**不是字符串**，前 32 字节是指针 + 计数 + `FFFFFFFF`，
  是个描述符结构。按字符串读会得到乱码，别再试一次。
- 厂商桶的两个 token 也读不到明文：`0x1CF50A0` 是运行时解密器，静态取到的是密文。
  桶编码靠与 `0x00AB` 已确认语义的一致性成立，字面量身份只能靠运行时断点拿。
- 曾试图把 01DD 的 9 个字段直接映射到 `0x00AB` 上报对象的镜像缓存（`+0x38..+0x47`）。
  **不成立**：镜像缓存的字段顺序是 MHz / cores+vendor / device+vram / mem+os，
  与 01DD 记录对象的 `+0x04..+0x15` 顺序不同，两者是各自独立的对象。值相同不等于同一块内存。
