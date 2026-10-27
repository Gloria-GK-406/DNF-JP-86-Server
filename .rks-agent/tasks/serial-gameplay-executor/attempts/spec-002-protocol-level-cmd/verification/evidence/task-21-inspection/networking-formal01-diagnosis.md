# Networking formal01：两个 Connection refused 的诊断边界

formal01在同source-freeze01自然完成Release/App/Protocol/Networking：build0/0，App5128仅准确旧2Dim、Protocol1773通过，Networking199=197通过+2失败。父runner依预设exit检查停止，尚未运行四controls；没有TERM或覆盖日志。

## 真实身份

完整两个failure block在networking-formal01-failure-blocks.txt，原console与metadata为verification/logs/task-21-formal-networking-01.*。

- BossTowerFrameIntegrationTests.Raw_move_loading_kill_and_result_hover_use_registered_boss_frames：SocketException Connection refused，TcpClient.CompleteConnectAsync→测试line61，host.StartAsync(line57)之后首个TCP连接。
- SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close：同SocketException Connection refused，CharacterizationHost.ConnectAsync154→EnterWorldAsync170→RunAsync153；尚未送达本例选角流程。

这不是两条Dimension内容基线，也不符合AGENTS已登记的SQLitePCL释放身份；不能直接归入。AGENTS所述满载socket可能假红举例是取消预算，并非本次明确根因。

## 只读追踪与假设检验

实际源码BossTower测试37-38/129-130、CharacterizationHost233-245均先用loopback TcpListener(0)选空闲端口后Stop，再以该端口配置host；Boss在StartAsync后直接Connect，Characterization先等待launcher HTTP再Connect game TCP。原完整host日志有“服务器已启动”等生命周期消息，但并行交织且未记录本失败case分配的动态端口，无法将具体端口/监听状态逐时关联。现有日志不能证明端口竞争、耗尽、IPv4策略或服务启动竞态，均不作确定归因。

一个可检验的假设是连接建立与并行启动时序有关；同freeze01、无源/环境/timeout/parallel/Skip变更，精确只跑这两目标作最小隔离。task-21-networking-isolated-01于19:04:29.388109～19:04:35.188479 UTC实际exit0、2/2通过；这证明相同构建目标可达，并不单独证明上述假设/rootcause。Networking与Host测试源均在nonowned库存，未修改；Task21产品只触排序3route/2whole及其metadata，不改变监听配置/连接建立路径。

## 后续证据

精确隔离后才按主控已授权，补新App emptycase的physical Same引用断言；这不是网络修复，不能解释连接恢复。freeze02只变该App测试的一条断言/实际输出，其余所有产品保持freeze01。随后重新Release+三full+四controls；最终结果以02实际为准。若Net02再出现失败，保直接证据而非盲重试。所有01/isolated原raw永久保留，未声称已修网络根因。
