# A21 自动签到验收

独立验收库从原「测试工会」备份并迁移。原有推荐频道、GM 余额和捐赠记录仍可检查。签到按服务端本地日历日统计。

1. Client-26 用 GuildViewer 先进 CH10，打开公会信息。确认右侧今日签到显示 1/2，点蓝色信息按钮，明细只有 GuildViewer。
2. Client-25 用 GuildPilot 进 CH10。观察打开的公会信息是否变为 2/2，再点开今日签到明细，核对 GuildPilot 和 GuildViewer 均显示。
3. GuildPilot 返回选角。用 GuildViewer 重开明细，仍应为 2/2 和两个名字。
4. GuildPilot 当天重登或切频道，确认数量和名字均不重复。

自动化已验证服务端日期跨日、旧库迁移、双连接和 Host 重启。用户在独立服双端复测后回复「ok，符合预期」。
