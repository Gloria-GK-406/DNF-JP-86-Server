# 马战实施结果

基线 `b4bf8d0d01113c39962c885b44002453dcb740c6`，分支 `task/warehouse-storage`。T-001..004完成，生产目标冻结；未部署、未操作真实数据库或客户端。已有经验修复保持在基线内。

## Changed / Why

- 同一个活动实例提供管理、玩法和只读投影；活动日历使用 `joust event/0/0`，通用启停/日期和独立赛程设置分开。
- HTTP→领域→规则→typed PVF来源；PVF骑士、材料、奖励返回名称和ID。赛程版本、轮次完整规则/结果快照、投注键与一次性派奖保存SQLite。
- 择一币种跨同币槽扣除；等级、限额、封盘、已结算轮次和非法材料校验，扣料/投注与奖励/结算分别保持同事务一致性。关闭/到期仍履行已下注轮次的派奖。
- 原生查询、下注、历史及状态/投注/赛果通知，选角入场、在线变更和重复帧当前状态刷新完成接线。耗尽槽登记到原EffectOperationContext，已提交重发不会再次扣料；选角lease防旧角色响应污染。
- 专属GM弹窗只编辑每日赛程；PVF内容只读。冲突或结果未知保留草稿并要求重读核对；普通刷新外部修改也不能无提示覆盖。
- 预审边界修正：查询/下注补结算用瞬态标志刷新邮箱，不依赖返回的阶段；领域/API/UI拒绝零赛果窗口；归档旧币特许不绕过原有选角过期清理，文档明确使用有效测试实例。

## Checks

阶段实际结果见 [backend-result.md](backend-result.md)、[ui-result.md](ui-result.md)、[protocol-result.md](protocol-result.md)。最终接线阶段Application105/105、Protocol70/70、Host13/13、UI聚焦49/49及UI构建通过；此前完整UI137/137通过。所有阶段测试停止，交回写权。最终综合验证以 `verification/completion-evidence.md` 为准，不能把早期阶段检查提升为全任务完成证据。

## Coverage and Evidence

| 工作 | 对应条件 | 交付锚点 |
|---|---|---|
| T-001 静态证据 | SC-1,6 | protocol-preparation.md、七条protocol人工记录与catalog |
| T-002 后端 | SC-1..5,7 | backend-result.md、真实SQLite/PVF集成和HTTP测试 |
| T-003 原生及通知 | SC-1,3..7 | protocol-result.md、dispatcher/codec/maintenance测试 |
| T-004 专属UI | SC-2,7 | ui-result.md、JoustSettings/Events交互测试 |
| T-005 综合交付 | SC-1..7 | 独立完整结果review、fresh综合checks与一次本地结果提交 |

## Remaining

T-005独立审查approved，最终fresh相关650/650与两端构建通过，见 [最终验证](verification/completion-evidence.md)。开发候选随一次本地接受提交封存；原生窗口、黑骑士、阶段业务映射、动画/倒计时和真实领奖单列于 [Human-Test-List.md](Human-Test-List.md)，尚未ClientVerified。wire不携带可编辑赛程时长，不能声称任意短赛程原生显示精确；本任务不部署。
