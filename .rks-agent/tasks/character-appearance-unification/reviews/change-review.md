---
kind: rks-review
verdict: approved
---

# 角色外貌统一计算：独立结果审查

日期：2026-10-14。审查对象为 `task/next-round-20261013` 工作区相对 `1e146ba1af7af72c762524c68fbea1e4e2e32196` 的全部 tracked diff，以及本任务新增的三份外貌源文件、两份测试文件和 `Document/CharacterAppearance-20261014.md`。HEAD 仍为上述基准；这是未提交工作区结果审查。排除既有 `Tool/WireSmoke/__pycache__/`。

依据：仓库 AGENTS.md、TaskContract revision 1、spec.md，及 reviewing-work 的 Code / Deliverable 判据。控制者补充确认的既定用户要求“根据配置直接选择普通还是转职”属于本次契约。由独立审查上下文直接检查实际代码、调用方、原始客户端 dump 及 TRX，未实施产品代码修改或部署。

## 裁定

当前结果无未解决的阻塞缺陷，批准通过完整结果审查门。本结论不代表部署完成或 A21 实机显示验收通过。

## 已修复的审查发现

**P2：关闭原生职业装扮时无法选择 Default。** 初始 `CharacterAppearanceServices.cs:19` 仅在 Awakening / Career 间选择，已转职角色即使关闭该选项仍返回职业候选，违反最终语义结果要求。实现方随后将关闭分支改为 `avatars.Default`。审查者重新读取实际代码，确认开启时使用有效 Awakening、关闭时使用 Default，真实 Identity 不被改写。新增四阶段关闭回归覆盖 packed 0 / 1 / 17 / 33，并保留八种显示配置组合、未知位及称号部件断言。该发现已解决。

## 独立检查结论

- `ICharacter` 从当前 Identity 一次建立候选。Default 去除成长与觉醒，Career 保留当前成长而去除觉醒，Awakening 对未转职状态回退 Default、未觉醒状态自然回退 Career；未缓存身份或显示设置。
- 最终外貌入口为 `CharacterAppearanceRuleService.Resolve`。部件过滤、顺序、透明装扮资源、称号外观及显示选项在该入口组合；规则无存储和通知依赖。原 DrawnAppearance 门面只委托规则，部件函数没有第二份生产决策实现。
- 角色页列表通过 AppearanceDomainService 读取账号 blob；两个 Roster 共用 `EncodeRosterState`，尾部长度保持 36，offset24 从 Display.VisibilityBits 映射。Job / GrowType 原值保留。未引入伪造装备 ID。
- 首次登录、运行期当前视图、支援角色均在已读取当前装备和设置后调用 CompleteAppearanceProjection。登录 0165、称号帧及后发 Roster 消费同一语义值，后发 Roster 不再固定清零显示位。
- 换装的 MoveItemSpace 刷新、TitleBook 通知与同城发布均重新读取当前视图。职业状态刷新会在刷新身份后重新完成 Display；未发现携带旧 Display 覆盖当前外貌的实际生产路径。
- ReplayStateDomainService 对本次身份、装备与设置重新投影，ReplayCharacterState 对 Display 部件及 ExpansionData 做复制，避免重放共享可变数组。PvP 信息复用世界外貌编码，绝望塔独立快照使用同一规则并显式给兼容选项。
- 保存设置仍通过 EventService / 通知投影发送 0165。blob 是选项真源；兼容位只参与读取和临时输出，无数据库结构变更或可见性双写。
- 新依赖未形成直接构造环：CharacterDomainService 对仓储和 PlayingCharacter 保持既有工厂延迟解析。正式 ApplicationModule 注册会提供 AppearanceDomainService；可空兼容构造路径与旧 DTO 回退仍委托同一规则或既有字段，不是另一份外貌算法。

## 客户端静态依据与验证边界

审查者使用 Capstone 只读检查 `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，独立确认 VA013E93B8 调 VA013E8600、VA013E891E 调 VA013E1A30，subtype0 的 VA013E7BDE 也调同一函数；该函数读取 bit1，并对 bit3 / bit4 取反后更新显示。文档关于选角 bit0 / bit1 上游注释不可照搬的判断有实际静态依据。

客户端 renderer 对各职业资源的最终效果、首次进入时机和普通/职业外貌实际绘制仍由用户实机验收；本审查不将 NativeAvatar 的语义选择等同于已证实的 renderer 效果。Document 与 protocol 保留 CurrentImplementation / ClientStatic 和待实机验收边界，未升级为 ClientVerified。

审查者未重跑测试，仅直接读取实现方保存的结果：`application-final.trx` 3832 / 3832（本次候选选择修复之前的完整应用回归），修复后的 `review-remediation.trx` 30 / 30，以及修复后的 `protocol-final.trx` 1519 / 1519；`git diff --check` 无问题。先前失败记录保留，不能当作通过证据；最终完成核验仍归控制者。
