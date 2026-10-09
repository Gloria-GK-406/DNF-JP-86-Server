# 称号簿投影：客户端发送与 ACK 局部 hook 研究

日期：2026-10-08。状态：ClientStatic 研究与实现建议；未实现、未加载插件、未修改 EXE。本记录不把服务端测试或旧实机观测当成新插件的验收。

## 范围与身份

仅将称号簿 → worn 的 `019D` 和 worn 镜像取消的 `019C` 改为服务器权威投影操作。真实 Main ↔ Book 的 Store/Withdraw 继续使用原生发送和 ACK 搬运；真正 Main 物品装备不得被泛化成取消称号簿绑定。

- EXE SHA256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- dump：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`。
- 内存镜像 VA base `00400000`；以下 RVA = VA − `00400000`。使用 Python Capstone 只读反汇编；本轮没有写客户端内存、没有运行 VM stub 来推定 helper 的物品处理。
- Confirmed 表示本 dump 明文指令直接支持；High 表示调用约定/用途有强静态支持；Hypothesis 表示待验证的实现方案；Unknown 表示缺少对应路径或运行观测。

## 最窄推荐组合

1. detour `016FA5F0`，仅书容器、装备槽 13、op 0/2 的投影请求，独立调用原生 packet writer，构造相同五个 i32。不进入原 sender 的物品检查、旧 title 预处理或先行 helper。
2. 独立发送保留原生书容器 pending 门及 push7。由于不进入原 sender，无需修改发送侧 `016FB56D`；其它容器、普通装备调用仍走原 trampoline。
3. 仅替换 `019C` ACK 的 `0111CE13`、`019D` ACK 的 `0111D172` 两个 CALL，投影模式下 consume 对应书 pending，返回 true。共享 `052583D2` 本体、space0 的 `016FF840` CALL 保持原样。
4. 额外把已绑定的 MirrorWorn 向空 Main 槽卸下，重定向为绑定 category/index 的专用 `019C`。仅书 op0/2 hook 不足以覆盖这一入口：已确认普通卸装存在 op6。

这是基于 Confirmed 地址/ABI 的实现方案，尚未 ClientVerified。此前“原 trampoline+bypass1”的候选已撤回：保留实体原件的自由栏会受到更早的 op2 拒绝门；原函数仍会先行搬物品。不能全局 NOP helper 或把所有 `0013` 改成 `019C`。

## 发送入口、ABI 与原字节

`VA016FA5F0 / RVA012FA5F0`：

```cpp
// Confirmed：ECX=this；callee ret 0x14。
bool __thiscall NativeSend(Container* self,
    int containerSlot, int equipSlot, int maxPending, int op, int bypass);
