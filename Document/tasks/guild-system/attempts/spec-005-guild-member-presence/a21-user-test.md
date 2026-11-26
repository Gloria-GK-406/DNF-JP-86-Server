# A21 公会成员上下线验收

隔离服保留「测试工会」、GuildPilot、GuildViewer 和此前的金币捐赠。两端选择 CH10。

1. Client-26 用 GuildViewer 先进入游戏。打开「公会成员」，勾选「显示未在线用户」，看到离线的 GuildPilot 后保持名单打开。
2. Client-25 选择 GuildPilot 进入游戏。GuildViewer 应收到「玩家[GuildPilot]进入10频道」，名单里的 GuildPilot 应立即显示「在线」。
3. Client-25 返回角色选择。GuildViewer 应收到「玩家[GuildPilot]已退出游戏」，同一行应立即变为离线或显示离线时间。全过程保持名单打开。
4. 再让 GuildPilot 进入游戏，然后直接关闭 Client-25。GuildViewer 应看到同样的离线提示和名单变化。

验收以 GuildViewer 画面为准。A21 对同一角色同一种状态的重复提示有两秒抑制。
