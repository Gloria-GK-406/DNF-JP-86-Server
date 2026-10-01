---
kind: rks-review
verdict: approved
---
# 装备实例重构及普通强化字段修复：当前独立全结果审查

## 审查目标和范围

2026-09-30，由生产者之外的新鲜独立 reviewer 执行 complete mixed change review，没有再次委派。应用 `reviewing-work/references/code.md` 和 `references/deliverable.md`，依据本仓 AGENTS.md、[TaskContract](../TaskContract.md)、[Spec](../spec.md)、[Plan](../plan.md)、`sdd-execution.yaml` 与 TASK-001 至 TASK-004 的实际计划/结果。该 review gate 未要求单独 challenge set。

目标工作树为 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`，HEAD/基线为 `6e60c4c67417ce06b88e9e99277fb1e4c0239726`，结果尚未提交。检查完整 Server 差异及全部新增 Server 文件、关联文档和新部署/恢复工件。32 个 Server 文件（含删除标记）的集合指纹为 `b60d00685bacb7127918aa9c314bb45ca96fb248e3cfb95a9d318abb5a47a3b0`；reviewer 独立计算逐文件 hash、集合 hash，并与 Git 的完整 Server 变更集合交叉核对，无遗漏或 mismatch。仓库无 `.codegraph/`。

旧 [round-1](change-review-round-1.md) 的 approval 漏掉普通强化字段语义错误，不是本目标的 approval。本次重新检查字段依据和回归 oracle；未变的身份/事务等基础检查可参考旧记录，但当前判决建立在当前源码及证据上。

## 判决

**approved**：当前完整代码和可验收交付物未发现未解决的阻断缺陷，可以进入修复版最终人工验收。该结论不表示 SC-6 已满足，不授权生产资产恢复、提前提交或标记 ClientVerified。

用户已反馈正常退出，仅支持服务切换和当前离线检查，不能视为 UID689 恢复授权。E-01 结构认可、修复版本 R-01 新请求即时强化、R-02 正常退出重登仍是下一阶段人工门。

## 决定性检查

| 检查面 | 实际检查与判断 |
|---|---|
| 字段依据 | `Docs/GM工具_新背包表结构与ItemCore语义.md:458/462/517` 与旧 `86JP/.../ItemCore.cs:183-186` 区分 Attribute 低5位强化、高3位密封及独立附魔升级次数。当前 DrawnAppearance 已按 Attribute&0x1F 投影等级，附魔次数另作 Flag20。客户端静态 NOTI0D entry 表区分 +0A ext_data1 与 +12 enchantUpgrade；当前共享 writer 和 persistence encoder 分别写 Attribute/core9、EnchantUpgradeCount/core17。上述依据支持最小字段映射修正，不支持新增未知通知或宣称客户端已验。 |
| setter 与读者 | Data 只增加只读 `ReinforcementLevel`；实例 `SetReinforcementLevelAsync` 拒绝超过31，写 Attribute 低5位并保留高3位和附魔次数。全生产检索中普通强化 Domain/Rule、RepairRule、Outworld 准入均使用正确语义；保留 bead 升级拒绝、独立 codec 字段和原外观映射，没有机械全局替换。+30→31及31拒绝有直接回归。 |
| 规则/领域/实例分工 | 强化 prepared rule 接管内容、费用、概率、处罚、保护优先级和销毁产物决定；只接受快照/保护事实/显式 draw，不查询角色库存。Enchant/Repair 规则拥有其内容与公式。实例只修改字段/销毁，领域保留会话、资产、期限、空间和事务编排。旧 snapshot 及其修理公式已删除；分解原内容准入旁支没有被包装成全部分层完成。 |
| 身份与调用覆盖 | 当前实例每次按 owner+UID 读最新暂存状态，绑定原事务；共享 tombstone 关闭销毁后另一句柄和同 core 重放。暂存身份读取复用结算 Survivors，并补合法 transfer source；写入不接受 UID 盲写。9项真实 SQLite 实例回归覆盖多句柄、同模板多UID、生产 Flush 移动语义、越权、提交/回滚寿命、销毁与未分配UID。五项养成、修理/自动修理、耐久和分解装备 writer 均迁至实例；残余容器写入是工具/产物或空间模拟。 |
| 独立 wire/persistence oracle | 新 handler 测试经实际 CommandBus/SQLite，断言原始 core9=0xA1、core17=1和 UID 保持，再调用实际注册 NOTI0E encoder断言 entry+0A/+12。Clear/重新 Load 后通过注册的完整 NOTI0D snapshot encoder核对相同字节。高3位0xA0与非零独立附魔次数能辨别错误映射；不是只以新 getter 自证。原始 RED TRX确为预期161/实际160的行为失败，GREEN同用例通过。 |
| 直接及跨功能回归 | 成功、保级、降级、保护等用例保留原资产/事务检查并增加原始字节；修理分别以0xA0/0xA1及附魔0/7证明费用113/226取决于强化位段；Outworld独立附魔非零仍可处理+0、Attribute+1拒绝；附魔/锻造/封印使用不同强化、密封和附魔值并比较完整 core。既有销毁、提交失败、重放、保护券和产物容量回归保持。 |
| golden 适配 | Persistence两个完整文字dump因新增公开只读派生属性失败。差异仅补6行，原 seed、原字段与扫描未改；139/169/109/49/89/69 &31恰为11/9/13/17/25/5。没有忽略字段、缩小测试或改变存储布局。完整 Persistence复跑385通过支持该适配。 |
| 文档和人工门 | 当前 diagnosis、field-fix-current、implementation-examples、Human-Test-List准确纠正旧字段语义；旧证据有历史范围提示。TASK-004及 Human-Test-List使 UID689恢复仅为附加核对，R-01仍使用未变UID339执行一次新强化，不能以直接改数据或旧成功动画代替新版本请求验收。其他GUI范围保持原清单，不扩大本次门。 |

## 原始证据和当前部署

reviewer 读取原始 `full-test.log`、`persistence-final.log` 以及八程序集最终 TRX，按实际 `UnitTestResult` outcome 独立统计：Application2314、Protocol1202、Infrastructure408、Persistence385、Networking72、Host24、HttpApi50、Pvf60，共 **4515 Passed、0 Failed、2 NotExecuted**。两个 NotExecuted为 Town/Quest missing-archive 负向条件测试，与控制台2 skip一致；不能使用 Counters 中不准确的 notExecuted=0 隐去跳过。

首次完整 solution 仍为 Persistence383通过/2失败，原始失败证据保留；随后只修改该测试文字golden，并完整复跑Persistence385通过，其他程序集目标未改变且原结果通过。本报告不虚构整条 solution 最终 exit0，也没有机械重复全套测试。Release零警告/错误、相关装备319回归和 publish结果来自当前任务验证记录；reviewer另执行 `git diff --check` 通过。

检查新 probe 源码和日志：实际模块、真实PVF、隔离 `probe.db`，选角编码60消息；UID339实际报价450金币/3037×10、固定draw成功+1、两句柄及跨事务身份持久化通过。probe直接调用规则/实例，没有按玩家业务扣费，因此只证明这些能力，不证明GUI扣费闭环。

reviewer只读核对7个主DLL（Abstractions/Application/Data/Persistence/Protocol/Networking/Host）与当前工作树Host Release输出逐字节一致；PID41000实际exe位于新 runtime，7001/10010/10011/18080均属该PID且均为Listen，没有游戏已建立连接；正确 bootstrap 路径 `/api/launcher/v1/bootstrap` 返回apiVersion1/status ready，stderr为0字节。未停止/启动服务，未修改生产数据。

## UID689恢复提案的操作边界

reviewer读取提案和脚本，独立只读核对原部署备份 UID689 core9=0/core17=0，与当前生产/新备份 core9=0/core17=1相符。原备份位置为角色2/list3/slot12、耐久47，当前为角色2/list0/slot9、耐久48；新提案以当前完整core为基础，正确保留用户其间真实移动/修理结果，而非整件回滚到旧备份。

before/after均为82字节，只改变9和17；新备份quick_check=ok。已有隔离dry-run结果与完整after一致，金币89516/无色9796保持；生产仍与before完整一致，恢复未应用。

`apply-approved-recovery.py` 默认仅使用备份写隔离副本、已有副本拒绝覆盖；生产只有显式 `--apply` 路径。脚本在 `begin immediate` 中验证character2拥有者和完整before core，以 UID+完整core 条件执行单行 UPDATE，并检查rowcount=1、完整after及金币/无色不变，异常退出由事务回滚。只更新items，不改变slot/UID/资产，也不执行整库转换。

**授权和离线是控制器执行前的外部前置条件，脚本不自动取得用户授权或判断客户端在线。** 当前确认退出和只读离线状态不构成恢复授权；若后续要应用，必须先获得本提案的明确同意，再重新核对离线、owner和完整core。本 review 仅批准已准备的可复核提案与安全执行边界，reviewer未运行脚本、更未执行生产 `--apply`。

## 交接

阻断 finding：无。缺失本 review 前置输入：无。当前 `completion-evidence.md` 的 fail及旧版本历史不算新通过，控制器应在消费本 review 后用当前目标/PID/证据刷新 verification为 `manual_required`，继续E-01/R-01/R-02；不得继承旧22文件approval或旧实机结果。最终用户门通过前不seal、不commit、不冒认ClientVerified。
