---
kind: rks-review
verdict: approved
---
# TASK-008：完整装备实例与失败规则的独立 mixed review

2026-09-30，由生产者之外的新鲜独立 reviewer 直接执行，没有再委派。读取仓库适用 AGENTS.md、TaskContract rev4、spec、plan、TASK-008、sdd-execution.yaml、当前源与完整增量、70 原始参考、回归 TRX、实际 Probe、交付脚本和人测文档。应用 `reviewing-work/references/code.md` 与 `references/deliverable.md`；本 gate 没有要求 challenge set。工作区不存在 `.codegraph/`。

审查范围是完整累积装备实例、全部既有养成迁移、强化字段/保护/诊断及本轮失败规划与交付准备。审批不替代部署后 ready、人工验收、ClientVerified 或根契约完成；最后用户门通过前不提交或 seal。

**当前第2轮 approved**：首轮 F-1 已修正并经同一独立 reviewer 对整改及直接回归复核；下方保留第1轮 changes_required 原始判断。控制器可以进入已授权的备份、服务切换与新鲜核验，不能把准备批准当作服务已 ready 或最后人测已通过。

## 第1轮：冻结候选，changes_required

```yaml
kind: rks-review
verdict: changes_required
```

目标 HEAD/基线 `6e60c4c67417ce06b88e9e99277fb1e4c0239726`。独立从 Git 枚举 tracked diff、untracked 与删除标记，逐文件 SHA256 后重算 compact JSON 集合：**44 个 Server 文件，指纹 `36d1515f1efd615edef0edf2df77d220ad08cf35c47cc35c1a6bd8a5ab23858a`**，逐项等于 review-source-manifest 与当时 delivery-manifest，`git diff --check` 通过。

initial-source 的 33 文件逐项等于 TASK-006 approved diagnostic-manifest、TASK-007 [完整审查](change-review-expiry.md)所继承的目标。当前 27 文件完全未变、6 个原文件更新加 11 个新增组成 17 个 TASK-008 增量；真正未变证据按 [概率完整审查](change-review-probability.md)及期限审查继承。重新检查所有新/变重叠、当前 instance/store/container、事务、奖励容量、ACK/NOTI/replay 和 NPC 边界，不以生产者报告替代实际代码检查。

### 阻断 finding F-1：嵌套技能 value 被误认为装备顶层价值重复

`PvfEquipmentDisjointDefinitionSource.ReadDestructionEquipment` 对平铺 `PvfNpcShopDefinitionSource.Sections` 直接调用 `OneInt(sections, "[value]")`；`One` 要求整个脚本只出现一个同名 section。这不能区分装备自身价值与嵌套技能的 value。

Reviewer 直接用生产 PvfProbe 读取实际 `equipment/character/swordman/weapon/beamsword/101040127.equ`：顶层 `[value] = 104960`、`grade = 82`、`rarity = 2`，而 `all skill item container / all skill item / skill apply condition` 内另有三个合法 `[value] = 1`。实际 UID689 的只读生产 DI 副本 Probe 以旧强化等级10调用失败服务，返回 **UnsupportedOutcome（12）/Destroyed/空奖励**，与上述 null destruction source 相符。

实际后果：这件合法装备无保护自然失败时，新领域服务在扣费前返回不支持，不能按原玩法处罚/返还材料；成功尝试仍可进行，因而失败行为发生回归。资产安全拒绝并不能把合法模板误拒解释为契约允许的未知内容。此项违反 SC-3/RQ-004 的兼容要求及 SC-10/RQ-012 的完整基础产物要求，阻止当前候选部署。

最小整改：只在 destruction 读取中正确区分顶层装备字段与嵌套字段；合法嵌套技能 value 不影响顶层值，重复顶层 value 仍应拒绝。不要无条件 First，也不扩为 NPC 分解行为修复。补嵌套 value 与重复顶层值回归；实际 UID689 Probe 必须断言 Rejection.None、可计划完整结果且只读资产保持；重新生成当前源/构建/包身份后独立复核。

### 已检查的其他证据

