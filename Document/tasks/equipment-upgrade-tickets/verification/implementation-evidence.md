# 实现与检查证据

- 按0x0050所选材料槽识别PVF券，识别后不回退NPC。规则给出目标等级/成功率，effect计划生成消费与设置；概率失败只消费券。
- PVF具名字段读取完整4值声明；坏形状/范围/重复/双类型抛异常；合法未知模式返回明确UnsupportedContent。
- `red-coupon-correct-slot.trx`为修正物理槽定位后的有效RED（普通NPC材料不足）。此前`red-coupon.trx`定位错误，不作为行为RED。
- `tickets-related.trx`：125通过，0失败（添加额外准入边界前）；`tickets-content.trx`：25通过，0失败。最终当前版本以final-*结果为准。
- 中途错误分别是测试枚举/属性命名、测试基础信息构造顺序、通知数量偏移；保留日志但不升级为产品失败结论。
- 完整检查及独立审查后部署，人工验收参见../human-test-list.md。

## 完整回归首轮异常
首轮Application为2625通过、1失败；失败是未改动的通知墙钟耗时断言（3877ms超过2.5s）。记录保存在first-full-Application.trx/log与first-final-checks.json。NotificationBus仍调用CancelAfter(options.PerRecipientTimeout)，此测试传入50ms；单项原样复验notification-deadline-isolated.trx通过，164ms。支持测试受运行时调度影响的解释，但未做调度trace，不把原因标为确定，也不称为已经确认的基线失败。没有放宽断言或修改通知代码；全套复验结果另见final-*。
