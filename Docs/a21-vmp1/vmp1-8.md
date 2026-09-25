# `.vmp1` 个案：`CNGameCore::NetworkProc` 的虚拟化解析与协议崩溃定位

日期：2026-09-06。转储：`DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`
（固定基址 `0x00400000`）。

本篇是对另一 agent 正在做的**协议崩溃因果确认**的配合件：它给了执行锚点，
我负责把其中被虚拟化的那段指令按恢复技巧还原成「真代码」来解析。结论分两栏：
**已核实**（可判定的客观事实，逐条给字节/字符串证据）与**推断**（在事实之上的判读）。

---

## 一、身份：三个函数，全部有源码级地物证据

崩溃调用链 `CNGameSocket::Proc → CNGameCore::NetworkProc → NOTI 处理器`，三段身份都由
**解密后的字面量**坐实（字面量密文用 `literal_cipher.py` 的方案解出）：

| 地址 | 身份 | 源文件 | 证据（解密字符串） |
| --- | --- | --- | --- |
| `0x0163BBD0` | `CNGameCore::NetworkProc` | `GameCoreNetwork.cpp` | `0x032CB274`="CNGameCore::NetworkProc"、`0x032CB050`=源路径 |
| `0x0274E310` 区 | `CNGameSocket::Proc` | `CNGameSocket.cpp` | `0x035E83CC`="CNGameSocket::Proc"、`0x035E7BC0`=源路径 |
| `0x0117C520` | NOTI 处理器（大函数） | `GameCoreNetwork_NOTI_Etc.cpp` | `func-source-file-a21.csv` 映射 |

另一 agent 提到崩溃点几个指针 `da` 读出来是空的——那是加密字面量，解密后为：

- `0x035E8180` = `1.여기서 pop error 가 떨어지면 안된다.\n`（此处不应发生 pop error）
- `0x035E8138` = `2.여기서 pop error 가 떨어지면 안된다.\n`
- `0x035E80E0` = `1.need more body data : messagesize=%d\n`
- `0x035E8340` = `SELECTERROR`

即 `0x0274E344` 起的那串 push（行号 `__LINE__` = 830/848/857）是 **`CNGameSocket::Proc`
收包循环里「从流里 pop 一条消息失败」的报错分支**，不是崩溃本身，而是 `NetworkProc`
返回负值后的正常错误上报。

---

## 二、被虚拟化的是哪一段（已核实）

`0x0163BBD0` 的**头 5 字节被 Themida 偷走**，替换为
`e9 4e 81 15 04` = `jmp 0x5793d23`（VM 入口）。**函数体从 `0x0163BBD5` 起完整可读**，
是虚拟化前的原始源码（Themida 惯例：偷序言、留原体）。三条硬证据证明这是一个连贯完整
的真函数，而非残骸：

1. **尾声匹配**：`0x0163C168` 是标准 `/GS`+SEH 拆除——
   `mov ecx,[ebp-0xc]; mov fs:[0],ecx; pop ecx/edi/esi/ebx; mov ecx,[ebp-0x10]; xor ecx,ebp;
   call __security_check_cookie(0x2C627F3); mov esp,ebp; pop ebp; ret 0xC`。
   `ret 0xC` = this(ecx) + 2 个栈参，与函数体对 `[ebp+8]`/`[ebp+0xc]` 的使用一致。
2. **VM 入口自证**：`symeval` 跟踪 `0x5793D23`（`pushad;pushfd;call 0x5796c91`）折叠后，
   压入常量 `0x00000003` 并派发到首个 handler `0x05791F9E`。那个 `3` 正是源码最早的
   `mov ecx,[edi+3]`（从包偏移 +3 取长度）的偏移量。
3. **调用形态**：VM 首 handler 流入 `0x05791FBA` 的 `lea eax,[0x501af1b]; call eax`——
   这是 VMProtect 的 `VM_CALL_NATIVE` 出口（`0x501af1b` 在 `.tls`，是回原生代码的 thunk）。
   VM 对子处理器的每次调用都以这种出口发生，所以另一 agent「VM 内部走到崩溃」的观察，
   本质是 **VM 的 call-out 把控制交给了下游原生处理器**。

> **判读（推断）**：整个 `NetworkProc` 被虚拟化，但它在语义上只是一个
> **包分发器 / 反挂校验器**。它的原生 call-out 把真正干活的子处理器逐个调出去执行。

---

## 三、`NetworkProc` 干了什么（从 Rosetta stone 还原，已核实字节）

入参：`ecx`=this（记为 `esi`，内部 `mov esi,ecx`）、`[ebp+8]`、`edi=[ebp+0xc]`=**收到的包缓冲**。

**包头布局**（从对 `edi` 的访问还原）：

| 偏移 | 宽度 | 含义 | 证据 |
| --- | --- | --- | --- |
| `+0` | u8 | **类别选择**（0=NOTI / 1=CMD / 其它=非法） | `0163BECC movzx ecx,byte[edi]` |
| `+1` | u16 | **包类型 type** | `0163BF4B movzx edx,word[edi+1]` 等多处 |
| `+3` | u32 | **消息长度 messagesize** | `0163BC16 mov ecx,[edi+3]` |
| `+0xF` | — | 体起点（`长度-0xF` 即净体长） | 反复出现 `sub …,0xf` |
| `+0xE1` | — | 另一子缓冲（早期分片路径） | `0163BC29 lea eax,[edi+0xE1]` |

**主流程**（`0163BEBA` 起，按 `byte[edi]` 三分支）：

