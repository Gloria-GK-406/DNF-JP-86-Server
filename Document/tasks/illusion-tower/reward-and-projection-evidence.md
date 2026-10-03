# 迷妄之塔：实现阶段奖励与结算投影补证

2026-10-03。输入身份沿用 [研究报告](../../IllusionTowerResearch-20261003.md)；只读补证，没有新实帧。下述70地址是70Reference，不能提升为A21实机结论。

## 经验

70 `_beginTowerClearProcess` 在 `08467A14/08467A2E` 读取每成员的当前角色等级并调用 `get_mob_reward`，`08467ACE` 调用 `generateRewardExp`。令奖励层 F 为成功时当前层、失败时 `max(1,当前层-1)`：

```text
M = mob_reward(成员当前角色等级)
基础结算经验 = trunc(F * M * illusionRewardExpWeight[F])
```

`generateRewardExp 08462538` 在 `084625EB–0846265B` 应用迷妄表，再调用 `getClearRewardBonusExp`。`08462661` 首个超限比较只记录日志，但 `08462940` 后的分支在 **08462A02** 确实把最终经验设为 **1,100,000**，随后保存。这个上限必须作用在最终结算值，不能只改角色进度而让奖励分解与总值矛盾。

70的 `mob_reward` 来自数据库 `monster_reward_ref(level,exp)`（`set_mob_reward 083605A8`，SQL VA `08C32E24`）。本仓可用 `MonsterExperienceWorth.For(level, ExperienceTuning.Curve)` 作为当前实现曲线，但它不是从70数据库读取的证据。

70 `handleKillMonster 0846483E`：`08464A94` 先登记死亡，`08464DAA` 起按成员计算；`08464E0B/28` 取各自角色等级的 M；`08464E69` 以rank=0、difficulty=0调用 `getMonsterTotalExp`；`08464EE4` 仅actorType=5应用APC倍率，`08464F7F` 再应用monster倍率，`08465125` 即时发经验，`08465285` 推任务。

```text
E0 = trunc(M / 2 * NormalRankRate * NormalDifficultyExpRate)
E1 = actorType == 5 ? trunc(E0 * ApcExpWeight) : E0
KillExp = trunc(E1 * MonsterExpWeight)
```

除数2由 `085A23DC` 使用的常量 `08CBBBD4=2.0f` 确认。当前PVF APC倍率3、monster倍率1。没有普通队伍人数除法、队伍倍率或怪物与角色等级差惩罚；不能复用整个 `WorthOfKill`。角色加成仍须显式界定，不自动继承普通副本所有buff。

具体加成补证：`sumGainedEachExp 085A24C6` 的 `isWorldMapTowerArea=true` 跳至 `085A265A`，跳过FatigueGrownUpBuff、growth-power、Weekend以及premium type79的 `isAffectedPremium/getAddExpRateFromPremium 085A2623/263A`。70的type79与当前A21成长契约84的对应已有 `growth-contract-drop` 研究。因此塔kill不能复用普通成长契约；塔只填STGainedEachExpData.base，其余清零，也没有现有Channel-kill来源。clear自建参数不走set_clear_reward_exp_parameter，成长契约+0x44与SpiritPotion+0x2C保持0。塔clear另填avatar/creature/event/channel/PremiumAdvantage/server/manage，GetAdvantageExpRate的独立黑钻映射仍Unknown，不能径直当BlackDiamond。当前最小实现采用有依据基础值，不继承普通四类加成；未恢复的运营等加成保留边界。

## 奖励候选与金币

70 `08467916–084679A2`：候选数 `min(10,DgnMaxClearItemCount,IllusionCardCount[F]+RecordBonus)`。当前DGN最大4，15层表为 `1,1,1,1,1,2,2,2,2,2,3,3,3,4,4`。`08467546–084675BB` 的纪录加成仅成功时可能个人新纪录+1、榜首更新+2。本任务不实现排行榜，采用RecordBonus=0；不能称为完整官方纪录策略。

`generateRewardItem 08461FC0`、调用参数 `084679BE`：

```text
P = min(10,F)
roll = get_rand_int(1000)
roll < 50*P => 金币
roll < 99*P => 物品生成分支
其余 => 空
```

PVF三个参数为50、49、100。`importDeathTowerScript 089089F3` 的 `08908C3F/70/75/CA6/CAB` 将其累计为50、99、199；上述生成器只读前两个阈值。**第三项100不是随机分母**。物品生成分支仍可能返回空。`get_rand_int 086B1B87` 普通分支 `086B1BC8` 为rand()%N，但低值补救分支 `086B1C61/77` 允许返回value==N，因此可能范围0..1000，不能严格写成均匀0..999。采用本地确定性PRNG时须保留与原生成器不等价的边界。

