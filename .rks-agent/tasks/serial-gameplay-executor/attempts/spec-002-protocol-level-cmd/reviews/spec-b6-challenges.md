# Spec B6 ready R1 — 独立有界挑战（2026-10-25）

## 目标与来源

本 fresh context 独立于 B6 producer 及后继 reviewer，按 `generating-adversarial-challenges` 只生成挑战，不给 review verdict、不批准实现或阶段状态。唯一工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；`.codegraph/` 不存在，直接检索当前源。目标是 `../spec.md` 的 B6 ready R1：**66834B / SHA256 `d20f90fb095438d5408b9f7bb54e69af3224c41e1f749d14a475a8104b9956fd`**。配套 `../verification/spec-b6-amendment.md` 为 **9696B / `3fd97177c73e11bc687931ab19d3c90a68c2e49de1493744a0d3f0ffdcec9ebb`**。

实际读取根 TaskContract r4（32812B / `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4`）的结果B、Constraints及验收／授权，child r1（8898B / `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`），B6 主控裁定（6900B / `7fab8c2ace4ffb0f58038690c52f8e1c6e15c14cb3974084e07ed91a7e4947da`）、当前Spec及amendment、B5原稿快照（53754B / `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790`）、TASK-32已提交报告及52场case manifest，并核实际生产机制／协议源。旧调查approved只说明before证据可用，不作为B6批准。

攻击范围限本次变化：四 remaining opening 与 Rental 的各自恢复边界，NoRoom正常回滚拒绝，Main与独立premium／refresh提交点，fine→whole scope的失败位置／帧存活及默认保护，精确参与者／物理来源和替换隔离，六消息现有 replay family 有限owner的先旧后新条件，以及根纯搬迁／语义分提交约束。原 Mailbox／其它raw owner／Compose与后继整体迁移不重开。

## 保留集

- high-risk：**0**。
- low-risk：**0**。

未发现本轮需保留的、能从目标缺陷连到错误／不可实施结果且不能由后继便宜控制的挑战；不为填数量制造CH项。此结论不是完整性证明或 `approved` verdict，后继fresh reviewer仍须独立裁决当前目标。

## 决定性核查与未升级为挑战的边界

1. **健康回滚与已提交没有混同。** 当前 `EquipmentRentalDomainService.cs:75–77` 的 NoRoom 正常返回且RequireRollback，旧 `UnitOfWorkBehavior.cs:55–60` 先真实Dispose再记RollbackConfirmed，却由 `SelectedSessionSettlementBehavior.cs:19–24` 进入要求Frozen的 `EffectOperationContext.cs:102–107`；原 `RentalSlice.cs:52–56/:118–126` 已有Result2编码／mapper。B6专节与AC-004直接要求真实满32格、完整资产含rowid、`01 02 00 00 00`、ConfirmedRollback／原entry与Ordinal重试、无新增保护／refresh。现显式 `CommandWorkUnits.cs:281–290/:312–319` 已按真实回滚记Failed并只在Committed时Settled，Spec未强迫另造恢复框架；异常Rental确认回滚仍不获opening式恢复。

2. **scope与第二单元不被旧零帧假象掩盖。** B6明确六Main／Lottery独立ReadOnly／Rental独立Transactional，不把Main已durable与第二单元回滚合并；真实旧fine scope在 `CommandBus.cs:214` 起的内联scope内，whole `:87–128` 在body之后Dispose并最终FailAsync。B6专节、VC-B6及AC-005明列新scope前可有原success／normal refusal帧，按真实unit状态发送再关闭，不补失败ACK／撤帧／提前flush；同时要求真实scope资源和实际Send→Close／原链／替换对照。这是已列有意差异，不是Spec遗漏。

3. **默认保护依真实事实，非异常名称。** `ExplicitEffectRequest.cs:43–51` 不将已Committed entry改写；`CommandWorkUnits.FailAsync:370–384` 根据HasCommittedUnit与CommittedParticipants判定typed及精确保护。B6另段明确ledger、Completion和最终错误类别分开，纯ConfirmedRollback后普通第二错误不一概伪Uncertain／增围栏；C1／C2原不确定及独立同对象发生保持，全部清理／guard健康才允许拒绝恢复。既有raw例外及精确来源身份没有被一并扩大。

4. **replay owner补证是真正的前置条件。** 当前legacy `LegacyReplayStateProjectionDelivery.cs:18–24` 结束原普通写后传null attempt，whole `CommandBus.cs:181–192` 按实际registration签同family attempt；`ReplayStateProjectionDelivery.cs:33–46` 的Create／batch与`:47–62`的后续Town分段，`ExplicitEffectRequest.cs:54–65`的精确Committed replay门仍在。TASK-32 case manifest实际52场无encoder fault，B6正文明确不能据静态共享或该调查冒原cause／ledger／无围栏证明，必须逐具名旧decoder实际encoder oracle再新同容器对照，未满足不授owner并返受影响门。首次成功／拒绝／其它两类许可及Mailbox能力未获权。要求未来Task提供该证据合理，当前没有它本身不是Spec缺陷。

5. **提交分离未被改名豁免。** 根Constraints第210行的纯搬迁／语义分提交仍由B6专节及AC-005约束，Spec要求独立NoRoom修正与可独立纯结构准备分开，明确不能仅把混合提交改称语义提交；可执行依赖和最小前置由受影响P7决定。此处未预写Task／API，也未宣称已经完成分离。后继Plan必须落实该约束；把尚未产出的Plan细节升为本Spec高风险没有新增任务结果保护价值。

以上均为只读源码／制品核查，未启动native、构建／测试或新探针；没有把TASK-32受控transport当TCP／A21，或把142 focused当新八full／DFO74+7／2秒结果。只写本不可变挑战文件，未修改目标／上游／Plan／产品／正式测试／canonical／Git；完成后停止writer，交主控选择另一fresh reviewer。
