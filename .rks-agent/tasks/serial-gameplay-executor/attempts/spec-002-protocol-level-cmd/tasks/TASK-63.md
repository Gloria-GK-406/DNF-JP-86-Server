# TASK-63 — 定时来源与日切分轮

承接 T-010 和用户流程 §8，在 HTTP／GM 批次提交后开始。迁现存十九个 pending timer 来源，并同步迁入共享同一推进体的 Joust 启动恢复，共二十条来源；已闭合的两塔不重复计数，其他来源留下一批。F-07 的扫描、逐会话子 CMD 与主题状态一起实施。

## 合同与计划

定时源统一 typed 内部 CMD 入口，保原轮次合并、手动调用、唤醒、截止时间、取消、链与排空。真实业务从生产者迁到 CMD，删除失去最后消费者的 fine。扫描只返回当前周期及不可变精确会话 ID；生产者在执行器外逐个提交，子 CMD 按 FIFO 重新读当前事实。旧周期、退场和替换不能登记或推进状态，下一轮能补齐；busy 必须覆盖整轮。

去重状态归领域服务并保每主题的原含义：配额只按周期，Premium 保完整规范化权益指纹和前次激活标记、同周期变更及清零帧顺序，Fatigue 保完整余额比较。只有原实际登记结果认可且当前周期、精确会话仍有效才更新水位；目标不伪造来源连接。原逐目标业务失败类别及 Warning 保留，罕见基础设施路径按 VC-B40；不得清 sticky 或通过包装原队列／bus 冒闭合。需要新能力由主控当场裁定并登记 backlog。

先复用强测试，只给原未覆盖业务补旧 green；F-07 新行为用真实排队、跨周期、替换及重复 tick 守护。冻结后 fresh Release 与八项目正式全量，原 require_escalated、并行、5m、detailed、DFO，精确核原失败与跳过、strict 和八反例。仅更新实际迁完账行，AGENTS 只名单，Result 三行，不独立评审、不外部写。

## Result（Outcome: done）

迁移账行：20个来源与12个最后消费者fine闭合；来源66 closed／7 pending／12 retained，fine197 closed／7 pending，原ID／original／顺序和非目标行不变。
行为变化：F-07按真实FIFO分轮，旧RED／新GREEN；保三种主题水位及实际登记、原业务拒绝／帧序／关闭，罕见基础设施按VC-B40。其余7来源留后继。
新增能力或注入类别：主控批准typed producer、三个领域状态边界、具名RO取消／登记事实／scope标识，七单元复用WriteResult。fresh Release零警告错误；两轮组合9801通过、2原Dim、18原跳过，新增18删0，DFO67＋7、strict及八坏例通过；详task63-verification。