- `TryReinforce(draw)` 只比较原失败阈值，费用、成功概率、保护资格/优先级与 UID/wire 行为未另改；失败服务只接收冻结 facts、显式保护及奖励 draw，instance 扩展不写状态或查询库存。旧处罚/单奖励入口已无引用。
- 实际 70 ELF SHA256 独立核对为 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。亲读 GetResult、additional、sell-price、bonus 与 _ProcPenalty 原始反汇编及常数：float32 1.1 常数乘价值再截断、普通/随机200/30卖率、整数 cube 运算、grade/divisor 商先存 float32 再截断、整数 great 阈值、原等级平方奖励与保护提前无产物，均与算法对应。NPC 原路径没有使用新 destruction 方法。
- 领域先统一模拟扣材释放、叠堆、不同产物争槽、账号材料扣费后容量与 ACK 数量上限；实际追加统一当前命令事务。提交后通知对同槽/虚拟槽取最后余额，ACK MaterialRemaining 是全部结算后值；commit 失败和中途发奖异常整批回滚，同序列重放只返回原 ACK。
- 直接解析最终 TRX：Application2383、Infrastructure412、Protocol1202、Persistence385、Networking72、Host24、HttpApi50，共 **4528 Passed/0 Failed/2 条件 skip**。两个 skip 是真实 PVF 可达时不适用的 missing-archive 用例；中间无 PVF 2198+171skip 与 Protocol1137+60skip 只作历史。RED 的两条实际 SQLite 失败分别为白装无色缺失、追加预期1实际4；未用旧记忆推断任何宠物失败。
- Release solution 日志为0警告0错误；当时69文件候选包逐文件/集合与 manifest 相同，11个 Dfo DLL 与 Host Release 对齐，appsettings/experience 与旧运行包逐字节相同。Probe clone 与生产47个业务表只读比较完全一致，quick_check ok。以上不能覆盖 F-1，旧候选不批准。
- `deploy.ps1` 已收口为停服务前精确 PID/path、无 DNF/无 Game Established、完整 source/package 集合和 SHA、原配置保持及新鲜在线备份，Hidden 启动、bootstrap ready、四端口均属新 PID，再验证47业务表和三个 PVF；异常只恢复旧服务，不恢复数据库或重发资产。审查指出的包集合缺口及 PowerShell 双反斜杠归一问题已修正为 DirectorySeparatorChar 替换。仍未执行部署。
- 当前 rev4 completion-evidence 保留 manual_required/SC10；旧记录进入 history，Protection 清单标历史且说明用户已丢弃普通/超级强化券。新 F 清单明确自然损毁、净余额与正常重登，不补历史材料，不重发券或资金。

第1轮结论：F-1 为实际阻断缺陷，控制器已接受并交回限定整改；其他证据可在目标身份真正未变时继承。当前尚不 approved、尚不 ready、尚未完成根契约。

## 第2轮：F-1 整改及直接回归，approved

```yaml
kind: rks-review
verdict: approved
```

本轮继续由原独立 reviewer 直接检查，未另行委派。亲读整改生产方法及全部新增回归、最终实际 Probe 与原始 TRX、当前交付脚本、更新的 Plan/TASK-008/执行游标及完成证据。没有扩大任务权限或验收范围。

### 当前目标及证据继承

当前 HEAD/基线未变。独立从 Git 重新枚举并逐文件重算：**44 个 Server 文件，指纹 `a0cda7a88dc3f7d24ec4b8a49bba6ac77aeaf3f5b0bf889ea6ed9a3b88b447dd`**，与最终 delivery-manifest 完全一致。第1轮 review-source-manifest 作为历史保留；逐项比较仅以下两文件不同，其他42项包括删除标记完全相同：

- `Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentDisjointDefinitionSource.cs`
- `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEquipmentDisjointDefinitionSourceTests.cs`

Reviewer 在内存中移除新增 DestructionRootSections 及其单一调用替换，重算源码 SHA 精确恢复第1轮源文件身份；没有修改产品文件。这证明 NPC 解析、系统表、旧内容算法等原方法逐字节保持。规则、领域、instance、事务、协议、replay 和其他养成的首轮实际检查及完整回归证据按未变身份继续有效。`git diff --check` 再次通过。

### F-1 的裁定：resolved

新增 destruction 专用 root-section 筛选识别显式闭合结构并用栈检查匹配，在闭合结构内部不收集顶层字段；已知技能容器缺少最终闭合也拒绝。筛选后的顶层仍使用唯一 section/单整数校验，因此没有以 First 放过重复顶层值。新增测试同时把 value/grade/rarity/random option 放入嵌套条件，验证其不能覆盖顶层事实；重复顶层四字段、缺少顶层 value、未闭合及错误闭合均拒绝。测试还明确 NPC 原读取结果保持，未将修复外溢为 NPC 行为变更。

