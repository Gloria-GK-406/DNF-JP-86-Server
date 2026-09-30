---
kind: rks-review
verdict: changes_required
---

# spec-003-magic-seal 完整增量审查

- 目标：`task/solo-play-roadmap` 的 `HEAD=8ab85ebdcd5c872d6d7c762e8766a21ad015a7f9` 至当前未提交工作树；包含 11 个已跟踪修改和全部 22 个未跟踪源码、测试及任务文档。未将本审查文件计入受审目标。
- 范围与准则：mixed complete-change gate；`TaskContract.md`、`spec.md`、`plan.md`、`evidence-and-design.md`、仓库 `AGENTS.md`、`protocol/README.md`，以及 `reviewing-work` 的 Code 与 Deliverable 准则。核对了真实 A21 PVF 的装备/卷轴与随机选项声明、相关命令事务及协议投递路径。
- 验证边界：做了只读源码、差异、PVF 探针与 `git diff --check` 检查；本次未执行构建或测试，因为审查授权只允许写本文件，构建/测试会生成其他工作树文件。测试源码存在不等于本次测试通过；无 A21 实帧或 GUI 验收。

## 阻止通过的发现

1. **未经确认的 `0x0191` 第二字段直接触发卷轴扣除（协议/资产边界，RQ-001、RQ-002）。** `protocol/game/client-to-server/0x0191.md:31,53-57` 明确把第二个 `i16` 的“卷轴槽”标为 `Hypothesis`，并注明上游不读取该字段；`protocol/README.md` 又规定 `Hypothesis` 不得直接成为生产领域语义。但 `MagicSealHandler.cs:57-59` 把非负字段映射为材料槽，`EquipmentMagicSealDomainService.cs:96-117,125-132` 据此取物并消费一张卷轴。PVF `2600460` 的 Lv30 限制只证明物品属性，不能证明 A21 此字段指向该物品。若 A21 写入的是其他含义或槽位，合法解封会误扣玩家资产。**最小修正：**在补足 A21 构包调用方/字段来源或实帧差分证据前，拒绝所有非哨兵第二字段且不扣材料；用测试钉住该拒绝。若取得证据，再恢复卷轴结算并更新协议记录。

2. **重放向客户端发送过期金币余额（投递/重放，RQ-004）。** `MagicSealHandler.cs:35-42` 对缓存同序列请求重新发布旧 `Receipt`；`MagicSealCompleted.cs:37-38` 把收据中的 `GoldRemaining` 当作绝对金币刷新值。比如解封后余额 99,500，随后其他命令花费 100，再重放原请求，服务器会投递 99,500，虽然 SQLite 当前是 99,400；同一处理器的装备槽却从当前存储读取，形成不一致投影。`MagicSealHandlerTests.cs` 只测紧邻重放，未覆盖中间资产变化。**最小修正：**重放投递时从当前角色真源读取金币，或只重发不会覆盖现值的结果；补一条“成功结算→其他金币变更→同序列重放”的回归测试。

3. **已接入 `0x01B6` 的结果体仍是猜测，无法证明客户端能读取（协议/交付，RQ-004、AC-004）。** `protocol/game/client-to-server/0x01B6.md:56-58` 只定位了客户端响应 handler，承认其 body 读取未闭合，并将单字节成功/双字节失败 ACK 称为兼容性假设；`MagicSealMessages.cs:53-54` 与 `MagicSealHandler.cs:91-105` 却发送该形态和自定错误码，成功前已扣金币并改装备。`MagicSealWireTests.cs` 只验证服务端自己的编码假设，没有 A21 接收证据；`evidence-and-design.md:14` 所称“证据支持的 ACK”与此不符。客户端可能不能解释响应，造成资产已结算而 UI 卡住或错误提示。**最小修正：**静态闭合 A21 `0x00C45060` 的 body 读取并据此修正编码/测试，或取得实帧；在此之前将该入口隔离为不结算资产，并同步文档与人测清单。

4. **规则读取与随机输入绕过本仓约定，方案和实现不一致（规则/可复现性，RQ-003、AC-003）。** `AGENTS.md:104-133,881-893` 要求内容源只由规则服务读取、规则使用显式种子；`plan.md` 的 Shared Decisions 与 `evidence-and-design.md:10,14` 也写了该路径。实际 `EquipmentMagicSealDomainService.cs:16-17,55-64,151-183` 直接注入 PVF 内容源并在有角色状态的服务中选权重，生产随机源 `EquipmentMagicSealRollSource.cs:7` 使用 `Random.Shared`，没有可记录的本次种子。现有固定返回零的测试替身只能证明固定替身，不能复现一次真实结算的抽样。**最小修正：**把内容读取、候选/费用规则和带显式随机输入的抽样收进 `Application` 规则服务；领域服务只传装备事实并结算返回的决定，测试固定输入与生产接口一致。