// EBP+08 = containerSlot，+0C = equipSlot，+10 = maxPending，
// +14 = op，+18 的低字节 = bypass。
```

入口前 24 字节：

```text
55 8B EC 6A FF 68 B4 AB DC 02 64 A1 00 00 00 00 50 83 EC 58 53 56 57 A1
```

前 5 字节 `55 8B EC 6A FF` 是完整指令。入口可以由插件 hook/trampoline 管理；地址必须按固定 EXE 身份和预期字节验证，不手工改盘上 EXE。`016FB67F` 再次确认 `ret 14h`。

书容器身份为 `DWORD[03A5C8DC + category*4]`，category 0..4；`016ECC20 / RVA012ECC20` 用指针身份遍历该数组并输出 category，`ret8`。普通 Main 为 `DWORD[03A5C9BC]`。不要凭类名、slotType 或容量把其它容器识别成书。

`016FA624..64B` 原生 pending 门：数量 `(self+70 - self+6C)/4` 达到 maxPending 则拒绝；数量至少 1 时，末元素不是 7 也拒绝。投影请求宜保持 maxPending=1，防止原生递归增大限制造成重复发送。应验证槽下界及 book 模型容量，而不是照抄原生只有上界的比较。

最小入口逻辑示意（不是已运行代码）：

```cpp
if (projectionEnabled && ownActor && equipSlot == 13 &&
    IsTitleBookPointer(self, &category) && (op == 0 || op == 2)) {
    // op2 的 category/index 由权威 worn binding 校验/指定。
    if (!ValidBookSlot(category, containerSlot) || HasPending(self)) return false;
    const int id = op == 0 ? ReadBookDefinition(self, containerSlot) : ReadWornDefinition(13);
    return DirectNativePacket(self, op == 0 ? 0x019D : 0x019C,
                              3, 13, id, category, containerSlot);
}
if (projectionEnabled && HasCurrentWornBinding() && self == Main &&
    equipSlot == 13 && ValidMainSlot(containerSlot) && MainItem(containerSlot) == nullptr) {
    // 目标为空时卸下；具体普通 op 白名单应覆盖已确认 op6，
    // 不凭“绑定存在”截获 Main 槽非空的新称号装备。
    return ProjectedCancel(Binding.category, Binding.index);
}
return Original(self, containerSlot, equipSlot, maxPending, op, bypass);
```

独立发送需要明确自己的场景/槽/当前人物/binding 门，服务端仍校验权威物品归属。不能把中段 `016FAF9C` 当独立函数调用。尤其 `016FA7C4..7E0`：getter `016F0590(self)` 返回22、源物品slotType为13、op非0时立即false；该getter解码self+34。自由栏保留实体时，不适合套用原book op2路径。具体容器类型22的初始化归属本轮没有重读构造器，但条件门本身为 Confirmed，独立构包可以完全避开这项不适用于镜像的验证。

## 为何绕开旧称号预处理

Confirmed：`016FAD29 cmp byte [EBP+18],0`，非零跳 `016FAF9C`；该目标是原函数内部基本块，需要已有 EBP、SEH 和 locals，不能作为普通 ABI 入口。

- `016FADFB` 调 `0257CD90` 找旧 worn 定义匹配的空书槽。
- `016FAE66..7C` 递归调用同一个发送函数，参数 oldBookSlot、13、maxPending+1、op2、bypass1。递归成功后 `016FAE83` 跳 `016FAF9C`，外层继续自己的请求；外层本来是取消时存在静态双 `019C` 路径。
- 找不到旧书位置且外层 op0 时，`016FAF70..7E` 改用普通 Main 容器递归 op3/bypass1；`016FAF83..99` 直接返回，原书请求不再发送 `019D`。
- 这证明原生路由与投影期望冲突。旧日志连续两次 Store 的真实来源仍未通过出站调用栈证明，不能把每个旧 double019C 样本都归因到上述递归。

## 原生请求构包与序列化原语

Confirmed：`016FAF9C..016FB123` 先根据书指针取 category；op0 使用 `019D`，op2 使用 `019C`。请求 body 是 20 字节：

```text
i32 space=3, i32 equipSlot=13, i32 definitionId, i32 category, i32 bookIndex
```

`019D` 的 definitionId 来自指定书槽；`019C` 在 `016FB024..36` 重新读取当前全局 actor 的虚表 `+A58(13)`，不是旧局部缓存中的 ID。所以递归与外层请求的 ID 是分别构包时读取的，不能在无实包时断言二者永远相同。

物品 ID 读取路径是 item 虚 `+28()` → 返回数据指针 → `+14` 的属性包装 → `00444CD0 / RVA00044CD0` getter。getter 内有校验 `02C4D110`，返回包装 `+4` 值；比裸读 item+20 更符合原生访问。actor `+A58` 在旧只读会话落到 `01839480→018364F0`，本轮没有新 live vtable 复核；调用虚表比写死 actor 数组偏移稳妥。

| 原语 | VA / RVA | ABI 与用途 |
|---|---|---|
| writer getter | 0274CA50 / 0234CA50 | 无参数，EAX=`[03B7CEE4]`；字节 `A1 E4 CE B7 03 C3` |
| StartPacket | 0274D1E0 / 0234D1E0 | ECX=writer，参数 opcode、selector(原生0)，ret8 |
| WriteI32 | 0274D340 / 0234D340 | ECX=writer，参数 i32，ret4 |
| Flush | 0274E6D0 / 0234E6D0 | 原生无栈参数 CALL；实现保护区未完整展开 |
| pending push | 01BF01E0 / 017F01E0 | ECX=self+68，参数 `int32*`，ret4；使用原生 allocator |
| pending pop | 016F3360 / 012F3360 | ECX=self，无参数，ret0；只做非空 end-=4 |

可直接实现的调用规范（按原生顺序，地址按 image base+RVA 解析）：

```cpp
// ABI表达，不要求C编译器直接支持__thiscall；可用小型x86 asm adapter。
void* (__cdecl *GetWriter)(void); // 0274CA50
int32_t (__thiscall *StartPacket)(void* writer, uint32_t opcode, uint32_t selector); // ret8
int32_t (__thiscall *WriteI32)(void* writer, int32_t value); // ret4
void (__cdecl *FlushNoArgs)(void); // 0274E6D0：不使用其返回寄存器
void (__thiscall *PushPending)(void* vector, const int32_t* value); // ret4
void (__thiscall *PopPending)(void* container); // ret0

