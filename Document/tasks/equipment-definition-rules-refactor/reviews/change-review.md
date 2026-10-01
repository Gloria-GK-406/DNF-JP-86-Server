---
kind: rks-review
verdict: approved
---

# 装备基本信息与统一规则：完整差量独立审查

审查基线：`38f71bd2c14ef57855c9c3ab9ddb0d83bf21de6d`。目标：该基线至当前工作区全部任务所属生产、测试及文档差量，包括未跟踪新文件；已包含最终新增的真实项链/戒指断言和 `ComponentLifecycleTests` 内容准备夹具迁移。本报告批准此实现与文档审查门，不代替完成验证、部署记录或用户最终验收。

依据：`TaskContract.md` revision 1、`spec.md`、`plan.md`、`spec-result.md`、`Human-Test-List.md`、`sdd-execution.yaml`、仓库 `AGENTS.md`；采用 `reviewing-work/SKILL.md`、`references/code.md` 与 `references/deliverable.md`。独立读取了完整 `git diff 38f71bd2` 与全部新生产/测试文件，追踪相关命令日志、事务、异次元 handler 和启动准备链路，并读取已有 TRX。未修改生产/测试代码，未运行 build/test、提交或部署。只运行只读检索和 `git diff --check`，后者通过。

## 裁定

未查实当前审查范围内的阻塞缺陷，无需要最小修复的未决 finding。

| 条件 | 核对结论与依据 |
|---|---|
| SC-1 | 新 `EquipmentDefinition` 仅包含等级、品级、稀有度、归一化类型、`ushort? MaxDurability` 及禁止内容声明。全 `Server/src` 检索无旧基本信息类型、`CanReinforce`、`EquipmentTypeWeightIndex` 或三个旧规则接口；旧实现与强化源的旧读取/缓存均删除。强化、异次元、锻造迁移到同一 `IEquipmentUpgradeRuleService`，强化失败服务迁移到通用内容源。DI 接线相符。 |
| SC-2 | `PvfEquipmentDefinitionSource` 区分缺失耐久 `null`、零和合法 ushort 值；数字字段严格验证类型、唯一值与边界。最终真实 PVF 用例实际读取手镯 `100310498`、项链 `100310755`、戒指 `100320520`，三者完整事实且无耐久；武器 `101010437` 保留 45 耐久，肩甲 `100170123` 保留耐久。最终 Infrastructure TRX 84/84，无跳过。 |
| SC-3 | 索引未收录模板返回 null；已收录模板缺失、必需字段缺失/重复、数值错误/越界、类型非法、禁止声明非法/重复均抛含 item/path/field 的 `InvalidDataException`。索引亦严格验证 ID、路径、重复、空/缺失/非脚本/截断。模板读取成功后才写缓存，索引读取成功后才赋值；异常不会写入负缓存。重复读取非法条目/非法索引仍抛异常的用例已覆盖。 |
| SC-4 | 异次元规则继续保持最低等级、稀有度、称号排除、初值表、已强化及属性状态限制；不读取/比较可选耐久。新增三类无耐久首饰 handler 交易用例证明成功赋予、扣一份材料、SQLite core 改变及刷新。已有领域/handler 失败不扣、UID/其他字段保持、容器重建后重读及事务失败用例保持。实际客户端显示和真实重登留给人工清单 E02–E04/E08。 |
| SC-5 | 普通强化将旧费用类型索引映射原样移入规则服务；费用公式、当前等级默认中性权重、舍入、等级/稀有度上限、普通强化禁止内容及称号规则保持。满耐久判断只在声明耐久时执行，零耐久仍必须精确相等。保护券优先级/选择、成功率和失败结果算法未改变；失败奖励服务只有内容类型/依赖迁移。锻造继续只接纳武器，原表、材料费用、成功率、等级及失败不降级行为保持，无金币费用新增。 |
| SC-6 | 内容异常没有转为业务拒绝；异次元生产 handler → transactional command → 领域 → 统一规则的异常到 `CommandLoggingBehavior` 记录 Error 后重抛，handler 标记失败并重抛，不发 ACK。`UnitOfWorkBehavior` 非正常返回不提交，dispose 回滚；当前装备基本内容读取均在扣材料/金币及改装备之前。新增异常用例实证 Error、材料与持久化/运行态装备不变、无 ACK及重试再次抛出；既有结算异常用例另外覆盖扣料后的真实回滚。启动仅准备必需装备索引，非法索引阻止监听；未承诺预解析所有模板。Host 非法索引 2/2 与修正后的生命周期失败/未 ready 用例 1/1 支持此边界。 |
| SC-7 | 文档明确自动验证、未部署与人工待验收，人工清单覆盖三类首饰、失败不扣、普通强化、保护券、锻造及退出重登。SC-7 仍须用户最终反馈；本评审不关闭它或根任务。 |

原始 `[impossible contents]` 的合法声明由通用源保留为不可变集合；普通强化的 `upgrade` 禁止检查保持，新基础增幅资格查询也解释该声明。异次元和锻造没有借本次重构新增交易行为或改写其既有操作语义。

## 验证证据与限制

读取的 TRX 位于主仓库 `artifacts/equipment-definition-rules-20261001/tests`：

