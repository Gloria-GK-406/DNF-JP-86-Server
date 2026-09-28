# A21 7377 道具使用门：静态指令与运行模板交叉核验（2026-09-28）

## 当前最早的确定拒绝点

PID 15796 的 7377 模板是 `0x54F4C100`，键为 `event/burning/item_burning.stk`。客户端资格函数 VA `0x017F8770` 在 VA `0x017F87E6` 将参数 `0x0A` 传给位读取器 VA `0x029363E0`，`ECX` 为模板；它计算 `template[0x2BD + (10 >> 3)] >> (10 & 7) & 1`。当前模板 `+0x2BD..+0x2C0 = 00 14 00 00`，因此第 10 位是 **1**。VA `0x017F87F1` 的 `jne 0x017F88FD` 随即返回 `AL=0`，且位于以下 7377 硬编码分支之前。这是此资格函数在当前物品和模板值下的**确定拒绝分支**；其上层 VA `0x01803CBF` 调用后会把资格字节写为 0。红色图标与右键不发 CMD 的完整 UI 调用关系仍应以现场断点核验，不能仅由静态链宣称所有 UI 路径都已闭合。

位组写入者是 VA `0x0294EA40`：VA `0x0294EAD2` 把解析的 PVF 字符串交给 VA `0x0294E3B0` 得到 0..31 的位索引，VA `0x0294EB20` 对模板 `+0x2BD` 位组置位。栈装载解析器 VA `0x02995653` 调用该写入函数。独立只读审计已进一步闭合标签来源：外层标签映射树的 `[impossible contents]` 值 `0x1404` 对应 VA `0x02995650` 解析分支；内层内容名映射树的 `enable` 值 `0x464C` 经 VA `0x0294E3B0` 返回索引 **10**。因此当前直接拒绝位确为 `[impossible contents]` 内的 `enable`，不是 `[usable expired item]`。同一位组的 bit 12 映射 `hardcore mode`；当前本处直接拒绝由 bit 10 造成。映射树节点、值和分流地址的独立证据见同目录 `audit.md`。

## 后续 7377 专门分支及边界

在指定的 A21 `DNF-memory-rva.bin`（ImageBase `0x00400000`）中，物品 ID `7377`（`0x1CD1`）被客户端函数 **VA `0x016DC620` 显式特判为返回 true**。该函数的另一条路径查物品模板并返回 `template+0x367 == 1`。在物品资格计算函数 VA `0x017F8770` 的 VA `0x017F8A1B` 调用处，true 会跳向 VA `0x017F88FD`，返回 `AL=0`。上层 VA `0x01803CBF` 收到 false 后走 VA `0x01803CF1`，最终在 VA `0x01803D03` 把该物品的资格字节写为 `0`。

这是 **ClientStatic** 条件拒绝链，但 **当前会话已实测排除此链为根因**：VA `0x017F8A05` 前调用的 VA `0x014172D0` 查单例 VA `0x03A41E80` 的 `+0x18` 集合中是否有键 `0x66`（102）；PID 15796 的只读 RPM 显示该集合为空，`AL=0`，跳至 VA `0x017F8A4D`，不会执行 VA `0x017F8A1B` 的 7377 特判。不能给 `template+0x367` 或该谓词猜测业务名称。

## 字节级链条

| 位置（VA） | 指令/结果 | 含义界限 |
|---|---|---|
| `0x016DC626` | `cmp eax,0x1CD1` | 参数物品 ID 与 7377 比较 |
| `0x016DC62B` → `0x016DC651` | `je` → `mov al,1; ret` | 7377 无条件返回 true，不查模板 |
| `0x016DC62D` → `0x016DC64C` | 其他 ID 查 `[0x03A5C914]` 管理器，比较模板 `+0x367` 与 1 | 该偏移语义未证实 |
| `0x017F8A1B` → `0x017F8A25` | `call 0x016DC620; test al,al; jne 0x017F88FD` | 此调用点将 true 视为拒绝 |
| `0x017F89F7` → `0x017F8A07` | `push 0x66`；取单例并查 `singleton+0x18` 的集合；`AL=0` 时跳过 7377 特判 | PID 15796 集合为空，当前特判不可达 |
| `0x017F88FD` | `xor al,al; ret 4` | 资格计算返回 false |
| `0x01803CBF` → `0x01803CC6` | 调资格计算；false 跳到 `0x01803CF1` | 上层继续保留 false |
| `0x01803D03` | `mov byte ptr [eax],bl` | 把资格结果写进上层条目 |

