# 迷妄之塔首次进入崩溃：扣券回执顺序

## 现场与证据

2026-10-03 23:32，角色 `1123`（character 2，level 80）选择迷妄之塔后客户端退出。现场服务端为 `3c58b51b`，服务进程未退出，入塔事务已提交。

- 原始样本：`E:/Project/DNF/Document/illusion-tower-acceptance-20261003/samples/20261003-233303-004-first-entry-crash/`，包括客户端 2 的 fault 日志、服务端 stdout/stderr 和部署清单。
- 客户端 exe SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 客户端与服务端 PVF SHA-256 相同：`3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`。
- 静态 dump 身份见 [inputs.json](outputs/illusion-tower-20261003/inputs.json)，SHA-256 `aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d`。本次反汇编摘录保存在现场目录 `entry-crash-disassembly.txt`。

## 已确认的直接崩溃路径

**ClientRuntime / Confirmed**：fault 日志第五次异常为 `C0000005`，RVA `022D81B9`，读取地址 `00000008`，ECX 为零。前四次异常发生在启动保护代码，不能混作本次入塔故障。

**ClientStatic / High**：按固定 image base `00400000` 将栈 RVA 转为 VA：

```text
CMD 0012 DELETE_ITEM handler
  01130738 call player.vtable[A04] → 返回地址 0113073A
  0186E8DA → 01648C00
  01648C39 call tower.vtable[7C] → 返回地址 01648C3B
  026D81AB call 0171C310          → 可选 DGN 表指针
  026D81B2 call 01A15860          → 按索引取表
  026D81B9 mov esi,[ecx+8]        → 空指针读取，与现场吻合
```

`0171C310` 检查 DGN 的可选表启用标志；未启用时返回零。塔属性路径没有空值检查。此证据确认客户端是在处理扣券成功回执的属性更新路径上崩溃；表的业务名称、为何此时不可用仍未确认，不能直接归因为 PVF 缺文件。

**CurrentImplementation + 服务端发送日志 / Confirmed**：23:32:21.482–.484 的旧顺序为角色扩展状态 → 000F ACK → 0003 状态 → 001A host → 019F support → **008E 塔信息 → 008F 楼层 → 0012 扣券回执 → 000E 背包更新**。日志记录消息类型、长度与次序，不是完整明文抓包。

**ReferenceOnly**：`ServerS4A21/Server/DfoServer/Network/Handlers/Dungeon/DungeonEntryHandler.cs` 的塔入口先 `SendEntryCostUpdates` 再 `SendEntryPacketsAsync`。这只用于交叉验证修正方向，不替代 A21 证据。

## 修正与验证边界

`IllusionTowerProjectionDelivery.EnterAsync` 将每位接收者的已提交扣券回执和背包更新一起前移到该接收者的入塔帧之前。事务扣除、收件人、扣除数量、楼层内容及既有发送失败隔离保持原语义。客户端此时仍处在塔模式切换前，避免这次回执进入尚不可用的塔属性分支。

自动回归复用真实 PVF 的公开入口完整流程（首次入塔、15 层、回城、再次扣券入塔）与双人队伍入口，新增外发消息次序断言。测试不模拟客户端，不证明实机已恢复。

- RED：生产代码未改，校正后的两项用例均因 `008E/008F` 先于 `0012` 失败。最初测试误把同枚举的角色扩展状态算作背包更新，随后修正筛选并重新取得有效 RED；编译失败不算 RED。
- GREEN：Application 的 `IllusionTower|CommandHandlerArchitectureTests` 共 70 项通过，0 失败、0 跳过。
- Protocol 的 `IllusionTower` 共 18 项通过，0 失败、0 跳过；结果为 `entry-order-protocol.trx`。
- 结果目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/illusion-tower-entry-order/`，有效 RED 为 `entry-order-red-corrected.trx`，GREEN 为 `entry-order-green.trx`。
- 执行参数：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；`dotnet test` 对应 csproj，`-c Release --no-restore -m:1 -p:UseSharedCompilation=false`，RED filter 为两个上述用例完整名称，GREEN filter 为上述类名片段。沙箱内测试启动两次停滞后取消，受控提权执行取得结果。

**实机仍待复测**：重启客户端，重新进入塔，观察第一层是否完成加载且保持至少 10 秒；再验证扣券一次。若仍崩溃，保存新 fault 日志与同轮发送日志，判断是否仍为同一属性路径。不要因该顺序修正通过测试就将塔入口标为 ClientVerified。

旧崩溃轮次的门票已提交扣除；本修正不自动补发、不修改客户端二进制，也不把尚未证实的首层延迟发送方案混入。
