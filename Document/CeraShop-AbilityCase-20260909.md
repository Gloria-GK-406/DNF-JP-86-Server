# 商城上衣属性与返回游戏修复（2026-09-09）

## 本次观察

用户确认黑钻不可用提示正常；购买仍失败，“退出”特指退出商城/返回游戏。

运行日志 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Logs/server-20260909-194235.stdout.log`：`Item 413500013 has no ability 2.`，随后 `CERA web ticket ... unavailable, request bytes=0`；没有新的退出商城请求。

## 上衣选项

装备 `equipment/character/knight/avatar/coat/chn_equ_upper_avatar_knight_coat_413500013.equ`（黄色白领丽人上衣）通过 `[ability case index] 1250` 引用 `skill/abilitydatas.dat`。目录商品650021/650022/650023对应7天450、30天990、永久2970点券。公共表case1250的选项2是 `[SKILL_LEVEL] [knight] 41 1`，合法选项序号有间隙，例如4和39不存在，不能按列表位置重新编号。

原实现只读取装备内联 `[avatar select ability]`，遗漏公共表，使非零合法选择被误拒绝。新解析同时支持四字段数值属性和五字段技能属性，按声明ID验证、保存；缺失/重复/不闭合的公共表及不完整选项整体拒绝，不接收部分解析结果。此处保存时装属性选项，不直接给角色购买技能等级。

真实PVF与SQLite回归：修复前属性2/40失败、属性0意外通过；修复后三种期限与不同选择均通过。证据在 [Verification/CeraShop-AbilityCase-20260909](Verification/CeraShop-AbilityCase-20260909)。

## 客户端等待标记

二进制身份与镜像同 [前次证据](../protocol/patterns/data/traces/a21-cera-feedback-20260909.md)。静态地址均为RVA（基址00400000）。

- CMD0041 handler `00D1A0F0`：`00D1A103..00D1A107` 检查success，失败直接去 `00D1A173` 返回，没有清理商城标记。
- CMD0040失败入口 `00D24820..00D2482D` 则显式调用 `0071BF60` 清标记。这两条失败路径并不等价，发送0041失败再加提示仍不足以恢复商城。
- `0071BF50` 设置商城对象+`20C20`=1；`0071BF60` 清零。`00744B85..00744B8B`、`0071FEFA..0071FF01` 在标记为1时跳过商城操作。
- 2026-09-09 只读进程内存：客户端PID41012，GameContext VA03A5C9B8指向03B67DC0，GameContext+8FAC指向商城44A80000，商城+20C20实际为`01`。未修改运行中内存。
- 通用dispatcher `012376FD..0123770E` 按cdecl传入4个参数并回收16字节，补丁必须原样转发这4个参数。

置信度：静态失败分支、偏移和当前标记值为Confirmed；补丁后的真实按钮行为仍待实机验证。

## 已获准部署的兼容补丁

已准备独立可选插件 `0150_CeraTicketFailureFix.dll`，仅挂接A21的CMD0041回调。先调用原处理器，仅在失败时清理商城等待标记；成功票据、协议载荷、玩家数据不改动。不改DNF.exe或PVF。

安装前校验目标handler指令，以及证明商城偏移的getter/clear函数字节；不匹配就不安装hook。插件通过现有ClientHack加载，主程序重启后生效。回退只需在客户端关闭后移除新增DLL，不替换原插件。

源码：[cera_ticket_failure_fix.c](../ClientHack/plugins/0150_cera_ticket_failure_fix/cera_ticket_failure_fix.c)。构建输出：`ClientHack/out/cera-feedback-win32/stage-a21/plugins/0150_CeraTicketFailureFix.dll`；SHA-256 `8EBA4EBCEC12C39DA3FF4FF4549E00F659EF4A0D6B9B7ACC63053E8974CC9532`。

已用VS2022 Win32 Release构建，native回归通过：四参数转发、成功分支保持等待、失败清理、空上下文保护、地址指令不匹配拒绝。构建与验证命令：

```powershell
cmake -S ClientHack -B ClientHack/out/cera-feedback-win32 -G 'Visual Studio 17 2022' -A Win32
cmake --build ClientHack/out/cera-feedback-win32 --config Release --target plugin_cera_ticket_failure_fix_a21 clienthack_cera_ticket_failure_fix_tests
ctest --test-dir ClientHack/out/cera-feedback-win32 -C Release -R clienthack_cera_ticket_failure_fix_tests --output-on-failure
```

用户在本次确认“允许部署，并关闭当前卡住的客户端”。部署时客户端进程已退出，因此没有强行结束进程。已将新增DLL复制到 `E:/Project/DNF/DNFClient-A21-protocol/plugins/0150_CeraTicketFailureFix.dll`，源/目标SHA-256一致；仅添加这一文件，重开客户端后加载。真实UI复测尚未完成。

## 完整验证与已知限制

- 全解决方案Debug测试首次：2664通过、1失败、2条件跳过。唯一失败为 `DarkKnightComboBookTests.WhatIsAnsweredIsWhatTheRowsHold` 在SQLite初始化时出现ObjectDisposedException；失败原始TRX保留，不声称首次全量通过，也不认定它已是已知仓库缺陷。
- 该失败用例单独复测通过；随后整个Application项目独立复检1246/1246通过。本次变更相关测试均通过，其他项目首次均通过；结果汇总见 [verification-summary.json](Verification/CeraShop-AbilityCase-20260909/verification-summary.json)。SQLite异常根因尚未证明。
- 原生插件测试1/1通过，覆盖ABI参数转发、地址不匹配拒绝、成功不改等待标记、失败解除等待、空上下文。
- Host Release构建0警告、0错误。重启前SQLite一致备份通过integrity_check：`Server/src/Dfo.Server.Host/bin/Debug/net10.0/Data/backups/before-cera-abilitycase-20260909-215341.db`。未重置余额或已购宠物蛋。
- 新Host PID34072，21:54:16启动；监听7001/10010/10011，标准错误为空。运行日志 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Logs/server-20260909-215416.stdout.log`，仍使用原玩家数据库和A21 PVF。

下一步实机复测：重新启动A21客户端，购买黄色白领丽人上衣并选择属性，再点黑钻、关闭提示、退出商城；以上UI结果不可由单元测试替代。
