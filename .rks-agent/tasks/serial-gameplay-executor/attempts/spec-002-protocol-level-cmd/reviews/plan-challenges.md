# spec-002 高层计划挑战集（2026-10-24）

- 独立生成b_plan_challenges，generating-adversarial-challenges；目标首份ready plan.md174行、task-units，SpecB1 Round2 approved、Contractr1、baseline10a7218a。
- 只读Plan/Spec/实际刷新源码，不写产品不跑测试不出review verdict。只保留有源码因果的这一项；普通Task细节不机械升高风险。

## CH-1：统一周期水位会吞Premium同周期权益变化

- Risk: high-risk
- Failure and impact: Plan98规定相同topic/session/period不可二次刷新，会在当天首次登记后跳过同周期权益新增/变更/到期，尤其水晶/成长到期依赖清零NOTI；客户端可能保留失效权益到下周期。超出VCB3仅FIFO交错的范围。现Plan跨周期/session/重复tick检查不能区分重复投影与同周期真权益变化，结构检查也发现不了合法跳过。
- Evidence: PremiumDailyRefreshService.cs44水位为(Period,Active,CrystalActive,GrowthActive)，62～71比较完整权益指纹，74～84靠前次Active发清零并更新；DimensionQuotaDailyRefreshService18～38才是单纯周期水位。Plan102允许细化键但不得削弱同周期不可二刷，实际矛盾。
- Minimum correction: 通用边界改为沿主题现有投影状态去重；配额保周期，Premium保完整权益指纹及前次激活状态。scan仍只period/exact identity。T010加同周期激活→到期必须按原序清零及状态、下一同态轮不重复的区分例。不新造锁/审计/receipt。