`0x017F8770` 的参数 `[ebp+8]` 是 item 对象；调用 item vtable `+0x28` 得到物品实例信息，取 ID 后送进 `0x016DC620`。函数中 vtable `+0x148` 另用于取得模板指针；它不是 `item+8`。这两条对象链不可混淆。

7377 还有多处显式 `cmp ...,0x1CD1`（例如 VA `0x016E5ECA`、`0x016E6360`、`0x016E7607`、`0x016E79D5`）。这些位置证明该 ID 在客户端有多处特判，但不能仅凭这些比较推断其完整业务作用，也不能认定与当前不可达分支等价。

## 右键与发包的验证点

以下为**未来可执行的调试建议**，本次未 attach，也未修改进程：

1. 优先断在 VA `0x017F87EA` 或 VA `0x017F87F1`，确认 `EDI=0x54F4C100`、`AL=1`，并跳到 VA `0x017F88FD`；随后在 VA `0x01803CBF` 核对传入的 `ESI=0x54865400`，调用返回 `AL=0`。这是对当前 live 位值预测的最短动态验证路径。
2. 如未来 `0x66` 集合状态改变，再断在 VA `0x017F8A1B`（或单步到 `0x017F8A25`）观察该分支；当前会话该断点不应命中。
3. 断在 VA `0x016FFE30`（`CNRDItemManager::requestUseWasteStackable`）与 VA `0x01700905`（`push 0x2C` 的构包前，实际 `beginPacket` 调用在 VA `0x01700910`）。若上述资格门已返回 false，这两个断点预计不命中；命中则说明还有另一条使用分流，继续看返回值与发包结果。

当前进程 PID 15796 的只读快照 `live-items-readonly.json` 已核对：主背包管理器为 `DWORD[0x03A5C9BC] = 0x4692A0C0`，数组 `[M+0x58]=0x473E4800`，槽 71 的物品对象 `0x54865400`，`item+0x20` 明文 ID 为 7377，vtable 为 `0x032D8DD4`。`CNRDItemManager::findItemBySlot` VA `0x016EE6C0` 以 `ECX=M`、栈参数槽号调用，先校验容量，再返回 `DWORD[DWORD[M+0x58]+slot*4]`。此地址链只用于**只读**核验。vtable `+0x28` 对这个对象返回 `item+8`；vtable `+0x148` 经 `item+0x440` 字符串键及全局模板管理器查模板。`live-template-7377.json` 已验证模板指针 `0x54F4C100`、键 `event/burning/item_burning.stk`；当前模板的 `+0x42C`、`+0x9DC`、`+0xB64`、`+0xAC0`、`+0xC10`、`+0x60C`、`+0x628` 均为 0，对应本函数中的附加模板限制不触发。槽 65 是 `professional/common/disjoint_portable1.stk`，不能当成正常恢复药对照。

## 证据来源

- 指定 A21 dump：`E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`；上述地址均按字节反汇编核对，地址表用 VA。
- 本目录 `live-items-readonly.json`：当前角色槽 65/69/70/71 的只读 RPM 快照，仅证明对象指针与实例字段，不证明分支实机命中。
- 本目录 `live-condition-102.json`：PID 15796 单例条件集合的只读 RPM，头节点和 root 为 nil，证明当前 `0x66` 不在集合。
- 本目录 `live-template-7377.json`：当前进程 7377 模板的只读 RPM 与键。
- 本目录 `audit.md`：独立只读复核 `[impossible contents]` → `enable` → bit 10 的两层映射与返回方向。
- `protocol/game/client-to-server/0x002C.md`：普通消耗品的 CMD `USE_STACKABLE` 身份；`0x00A0` 为 `USE_BOOSTER_ITEM`，`0x0204` 为 `USE_STACKABLE_ACTION`，不能混为一条。
- `DNF-JP-86-ClientHack/.worktrees/client-debug-lab/Docs/a21-units/unit-017.md`：`CNRDItemManager` 主背包与 0x002C 构包背景；具体 7377 拒绝链以本次指定 dump 指令为准。
