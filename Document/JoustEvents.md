# 骑士马战大竞猜使用说明

原生04B8是带公告和自动查询副作用的状态事件，不能作为普通数据刷新重复发送。选角和轮次/阶段变化发送状态；成功查询、下注、重放与通用活动刷新仅发送04BA→04B9→可选04BB详情，关闭查询只回ACK6。旧版查询回传state1会在竞猜窗口打开时形成查询循环并刷屏；静态消费证据见protocol中的04E6与NOTI04B8记录。

服务器启动完成前同步恢复当前轮次，阶段按北京时间与轮次原定开始时间的差值计算，不从开服时刻重新计时。活动中途开始或启用时，只要当前时间在活动有效期及当日赛程内，也可加入已开始的轮次；其原定开始时间不变。已有骑士、赛果和赛程快照直接复用，已有未结算投注继续补结算；不补造停机期间从未生成的历史轮次。默认12:00轮次在12:10为投注、13:30锁定、13:40八进四比赛、13:43:20半决赛、13:46:40决赛、13:50结果展示，14:00进入下一轮。活动未启用、未到有效期、已过期或不在每日赛程时不创建当前轮次。启动恢复异常直接阻止服务ready，修复故障后的再次启动继续恢复；结算邮件与完成标记同事务，重启不重复派奖。

GM 活动列表配置名称、说明、有效期和启停；“设置活动”打开马战专属赛程弹窗。玩法事件ID2365为服务器/参考选择。开启且有效、PVF可用时，EVENT_INFO提供`joust event/0/0`。原生客户端解析两个斜杠后才取首段resolver；RVA01018410/010186B0拆分，01018550复制首段，0100F030把joust event映射到窗口01FC（01013968→01012F92）。这条链属于ClientStatic，窗口实机仍待验收。

专属 API：`GET /api/gm/v1/events/joust/settings`；`POST /api/gm/v1/events/joust/settings` body `{expectedRevision,schedule}`。需要既有GM全权限登录和CSRF。schedule为`dailyStartTime/roundsPerDay/roundIntervalMinutes/bettingMinutes/lockMinutes/stageSeconds`；默认`10:00/7/120/90/10/200`，按UTC+8，验证每日1..24期、不跨午夜、全部阶段不超过轮长。保存返回revision；已生成轮次保留设置和内容快照，未来轮次采用新设置。

PVF只读源`event/chn_event/chn_joust.evt`：最低17级、每轮个人总投注最多1000。普通骑士七位和特殊骑士一位随机参赛，8→4→2→1结果保存后不重抽。两种币490002916/490700609可任选一种跨同币槽扣除，优先原生指定物理槽；不会混扣另一种币。参与奖490002925数量为个人下注总额；中奖奖490002916数量向下取整(胜者个人下注×一位小数倍率)。只有Result开始才同事务邮件派奖。停用、到期、重启仍结算已有投注；已settled轮次即使服务器时间回调也不能再下注。

原生CMD04E6查询和04E8历史均14 opaque；04E7是23B，槽号i32；opaque不是业务ID。下注用物理连接128frame有界账本识别相同seq+完整body的重复帧，正常seq回绕有新ordinal，属于新下注。域键保存物理身份/selection generation/ordinal。新连接上内容相同的新请求也属于新请求，不能宣称跨重连任意重复点击自动去重。已提交重复帧只刷新当前库存与当前Joust（赔率/个人投注/阶段），不重复旧ACK或业务；TCP发送失败不是交易回滚证明。结果不确定时传播并由既有会话安全机制处理，不伪装成安全失败ACK。

通知构包3/90/46/31B，ACK历史固定3505B（包括flag1非零错误）。入场读取只读Joust投影；每秒后台事务推进提交后仅在Active/RoundId/Phase变化推送，成功下注主动更新所有在线选角角色赔率和个人状态。查询补派奖后刷新邮件计数；通知Compose仅只读，不补推进。

原生边界：wire没有可编辑StageSeconds、轮间隔或开始时间。服务器赛程是权威，但原生本地倒计时/动画可能硬编码；短赛程是否能及时中断/同步必须实机观察，不能宣称任意StageSeconds展示精确，也不宣称客户端静态活动日期动态更新。State映射Inactive0/Betting1/Locked3/比赛4/Result2、特殊隐藏flag、Aux0首项为明确ServerPolicy；State0..5消费细节、Stage3最终文本仍有Unknown。Win/Loss字段A/B首版0；Aux实际是胜负内容表有效index，PVF各28整数=4×7，0合法，不是padding。本次不修改PVF或客户端。

自动检查见[协议阶段报告](tasks/event-joust/protocol-result.md)，人工步骤见[验收清单](tasks/event-joust/Human-Test-List.md)。当前ClientStatic/CurrentImplementation，不是WireCaptured或ClientVerified。

阶段映射首版沿Reference：Closed0/Betting1/StopBetting3/Racing4/ResultReview2；静态scene切换仅支持消费链，不能证明业务语义。04BBStage0..3推动对应比赛动画。State5备用路径保持Unknown，本实现不发送5。

2026-10-06审查修正：下注+封盘+三段比赛总时长必须严格小于每期间隔，保留正时长Result展示窗口；刚好占满轮长的赛程在领域/API/UI一致拒绝。查询补派奖标志独立于返回阶段，跨轮或日程外补结算也刷新邮件计数。

活动币测试使用有效实例（本活动ExpireTime=0奖励模板或GM明确配置的有效期）。领域对归档2016时间的活动专属特许，不会阻止既有选角ExpiredCharacterItemCleanup清理历史过期库存；不承诺旧币重登仍保留，不改全局清理。