直接解析 `nested-value-red.trx`：实际长剑及合成嵌套模板两条断言均先得到 null，2 Failed。随后 `nested-value-green.trx` 为14 Passed；最终 `infrastructure-root-scope-final-real-pvf.trx` 为421 Passed/0 Failed/2条件 skip；`application-root-scope-associated.trx` 为134 Passed/0 Failed/0 skip。两个 Infra skip 仍是真实 PVF 可达时不适用的 missing-archive 用例，没有借 skip 隐藏 F-1。

实际生产模块 Probe 新增 UID689 的 Rejection.None 硬断言，使用真实 PVF、实际角色2的 clone 装备实例，通过真实 CommandBus 事务进行只读规划：顶层 value104960/grade82/rarity2/rate200；原+10无保护失败规划为 **10无色 + 1个3166，无强化痕迹**。before/after instance facts 相同。白12866仍33无色；稀有10111原+11仍2无色+1个3166+1痕迹；普通/超级保护分别保至0/10且奖励为空。逐级穷举目标2至15的100000个 draw，成功数量仍等于100000减原 FailureThreshold。Probe 只解析领域结算服务，不调用实际强化结算或写装备；合成白/稀有 facts 只是内存规划输入，不是生产验收样本。

Reviewer 重新通过只读 SQLite 比较 clone 与当前生产：两库 quick_check ok、**47个业务表逐行完全相同**。没有资产发放、恢复、券补发或 PVF 写入。F-1 的实际合法模板误拒及其直接回归已解决，剩余未证明的真实客户端效果继续留给人测。

### 最终构建、包与部署准备

`release-root-scope-build.log` 为 Release solution0警告0错误，`publish-final.log` 给出最终候选输出目录。新包实际递归文件集合和 SHA 逐项等于最终 manifest，**69文件，包集合 compact JSON SHA256 `d1d1045a64414b45836db301cae1818d7461fdf223f3bc5a8015809b36c0754b`**。11个 Dfo runtime DLL 逐字节等于当前 Host Release；Probe 实际引用的6个共同 Dfo DLL也逐字节一致，其余5个组件并非 Probe 引用。新旧 appsettings.json/experience.json保持一致。

必要程序集证据更新为：Application2383（生产 Application 源未变）、Infrastructure421、Protocol1202、Persistence385、Networking72、Host24、HttpApi50，**4537 Passed/0 Failed/2条件 skip**；另有134条整改关联回归。此处不把重复专项相加，也不冒称本次重新执行整个 solution test。

最终 `deploy.ps1` SHA256 `cbae91f64e1af5a97f86c61165563addc515e67570dfdc996f6837aba937716f`。Reviewer 实际只计算其停服务前的包路径归一/集合比较，得到69文件且与 manifest 同集合；PowerShell AST解析0错误。DirectorySeparatorChar 替换没有前一版双反斜杠绑定错误。其余精确 PID/path、游戏退出、全源/全包 SHA、备份、Hidden 启动、四端口/ready和数据保持步骤按首轮已读脚本继续有效。

其他最终交付工具 SHA256：

| 工件 | SHA256 |
|---|---|
| state-check.py | 68225c793ec6ed593ab81039705c1609e027108376492e87bf7da721a21b4838 |
| make-manifest.py | 1feb3ed6fb60e117fc7e588a18c6fe1f07a26d76fcdcf38bec3fda1120220129 |
| Probe/Program.cs | d695b7710269b6ff5600474359faa303b2d6aca74cf471095a648f06dc475ca4 |

审查现场仍是旧概率诊断 runtime **PID40060**，没有10010/10011 Established游戏连接；新包没有切换。三个实际 PVF 读回仍分别是已批准期限候选：debug/protocol `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`，protocol2 `3847DAEE513DED7039AEFB2896FF1EB03E6FE321E40214BF12CE093BE57DE111`。部署脚本必须在执行时重新核对条件并完成备份、ready及47表/三PVF保持核验。

### 完整交付结论与剩余门

Plan/TASK-008明确候选先供本完整 review 判断，批准后且游戏正常退出才切换；当前游标为 review/in_progress，Task代码交回不代表 SC5/SC6/SC10 的运行和人测条件已完成。completion-evidence 保持 manual_required，保留本轮真实内容整改与原始失败历史，F清单区分净材料变化、自然损毁与正常重登；不恢复旧资产或制造生产样本。

**approved**：当前完整 mixed 准备结果没有未解决的阻断 finding，也没有缺失本 review 必要输入。首轮 F-1 已由独立复核解决。此批准仅允许控制器继续授权的部署与新鲜验证；部署 ready、用户认可结构、损毁材料即时显示/正常重登及原未确认保护门继续待验，不能标 ClientVerified、根契约完成或提前提交完整结果。
