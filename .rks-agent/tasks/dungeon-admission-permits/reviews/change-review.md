---
kind: rks-review
verdict: approved
---

# 地下城资格凭证重构独立审查

## Target / Scope

- 基线：`67ffa14f82e8e789e53e7a22fe1af1b85627d57e`；目标：当前隔离工作区未提交 diff，含新 Admission 接口/实现、测试工厂、Document 和 AGENTS 修改。
- 最终审查指纹（Git blob SHA-1）：tracked binary diff `f63f7e167f0ad5e5292b781f93ef0f2728ecbb9b`；新 Admission 实现 `5b05e94289abb9f55f76e3f86eda838a155c89b6`；新 Admission 接口 `c6e6a6cf58b569c8a296aa1bacb9123345b86575`；新测试工厂 `2fedd64774d3b8568d47172031fde614204f0f1a`；新 Document `052e792f2075eac49c6867f5c9bb63c0152d9e11`。
- 独立审查，未修改生产/测试，未启动构建或测试。依据 TaskContract revision 1、spec、仓库 AGENTS、`reviewing-work` 的 code/deliverable references。
- 覆盖普通、循环、主动深渊、镜像、异界生产调用链，费用规划与共享材料、凭证身份与事务绑定、失败回包、rollback-only、DI 以及文档一致性。
- 构建/全回归由主代理串行执行；本报告不把主代理口头状态当作独立运行结果，不构成客户端实机验收。

## 已修复的审查发现

### R-2 [P1] 新增普通运行态拒绝切断既有再挑战入口

首次检查时 `DungeonAdmissionDomainService.PrepareAsync` 中 `runs?.Find(member.CharacterId) is not null` 对所有入口返回 `AlreadyInDungeon`。这不是对基线职责的平移：基线普通 Visit 仅排除塔状态，普通、循环、主动深渊、异界可以替换旧 run；镜像才有普通 run 存在拒绝。

生产路径可直接确认：`DungeonSelectionSlice.cs` 的 `EnterSelectDungeonHandler`（约590-619行）在 CMD 000F 时记录新 intent 并离开城镇投影，不结束旧 run；CMD 0010 的 `SelectDungeonHandler` 使用该 intent 进入 CMD 入场。`DungeonEntryProjectionDelivery.cs:89` 保存 `previousRuns`，145行以后在新入场成功后清理旧 run 的投递成员，明确支持旧实例替换。现有 `PvfCycleDungeonTests.cs:20` 也记载循环再挑战的 000F(previous82) → 0308(new85/15217) → 0020(old15242) 序列。

后果：已经有旧 run 的角色打开有效的新选择后，资格阶段直接拒绝，不能走到原有替换行为；影响普通/循环及主动深渊再挑战。当前已将该拒绝限定 `options.MirrorCatalog` 非空，塔互斥仍保留。新增 `Committed_ordinary_run_can_be_replaced_by_a_valid_retry` 使用真实材料、疲劳和事务，显式注入 runs，验证两次提交替换旧 run 且各收费一次。静态复审通过。

### R-1 [P2] 深渊票充足但不同 ID 普通材料不足时错误类别漂移

首次检查时所有合并候选失败都返回 `MissingHellTicket`，会把基线的普通材料缺失 `MissingRequiredItem`（0x11）改成深渊票缺失（0x1B）。当前修复按票 ID 先验证替代票及同 ID 需求，再验证全部普通需求，Consume 以 `HellTicketItemId` 做同样的区分。静态复审通过；对应新增三条判据包含在已读取的 review-fixes 结果中。

### R-3 [P2] 镜像布局准备后缺少精确会话复核

首次检查时只在收费前检查镜像精确会话，随后异步布局解析后 BeginEntry 不复核。若此时会话换代，费用会提交，而后续协议投影仅终止旧运行实例。基线镜像在布局解析后会返回无槽位的 `MissingPrerequisite`。

当前 BeginEntry 返回可选强类型拒绝，启动前对凭证保存的 SelectedSessionIdentity 复核，保留原错误类别、无槽位和 Detail。Visit 收到拒绝不创建房间；Execute 标记 rollback-only 并使全部预付费用回滚。新增 `late-session` 边界在布局解析时改变 generation，并验证返回拒绝、Visit 未创建、提交后 quota 仍0。静态复审及该项 TRX Passed 证据均已确认。

## 被反证的候选

曾提出 late-session 测试的预提交 `quota.Reservations == 0` 应改为1；经独立读取 `CharacterSkillLearningTests.cs:427-433`，`LearningFixture.Scalar` 每次建立独立 SQLite 连接，读的是已提交视图，0正确。review-fixes TRX 的 late-session 结果也为 Passed。该候选 rejected，无需修改。

## 其他审查结论与证据边界

- 普通 Visit、循环经普通 Visit、主动深渊、镜像、异界均进入 Admission 的 Prepare → Consume → 玩法目标构建/EnterGranted；资格不解析迷宫、出生房或怪物。
- 材料规划先分配只允许随身的需求，再分配允许账号材料的需求；持有要求参与分配但不扣，重复栈费用合并。重复账号成员被拒绝，因此队内不会重复使用同一账号共享材料余额。
- Admission 私有 Batch/Receipt 校验签发服务、事务引用和单次状态；Receipt 绑定地下城及完整参与者序列。材料、疲劳、quota、镜像精确会话在消费前复核，镜像布局准备后再复核会话。Document 已明确是镜像精确会话。
- 凭证无疲劳费用；Visit 出生房扣1，Move 首次房间扣1，重访不扣。无需疲劳的定义/布局跳过。
- 收费写异常在 Consume 内标记 rollback-only；收费后目标构建拒绝或异常也标记。UnitOfWork 在正常拒绝返回前回滚；直接 Commit 对 rollback-only 也回滚，after-commit 不执行。收费前只读资格拒绝不必标记。
- 因此旧的“普通地图拒绝后，同一事务入塔并提交”测试不再是有效用例：收费后拒绝要求整个事务回滚。主代理改为结算拒绝事务后另开事务入塔，保持未预留模式与后续可入塔的断言，符合本次契约。
- MirrorEntryRecorded / DimensionEntryRecorded 仍以 DirectAsync 写入 UsageQuota；镜像保持 UTC+8 自然日、异界保留 daily reset 周期。DI 仍注册同一个 Admission singleton，未发现新增依赖环。
- 协议映射文件未改；失败仍由现有投影输出错误类别及成员槽位。成功扣费投影来自 Receipt 的库存变化。
- 已直接读取 `artifacts/admission-verification/full/build.log`（0警告/0错误）与首轮 full 测试报告：首轮 Application 仅上述旧 map 测试失败，其余项目通过。首轮报告早于最终修复，不能作为最终全套通过证明。
- 已直接读取 `artifacts/admission-verification/review-fixes/Gloria_家庭台式机_2026-10-11_10_16_22_net10.0.trx`：133 passed、0 failed、11未执行；包含 late-session Passed。未执行项为无PVF条件下跳过的内容判据。真实PVF最终全套由主代理执行并记录于 completion verification。
- 当前完整修改范围不存在未解决的确凿阻断，独立审查批准。批准不替代最终全套验证或客户端实机验收。
