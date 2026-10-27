# 主控两项 observer／验证口径澄清

这是本次实际执行期间主控明确的有界技术澄清；不修改产品、测试、sourcefreeze、Task WHAT或原raw。required独立review仍判断证据充分性，不以主控认可代结果批准。

1. 原8类坏副本语义优先。原工具 telemetry-misclassified／closed-transition-without-destination 必须修改真正对应telemetry／transition行。Task“坏副本只变本次2ID”是规划措辞冲突；临时反例按原工具各自所需行修改、在新E20落盘并真实拒绝。正式canonical ledger.json/md仍且只能变message66533及原CollectionBoxCommand两ID，actualdiff逐值证明其它rows/baseline不变。原工具不改、不造较弱2ID伪例、不重写Task原计划。

2. 四oracle跨独立run的随机领域事件身份只做具名关系校准。owning源：Application/DomainEvents/EventService.cs:11 为每provider DomainEventInstance.Id=Guid.NewGuid，:30每Enqueue新EventId，:29及:86每事务EventBatch.Id。Persistence/DomainEvents/SqliteDomainEventPersistence.cs:15-22将三GUID原值写domain_events，:26-32将原EventId写delivery外键及原OccurredAt到next_attempt_at。它们跨run本来不同；不是业务字段变更。

旧／新样本在oracle-before-after-checks.json保全部原行与三GUID双射：seed sequence1、put sequence2/3、共享batch、同provider origin；native rollback瞬态2/3与retry重新生成的2/3仍不同身份，不能强合并。主控要求且比较器实际验证：有效非空GUID；EventId独立；Delivery外键无孤儿；sequence对应payload/type/schema/time全相等；三类ID各自双射并跨同oracle各phase一致；batch分组和共同origin保持。SQL按随机GUID排序的观测按sequence/subscriber稳定比较，不外推为wire顺序变化。每行/列/数量/状态/NULL/BLOB及原GUID全部保；绝不按任意GUID正则忽略字段。

原Item DB仅created_at/updated_at两列跨run保真实UTC窗口与created≤updated关系，72个字段出现保原值/row/run/path。其它item列/core/可空扩展/UID/slots/selection/bytes逐值相等。same-run SQL/native rollback仍原Assert.Equal全部字段精确，无ID或时间豁免。首次直接相等比较失败脚本及真实复现diagnostic保留，后继新r2比较器校准不冒原时点已执行。
