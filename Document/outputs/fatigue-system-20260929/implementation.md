# 普通地下城疲劳闭环（2026-09-29）

## 范围与规则

CurrentImplementation；本轮未部署、未作实机验收。

- 上限 = `Fatigue:BaseTotal`（156）+ 账号黑钻 `GetExtraFatigueAsync`（默认32，无权益为0）。
- 普通副本先检查所有参与者余额，任一为0则整队拒绝，尚未创建房间或扣点。单人拒绝码0x16，组队0xAD携带队员槽；A21具体拒绝文案待实测。
- 入场首房扣1；每个首次进入的新房扣1；重访、重复/过期换房不扣。余额最低0，已入场可继续走完整图。
- PVF `[no fatigue]` 经 `DungeonDefinition.ConsumesFatigue`、`DungeonLayout.ConsumesFatigue` 传到领域层；免疲劳副本跳过余额门与扣点。
- `character_fatigue` 每角色一行：`used`、`consumed_today`、`updated_at`（完整Unix毫秒）。无记录=未使用。
- `SpendAsync` 扣实际可扣点数并累加 `ConsumedToday`；`RefundAsync` 恢复余额但不减累计消耗。均要求领域事务。
- 复用 `Game:DailyResetTime`，默认UTC+8的06:00；记录时间不在当前业务区间则按0读取，下次写入覆盖旧记录。无需每日清表、离线计时器或历史日流水；重启不会恢复当天已消耗值。
- 一个全局分钟调度器刷新在线角色的变化值，跨日最多约一分钟更新UI；业务读取在边界立即生效。会话身份校验和账号过渡门阻止旧会话投递。

## 边界与事务

`DungeonVisitDomainService` 负责检查/扣除；`TransactionalDungeonVisitDomainService` 复用全局写入门与领域事务。入场/移动聚合先创建预览并登记事务参与者，SQLite成功后发布，回滚不发布房间。
协议层不再重复扣点，只在入场/移动后推送权威余额。业务入场提交后发生的网络发送失败或掉线不退还已扣点；网络与数据库不是分布式事务。

登录CMD0004 body+11/+13/+15投影为已用/上限/累计，NOTI0024采用同一三字段加两个0尾字段，共10字节。此前登录首字段固定0已修复，避免重登显示满疲劳。第三字段累计用途是70Reference迁移，A21增量UI与累计消费效果仍须实测，不能当作客户端已证。

未实现：燃烧疲劳（双扣与经验倍率）、疲劳药使用入口、累计150赠币、塔/锦标赛等专门计次规则。本轮只尊重数据中已有免疲劳标志。恢复API已为后续药物预留。

## 70Reference与证据边界

研究对象：`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。

- `CUser::CheckFatigue` 0x08656500：已用与上限比较；`CParty::CheckMemberFatigue` 0x0859D5E8检查成员。
- `CUser::FatigueUp` 0x08655C60：余额消耗与累计消耗分离，余额封顶；`gainCurCharacFatigue` 0x0864F7FE恢复余额，不倒扣累计。
- `RecoverFatigue` 0x08657ADA、`TimerFatigueReset` 0x08633A04、`GameWorld::ResetAllFatigue` 0x086CC3EC，以及登录 `Inter_LoadEtc::SetFatigue` 0x084C311A，支撑统一重置与离线补算思路。
- `check_visited_map` 0x08307D7C → `changePartyMemberInfoOnMoveMap` 0x085A8E3C：访问记录区分新房与重访。
- 本地70样本及Docker模板均存在普通入场/移动 `FatigueUp(0)`，属于修改过的参考样本，**不能据此复制0消耗**。普通房1点采用用户确定的业务规则。
- 70 `SendFatigue` 0x08656540写4个u16（8B），A21静态读取5个u16（10B），未照搬70包长度。A21既有选角实测证明第二字段减第一字段是剩余值；见协议NOTI0024。

## 实机验收顺序

1. 记录城镇疲劳，进入普通副本，应减1；进入新房再减1；回旧房不减。
2. 退回城镇、换角色并重登，疲劳保持；后续可重启验证持久化。
3. 测试角色置剩1：可入场变0，继续走图不受阻；回城后再次入场被拒。
4. 组队含一个0疲劳角色：全队拒绝，其他成员不扣点；都有余额时新房各扣1。
5. 受控时钟跨业务日：在线刷新与离线重登恢复；免疲劳地图按PVF标志单独验证。

运行中的服务器和数据库本轮均未改动；部署后再由用户完成上述GUI验收。
