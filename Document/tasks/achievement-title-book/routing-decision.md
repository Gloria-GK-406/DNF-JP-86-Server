# Routing decision

- 授权：TaskContract revision 1，用户明确要求任务契约、实现及部署。
- Ready：事件协作和职责已达成一致，客户端实机验收由用户完成。
- SDD value：必须避免将称号奖励塞回成就组件、客户端上报任意完成以及存取丢失属性。Spec 固定职责和验收，Plan 先核对 PVF/协议再实施存储和事件，独立结果审查检查遗漏。相较直接边写边决定，提前确认物品槽位与事件事务依赖避免持久化格式和业务接线返工，值得增加短计划。
- Structure：Single Spec；成就完成到称号存取是一个玩家可观察的完整闭环。
- Controller：sdd-standard / planned-delivery。Lite 没有计划，无法提前明确 PVF 条件与权威业务事实覆盖及协议布局的依赖。无已证实的不可恢复数据丢失路径；复用事务和备份，因此不升级 Governed。
- Workspace：复用 E:/Project/DNF/DNF-JP-86-Server/.worktrees/abyss-research，task/abyss-research，HEAD 774f7b3581be1eeb1689b9220f4bf8ca31852caf。用户要求继续原分支；保留 protocol/game/client-to-server/0x0010.md 与 Document/abyss-research-20261007/{manual-hell-refusal-ui-lock,random-hell-deferred-issue}.md 的原始内容，排除接受提交。
- Reroute：若真实 PVF/客户端语义否定已接受闭环、无法校验可测试条件，先报告证据并重新收敛；不伪造奖励或改客户端绕过。
