---
kind: rks-review
verdict: approved
review_round: 2
reviewer: /root/gm_mail_final_review
target: spec-004-official-mail bounded F-001 remediation and direct regressions
accepted_head: 573be46b075d8363f0882c2f40a5a0135d1ada0c
contract_revision: 1
approved_spec_sha256: 4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F
owned_manifest: ../verification/owned-files-r2.sha256
owned_manifest_sha256: 9D31E9639EF5E2B161DB24C788AB584A517BA604BD4B31D46B4184BCC5FFF770
owned_file_count: 29
previous_review: ./change-review.md
previous_review_sha256: 1A3D733A6952D2C3B79464341C3500D8CC440E526EBCCB4DFAEB5A1956967AC0
change_challenges_sha256: EEB9A80FAD1D2401088DCB26EBDCF678676A36F4958F5BC4733CF42CE837E3F2
---

# 官方邮件有界最终复审：Round 2

## 身份与范围

由原独立最终 reviewer 执行，继续与 producer、Spec/drift reviewer、挑战作者分离；适用此前加载的 reviewing-work Code / Deliverable 判据及 AGENTS。当前 final-review / in_progress，依据仍为子合同 r1、批准 Spec 和首次整改授权 `verification/remediation-round1.md`。本轮只裁决 F-001 与直接回归，Round 1 未变化的完整 mixed 审查证据按 `change-review.md` 保留，不重做全矩阵或扩大方案。

实查 HEAD、Spec、immutable challenge 与旧报告身份均保持。r1/r2 manifest 路径集合相同，29/29 当前文件 SHA 一致；仅两项变化：

- `GmOfficialMailDomainService.cs`：`BDF64621A850FA596E7B5AFF28A6754325D0A68B795EE1824CCB1FB7CA5BFF95`。
- `GmOfficialMailHttpTests.cs`：`F96272347688CF69E689598395C0D8DC2BF6F5F564480CEF2BA0CC6F5EF346F6`。

## 裁决与 finding closure

**approved；F-001 closed。** 当前整改满足 RQ-005 / SC-2 的正常初始 core 要求，未发现其直接影响中的新阻断。批准完整结果的当前有界复审门，不宣称主控完成验证或真实客户端验收已通过。

`Server/src/Dfo.Server.Application/Administration/GmOfficialMailDomainService.cs:107,117-120` 在每人每件展开处生成 `1..int.MaxValue-1` 的正常随机实例值，以全批 HashSet 防重复，再 `with` 只替换 Value。与既有 `CharacterRosterService.EquipmentCore` / `OpeningEffectHandlers.Next` 的初始化一致，既有耐久、封装及其它 core 保留。世界 UID 仍由原世界表独立分配，邮箱保管及领取沿原路径；没有把 Value 替换成世界 UID 或修改 schema/wire。`:99-100` 的成功收据重放仍先于创建循环，因此原编号重试不生成新的随机值或装备；全批事务、审计及提交后通知顺序未变。

实查扩展真实用例 `ActualPvfEquipmentStackAndAccountMaterialsAreDetachedThenClaimedThroughExistingMailbox`：从 HTTP 发送及 SQLite 四个实际装备 core 开始，断言值正且互异；正常 `MailboxCommand.Open/Claim` 经合成已选会话执行，解析实际注册 MailboxCompleted/encoder 的邮箱摘要和 Inventory delta，校验原实例字段。领取时核原 UID、core、耐久/封装；领取前后同 UUID 重试核原装备及完整行不改变，只有一条成功发送审计。测试没有以 mock core、另造 encoder 或背包直接写入替代产品路径。模拟会话只证明服务器投影。

## RED/GREEN 与测试 oracle 裁决

直接读取原始整改日志，不仅采纳生产报告：`remediation-round1-red-r2.log` 成功编译后在四个实际 Value=0 的范围断言失败，1 failed，是有效行为 RED；`remediation-round1-red.log` 的 xUnit2031 analyzer 编译失败不计行为 RED。`remediation-round1-green-r3.log` 同用例 1 passed。

接受两个中间 oracle 修正：

- `green.log` 的缺 concrete handler 服务来自错误解析方式。实际 `NotificationDispatchingExtensions.AddNotificationHandler` 注册 `INotificationHandler<T>`；当前测试解析已注册泛型接口，仍使用真实 MailboxCompleted 投影，没有绕过失败或改变产品。
- `green-r2.log` 比较领取前后全表，额外两行是物品 3 正常领取产生的堆叠 core；实际 `MailStackTransfers.GrantAsync` 经正常容器新增堆叠物。这不代表额外装备。当前测试同时比较原四装备 core/UID不变及领取后整表在同编号重试前后不变，保留防重与初始化判据，未把真实产品缺陷放宽成通过。

当前相关日志为 Host 91/91、Protocol 24/24、Architecture 34/34，均显示通过；旧全量 5355 / 前端 41 证据仍按 Round 1 的明确历史目标引用，不能称为整改后的新鲜全量结果。

## mixed 证据与挑战

已查看 `verification/browser-r2-result.png`、浏览器记录及原始 `artifacts/gm-tests/spec004/browser-r2-database-proof.json`：当前真实隔离页面操作 `abc21fb8-1a52-47f2-8e6c-24dedcaf3239` 对角色1/2各发27312×2、金币0，两封邮件和一个收据。世界 UID 为1/2/3/4，四个 Value 分别1167942060、1245084504、1350581995、221843989，均正且不同；耐久45、seal0。截图对应同一收据及 UID。旧 core0 库保持整改前 finding 证据；不把它当作修后通过，也不声称新浏览器检查证明领取。

immutable change challenge 集仍为0项，无逐项 hypothesis 待裁决。本轮根据实际修改和证据关闭自行发现的 F-001；没有修改挑战集合。其它产品/说明/CurrentImplementation 证据等级按旧报告保留，没有新增 scope、ClientVerified 主张或独立输入缺口。

## 交接边界

主控继续串行 fresh 全量验证，并用新的 VERIFY_ONLY 隔离夹具取得实际退出码与停止证明；新浏览器 runner 也因 App 关闭失去 session，不能当作正常退出证据。真实 A21 邮件表现仍属既有 MAIL-01..09 验收，Vite chunk 观察保留。上述不构成本轮额外产品整改或新增接受条件。

本轮仅新建此报告，旧报告未改；写前确认输出祖先无 reparse point。未运行共享 .NET build/test、修改产品/Spec/合同/挑战/游标/Git，未启动服务或操作现服/真实数据/客户端/PVF。报告完成后停止。
