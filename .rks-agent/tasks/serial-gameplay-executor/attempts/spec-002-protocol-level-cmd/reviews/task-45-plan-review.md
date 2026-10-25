# TASK-45 / P9 Plan Review R1

```yaml
kind: rks-review
verdict: changes_required
remediation: material
remediation_target: spec
```

**结论：revise。** 技术方案和拟定验证足以支持这个窄接缝；阻断项只有当前 Spec 的封闭能力类别不包含调度唤醒，不能仅以 Plan 清单新增实现。

**对象与边界。** 审查 `plan.md` P9 ready R1（Planning Choice、§5、P9末节）及 `tasks/TASK-45.md` revision 2；基线 `cc212b99`。适用 `using-rks-agent` 的有界委派规则、`reviewing-work/references/plan.md`、process-reset §3～4、根契约 r4、Spec B6 与实际 AGENTS。只评 P9 增量，不评价 executor 正在修改的产品，不运行测试，不批准 TASK-45 Result。

**F1 — Spec RQ-009 与新增类别冲突。** RQ-009 明文“CMD 注入面只含”领域形状、既有例外、发件箱登记、关闭、投影登记、槽位映射、日志以及“只管理当前 CMD 显式工作单元”的机制能力；紧接的注入说明再次限定“这是明示的能力范围，不是任意 DI 豁免”。`IAltarSchedulerWakeSignal` 既不登记投影，也不管理当前工作单元，而是唤醒外部 scheduler；“具体机制能力由 Plan 具名说明”允许确定接口，不能消解此前的类别封闭。原父 RQ-070(b) 亦有具名协议边界范围。仅在 Plan §5 加白名单会令实现通过改后的反射测试，却违反仍生效的 Spec 验收边界。

最小修正：主控在 B 的 RQ-009 及其注入约束说明中明确增加“复用既有状态、仅同步无参唤醒原调度生产者”的能力，写明不读写世界、不直接入队、不暴露等待/任意调度、不接 callback/request，说明对父约束的有限补充；保留根、中心方案及其他 RQ/AC。按 process-reset §4 对这项 Spec 能力边界作有界独立审查即可，不需要挑战或新用户审批。实现时同步 AGENTS 的现状能力清单及精确类型豁免理由。`material/spec` 表示缺口归属上游能力约束，不表示要求扩大工程或改变中心方案。

**技术依据与验证判断。** `MonsterDeathSlice.cs @ cc212b99:455–490` 的受保护段先处理 creature/clear，再 death 投影、clear 投影，最后条件 Wake；`AltarProjection.cs:68–72` 的 Wake 只委托原信号。`AltarScheduler.cs:11–27` 是容量 1、DropWrite 的唯一 Channel，Wait 保留取消与最小 50ms deadline；同文件 scheduler 的 timer 作业及合并机制独立。`ProtocolModule.cs:274–277` 已注册原 singleton；alias 同对象无需新增状态。`NetworkMessageDispatcher.cs:59–67` 的新薄 handler 位于 queue 外，因此移到 SendAsync 返回后无法保持原作业内时点。新增窄接口、原地调用、精确 Type 白名单及负例是合理的最小实现，无需旧 Delivery、假 DomainService 或可执行输入。

P9 已要求旧新正式作业时点、成功投影后的 Wake、投影失败不 Wake、whole scope 失败前已经 Wake、同实例/合并/取消/deadline及白名单正反例，冻结后八项目 fresh full，足以进入实施。后续证据必须区分旧 fine Dispose 与新 whole Dispose 的真实发生位置，不能把 scope 生命周期变化写成旧新时点相同；这属于 TASK-45 已要求列明的迁移差异。Timer 来源保持 pending，未将该接缝视为调度器迁移完成。无其它阻断项。

## R2 — B7 / P9 有界复核

### Spec B7 ready R1

```yaml
kind: rks-review
verdict: approved
```

范围为 `spec.md` 的 RQ-009、注入约束专节、AC-001 和 B7 修订记录；适用 `reviewing-work/references/spec.md`，其余根／子契约及已批准范围沿 R1 证据，不重开。**R1 F1 已解决**：能力类别现由 Spec 明确授权，限原祭坛生产者、原用例位置、同步无参且无需等待的提示；世界读写、命令／请求／回调／业务快照、入队／等待 CMD、任意调度／取消／等待均被排除，不授予其它技术注入类别。AC-001 将正反类型判据、当前作业内投影后时点、whole 清理前已唤醒／投影失败不唤醒及同实例／合并／deadline／取消列为可判定结果。该有限增量明确补充父注入约束，保持根的单执行器、薄 handler、既有事务和事件边界；未发现需改根或中心方案的缺口。无未解决阻断项，本批准只覆盖 B7 增量的可实施／可验收性。

### Plan P9 ready R2 / TASK-45 revision 3

```yaml
kind: rks-review
verdict: approved
```

在上述 B7 approved 后，按 `reviewing-work/references/plan.md` 复核 Planning Choice、§5、P9 末节与 Task revision 3 的承接。R1 的技术证据和验证判断继续适用：`IAltarSchedulerWakeSignal.Wake()` 由原 `AltarWakeSignal` 实现，DI alias 同 singleton，仅精确接口获具名豁免，原完整 signal、相似接口、Delivery 与 queue 仍拒绝；调用留在原受保护 body／try 的成功投影后，原 scheduler 的等待与状态归属不动。旧新正式用例、类型反例、冻结后 Release build 与八项目 fresh full 已进入执行要求；Task 已明确八项目要求替代第 6 项原相关项目最低要求。scope 证据按 B7 AC-001 的 whole 清理位置解释，继续区分旧 fine Dispose 与新 whole Dispose，不宣称生命周期位置完全相同。

无新增阻断项。此批准允许主控消费 B7/P9、形成规划检查点后放行该接口及判据增量；不批准尚未完成的产品、Task Result、Timer 来源闭合或整个功能簇完成。实现时仍应同步 AGENTS 的具名能力现状，后续结果审查核对实际接线与测试。此次仅阅读规划 diff、追加本 R2，未运行测试、修改产品或提交；R1 原文保留为历史结论。
