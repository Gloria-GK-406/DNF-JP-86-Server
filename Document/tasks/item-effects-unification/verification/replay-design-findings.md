# 统一重放后继的只读准备

2026-10-01；effects_session_fence_scope与effects_replay_projection_scope只读当前源码，根综合；不是已完成保护或正式结果审查。TASK-008正在实施，后继详细Task依真实产物渐进编写。

## 输入覆盖

23 selected命令中3 XP继续按actor／recipient／clear事件claim；其余20个输入主结算命令对应18 Request CLR／20 wire CMD。

| Request | Wire | Main command |
|---|---|---|
| UseStackable | 002C | UseStackable |
| RenameCreature | 0064 | RenameCreature |
| QuestClearCoupon | 0204 | Coupon／Fatigue（Action分支） |
| LearnSkill | 001D | LearnSkill |
| FinishQuest | 0022 | FinishQuest |
| ReinforceItem | 0050 | ReinforceItem（强化／增幅） |
| EnchantByBead | 0110 | EnchantByBead |
| MagicSeal | 0191／01B6 | MagicSeal |
| Outworld | 00CC／00CD | Outworld |
| Forging | 01B7 | Forging |
| DisjointItem | 001A | NPC／PortableDisjoint |
| CompoundItem | 0019 | CompoundItem |
| PackageOpening | 00A0 | PackageOpening（含现有Avatar分支） |
| AvatarPackageOpening | 0207 | AvatarPackageOpening |
| MagicBoxSingleOpening | 00D0 | MagicBoxSingleOpening |
| MagicBoxBatchOpening | 0450 | MagicBoxBatchOpening |
| LotteryOpening | 001B | LotteryOpening |
| VendingMachine | 00D7 | VendingMachine |

全部已有metadata；Rename.Metadata为init属性，HEAD已有且decoder明确赋值，不需要新增，不可仅扫ctor误认缺失。NetworkPlugins传输边界仍须增加完整body immutable身份（SHA256 hex＋长度），包括decoder忽略尾部；不能用parsed fields／checksum替代。

附加证据边界：protocol/patterns/game-frame.md原339保留seq具体重置／回绕／响应关联尚未完成逆向；a21-return-selection-live-20260908与原始Host日志证明两轮Return→Select重新发布，但没有同物理连接两轮相邻C2S seq。probe两个0007 seq来自不同connection，旧packet_log未有0007，现无完整capture闭环。故全连接expected-next是CurrentImplementation，不能标ClientVerified；human必测同连接“用道具→返回选人→再次进入→连续两次使用”，保存0007／0004／首批C2S type/seq/connection，核对新会话没有被旧窗口误拒绝。不能据不存在的证据自行发明重置规则。

## 推荐的共享接缝（后继详细计划仍须核对）

- 请求marker在Abstractions，18 Protocol请求实现，Application不能refProtocol。
- dispatcher只串行同物理Connection对象的operation ledger（CWT），同步安装固定原connection／准入前exact Session／metadata的AsyncLocal frame；gate等待前后Session不一致拒绝，不重新Bind角色。
- 首次dispatcher不能跨handler拿lifecycle Shared，否则Fatigue/Lottery/Vending会Shared→Exclusive自锁；业务沿45与原outerExclusive。重放不进原handler，由current refresh持Exclusive覆盖snapshot和send，其read45再借用。原只读准备建议Shared已被009实际caller检查修正：别的party reporter能给该recipient以Shared结算并发XP／quest通知，read57释放后不能让新通知先到、旧snapshot再覆盖；010不预持Shared再调用Exclusive refresh。新增真实模式Exclusive借用，live Shared不升级，pending/canceled/closed不作为持有。
- 主结算marker区别20主命令与3XP/后继readonly命令。58.next包60，正常返回当下在57内同步记Committed，早于任何通知／发送。Lottery后来ReadPremiumSnapshot不能覆盖主提交信息；同frame第二个主结算明确拒绝。
- shared Apply收集typed changes/effects的不可变footprint（实际touched slots、projection needs、pet UID）。只观察已决定执行，不追加业务effects。不缓存旧receipt／可变name／委托。提交前冻结，unknown change在提交前拒绝，提交标记自身异常按postcommit uncertainty处理。
- Pending／Committed／ConfirmedRollback／Rejected／Blocked分开。新鲜下一序号同参数正常执行；非fresh只允许同exact Session、selector/type/seq/fullrawidentity、128所有入站帧窗口中的committed refresh或confirmedrollback重试。冲突／过旧无缓存拒绝，Fresh/新Session不清物理fence。完整ushort轮回无nonce的古老raw碰撞保持Unknown，不承诺绝对排除。
- old Reinforce/Enchant/MagicSeal/Compound/Outworld/Forging六guard及注册/handler分支关闭；Lottery Completed／CompletedChange、Vending Completed／Remember分钟业务cache关闭，不留假去重sequence参数。原direct-handler replay测试搬到真实dispatcher保留所有语义保护；Empty metadata仍可trusted直调，真实dispatcher缺有效ordinal/hash不得绕过。

Footprint覆盖：Consume源槽；EquipmentChange Before.Slot（含destroy）；Stackable／Prepared／BasicGrant所有实际槽；Virtual／account／gold；Fatigue／Usage；Quest rows/offers；LearnedSkill／Builds／Grow／XP；pet stomach/name。Cooldown／Spirit／luck无现成可安全重放消息应具名记录known no-message，不能default静默遗漏。请求最低projection需要补无退款Learn／合法空plan／正常拒绝没有Apply的情况。

## 纯读与发送准备

- InventoryEntry.ReadCurrent绕过LegacyWornNormalizer；entry Load保留。Opening首发receipt、Recipe.Refresh、CurrentView selectionEntry=false及support inventory换pure；其他范围外查询具名记录。
- ReadReplayStateCommand为Transactional＋selected marker，一次保护内直接领域读取，无repository进Protocol、无nested bus、无Seeking.Synchronize写。复制pet name、avatar detail、skill extras与其他可变存储数组，结果集合immutable。前后exact检查。
- Entry／Account／SkillState.Read/Builds／SkillPoint.Ledger／CreatureStore.ReadAll／Fatigue Balance／Usage snapshot／Premium读取均当前纯读。各day reader原各自读clock；同DB transaction不等于同逻辑日。要统一日界须具名at接缝＋跨日验证，否则明确不宣称。
- Current库存用000D绝对全表＋footprint当前为空物理槽000E(-1)，金币ID0，Main固定14virtual包含零，不假定full表省略即自动清空；零晶体event过滤保持，运行中000D零余额实际效果留human。
- Pet0069 u8、Skill0013固定两页/u8不可chunk；Usage021E已有empty reset＋255chunk；Premium完整绝对是CMD036F；XP当前level/total/SP/TP，BonusLines.None；quest/offer/name当前，禁止旧奖励popup或扣数量ACK。
- sender新增具名SendBatchAsync typed message batch：一次捕获transport entry，验证全部registered encoders/endpoint，全组实际frame物化（含1MiBcap）后才发送。原SendExplicit只逐msg编码，不可假称已有batch预检；保留best-effort语义。后置非法pet/skills/avatar/unknownencoder/oversize不能先发usage reset。实际网络途中断线仍可能部分投影，但已提交ledger先记，不再执行业务。
- 新batch接收原NetworkConnectionContext（或等价不可绕过的expected context），不能仅以可能复用的ConnectionId捕获“当前”entry。registry查到后必须ReferenceEquals(expected)，然后整个组只发送到该次捕获的transport Session；编码期间replacement亦不得改投新连接。沿现exact-close的ABA边界补发送测试。