// 仅在native UI线程、确定有效书self/slot/actor/ID且pending为空时调用。
StartPacket(GetWriter(), opcode, 0);
WriteI32(GetWriter(), 3);
WriteI32(GetWriter(), 13);
WriteI32(GetWriter(), definitionId);
WriteI32(GetWriter(), category);
WriteI32(GetWriter(), bookIndex);
FlushNoArgs();
int32_t pending = 7;
PushPending((uint8_t*)self + 0x68, &pending);
// 不调用052583D2，也不写Book物品数组和actor装备数组。
```

原生本身不使用这些调用的EAX返回值。上表的Start/Write返回寄存器可供诊断；没有把Flush的Unknown返回类型作为成功条件。C17实现若用cdecl wrapper，必须由adapter设置ECX并让native callee清理自己的8/4字节参数，再按wrapper本身的cdecl约定返回，不能把this指针当普通第一个栈参数直接call。nativeStart的asm序列是 `push 0; push opcode; call GetWriter; mov ecx,eax; call StartPacket`；每个Write是 `push value; call GetWriter; mov ecx,eax; call WriteI32`。

可验证字节：Start `0274D1E0` 与 Write `0274D340` 的入口字节应由部署代码从固定dump提取并校验；本文下文附前16字节。Flush入口前16字节 `55 8B EC B8 50 00 04 00 54 89 24 24 68 4A E0 DC`。packet writer global是 `03B7CEE4 / RVA0377CEE4`，它不是已确认的SOCKET值；原生writer/flush负责传输，插件无需另取SOCKET或自己调用send。此路径不存在已确认、必须交给实现的裸socketglobal。

```text
0274D1E0 Start       55 8B EC 83 EC 18 A1 E0 CE 99 03 33 C5 89 45 FC
0274D340 WriteI32    55 8B EC 80 79 08 00 75 09 B8 54 00 00 80 5D C2
01BF01E0 PushPending 55 8B EC 56 8B F1 8B 46 08 57 8B 7D 08 3B F8 73
016EE6C0 GetItem     55 8B EC 51 56 8B F1 8D 4E 44 8B D1 83 E2 0F 8D
00444CD0 ID getter   56 6A 01 6A 32 8D 71 04 6A 04 56 E8 30 84 80 02
```

书槽 getter `016EE6C0 / RVA012EE6C0` 使用 ECX=container、栈参数slot，原生 `016FB010..13` 直接这样调用后把EAX当item。当前自己人物指针 global `03B3DFB8 / RVA0373DFB8`，取worn时调用 `item = actor->virtual+A58(13)`；然后由item的`+28`与上述getter取ID，始终检查null。

getter 前的 push 是供下一个 writer 方法消费，不是 getter 本身的参数。共享 writer 只能按原生 UI 线程调用方式使用。Start/Write inactive 返回 `80000054` 的分支可见；Flush 的完整状态码语义为 Unknown，不能据此写未经验证的成功判断。

## pending 生命周期和发送侧 hook

Confirmed 原顺序：`016FB4E5 call Flush` → `016FB4F8 pending.push(7)` → 条件允许时 `016FB56D call 052583D2`。

原先 helper 是否空队列仍搬物品的推断已撤回。独立入口执行/静态链支持：`051F05BC..C4` 取 end−begin，`0520BC64..7A` 检查正数量；空时走 `051CFD4A` false/ret8。非空在 `05253717..1A` 先 end-=4，再进入角色/物品处理。不把保护区深层所有处理概括成恒定 swap。

因此原生若先行 helper 执行，7 已被消耗，ACK helper 再遇空队列返回 false；若先行门不成立，则 ACK 消耗并处理。投影方案应阻止先行搬运，保留 7 给 ACK 清理，不能在发送阶段自己先 pop。

发送侧局部 CALL（用于说明为何独立发送不应调用它；不属于当前推荐的修改点）：

```text
VA016FB56D / RVA012FB56D：E8 60 CE B5 03  → 052583D2
016FB562 EBX=[EBP+0C]；016FB565 EAX=[EBP+08]；016FB568 ECX=[EBP-20]
016FB56B push EBX；016FB56C push EAX
// callee ECX=self，args(containerSlot,equipSlot)，ret8。
```

门为解码全局 bool `03A5AFD0`，或 `017623A0(actor)` 且 actor 虚 `+3C` 成立；不要以一份旧 live 的 false 值推断所有场景不会执行。此前备选局部wrapper是在线程上下文匹配self/slot/13时不调用原helper并清理8字节参数，但它不能解决更早的实体原件检查门，已不推荐。该CALL后还有原生actor/外观尾部，独立发送也一并避开。

`016FB66C` 是mov AL,1与原函数epilogue。它和`016FAF9C`一样依赖已有原stackframe/SEH，不作为独立C调用入口；当前推荐直接构包，无需新增这两个内部jump。

## ACK 两处局部替换、成功和失败

| 操作 | 接收函数 VA / RVA | 局部 CALL VA / RVA | 预期原字节 |
|---|---|---|---|
| 019C worn→book | 0111CD40 / 00D1CD40 | 0111CE13 / 00D1CE13 | `E8 BA B5 13 04` |
| 019D book→worn | 0111CFF0 / 00D1CFF0 | 0111D172 / 00D1D172 | `E8 5B B2 13 04` |

共同 ABI：ECX=`titleTables[category]`，栈参数 `(bookIndex,13)`，callee `ret8`。019C 前缀 `0111CE03..12`：`8B 55 F4 8B 45 F8 8B 0C 85 DC C8 A5 03 6A 0D 52`；019D 前缀 `0111D162..71`：`8B 55 E8 8B 45 F0 8B 0C 85 DC C8 A5 03 6A 0D 52`。

x86 MSVC可用 `bool __fastcall AckShim(Container* self, void* ignoredEdx, int bookIndex, int equipSlot)` 接这两处CALL：ECX接self、EDX占位、两个栈参数由callee清8字节。EDX入口值不是有效业务参数。局部替换仍是5字节 `E8 <signed rel32>`，rel32=`shimVA-(callVA+5)`；回滚保存上述原5字节。仅改当前进程CALL，由插件管理安装与回滚，不改EXE文件。

native ACK success body 是 `u8 success + 4*i32(space,slot,category,bookIndex)`，17字节；ACK 不含 definitionId。failure body 为 `u8 success=0,u8 error,i32 space,i32 category`，10字节；外层已消费 status/error 后才调用本文 handler。不能复用请求的五字段结构去读 ACK。

建议 shim：投影模式、匹配 own actor/book binding 或对应在途请求、slot13、category/index有效时，只调用 `016F3360(self)`，返回 true；其它情况下仍调用原 `052583D2`。native pending 的 begin/end 分别 self+6C/+70；不要去 call `051F05BC`，它是保护 helper 内部块，不是公开 pop API。pop 原字节完整为：

```text
8B 41 6C 3B 41 70 74 0E 8B 41 70 39 41 6C 74 06 83 C0 FC 89 41 70 C3
```

成功 helper 返回 true 后，native 仍可能调用 actor `+74C/+750`、其它控件更新与 `02578240` 重建：019C 重建 CALL `0111CF8E`，019D `0111D302`。本轮未完整证明 actor 两个虚方法完全没有装备/展示记录副作用，因此准确承诺是“两个搬运 helper 改为 consume”，不是“ACK整个函数只有pending写入”。严格要求所有 actor 写入由0002独占时，须进一步验证虚方法或另外局部跳至 UI 尾部，不可静默忽略该边界。

failure 保留原路径：019C `0111CF9B..CFE8`、019D `0111D31C..362` 读取 space/category，映射容器后 pop，category<5 时再 pop 书 pending；调用 `01119280` 渲染 error。space3/17映射普通容器为 null，实际只清对应书 pending。无先行搬运的投影模式无需物品回滚。插件自有在途记录也必须在 failure/logout 清除，不能只有 success CALL 才清。

保持原生 native space3（或原生17）ACK；先前 space18+category 绕过方案不能替代正确清理。server/plugin 都应验证 category 下界和上界，原客户端只有 signed `<5` 的路径不是安全校验。

真实 Main↔Book ACK 保持以下 CALL 原样：019C `0111CF6D / RVA00D1CF6D`，字节 `E8 CE 28 5E 00`；019D `0111D2E1 / RVA00D1D2E1`，字节 `E8 5A 25 5E 00`。二者目标 `016FF840`，其 `016FFDB8` 是 `ret14h`。替换这里会扩大到本次明确保留的真实物品取出/存入。

## MirrorWorn 卸下与真实背包装备边界

Confirmed 静态 Main 空槽卸装调用：

```text
023DB860 ECX=[03A5C9BC]；023DB866 push EDI；023DB867 GetItem(016EE6C0)
023DB86E 非空跳023DB919
023DB874 ECX=[EBX+18C]  // 装备槽
023DB87A push EAX      // 前一GetItem返回0，即bypass0
023DB87B push6         // op6
023DB87D push1         // maxPending1
023DB87F pushECX       // 装备槽
023DB880 ECX=Main
023DB886 pushEDI       // 空Main槽
023DB887 call016FA5F0
```

另一调用 `0231B261` 为 op6/max1/bypass0，self=`MapSpace([EBX+8])`，containerSlot=`[EBX+C]`，equipSlot=`0166EB30(ItemSlotType)`。native 发送入口 `016FAC20..57` 在普通目标槽为空时从 actor 装备槽取当前物品；`016FB128..` 有非书 `0013` 编码分支。

因此在该共同入口，能将“Main空槽 + equip13 + 明确书 worn binding”重定向专用取消；不要把 op0/2 当全部卸下白名单。此处可阻止这些路径在调用 sender helper 时把镜像闪现到 bag。用户具体拖动、双击及其它全部卸下入口是否都经过这两调用，仍 Unknown；最小补证为单次人物→空Main拖动的出站0013调用栈，或受限定入口的插件日志。不能仅凭本轮静态图声称全部人物操作覆盖。

Main 槽非空的真正称号装备、非slot13、无书 binding、其它装备容器，继续原流程。server 仍需保证收到绕过客户端的0013时也不会持久化 MirrorWorn 到 Main，客户端过滤不是域规则替代。

## 服务端来源扩展和登录恢复

原生请求的 category/index 是此次目标/来源书槽，0002 worn 实体只有 ID 并不足以重建“属于哪一条书记录”，同定义重复物品和自由槽尤其不可靠。插件需要服务器明确来源 `(category,index,definitionId)`，加选角/session 或 revision 以排除旧角色状态；来源为空应明确表示未绑定。具体扩展 opcode/magic/version 与 codec 由协议实现统一分配，本记录不擅自占用新 opcode。

插件收到权威来源后，佩戴 UI 与取消路由用同一 binding；取消不再通过 `0257CD90` 的候选扫描反推。选角恢复需在书模型创建及角色信息/书数据就绪后应用，提前收到可暂存；logout/角色切换清空 binding、线程上下文和自有在途记录。不能用 itemID 相等就恢复 category/index。

成功流程建议：server 确认该请求 → native ACK清pending → 0002更新 worn → 权威来源扩展更新 binding/UI。ACK如果先到，短暂旧UI需由扩展/UI hook处理；不改native包体长度去塞来源。真实 Main↔Book 仍遵循其原 ACK搬运与权威同步安排。

## 证据边界与最小验证

- Confirmed：发送参数、bypass跳转、原生两个递归分支、两 ACK CALL字节/ABI、pending push/pop、space0两调用不在目标范围、普通Main空槽op6调用。
- High：限定作用域独立调用原生writer构造相同五字段、保留pending且不进入原sender，可避开不适用于保留原件的物品门和自动搬运；须以实现后日志/实包验证原语适配和作用域。
- Hypothesis：本记录的插件 hook 组合、TLS范围、在途匹配、扩展 binding 生命周期；没有新插件实机结果。
- Unknown：全部人物卸下入口覆盖、native ACK尾部actor+74C/+750完整写入语义、Flush全状态码、patch安装/卸载时pending已有请求处理、旧double019C每一份样本的真实来源。

实现验收至少记录：单次book equip只一019D且无自动旧title0013/递归019C；书cancel和已绑定人物→Main均只一019C且bag无镜像；ACK前pending1、ACK后0；failure清0且物品不变；真实Main↔Book仍有原搬运；真正Main新title装备仍0013；登录/切角色恢复正确binding。出站帧 body、调用点与before/after目标值比只看200ms pending快照更有辨别力。两 CALL字节不匹配、身份不匹配或扩展未协商时应停用此功能并保留原路径。