塔取生成器index2，`CItemGeneratorMgr::initialize 08533BC8` 的 `08533C7A/CA0` 证明其是 `CClearReward`。金币虚调用为 `generateSpecificItem 08538C84`；塔传成员等级、difficulty0、party1、rankIndex1、四种演员计数均1（`0846205D–08462099`）。排除未展开的运营调整，T为正值截断：

```text
G,V = GoldGenTable[成员等级]
W = PartyGoldRate[1] * DifficultyGoldRate[0] * (1 + RankExpBonusWeight[1])
A = T(G * 175 / 1000)
B = T(7 * A / 2)
C = T(B * W)
D = T(C / 2 * W)
NativeGold = T(D * (100 + GetRandomRange(-V,V)) / 100)
TowerGold = T(NativeGold * RewardGoldWeight)
```

锚点 `08538D0E–08538D65`、`08538D68–08538DC6`、`08538DDE`、`08462210–0846226C`；当前塔金币倍率11。随机端点与原PRNG等价性尚未证明。本仓可组合GoldDropTable/ClearRewardRate内容，不能直接调用普通 `CardGoldOf` 或四牌 `FaceFor`。

70 `CClearReward::generateRandomItem 0853873A` 先取drop-kind，`085395FC–08539621` 将表第一项存入+0xA8；当前A21 `[drop kind prob]=10000,0`，只走随机分支。`085387C0` 随机分支使用角色等级的default profile、difficulty0、map/rateWeight1、refRate100、reduce0，再按itemType1–4抽类型，独立抽稀有度后以角色等级选候选。当前 `[drop item type prob]=0,9500,0,500`，装备与时装是不同分支；没有可生成时装池时该分支返回空，不能把概率转给装备。

70 `CGenerateRandomNumber::generateNumber 085334A4→CMTRand::randInt 080CBABE` 为双端0..N；清奖励稀有度在 `08538962` 固定N=1,000,000，不是当前表末尾1,000,002。取第一个threshold>=roll：Common0/500000、Uncommon1/799900、Rare2/999900、Unique3/1000000、Epic4/1000001、Chronicle5/1000002。后两档在这个70调用路径不可达。

满包边界：塔在 `08463368` 调 `insertItemIntoInventory 08502D86`，`08463374` 遇返回负数直接跳 `084633FC` 的下一候选；成功才发列表更新。插包失败尾部 `08503383` 返回-1（装备空间扫描 `085025F8`）；未发现该路径自动邮寄或重试。因此按70参考可记录该候选未交付并继续结算，不能自行增加邮件补偿，也不能把候选存在写成资产已交付。

## 最小结算与继续投影

A21 `0090` body最小完整长度305B：

| offset | 类型 | 内容 |
|---:|---|---|
| 0 | u8 | 未完整命名flag0，0 |
| 1 | u32 | 展示时间 |
| 5 | u32 | 展示层数 |
| 9 | u8 | 未完整命名flag3，0 |
| 10 | u32 | dungeonId=11001 |
| 14 | u8 | hasPersonalBest=0 |
| 15 | 5×58B | 五组空排行 |

每组为 `8 × (u32 nameByteCount=0,u8=0,u8=0) + u16=0 + u32=0 + u32=0`。头读取 `01153AD5–01153B40`；`01153B96` 仅控制额外个人最佳组；`01153D30–01153EE9` 固定读取五组八行；`0274FC10/0274FC2F` 确认空字符串仍有u32长度。**ClientStatic/Confirmed：形状与宽度**；时间/层数语义为High。`01153F17→02619C00(1)` 打开结算组件，所以不实现排行榜也不能省略0090。

`0091` 四组按队伍槽0–3，`01148898` 查成员，`01148908` 将对应槽位传入奖励组件；不是四类奖励。70 `makeDisplayReward 084602C4` 同样按四槽。70 `onTimer` 的 `0846312B` 以后自动发金币/物品，`084634B7` 发XP；不能用普通0047翻牌作为塔领奖入口。

0091每行取Inven_Item的itemId(+2)与get_add_info(+7)，见 `08460316/0846033B/0846034A`。金币生成 `08538E10/14/28` 写type2、itemId0、add_info金币数，因此为 `(0,amount)`。空候选 `08461CDB/CE9` 清字段并设itemId=-1，入口 `08461FDE` 已写入候选总数，空分支不减count，因此保留 `(0xFFFFFFFF,0)`。装备第二word是实例add_info，绝非发放Count=1；当前物品策略默认附加值0时可以显示0，但这不是70装备全部为0的证据。

