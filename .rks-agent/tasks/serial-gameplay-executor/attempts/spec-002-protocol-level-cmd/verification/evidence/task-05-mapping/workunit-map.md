# TASK-05 Channel 迁移对应

| 原入口／实际单元 | 新入口／实际单元 | 保留证据 |
|---|---|---|
| Connect legacy handler在原inbound job读取provider日期key并登记同连接；没有旧细CMD/事务/selected参与者 | 薄handler交同request/context到ChannelConnectCommand，bus一次job，TimeProvider.GetLocalNow→共享Protocol纯函数→原ChannelConnectResponse登记；同样无事务 | 原handler/provider快照，三字面量key向量（原20260810000006、UTC±zone跨日及作业内Thai culture），UTC坏变异红→恢复绿；真实0B→0C/36Bbody/header |
| Script legacy handler→旧Direct GetChannelScriptCommand→IChannelDirectoryDomainService.ReadScriptAsync→返回bytes→登记同连接；UoW Direct放行无事务 | 薄handler→同bus ChannelScriptCommand→同domain ReadScriptAsync→原ChannelScriptResponse登记；无事务 | 原Get/handler快照，原查询bytes断言迁到真实CMD/outbox+同连接，原TCP解密bytes和padding保留 |
| Info legacy handler→旧Direct GetChannelInfoCommand→同domain ChannelsAsync→返回catalog→登记同连接；无事务 | 薄handler→同bus ChannelInfoCommand→同domain ChannelsAsync→原ChannelInfoResponse登记；无事务 | 原Get/handler快照，catalog断言迁到真实CMD/outbox+同连接，原TCP每字段保留 |

所有消息/decoder/encoder未改；当日key算法仍InvariantCulture、本地日期yyyyMMdd'000006'、14字符，不是hash。provider和Connect共用纯算法一处；其他三个响应编码器仍注入原provider。没有新增ReadOnly/Write边界、领域内容源或选择会话参与者；未调用旧bus/旧Get/其它CMD。旧Get的result类型断言因API删除而退出，bytes/catalog/原连接语义在新CMD和线级入口保留，原ScriptVersion及F129两个区分例不变。

仅闭合三消息及两旧命令；已有Task04四closed行保持原JSON值，baseline/original不改。其余业务155消息、旧命令200、执行器来源72仍pending。无LOGIN/entry policy/roster/effect/Main迁移，scope双故障组合本批无事务/selected参与者不适用。
