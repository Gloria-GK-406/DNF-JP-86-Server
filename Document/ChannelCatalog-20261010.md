# 全频道目录开放（2026-10-10）

用户授权：按上游开放全部有效频道，明确包含决斗频道。此项是目录、监听、登录环境和现有PVP技能投影的开放；不表示所有频道专属玩法均已实现。

## 来源与行为

UpstreamStatic：`ServerS4A21/Server/DfoServer/Network/Core/GameNetworkConfig.cs`与`Network/Protocol/ChannelProtocolHandler.cs`从`channel_info.etc`的server组1生成频道，端口10000+ID，普通环境1，raid环境23，决斗环境8/13/24。raid组type32的CH201不被原生客户端接受，上游跳过该声明。

当前启动组合根以同一脚本生成完整Channel:Channels与Networking:GamePorts，替换旧数组，避免目录广告了频道却没有对应监听。保留公共IP；UseScriptCatalog=false可恢复原有显式配置和NAT/独立端口映射。读取一次形成启动快照，修改后需重启。

- 普通/特殊：CH1、6、10～21、100、200，共16个；CH100原有70级限制及临时城镇位置策略保留。
- 决斗：CH50～54（环境24）、60～64（环境8）、68与70（环境13），共12个。
- 共28个有效频道，CH201排除。公共目录名#ch.ID，由客户端脚本显示本地化名称。
- 登录CMD0001环境按当前连接监听对应频道返回；未知/旧显式配置未声明环境时沿用GameLogin:Environment。
- 专属城镇沿用上游：决斗town10、CH200 town19、CH100 town17；这些位置不覆盖持久化旅店，普通频道不恢复到这些城镇。
- 所有决斗类型使用现有PVP技能状态与相邻入场投影分支；不把50～64、70误当普通PVE频道。
- 容量按上游：CH20/21为150、CH200为250、其他100。容量为目录声明，未增加实时负载统计或完整决斗/raid业务。

配置：Channel:UseScriptCatalog=true、Channel:IncludePvpChannels=true。显式目录仍作为关闭脚本模式时的兼容配置，自动模式完全替换其数组。

## 验证边界

自动检查覆盖真实脚本28频道、默认排除PVP、无效raid/重复/group隔离、数组完整替换与其他配置保留、监听身份与登录环境、现有CH100与登录回归。部署回执放在仓库根`artifacts/channel-catalog-20261010/activation.json`。实机频道列表、普通↔特殊↔决斗切换需用户验收，不能由监听就绪替代。

本次检查：Application频道/登录/位置/PVP/选角相关104项，Protocol登录/城镇/选角/PVP相关117项，Infrastructure频道/公共端点相关18项，以及Host启动/真实HTTP与TCP/脚本投影相关25项通过，无失败/跳过。仅针对变更相关范围，没有宣称全仓测试完成。