## 覆盖缺口与保留边界

- `AGENTS.md:862-878` 要求新协议的 Networking 线级集成判据；本增量只有直接调用 decoder/handler 的 `MagicSealWireTests`、`MagicSealHandlerTests`，没有新线级用例覆盖 `ProtocolModule` 注册和真实收发路径。补一条通过 Game 帧进入两类请求并核对出站类别/长度的集成用例，尤其要钉 `0x01B6` 结果体。`AC-002` 所列过期装备、错误槽位也尚无定向负例，补测试可防当前守卫回退。这是需要补齐的验证证据，不把“源码有测试”写作运行通过。
- 已检查 `0x0197`、`0x01C8` 未接入资产 handler，文档保留 `ClientStatic`/版本差异；`SealFlag` 未被借作随机词条状态；装备与金币/卷轴写入位于同一 `Transactional` 命令边界，已有 SQLite 故障回滚测试源码。以上不消除前述发现，也不等于客户端已验收。

修复后需对更新后的**完整工作树增量**重新进行独立审查；本 verdict 不代表其他装备 Spec 的状态。

## Round 2 — 2026-09-30

```yaml
kind: rks-review
verdict: changes_required
```

目标仍为 `HEAD=8ab85ebdcd5c872d6d7c762e8766a21ad015a7f9` 至本轮当前未提交工作树（含新增未跟踪源码、测试和 `CMD-0x01B6.md`），按同一 mixed complete-change Code + Deliverable 门槛审查。保留 Round 1 原文作为当时目标的审查记录；以下裁定只针对更新后的目标。本轮只读检查完整相关代码/文档、真实 A21 客户端反汇编与 `git diff --check`；依照“只允许编辑本文件”约束，未运行会生成其他文件的构建或测试，因此不声称测试已通过。

### Round 1 发现复核

1. **卷轴误扣：已解决。** `MagicSealHandler.cs:44` 仅接收第二字段 `-1`，`EquipmentMagicSealDomainService.cs:40-42` 再拒绝任何非空材料槽；卷轴消费调用已移除。`MagicSealHandlerTests.Non_sentinel_second_field_is_rejected_without_spending_assets` 与领域测试的同类用例钉了金币/卷轴不变，`0x0191.md`、证据设计和 Human Test List 也同步保留 `Hypothesis` 边界。
2. **重放旧金币：已解决。** `MagicSealCompleted.cs:36-39` 在投递时从当前角色读取金币；`MagicSealHandlerTests.Replay_after_another_purchase_reports_current_gold_not_old_receipt` 先把余额从 99,500 改为 99,400，再检查重放投递的绝对值。缓存收据仍用于识别已提交请求，未再次执行结算。
3. **`0x01B6` ACK 证据：最小体已闭合。** 新 `protocol/game/server-to-client/CMD-0x01B6.md` 给出 PE VA/RVA 和成功/错误分支；独立对 `DNF.unpacked-b1-debug.exe` 反汇编核到 VA `0x01045063` 检查 `[ebp+0x0c]`、成功分支至 `0x010450FF` 无额外流读取、VA `0x01045101` 从 `[ebp+0x10]` 取单字节原因。结合现有通用 CMD 状态分发证据，`MagicSealMessages.cs:53-54` 的成功 `01`、失败 `00 reason` 有静态读取依据。具体 reason 文案和原服附加字段仍未实帧确认，文档没有标成 `ClientVerified`。
4. **规则边界/随机输入：已解决。** `EquipmentMagicSealRuleService.cs` 独占 PVF 内容读取与权重选择，`EquipmentMagicSealDomainService.cs:58-59` 传入显式种子；`EquipmentMagicSealSeedSource.cs` 生成生产种子，收据与成功日志保留它。`Same_explicit_seed_reproduces_the_same_rule_decision` 对固定种子检查重复结果。领域事务仍负责金币与装备同一提交。

过期装备与错误业务槽位的拒绝用例已补在 `EquipmentMagicSealDomainTests.Expired_or_wrong_business_slot_cannot_unseal`，并核对了拒绝前不写金币或装备。`0x0197`、`0x01C8` 仍未接入资产 handler。

### 本轮阻断：线级测试没有证明 `0x0191` 接线，且请求头序号错位

