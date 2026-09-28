# A21 7377 可用性门独立审计

日期：2026-09-28。静态基线：`E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，按 ImageBase `0x00400000` 解码；动态只读对象：PID 15796。仅 ReadProcessMemory，没有注入、attach 或内存写入。

## 结论

**ClientStatic + 当前内存读取确认**：7377 模板的 `+0x2BD` 位图中 bit 10 是 `[impossible contents]` 的 `enable`。该位为 1 时，`0x017F8770` 在 `0x017F87F1` 直接走 false 返回，早于地图、等级等其他限制。当前模板位图为 `00 14 00 00`，bit 10 和 bit 12 均为 1；bit 12 映射 `hardcore mode`，本处直接拒绝由 bit 10 造成。

这确认数据与静态分支关系，尚未以断点证明右键事件实际进入此函数，也不单凭此审计宣称红色绘制分支已闭合；图标绘制由另一调查负责。

## 返回方向复核

1. `0x017F87E6 push 0xA; 0x017F87E8 mov ecx,edi; 0x017F87EA call 0x029363E0`，EDI 为 item vtable `+0x148` 返回的模板。
2. `0x029363E0` 按 `template[0x2BD + (index >> 3)] >> (index & 7) & 1` 取位。index=10 时取 `+0x2BE` 的 bit 2；当前该字节 `0x14`，结果为 1。
3. `0x017F87EF test al,al; 0x017F87F1 jne 0x017F88FD`。目标 `0x017F88FD` 返回 false。
4. 上层 `0x01803CBF call 0x017F8770; 0x01803CC4 test al,al; 0x01803CC6 je 0x01803CF1`；目标清零 BL，并于 `0x01803D03` 写入可用性缓存，最终 AL=BL 返回。

## 字段装载链

- `0x02995650 push 0; push ebx; call 0x0294EA40` 为模板内容限制解析调用；EBX 是模板。
- 外层标签映射树当前位于 `DWORD[0x04E1BF94]`（head `0x41C67E80`，131 项）。只读遍历获得节点 `0x41C676D0`：key=`[impossible contents]`，value=`0x1404`。
- 外层标签注册代码 `0x02992E05` 传静态标签字符串，`0x02992E2B` 设置 value=`0x1404`。同一分支的已初始化路径 `0x02992DF8/0x02992DFF` 跳到 `0x02995650`，调用上述解析器。
- `0x0294EA40` 循环读取字符串；`0x0294EAD2 call 0x0294E3B0` 映射为 bit index；`0x0294EB20 add byte ptr [eax+edi+0x2BD],dl` 将对应 bit 置入位图。写入前检查原 bit，避免重复加法翻转。
- 内容名映射树当前位于 `DWORD[0x04E1BB4C]`（head `0x466CE998`，14 项）。只读遍历获得节点 `0x466CE890`：key=`enable`，value=`0x464C`；节点 `0x466CE860`：key=`hardcore mode`，value=`0x4652`。
- `0x0294E3B0` 对 value=`0x464C` 的分发表索引为 9，转向 `0x0294E78E`，已初始化路径再转向 `0x0294E8F9 mov esi,0xA`，最终 EAX=10 返回。`0x4652` 对应 index=12。

## 上层资格缓存的边界

`0x01803AE0` 在调用这个谓词前还有对象登记、类型、模式与缓存检查。缓存失效依据角色对象 `+0x344C` 与全局 `DWORD[0x03A5BE84]` 比较；缓存位于角色 `+0x3450`，命中直接返回缓存字节。未命中时初值由角色 `+0x1D0 < 3` 决定，随后依次调用 `0x017F8770`、`0x017F6830`、vtable `+0x95C`、`0x017FA1D0`，任一 false 则缓存 false。因而当前位图足以证明重新计算时必然在第一谓词内拒绝，但不能仅靠模板读取断言本次鼠标事件的调用路径。

## 被排除的候选

`0x016DC620` 的 ID=7377 特判虽返回 true，调用它的支路要求条件集合含 `0x66`；当前集合为空，不能作为当前根因。`0x016E04E0` 的模板 `+0x188` 空列表返回 true，不能倒读为拒绝。这两条均晚于已确认的 enable 位检查。