| 文件 | 读取结果 |
|---|---|
| `infrastructure-equipment-final.trx` | 84/84；真实三类首饰/武器/防具用例 Passed，包含最终新增两类首饰断言 |
| `application-equipment-final.trx` | 326/326；三类首饰交易与解析异常 Error/无 ACK 用例 Passed |
| `application-equipment-real-pvf.trx` | 较早的 323/323，全执行 |
| `host-equipment-startup.trx` | 2/2，空/重复必需索引拒绝 |
| `host-lifecycle-remediation.trx` | 1/1，内容准备期间与失败后所有端口关闭，game readiness 为 false |
| `protocol-equipment-real-pvf.trx` | 24/24；替代较早无真实 PVF 时 21 执行/3 未执行的结果 |

`host-full.trx` 保留修复前 25/26 的历史失败：旧生命周期夹具在 `IIndependentDropRuleService` 注入阻断，但新增装备索引准备先拒绝假 PVF，导致旧夹具 Entered 超时。当前差量把阻断/失败注入移到首个准备环节 `IEquipmentDefinitionSource`，原全部端口及 readiness 断言保持；对应修复用例已复跑通过。不能把历史 Host 全回归写成 26/26，除非控制器另提供新的全项目通过结果。

Release 全解决方案构建 0 错误/警告为控制器提供的执行结果；评审没有自行执行或以构建代替代码检查。`spec-result.md` 与 `Human-Test-List.md` 当前仍是审查前/验证进行中的记录，最终测试计数、产物和部署状态由后续 `verification/completion-evidence.md` 统一更新，不能将本文用于声称已部署。

真实内容读取与合成首饰交易分别证明内容事实、规则及现有结算链路；不等于 A21 实机验收。没有将增幅等级提升交易或设计图晶体 x0 显示缺口当作本次回归，它们属于明确排除范围。最终完成必须继续保留用户验收门。

## Closing round：最终文档与验收边界复核（2026-10-01）

```yaml
kind: rks-review
verdict: approved
```

本轮目标是提交前的最终文档与验收结论：更新后的 `Human-Test-List.md`、`spec-result.md`、`verification/completion-evidence.md`、`verification/final-audit.json` 和执行状态。前轮的未部署、人工待验收及 Host 历史失败说明保留其审查时点；当前结论以下述最终证据为准。重新检查相对 `38f71bd2` 的生产/测试差量、变更清单和新增文件，未发现超出前轮已批准范围的生产或测试增量；前轮代码裁定继续有效。

最终文档复核无阻塞 finding，批准当前收尾候选：

- 独立重新解析四个最终 TRX 的 Counters 和每条 UnitTestResult：Application 326/326、Infrastructure 84/84、Protocol 24/24、Host 26/26；结果条数分别与 total 相等，全部 outcome=Passed，合计 `326 + 84 + 24 + 26 = 460`，无跳过。它们是文档列出的相关过滤及 Host 全项目，文档没有声称全仓所有测试通过。新的 `host-full-final.trx` 支持当前 26/26，修复前 25/26 未被删除或改写。
- 独立读取发布哈希记录并计算 Abstractions、Application、Infrastructure、GameServer 当前 Release DLL 与 `artifacts/equipment-definition-rules-20261001/runtime` 同名 DLL 的 SHA256，四项全部相等，并与 `final-audit.json` 相符。部署准备、启动及 ready 结论有单独记录，没有把本轮只读评审写成再次部署。本轮未运行 build/test 或操作服务/数据库。
- 用户原文依次覆盖 E01–E05、E07/E08 和最后的 E06。末次“失败回到10、再次失败回到0、装备没有损坏、两券消失”与 `accepted-protection-failures.log` 的 item=101040127、old=11→10/old=10→0、penalty=3、Downgraded、consumeProtection=True 两条记录一致。SC-7 已有用户最终反馈支持，E09 是对连续反馈的归档，不需要凭空增加一次重复确认。
- E07 实机证据明确为武器可以锻造、非武器在客户端选取阶段拒绝；服务端非武器拒绝归属自动用例，没有夸大为新的服务端拒绝实帧。保护失败日志也只声明日志事实，没有改写协议证据等级或宣称新抓包。
- 保护券发放保持清晰授权链：各20张属于未落正式资产的隔离 dry-run；正式各1张普通券7804/超级券10007840来自用户后续明确新授权，回执/备份另存 artifact。最终文档没有把补券写成原代码重构默认授权或混入业务代码范围。
- `spec-result` 的 completed、验证 decision=pass、用户项目归档与 `sdd-execution` completed 相互一致；此刻是本地 accepted-result 提交前的候选，尚未提供结果提交 SHA，不应对外报告提交已完成。继续保留 task/solo-play-roadmap 分支、工作区、服务、数据库与客户端；无 push/PR/merge/清理授权扩展。
- 克伦特增幅等级提升交易与设计图晶体 x0 显示仍明确留作后续 fix，未被本次最终验收闭合掩盖。当前资格查询、异次元赋予、普通强化和锻造回归的批准不等于完整增幅升级交易交付。

本轮只追加本评审文件并执行只读检查；`git diff --check 38f71bd2` 通过。根任务终态与本地结果提交仍由控制器按既有授权处理。