`0092` 的字节0把客户端奖励组件状态设为5，非0设为9（`01146E0F–01146E22`，setter `010C9200`，字段+0x24F4）。`010C8EE0` 的0048发送器在状态不为9时只禁止option0，其他option仍可发；`010C8FC0` 的[1,0]发送也要求9。70 `08463019–084630ED` 检查所有有效成员有4183后才允许继续。应命名ContinueEligible，不能命名ReturnEnabled。option0具体按钮文案待实机。

建议投影顺序0090→0091→0092；70安排奖励/EPLP定时，上游采用1秒、5秒，均不能称为A21必需延迟。首层008E→008F→001E立即发只有对照实现证据；70 `08463DDA–08463E47` 在8E/8F后检查ready，另有handleFinishLoading。本实现保留装载门，实机验证首层是否另需立即放行。

70 CMD0048 dispatcher `081FEEDC/081FF0BC/081FF147` 检查领导者和state1；option0/1延后3秒、option2延后1秒、option3仅 `SetSelectedEPLPCmd(3)` 后返回；`081FF2F5` 调 `handleSelectEPLPCommand 08466208`。

最终返回链已闭合：option0/1/2均设EPLP state3并安排timer4E；到期 `_onFinishDeathTower 08467F6E` 遍历成员onLeaveUser，然后 `08467FF8` 无条件调用 `ReturnToVillage`，必要时销毁塔，没有按选项自动重进或选图。默认timer4D传60秒（`0846354D/084672DC`，`InsertTimer 08630E22` 有SECOND_TO_MILISECOND）；选项3保持state2，使4D到期看到selected3后直接返回且不再排期。故0/1等待3秒、2等待1秒、3保持界面、无操作60秒回城，均为70Reference时序。再次挑战通过回城后新入场闭合，重新检查并消费材料；A21按钮文案/表现仍待实机验证。

## 当前迷妄任务与选图接缝

NOTI0005来源为 `character_dungeon_permissions → CharacterDungeonDifficultyDomainService.GetRecordedAsync → CharacterEntryAssembly`，不读取普通maze定义；未记录返回Normal难度0。不能为11001显示而伪造权限行。已确认的旧进入阻断是普通布局解析拒绝无maze，塔分支在其之前接入。

当前quest.lst登记的迷妄链62/63/64/65/92中，64/65的hunt enemy为 `11001 -1 608 2 1`；APC任务类型2与wire actorType5不同。未登记的训练clear文件不构成当前需求，无需新逐层任务事实；只在最终完成时报告整塔QuestDungeonCleared。

登记任务950要求 `10089420 ×10`，enemy reward item覆盖迷妄APC15402/20604/5602/10600/608。它是seeking库存目标，需要已有QuestEnemyDropDomainService/RuleService生成DirectQuestItem后发放，再报告QuestHoldingsChanged；不能受塔普通物品掉落0一并禁用。当前 `QuestEnemyDropRequest.Cell` 仅作确定性随机盐，不要求复用普通DungeonCell运行态。

## 塔内道具与公共能力接入

当前迷妄 `[tower limit of stackable item]=0`（`Document/outputs/illusion-tower-20261003/pvf.md:57`），内容源原样读取为StackableItemLimit。70 `CDeathTower::handleUseStackable 084662CC` 在08466328读限制，0跳至0846637F返回true；UseStack dispatcher 081F70BE false直接退出、true继续普通使用。故此值表示不启用塔专用白名单，并非禁全部道具（70Reference/High）。非零只允许6500–6999及10/24/51/2660234，当前迷妄不进入该分支。PlayingCharacter应识别塔存在并使用Dungeon场所，保留原物品等级/职业/场所/CD判断；不绕过既有规则。

静态核对的必要公共接缝：CargoTransfer、InventoryExpansion.CurrentAsync（含仓库金币）、RecoverStamina、SortItemCommand的仓库分支当前只拒绝普通run，须识别塔。TeleportConsumable和ItemSale已有精确城镇门，入塔撤销城镇存在后自然拒绝；不能把塔结算映射为普通通关商店。CreatureRuntimeDelivery和FinishQuestHandler职业变化广播要补塔内精确session受众。

PartyService/PartyCommandSlice没有副本内退队、踢人或换队长禁令；当前退队/踢人只End普通run，塔不主动查party会漏清成员/结算资格。成功队伍变更应同步塔离开与回城；显式换队长应同步塔leader或在修改party前明确拒绝。普通party自动继任按slot最小，塔原始按输入数组顺序，两者必须对齐，不能依赖调用顺序偶合。以上为CurrentImplementation的静态缺口，后续接入测试负责证明修复。
