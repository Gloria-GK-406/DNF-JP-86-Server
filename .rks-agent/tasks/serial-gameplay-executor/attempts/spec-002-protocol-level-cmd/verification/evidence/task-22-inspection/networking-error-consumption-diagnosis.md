# Native Networking02：Sell unmappable 旧日志定位不再命中

首正式Net02的原199-case full原parallel/10秒case timeout未改，readonly原case等 `Gameplay inbound:` 至 TaskCanceledException，WaitForErrorAsync:88 / test:71。其真实wire是Sell selector1/type0016/containerFF/slot65，前面healthy第二连接已经响应原GameExit ACK（否则不会到line71）。同一raw真实记录 `Gameplay GameSellItemCommand failed: root ... source Connection, connection Game:01-00-00-00, chain Connection:GameSellItemCommand`，紧随原 InventorySlotMappingException / `255/65 has no business mapping` / mapper→newwhole line29。完整log/metadata及mapping-whole-log-excerpt和13匹配行保留。

Task21 final02同display通过：

  已通过 Dfo.Server.Networking.Tests.Integration.InventorySlotMappingFailureIntegrationTests.An_unmappable_slot_is_logged_on_its_connection_and_the_host_serves_the_next_connection [971 ms]

原test逐byte等HEAD，完整readonly before及唯一一字符串候选保diagnostics；没有改产品/测试/core/timeout/parallel。原legacy route由dispatcher建 `inbound:GameSellItem` job，whole route由CommandBus以GameSellItemCommand建job并默认记录/消费；入站外层不再重复该错误。异常类型/内容/真实connection仍正确，已知的变化是日志job身份及旧test搜索谓词过时。这是本case实证的新error日志消费边界，不归Dimension或TCP偶发。

最小候选：只把该case `ErrorsContaining("Gameplay inbound:")` 改为 `ErrorsContaining("Gameplay GameSellItemCommand failed:")`；其余原类型/255/65/第二真实连接精确ACK及单错误断言不变。必要时可增强实际源connection/jobchain检查，但要主控明确第11owned物理路径/Task计划amendment，不自扩白名单，不新增raw许可或改sharedcore。当前候选未执行/未宣称修复；受影响的完整Task完成与ledger闭账暂停，native仍自然结束。
