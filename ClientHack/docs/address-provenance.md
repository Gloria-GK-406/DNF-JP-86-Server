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

## 服务器地址重定向

`0110_ServerRedirect.dll` 使用 IAT 表中首选地址 `0x027D3630` 对应的 DNF RVA `0x023D3630`。Host 完成 IAT 修复后，插件以当前 `ws2_32!inet_addr` 导出地址作为 expected bytes，并把该 DNF 私有槽替换为插件代理；因此不修改系统模块入口，也不会拦截进程中绕过此槽的调用。该槽的模块名、导出名和 RVA 与两份现有 capture 一致。

参考补丁中下列候选 Hook 已加入 `AzCapture`，但尚未进入生产插件：

| RVA | 候选行为 |
|---|---|
| `0x01CF9700` | DNF 内部 GameLog 入口 1 |
| `0x01CF9800` | DNF 内部 GameLog 入口 2 |
| `0x01D1D190` | 角色创建编辑器更新 |
| `0x01DB3800` | 角色名输入过滤 |

启用前必须取得两次隔离启动中一致的解包后入口字节，并写入受审查的固定签名；不能把运行时现读字节反过来当作 expected signature。

## 启动时序约束

参考 Az 在启动 Hook 内完成 IAT 修复和 GameGuard 补丁加载后才返回客户端。C Host 必须保持同样的可观察顺序：

`拦住游戏主线程 → 修复 IAT → 验证功能签名 → 初始化 Host 服务和全部 required 插件 → 释放游戏主线程`

曾经采用“修复 IAT 后先释放主线程，再由工作线程加载插件”的实现会稳定触发 `R6002 - floating point support not loaded`。这不是缺少浮点 DLL，而是保护补丁错过启动窗口造成的后续运行库错误。该异步释放路径已删除。

## A21 商城票据失败兼容修复（2026-09-09）

`0150_CeraTicketFailureFix.dll` 是可选的A21插件，不属于以上A12地址集合。固定镜像、用户复现和完整依据见 [商城属性与退出修复](../../Document/CeraShop-AbilityCase-20260909.md)。A21 EXE SHA-256为 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；RVA均以00400000为基址。

| RVA / 偏移 | 含义 | 取证指令 / 固定签名 |
|---|---|---|
| `00D1A0F0` | CMD0041四参数cdecl回调 | `55 8B EC 81 EC 88 00 00 00`；失败分支在00D1A103直接返回 |
| `0365C9B8` | GameContext全局槽 | 回调00D1A15A读VA03A5C9B8 |
| `01F00D10` | 商城getter，读取GameContext+8FAC | `8B 81 AC 8F 00 00 C3` |
| `0071BF60` | 商城等待标记清理，写商城+20C20=0 | `C6 81 20 0C 02 00 00 C3` |

插件先保留原回调行为，仅在success低字节为0时清理该等待标记；成功不变。getter/clear与hook目标先验证固定字节，失败不安装。仅部署新增DLL，不更改EXE/PVF；关闭客户端后删除新增DLL可回退。原始A21静态镜像与当前进程等待标记只读观察均见关联证据，不将未经实机验证的退出结果标为已通过。
