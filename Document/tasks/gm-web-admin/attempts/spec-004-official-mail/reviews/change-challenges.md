# Spec004 完整交付独立对抗挑战

本记录为冻结交付的 fresh change-adversarial 集合，作者 `/root/gm_mail_change_challenge`；独立于生产者和结果审查者。仅生成挑战，不作接受裁决。记录保持 immutable，后续有界修正由匹配结果审查处理。

## 目标身份与依据

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`。
- 基线 HEAD：`573be46b075d8363f0882c2f40a5a0135d1ada0c`。
- 子合同：`../TaskContract.md`，revision 1，目标及 SC-1..3；批准规格：`../spec.md`，SHA256 `4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F`。
- 完整产出：`../verification/owned-files.sha256` 的 29 个 tracked/untracked 代码、测试、页面及使用/协议说明文件；manifest SHA256 `963AF5D85502E0647C943437118CAB2DE591992D05808B3D6C429BD16A1B3908`。本轮逐文件复核 29/29 一致，无身份失配。
- 治理依据：适用工作区 `AGENTS.md`、父级中文/Document 指示及 `generating-adversarial-challenges/SKILL.md`。`implementation-report.md` 仅用于定位，判断来自实际源码、变更、测试及原始证据。

## 攻击范围与证据

围绕错误附件、重复发放、部分批次、失权发送、未知提交被误报和在线投影错用旧状态检查完整交付及实际邮箱/事务依赖，没有扩展为根任务或额外证明系统。

- `GmMailRuleService` 与实际 PVF reward/placement 准备路径共同约束永久普通装备/堆叠物；`GmOfficialMailDomainService.cs:96` 起在写事务内复核管理员、持久防重及目标归属，邮件、新 UID、收据和成功审计同事务。实际领取仍走原 `MailboxDomainService`、装备转移和账号材料容器。
- `GmOfficialMailHttpTests` 对真实隔离 SQLite/HTTP 包含五处中途故障、并发与重启重放、已删除旧目标重放、归属变化拒绝、独立管理员编号、满容量领取恢复、ID 范围耗尽及 durable commit 后 Unknown 恢复场景；检查了判据本身，没有仅采纳测试名称或报告结论。
- `GmOfficialMailEndpoints.cs:54` 的命令完成先于提醒投递；异常提醒只影响提醒状态；`MailboxChangedHandler` 现查邮箱数量，沿用既有通知链。`OfficialMailView.tsx:19,65` 起的生命周期/generation 和冻结 UUID 路径与权限页卸载配合；检查前端异常/刷新/迟到响应判据。
- 阅读 `frontend-all-final.log`、`frontend-build-final.log`、`solution-build-final-r2.log` 和 `solution-tests-final-r2.log` 的原始结果；阅读 `../verification/browser-verification.md`、实际预览截图及 `artifacts/gm-tests/spec004/browser-database-proof.json`，核对批量与单人收据、不同装备 UID 和零金币无附件的证据。

## 保留挑战

**0 项。** 在上述固定目标和攻击范围内，没有找到能把具体交付缺陷连接到误发、重复资产、半批提交、越权或错误恢复的可信实质路径。没有将非强制格式、一般可能性、缺少形式化证明或轻微说明更新问题升级为挑战。零项不代表完整性证明，也不替代独立结果审查。

本轮未 build、运行共享 .NET 测试、变更产品或操作现服。实际浏览器证据只证明隔离后台及其持久化结果，不证明 A21 客户端收到/领取。旧浏览器 fixture 因 App 关闭未取得最终退出码，不能据此宣称正常退出；主控最后 fresh verification 仍须取得真实退出结果，此已知后续步骤不另造阻塞挑战。
