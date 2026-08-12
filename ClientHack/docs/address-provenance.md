# 地址与行为来源

## 支持版本

- 文件：`DNF.exe`
- SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`
- PE32 首选 ImageBase：`0x00400000`
- SizeOfImage：`0x06693000`

源码统一保存 RVA，运行时再加实际模块基址。只有完整文件哈希通过、启动门命中且运行时状态与采集证据一致时，Host 才执行修改。

## 证据来源

行为与候选地址来自本地只读参考仓库 `S4A12ClientPatch` 的提交 `4adaee8`；可接受的原始字节和 IAT 瞬时状态来自 2026-08-12 对隔离客户端副本的两次独立、功能修改前采集：

- `runtime/captures/capture-1.jsonl`
- `runtime/captures/capture-2.jsonl`

`tools/compare-captures.ps1` 对 DNF RVA、模块相对地址和导出解析结果归一化后比较两份报告。两次采集的目标字节一致；406 个 IAT 槽位中，405 个可由模块名与导出名重新解析，一个 MSVCP 数据导出保留采集值并按所属模块 RVA 校验。

生产启动门另进行两次逐槽采集：前 341 个槽的瞬时值完全一致；索引 341 的 MSVCP 数据导出仅随模块基址变化，归一化 RVA 均为 `0x60F0`；索引 375 的 `msvcr71!swprintf` 槽稳定为该运行库内部兼容入口 `0x7C36A5DC`，其余索引 342–405 已等于各自解析目标。生成策略位于 `src/az/iat_gate_policy.h`。

每个可解析槽独立接受采集瞬时值或 `GetProcAddress` 当前目标，因此初始状态、任意部分已解析状态和完全已解析状态都能幂等通过。其他值立即失败；写入过程中任一步失败会按原值反向回滚。索引 341 只接受所属 `msvcp60.dll` 基址加 `0x60F0`。

IAT 所在页面混有打包器可执行桩，因此修改期间使用 `PAGE_EXECUTE_READWRITE`，完成后恢复原保护并刷新指令缓存。

## 已启用的 DisableGameGuard 补丁

`0010_DisableGameGuard.dll` 对以下 RVA 执行原始/替换双态校验：

| RVA | 行为 |
|---|---|
| `0x005F7CE0` | n5 check 1 返回成功 |
| `0x00D8A160` | n5 check 2 返回成功 |
| `0x00D8B1D0` | n5 check 3 返回成功 |
| `0x005F80FD` | 跳过 TCLS mode 初始化 |
| `0x00914B81` | 跳过加密 channel key 分支 |
| `0x00B9EB0A` | 跳过 safe-storage 初始化 |
| `0x02122BDF` | 跳过启动验证分支 |
| `0x01120157` | 绕过版本签名分支 |
| `0x0189D104` | 跳过 0x6F 事件处理 |
| `0x018AD5FB` | 跳过 game-state 验证 |
| `0x00AB5C80` | 恢复被打包器覆盖的函数序言 |
| `0x028D5C9C` | 写入已解密的 `az.dll` 模块名 |

完整原始字节位于 `include/clienthack/runtime_signatures.h`，替换字节位于 `plugins/0010_disable_gameguard/disable_gameguard.c`。

## 兼容与窗口补丁

`0020_ClientCompatibility.dll`：

| RVA | 原始字节 | 替换字节 |
|---|---|---|
| `0x00023EB0` | `68 A8 03 00 00` | `68 E9 FD 00 00` |
| `0x00B54606` | `74` | `EB` |
| `0x00D89628` | `0F B7 46 01 57 83 F8 57 7F 15 74 1A` | `0F B7 46 07 57 83 F8 00 7F 1C 74 0A` |
| `0x00F753A8` | `75 3F` | `EB 3F` |
| `0x018BFC00` | `BF 06 00 00 00` | `BF 03 00 00 00` |

`0900_PreventMinimize.dll`：

| RVA | 原始字节 | 替换字节 |
|---|---|---|
| `0x0099E698` | `FF 15 60 35 7D 02` | `83 C4 10 31 C0 90` |

## 网络补丁与 Hook

`0100_NetworkHook.dll`：

- 固定 XOR NOP：`0x00D8999B`
- Cipher Encrypt 透传：`0x01C11360`
- Cipher Decrypt 透传：`0x01C11420`
- ISocket Write staging：`0x048FF9AB`
- staging data offset：`0x15E218`
- staging size：`0x0AF000`
- write-position offset：`0x20D218`
- critical-section offset：`0x20D220`

三个 Hook 在调用 MinHook 前验证采集到的入口签名；固定 XOR 写入使用与其他补丁相同的双态校验和 Host 所有权回滚。

## 启动时序约束

参考 Az 在启动 Hook 内完成 IAT 修复和 GameGuard 补丁加载后才返回客户端。C Host 必须保持同样的可观察顺序：

`拦住游戏主线程 → 修复 IAT → 验证功能签名 → 初始化 Host 服务和全部 required 插件 → 释放游戏主线程`

曾经采用“修复 IAT 后先释放主线程，再由工作线程加载插件”的实现会稳定触发 `R6002 - floating point support not loaded`。这不是缺少浮点 DLL，而是保护补丁错过启动窗口造成的后续运行库错误。该异步释放路径已删除。