```
category = byte[edi]
switch(category){
  case 0:   // NOTI
     log("[NOTI RECV] %d (Size : %d)", word[edi+1], u32[edi+3]-0xF);   // 0x32CB198
     handler = *(void**)(0x3B51F20 + word[edi+1]*4);   // ← 按 type 查处理器表
     handler(...);                                     // 0163C11F..0163C12A
     ...
  case 1:   // CMD
     log("[CMD RECV] %d (Size : %d) Result : %s", ...);                // 0x32CB220
     h = call 0x274CA50(word[edi+1]); call 0x274D880/0x274DDD0(h, …);
     ...
  default:  // 非法类别
     log("HACK>> Network Packet Type Error [ %d ]", category);         // 0x32CB2B0
     log("CNGameCore::NetworkProc", 0x211/*=529*/, "GameCoreNetwork.cpp");
     call 0x27BE270(0);        // HackShield 上报/处置
     return 0x80000000;
}
```

**两处把包长下传给处理器**（关键，已核实）：

- NOTI（`0163C0A8` 及 `0163C11F`）：`eax=[ebx]`（=`u32[edi+3]`）`sub eax,0xf` → 作为体长，
  连同 `word[edi+1]` 索引的表项 `[0x3B51F20 + type*4]` 一起调用处理器。
- CMD（`0163BFD8`）：同样 `mov eax,[ebx]; sub eax,0xf; push eax`。

**分片/重组缓冲的上限校验**：`NetworkProc` 对**自己**的重组缓冲 `[esi+0x1022]` 处处用
`0xAF000`（716,800）做上限（`lea ecx,[ebx+0xf]; cmp ecx,0xAF000; jbe ok`，见
`0163BC76`/`0163BD23`/`0163BD58`）。**这个上限保护的是 `NetworkProc` 自己的大缓冲，
不是下游处理器的栈缓冲。**

---

## 四、崩溃定位（已核实结构 + 推断因果）

- 异常 `0xC0000409` = STATUS_STACK_BUFFER_OVERRUN，由函数尾声的 `__security_check_cookie`
  在 `0x0117D6AD`（`GameCoreNetwork_NOTI_Etc.cpp`，函数始于 `0x0117C520`）触发——
  说明**某个 NOTI 处理器的栈缓冲被写越界**。
- 该 NOTI 处理器**未虚拟化**（`0x0117C520` 起是纯 `push ebp;mov ebp,esp;sub esp,8`，可读）。
  真正带大帧的是它内部调用的子函数 `0x0117D720`：`sub esp,0x2D8`（**728 字节栈帧**），
  帧内有大量对齐的栈局部（多处 `lea [ebp-X]; and …,0xF`），在循环里调 `0x11191E0`。
- 另一 agent 实测「相邻两帧 ESP 差 0x2BC(700)」与 728 字节帧一致。

> **推断（待另一 agent 实测证伪）**：`NetworkProc` 校验的是 `0xAF000` 这个「重组缓冲」上限，
> **但它并不校验下游 NOTI 处理器 `0x0117D720` 那个 728 字节固定栈缓冲的上限**。
> 包类型 `word[edi+1]` 选出的某个 NOTI 处理器，把「`messagesize-0xF`」份数据按元素写进
> 728 字节栈帧而未夹紧 → /GS 越界。**被检查的界（0xAF000）不是要命的那个界（728）。**

---

## 五、怎么处理（给另一 agent 的可执行建议）

1. **因果确认的正确切入点不在 VM，而在 `0x0117D720`**。VM（`NetworkProc`）只是路由，
   它的 `0xAF000` 校验是安全的；关闭它附近的 42 字节意义不大。要么在 `0x0117D720` 入口
   下断读 `[ebp+…]` 传入的长度/计数，要么在 `0x11191E0` 循环里看写指针何时越过帧顶。
2. **锁定是哪一个 NOTI type**：在 `NetworkProc` 的 `0163C11F`（`mov ecx,[0x3B51F20+type*4]`）
   处记录 `word[edi+1]` 与传入的 `messagesize-0xF`，即可知道是哪个 type 打崩、长度多少。
   表基址 `0x3B51F20`，项宽 4，索引 = u16 type。
3. **若目标是止崩**：在 `0x0117D720` 的循环写入前补一个 `count/size ≤ 帧容量` 的夹紧，
   或在 `NetworkProc` 分发前对该 type 的 `messagesize` 设上限。前者治本，后者治标。
4. 崩溃报错串（第一节解出的那些）可直接对照 `CNGameSocket.cpp:830/848/857` 定位收包循环，
   确认是「pop 失败」还是「need more body data」触发的错误上报——两者语义不同。

---

## 六、方法备注

- 反汇编/折叠：`scratchpad/gs/xdis.py`（线性）与 `trace.py`（`symeval` 折叠 VMProtect 蹦床）。
- 本个案未走完整 VM 字节码——该入口 `0x05793D23` 不在既有 `vmp1-walk-a21.jsonl`
  的 962 个函数里（那局副本会话没触及 UI 线程解包路径，其密钥未被 `0160_VmEntryTrace` 捕到）。
  但**不需要**走完：Themida 留下的原体（Rosetta stone）+ 尾声/入口/call-out 三重自证，
  已足以把 `NetworkProc` 当真代码解析。若日后要完整走行，需在登录后 UI 线程活体捕获该入口密钥。
- 全部字符串来自加密字面量解密（`literal_cipher.py` 方案，对本转储同样适用，因 `.text` 固定基址）。
