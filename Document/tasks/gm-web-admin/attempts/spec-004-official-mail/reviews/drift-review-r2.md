---
kind: rks-review
verdict: approved
disposition: aligned
review_round: 2
target: spec-004-official-mail bounded F-001 remediation and direct effects
accepted_head: 573be46b075d8363f0882c2f40a5a0135d1ada0c
approved_spec_sha256: 4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F
owned_manifest: ../verification/owned-files-r2.sha256
owned_manifest_sha256: 9D31E9639EF5E2B161DB24C788AB584A517BA604BD4B31D46B4184BCC5FFF770
owned_file_count: 29
previous_drift_review: ./drift-review.md
previous_drift_review_sha256: 386F6A4279CFAD7A42B5BB459DA1FBAB0554149CCA91370F8871FE5324035F59
---

# Spec004 有界整改漂移确认：Round 2

## 审查身份与范围

本轮由原独立漂移 reviewer `/root/gm_mail_drift` 执行；保持与生产者及最终产品 reviewer 分离。适用已加载 `reviewing-work/SKILL.md` 与 `references/drift.md`。当前阶段 `drift-check / in_progress`，仅检查 `verification/remediation-round1.md` 授权的 F-001 修正及其直接影响，上一轮未变化矩阵按 `drift-review.md` 引用保留，不重审整个产品，不作最终质量/完成判决。

治理仍为原根/子 TaskContract revision 1、批准 Spec 及 root 方向；Spec SHA 与批准身份一致。immutable `change-challenges.md` SHA 仍为 `EEB9A80FAD1D2401088DCB26EBDCF678676A36F4958F5BC4733CF42CE837E3F2`，保留 0 项，不因整改重写挑战。HEAD 未变。直接比较 r1/r2 两份 29 项清单，无新增/删除路径，仅以下两个文件哈希改变；r2 全部实际文件哈希一致，旧 manifest 与旧 drift 报告哈希保持原值。

- `Server/src/Dfo.Server.Application/Administration/GmOfficialMailDomainService.cs`
- `Server/tests/Dfo.Server.Host.Tests/GmOfficialMailHttpTests.cs`

生产者附加轮次及 `producer-verification-r2.md` 仅用于定位；本轮实际读取整改实现、扩展测试、已有正常初始化对照、RED/GREEN 和相关回归日志，并对照主控新浏览器原始数据及截图。

## 裁决与直接影响

**approved / aligned**。当前修正恢复批准的 RQ-005“正常初始 core”目标，保持根 SC-4 的邮箱发放及领取路径；没有改变验收、授权、阶段分解或资产架构，未发现需要停止路线推进的漂移。

`GmOfficialMailDomainService.cs:99-100` 的已提交收据重放仍早于新实例创建。`:107,117-120` 在每人每件装备展开时生成 `1..int.MaxValue-1` 的实例 Value，用全批共享 HashSet 避免重复；`with` 复制只替换 Value，仍由原世界表分配独立 UID，再交系统邮箱保管。这与实际 `CharacterRosterService.cs:369-370`、`OpeningEffectHandlers.cs:57-59` 的正随机初始值方向一致；没有把模板级一次生成值复用给全批，也没有将 Value 误当作世界 UID。原耐久、封装、原子事务、角色/账号复核、持久防重与提交后通知顺序保持原路径。

扩展的 `ActualPvfEquipmentStackAndAccountMaterialsAreDetachedThenClaimedThroughExistingMailbox` 仍以真实 HTTP/SQLite/PVF 发送为起点：直接读取四件装备 core，断言正值且互异；通过实际 MailboxCommand Open/Claim、MailboxCompleted 投影与注册编码器检查邮箱摘要及领取后的背包字段，核对原 UID/core/耐久/封装。领取前后用同 UUID 重试，断言装备行与成功审计不新增。新增 `EncodeBody` 仅用于测试真实编码器，没有引入产品协议布局或旁路资产写入。模拟选中会话只证明服务器投影，未声称真实 A21 已收到。

本轮直接看到 `remediation-round1-red-r2.log` 的四个 Actual=0、范围断言失败及单例失败统计；`remediation-round1-green-r3.log` 同一真实用例通过。相关日志分别记录 Host 91/91、Protocol 24/24、Architecture 34/34，通过结果与有限改动的直接回归方向一致。本 reviewer 未运行 build/test；这些历史日志不替代主控最终新鲜全量验证。

## 混合产出证据与剩余边界

旧 `browser-database-proof.json` 的 core.Value=0 库及旧截图保持 F-001 证据，不作为当前修正通过证据。新 `browser-r2-database-proof.json` 与 `verification/browser-r2-result.png` 一致对应 request `abc21fb8-1a52-47f2-8e6c-24dedcaf3239`：角色 1/2 各两件 27312、两封邮件及一个操作，世界 UID 为 1/2/3/4；四个 Value 为 1167942060、1245084504、1350581995、221843989，均正且不同，耐久 45、seal=0。截图可见相同收据及 UID，保持原中文确认/结果产品路径。这补充当前实现的隔离页面证据，未覆盖真实客户端表现或将旧错误数据说成已修复。

主控通报本轮 browser runner 也因 App 关闭失去最终退出码；本报告不把 runner 当正常退出。最后 fresh VERIFY_ONLY 自检及停止核对、全量验证仍由主控完成，不构成新实机门或路线变化。MAIL-01..09 的 ClientVerified 边界与旧非阻塞观察按上一轮保留。

无阻塞漂移 finding。本报告只建议消费当前 `approved / aligned` checkpoint；F-001 是否达到完整质量接受，由当前最终独立复审裁决。root 剩余点券和集成结果仍按原 map 待交付，本轮不创建未来 child scope、不推进任何游标。

仅新建本 `reviews/drift-review-r2.md`；旧报告未改。写前核对输出绝对路径包含于任务工作区，祖先无 reparse point，文件事前不存在。未修改产品、合同、Spec、挑战、控制游标或 Git，未 build、启动服务，未操作现服/真实玩家库/客户端/PVF。报告后停止。
