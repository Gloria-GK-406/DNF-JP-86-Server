---
kind: rks-review
verdict: approved
---
# 绝望之塔独立结果评审

日期：2026-10-11。评审对象：工作区 `.worktrees/despair-tower` 相对基线 `b2c3134e856183cf000a1b3ef6dd769472a11f30` 的完整未提交实现、测试与协议/实现文档。独立评审者未参与实现，未执行 dotnet 或部署。

依据：TaskContract revision 1、spec revision 1、AGENTS.md，以及 reviewing-work 的 Code / Deliverable 标准。范围为源代码与对应说明；运行部署和真实客户端验收仍由后续完成验证负责。本评审没有发现有证据的阻断性缺陷。

## 决定性检查

- 入场先按持久化最高层选择下一层，塔内任意 DGN 都经 CMD 专用路由；单人、普通模式、最低等级、100 层上限和每日三次通关限制在领域入口判定。首次免费与同日后续邀请函费用通过同一入场凭证消费；布局/Cell 失败由原凭证执行边界要求回滚，没有新增独立扣票路径。
- 结算要求匹配角色、账号、run ID、塔布局和服务器 ClearOpen；长期层数条件更新使同层和同 run 重复结算不能再次发奖。奖励、楼层、每日通关计数及既有经验写入在命令事务内完成；满包走系统邮件，并沿用新装备实例的预备附件入口。
- 塔结算保留普通结果/经验外壳，跳过普通卡面生成及翻牌调度。ScoreScroll 不再为塔安排卡牌。SelectCard 的运行态卡面前置条件会拒绝塔内的普通翻牌请求，没有从空展示卡面获得额外奖励的入口。
- 015C 编码固定十个有符号物品/数量对，物品状态先于奖励面板通知。015B 只在 10..90 的整十层紧随 START_MAP 投递基础/当前层两个包，使用现有外观映射。注册、消息类型和编码器接线一致。
- 配额新 family 在持久化与 DirectAsync 订阅中完整接入，同时明确跳过没有证据的塔配额 NOTI。现有 Consumable/Premium/Mirror/Dimension 分支保留。

## 证据边界与待验证项

领域/事务/持久化测试属于 CurrentImplementation；100 层真实 PVF/Cell 创建不等于 100 层实机流程验收。015B/015C 前缀依据 ClientStatic，完整尾部参考 UpstreamReference；文档没有将其升级为 WireCaptured 或 ClientVerified。每日首入免费、邀请函重试、最多三层及 100 层停止的本服策略已明确记录。

主代理提供的更新测试进度：绝望塔定向 6/6 通过（含 100 层创建、每日重置、事务回滚和满包邮件）；Protocol 1441、Networking 91、Persistence 468、Infrastructure 872 pass / 2 existing skip、Http 50、Host 159、Pvf 80 通过。Application 首轮满包用例的临时 SQLite 删除 IOException 已定位到邮件查询使用独立只读连接池；测试末尾按相同只读连接字符串清理对应 pool，未修改产品代码，隔离测试已通过。Application 全量复跑仍在进行，本评审未将其写为已通过。

已复核并关闭此前非阻断维护风险：两个协议记录恢复原自动生成区、客户端 SHA 与定位元数据；新增完整布局位于 machine:end 后的人工区，并明确完整布局以人工核对结果为准。产品关键文件 SHA 与原评审一致，测试当前指纹确认包含只读连接池清理；该修正没有放宽奖励、邮件或楼层断言。

## 目标指纹

| 文件 | SHA-256 |
|---|---|
| DespairTowerDomainService.cs | ee2ab1333bfb86a07c59ebcf0bf9d9382a556bbf4cb0e5a5953d02e390dd4d09 |
| DespairTowerRuleService.cs | 921869536c918538eade270772fed27cffd455db75ad909c568872c0abe7b9bf |
| DespairTowerProjection.cs | 68beeef80bffffeaa21dbd9a228ba0a12494c23c6c73ef95383312f5be292d62 |
| DungeonClearSlice.cs | 90e42e8b1b380e828af83974900ce70dc0570bf8347779a62f81336f7e9e4fec |
| GrantClearExperienceCommand.cs | c74ca1cf2bccffa331e8bd968006d814892ed2c9ede34d21b9872fadf26bf795 |
| DespairTowerTests.cs | 089c405714ec3d7e1fed3eea39e339892c0e8dd823c4924429fe28fbd7886f44 |
