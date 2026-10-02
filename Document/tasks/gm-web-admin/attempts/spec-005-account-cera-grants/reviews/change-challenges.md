---
kind: rks-adversarial-challenges
stage: change-adversarial
state: frozen
spec: ../spec.md
spec_sha256: 958277A02E6AC3B67363D995E7728616939A57A752735B088D6EF2F08288EA0B
manifest: ../verification/product-owned-manifest.md
manifest_sha256: 0D3EBAF508FFE04891D0ADE3DF8D5196B0A5639F6E1120DEB6FDAEA0BE9E82F6
product_count: 23
retained_challenges: 0
---

# Spec005 完整交付挑战集

目标为工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin` 的冻结清单23项产品；调度基线 HEAD 为 `07e1f607777c082a09e4614d5d81cf137dee41ce`。本次实际重算 Spec、清单及全部产品文件 SHA256，均与调度身份及清单一致。依据 child `TaskContract.md` revision 1、已批准 `spec.md` 和 `generating-adversarial-challenges/SKILL.md`，以独立于生产者及最终 reviewer 的新鲜上下文生成；本记录不作审查 verdict 或完整验收结论。

攻击范围为账号资产整批原子性、三币范围及其它交易、管理员范围 UUID 防重与权限恢复、只按实际账号结算、提交后当前钱包/精确会话投影，以及冻结输入的未知结果恢复与失权迟到响应。读取产品实际源码、注册/持久边界和相关既有事务/投递接缝，结合测试源码、已有日志与 Chrome 证据核对，不根据生产者总结直接接受其结论。未运行 build、测试、浏览器或 Git，也未修改产品。

## 保留的挑战

**0 项。** 本轮未找到同时具备具体目标缺陷、可信触发场景、实质损害及不能由已有控制廉价收敛的因果路径；没有为凑数量新增 CH 项。

检查中影响该判断的具体控制：

- `GmCeraCommands.cs:13-19` 冻结完整账号范围；`AccountExecutionBehavior.cs:35-48` 先有序取得全部租约；`ApplicationModule.cs:545-547` 注册共同事务及 Grant 的 Account Exclusive。`GmCeraDomainService.cs:65-80` 先占写边界、复权、防重，再读实际钱包、逐项CAS并保存收据/成功审计；`SqliteDatabase.cs:63-67,88-102` 与 `UnitOfWorkBehavior.cs:36-49` 将这些语句归于同一结算。没有锁A写B、按角色增款或预览快照覆盖当前余额的已发现路径。
- `SqliteGmCeraPersistence.cs:23-38` 和 `CharacterDatabaseSchema.cs:235` 以管理员/规范化 UUID 持久唯一定位；`GmCeraDomainService.cs:67-72` 在当前目标校验之前恢复同内容原事实。`GmCeraHttpTests.cs:70-107,201-238,262-273` 对并发/重启/删除后恢复、第二目标CAS及收据/审计失败、未知提交、排队撤权和真实商城消费竞争提供具体断言；现存 cera-final.log 为17通过。
- `GmCeraEndpoints.cs:50-54` 只在命令结算返回后提醒；`AccountWalletChangedHandler.cs:13-18` 核对精确当前会话并现读三币，已有 NotificationBus 重试重新 compose。通知异常不触发再次增款；历史收据余额没有进入该投影。
- `CeraGrantView.tsx:36-65,70-73` 在发送前保存冻结 UUID/输入，未知结果保留同请求，查询404不释放它；`AdminApp.tsx:31-39,108-113` 的身份/权限切换卸载旧视图，配合组件 generation/alive 防止迟到资料恢复。`CeraGrant.test.tsx:52-95` 的存储失败、超时刷新、原内容重试、404与失权行为和现存前端54通过日志互相对应。

## 证据边界

`verification/browser-verification.md`、两张批量截图及 `artifacts/gm-tests/spec005/main-chrome-db-proof.json` 一致记录 TokenCera单账号+7、Cera两账号各+50、2笔成功收据及2条成功审计；fixture日志记录正常停止。正常刷新不等于真实断网；未知刷新恢复目前是前端行为测试证据。A21三币展示/提醒接收/重登余额仍按合同列为用户待验，未把这些升级为 ClientVerified 或漏实现。

旧Schema计数54→55的失败及窄修复已有保留日志；已读取目标3通过与完整Persistence396通过日志，未把已闭合失败当新挑战。后续独立完整结果审查及主控新鲜验证仍须继续。本文件一次写入并冻结，稳定风险基础上的小修复不要求重生本挑战集。
