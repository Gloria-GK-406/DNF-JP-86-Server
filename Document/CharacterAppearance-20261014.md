# 角色外貌统一计算

日期：2026-10-14；基准：1e146ba1；分支：task/next-round-20261013。

## 初始化覆盖的原因

A21 ClientStatic / High，输入为DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin（支持EXE SHA-256见protocol/NOTI0002）。USERINFO分派VA013E92AB判断subtype2，VA013E93B8逐角色调用VA013E8600；该函数在VA013E891E调用VA013E1A30读取显示字节。subtype0也在VA013E7BDE调用同一函数。

VA013E1A30读取u8后：bit1经VA013E1A76更新职业特效；bit3/bit4取反，经VA013E1AF3更新全身时装及觉醒装扮。它与NOTI0165的处理目标相同。因此上游AccountSettings.cs与UserInfoType2RosterTailBuilder.cs中“选角bit0/bit1是特效/觉醒”的注释不能作为A21编码依据：bit0实际另有状态用途，不能借用。

两个旧Roster编码器均将最后36字节中的offset24隐含在new byte[6]里写0。服务日志2026-10-14 10:12:07.782显示0165之后又发送AccountRosterStateMessage；后者调用同一显隐更新函数，把bit4清零，重新打开觉醒装扮。此前所怀疑的“对象尚未建立”是客户端存在的分支，但不需要用定时补发来修复这条已证实的数据覆盖路径。

## 职责

- ICharacter.GetAvatarCandidatesAsync一次读取当前身份；GetDefaultAvatarAsync/GetCareerAvatarAsync/GetAwakeningAvatarAsync提供便利查询。未转职返回基础职业，未觉醒返回当前职业外貌；不存在角色返回null。候选不读用户显示配置。
- 原生装扮没有伪造的装备模板ID，用CharacterCareer表示候选；协议真实Job/GrowType不被显示设置改写。
- 关闭原生职业/觉醒装扮时，最终语义候选选择Default；开启时选择当前有效Awakening（未觉醒自动回退Career）。ShowGrowEffect独立控制特效，ShowFullAvatar独立控制穿戴时装，不能拿二者改写真实职业。
- ICharacterAppearanceRuleService.Resolve仅吃候选、穿戴和只读配置，返回CharacterAppearance。部件过滤/排序、透明装扮显示、称号外观及显示选项在这一计算入口汇合。
- ICharacterAppearanceDomainService负责读取人物候选、旧兼容位和账号blob，再调用规则；持有事实的登录/当前视图组装直接调用同一纯规则，避免重复读取。
- blob访问器仍只解释槽1/55/74，VisibilityBits只是临时输出，不写character_settings。读取设置与最终外貌计算职责不同。
- 两种Roster现在共用AccountCharacterRosterPayloadCodec.EncodeRosterState，最后36字节offset24发送统一结果的VisibilityBits。世界subtype0 tail+47和0165使用同一值。

## 调用归一矩阵

| 消费路径 | 统一入口 |
|---|---|
| 角色页列表、登录后账号列表 | CharacterRosterService → AppearanceDomainService → AppearanceRuleService；同一Roster编码 |
| 首次登录、在线当前视图、同城发布 | CompleteAppearanceProjection → AppearanceRuleService，Public.Display随本次视图返回 |
| 换装、等级变更、称号装备/外观 | 读取当前视图后使用Public.Display；TitleBook不再独立算CloneTitle |
| 转职/一觉/二觉状态刷新 | CharacterStateProjectionDomainService刷新身份后重新完成统一外貌投影 |
| 保存显示设置 | EventService → 设置订阅者 → AppearanceDomainService → 0165 |
| 事务重试后的状态重放 | ReplayStateDomainService完成统一外貌投影并复制部件数据 |
| 支援角色 | CompleteSupportCharacterProjectionAsync调用同一完整投影 |
| PvP角色信息 | EncodeCurrentUserState消费同一结果 |
| 绝望塔人物快照 | AppearanceRuleService；无账号显示配置的独立快照显式使用兼容选项 |

DrawnAppearance兼容门面仅委托完整规则，不保留第二份计算；EquipmentAppearanceParts是规则实现内部的部件函数。Inventory/AvatarAppearanceRuleService负责穿戴时持久化透明装扮继承状态，不是第二个最终外貌计算器；物品列表的序列化同样不属于人物外貌决策。

## 验收边界

新回归先复现Roster尾部期望25却为0，修改后覆盖未转职、转职、一觉、二觉。检查配置组合、未知位保留、候选随持久身份变化、角色页真实存储读取与两次Roster编码一致；现有透明装扮、称号、职业进阶与完整登录回归共同保护。

这些属于CurrentImplementation与ClientStatic证据，最终角色页、首次登录、返回角色页、在线保存及换装的可见效果由用户验收。不修改客户端，不改变数据库结构。
