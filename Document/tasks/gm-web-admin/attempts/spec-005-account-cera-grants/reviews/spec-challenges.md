---
kind: adversarial-challenge-set
set_id: ef2df0b2-760a-41ca-897a-d141c042e0fd
stage: spec-adversarial
target: ../spec.md
target_sha256: 958277A02E6AC3B67363D995E7728616939A57A752735B088D6EF2F08288EA0B
contract: ../TaskContract.md
contract_revision: 1
contract_sha256: CA5347B254D3D2CD77C63F30226BEA845B9D1C20B673F5FD8C18165EE118192E
high_risk_count: 0
low_risk_count: 0
immutable: true
---

# Spec005 独立规格挑战集

## 身份、依据与攻击范围

目标为 `Document/tasks/gm-web-admin/attempts/spec-005-account-cera-grants/spec.md` 的上述 SHA；child 合同 r1 为当前授权边界。该集由独立于生产者和后续审查者的 fresh 上下文生成，未使用生产报告；生成后保持不可变。

治理来源：工作区及本 worktree 的 `AGENTS.md`、child `TaskContract.md`、根 `../../TaskContract.md`，以及目标明确继承的 Spec002 管理员访问边界与 Spec004 对外行为、原子提交及恢复语义。按 `generating-adversarial-challenges/SKILL.md` 评估可信失败场景及最小必要修正。

攻击范围：实际账号及无角色/离线前置、三币输入与上限、管理员权限及冻结确认、持久防重和同编号冲突、整批账号租约/事务/CAS、未知结果恢复、提交后当前钱包及精确会话投影。仅做静态文件和必要 live code 读取；未构建、运行测试、浏览器或现服操作。

## 保留挑战

0 项；high-risk 0，low-risk 0。本集不提供审查 verdict，也不证明规格完备。

筛选依据：

- `spec.md:25-32,45-63` 已明确实际 accountId 去重、三币有效范围、服务器身份、事务内复权、完整范围租约、恰好一行 CAS、资产/收据/成功审计同事务及持久唯一键；缺少某个具体实现 API 不形成规格缺口。live code `AccountExecutionBehavior.cs:23-48` 与 `AccountTables.cs:28-44,86-95,139-150` 提供可实施的账号范围及钱包接缝。
- `spec.md:55-59,69,81-82` 与继承的 Spec004 `spec.md:64-66,74` 对成功后目标变化、规范化重试、响应丢失、查无收据、未知状态及浏览器冻结输入已规定安全行为。现有 `GmOfficialMailDomainService.cs:94-103,130-148`、`SqliteGmMailPersistence.cs:12-13,39-52` 可供实现借鉴；不据此断言点券实现已满足要求。
- `spec.md:31,63,83,96` 要求通知现查当前钱包、按账号解析当前精确会话，并禁止历史购买收据和有角色 entry 路径。live code `CeraShopPurchaseCompleted.cs:17-23` 证实历史余额路径确实需要避开；`SelectedSessionDomainService.cs:19-44,48-65` 提供当前会话查询。通知仅为提交后尽力提醒、客户端展示留用户验收，故不因缺少 outbox 或完整投递实现细节保留挑战。

上述代码锚点均相对 `Server/src` 对应项目目录；规格已给出针对竞争、失败注入、异常钱包、重启防重和通知的验收要求。未发现另一个需要立即改规格、且不能由这些后续实施检查低成本控制的独立物质风险路径。
