---
kind: rks-review
verdict: approved
---

# A21 公会积分内容 `0x02EA` 补正审查

依据[任务契约第 2 版](../TaskContract.md)、[规格 RQ-05](../spec.md)（`63767f243`）及 `reviewing-work` 的代码审查与证据保留标准，审查该提交上的 `Server/` 工作树补正。`git diff 63767f243 -- Server | git hash-object --stdin` 得 `c71d229eeb8e0537cc2e9037994498b6abcc9d28`，`git diff --check 63767f243` 通过。

- **WireCaptured**　从本地 `Document/tasks/guild-system/.local-evidence/a21-donation-history/user-host-140116075.stdout` 筛出 `type=0x02EA`，实机入站帧 selector 为 `0x01`，seq `33`、`45` 的 body 均为 `0B`。客户端构包普查给出同等长度。
- **ClientStatic**　对本地 A21 b1 `ClientEvidence/binaries/DNF.unpacked-b1-full-e5a34bbf.exe` 用 `llvm-objdump -d --x86-asm-syntax=intel` 分别检查 `0x01120600` 至 `0x011206D7`、`0x0078D51A` 至 `0x0078D5C8`。成功分支在 `0x0112062F` 读取 `u32` 计数，循环在 `0x0112066D` 每次读 `44` 字节。记录的 `+34` 为类型，`+40` 的 DWORD 为格式化数值。`kind=4` 走 `0x5208 - kind`，选中 `0x5204` 金币捐赠文案，再经 `0x9CE2` 的 `%s+%dGM` 格式化。新编码器按同形状写 GM 增量，空成功载荷为 `01 00 00 00 00`。
- **CurrentImplementation**　两个 opcode 各自登记空请求解码和请求入口，由同一命令按入站类型选择同类型回包。请求仅接受零字节，读取入口要求连接当前角色在世界中。只读工作单元保护精确会话，按该角色查持久成员关系，再按所属公会、日期和记录号倒序取最多 `50` 条。畸形请求、无在世界角色或无成员关系都返回拒绝且无历史记录。`0x0328` 保留成功位后固定 `50 × 44` 字节、空槽补零、`+36` 的点数及 `+40` 的类型 `3`。

代码范围内无阻断发现。现有编码测试覆盖 `0x02EA` 请求、单记录、空表和拒绝，并保留 `0x0328` 固定槽回归。独立调用 `dotnet test --no-restore` 在本执行环境未完成生成，A21 列表显示与重开结果仍以新构建实机结果为准。

审查时 [`0x02EA` 协议页](../../../../../../protocol/game/client-to-server/0x02EA.md)人工区仍称无观察记录，[协议清单](../../../../../../protocol/catalog.md)也仍将实机窗口指向 `0x0328`。已交主流程更新协议证据及索引。