`MagicSealFrameIntegrationTests.cs:70` 将 `sequence` 写在 `frame.AsSpan(11)`；A21 14 字节入站头的 offset 11 是保留字节，序号位于 offset 12–13（`GameClientFrameRequestInfo.cs:60-61`，亦见 `protocol/patterns/game-frame.md`）。测试发出的 7、8、9 实际都被服务端解成序号 0，无法验证真实 A21 连续帧和重放门。该测试对 `0x01B6` 的无会话拒绝回包确实验证了该方向的 decoder/handler/encoder；但 `0x0191` 本来无拒绝回包，测试在发送它后只检查独立的 EXIT `0x0003` 响应（`MagicSealFrameIntegrationTests.cs:55-59`）。即使删掉 `0x0191` 的 decoder 或 handler 注册，EXIT 仍会回应，测试照样通过。因此 Round 1 指出的 `0x0191` 线级接线缺口尚未关闭，且新测试使用了错误线格式。**最小修正：**把序号写入 offset 12，保持 offset 11 为零；为 `0x0191` 加一个能观察到其实际派发的线级断言（例如在已选角、可结算的隔离角色上校验提交后的装备/金币及出站刷新），并使移除其注册会令用例失败。只发包后验证 EXIT 存活不够。修正后再执行定向测试及完整门槛验证，由后续独立轮次检查结果。

### 非阻断残余风险与文档修正

- A21 `0x0191` 第二字段含义、`0x01B6` 具体错误码文案、原服通知顺序及两入口 GUI/重登效果仍缺实帧或人工验收；继续保持 `ClientStatic`/`Hypothesis`，按 Human Test List 集中验证。
- `CMD-0x01B6.md` 首条把 `DNF.unpacked-b1-debug.exe` 的 SHA-256 身份说成沿用请求记录。实际检查：被反汇编的 unpacked 文件为 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A`，请求记录的 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` 对应原版 `DNF.exe`。地址处指令已独立核实，故不据此否定最小 ACK；建议在来源句分别列明原版身份与实际反汇编文件哈希，避免后续复核误用。
- 当前金币和当前装备槽在通知组包中先后读取，未建立同一只读快照；并发变化时仍可能出现跨瞬时投影。现有会话检查与重放刷新已消除 Round 1 的确定性旧金币覆盖，但实机投递时序仍应观察。

## Round 3 — 2026-09-30

```yaml
kind: rks-review
verdict: approved
```

目标为同一基线 `8ab85ebdcd5c872d6d7c762e8766a21ad015a7f9` 至本轮当前完整未提交工作树（含未跟踪文件）的 mixed complete-change 复核；Round 1/2 原文保留，前两轮 verdict 只描述各自当时的目标。适用 `TaskContract.md`、`spec.md`、`plan.md`、`evidence-and-design.md`、仓库 `AGENTS.md` 及 reviewing-work 的 Code + Deliverable 准则。

### Round 2 阻断复核

- **A21 序号位置已修正。** `MagicSealFrameIntegrationTests.cs:75-76` 在长度为 `GameClientFrameRequestInfo.HeaderLength` 的零初始化数组上向 offset 12 写 `u16 sequence`，offset 11 保持零；与 `GameClientFrameRequestInfo.cs:60-61` 的实际解析位置一致。依次送出的 7、8、9 不再被解析成三个零。
- **`0x0191` 线级派发已有可观察断言。** 集成测试在发送 `0x0191` 后调用 `CaptureLoggerProvider.WaitForAsync("MAGIC_SEAL no selected session change=False", ...)`，随后才发 EXIT。`MagicSealHandler.cs:29-34` 的 Debug 日志仅在该消息进入 handler 且无已选会话时产生；`ProtocolModule.cs:629,832-833` 注册该消息与 decoder。移除 `0x0191` 接线后测试不会看到指定日志，超时失败。该用例验证了传输、解码、派发到 handler 的路径；有会话的资产结算由领域/handler/SQLite 用例覆盖，不把无会话线测冒充成功结算线测。
- **证据文件身份已更正。** `CMD-0x01B6.md` 分列被反汇编的 unpacked 客户端 SHA-256 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A` 与原版壳 SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；本轮对本地两文件重算与文档一致。先前核实的 VA/RVA 和最小 ACK 静态读取结论未被改坏。

Round 1 四项阻断及过期/错槽测试的 Round 2 修正仍在当前代码与文档中；新增 Debug 日志不携带账号、装备或材料数据，线级测试的捕获器只用于本测试 Host。已做只读工作树/源码/文档检查与 `git diff --check`，未发现新的阻断缺陷。用户报告定向测试已通过；本审查未取得独立运行日志，也未重跑会生成其他文件的测试或构建，以遵守仅编辑本 review 文件的限制。`approved` 只表示本轮完整变更审查范围无未解决阻断，不替代后续新鲜完成验证。

残余边界：`0x0191` 第二字段仍是 `Hypothesis`，非 `-1` 请求继续安全拒绝；`0x01B6` 错误码文案、原服通知顺序、A21 GUI 与重登效果尚无实帧/人工验收，不得升为 `ClientVerified`。通知组包的装备槽与当前金币为先后读取，极端并发下仍有跨瞬时投影风险，集中实机验收时观察。
