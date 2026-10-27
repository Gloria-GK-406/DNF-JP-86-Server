# Task26 原新提交点、算法与证据边界

- 原一 IAccount-only Transactional fine → 有 selected session 时一 ordinary Write([],Ordinary,None)；无session原Silent explicit拒绝没有Write。正常领域拒绝也Commit，action/native异常Abort。无Main、footprint、ledger、attempt或raw/P3许可。
- 原约500行算法整体按机械归一同字节，见 algorithm-method-map.json；只改类型/方法名并去掉未读CommandExecutionContext。高层InventoryMove组合现有ItemMovement/CargoTransfer，既供whole也供原Session=null、Tower业务槽、多步同Tx直接领域能力。CreatureRuntimeChange属于Abstractions，所以业务request/result/status随新领域接口自包含，不引Data反向依赖。
- 原Resolve后仓库ReadCurrent/Account.Load、currentViews和Housing全部仍unit外，未新添ReadOnly/Write。两catch的非caller取消filter、业务Error、同一frames部分追加保持；Housing失败仍保先加入的两appearance帧。最后一explicit required-direct批次，非NotificationBus batch。
- 新Flow在产品前原registered decoder/dispatcher/fine/sameprovider SQLite/actualoutbox encoder上执行21组，再以同源码执行新whole；14/18/24B、完整metadata/SHA、真实持久资产与UID/完整82B core/BLOBhex/NULL/动态表全列，SQL action19/1811以及同participant nativeCommit19/787、causeSame/FK1/child>0/action完成、精确sourceclose/chain/nofence。双端action失败发生在source已扣至4、destination插入前；native失败前source4/destination3均已stage，回滚与同seqretry实证。
- 真正旧strong App249和Protocol106包含全部18旧源所属测试类与新21flow/原codec；new strong App249、Protocol107（加exact registration fact）全过。旧全部方法/InlineData/MemberData保持；驱动/领域owner/必要reflection接缝变化见source-freeze。直接领域多步/两wear同Tx和Session=null不硬拼成wire；局部Protocol/App whole test adapter沿原fixture交易，不冒production bus/core证据，不动公共Support。
- Flow的低层移动/cargo forwarding observer调用真实领域与SQLite；这里只为观测加marker，不为拒绝额外SQL而人为enlist。native/split路径可直接观察complete action；wear marker仅描述调用处Tx，真正保行为另有原强PVF和UID测试。
- Flow wear规则以固定synthetic item3000规则作控制，CurrentView使用已有测试baseline并合入真实当前inventory，Housing正常返回null。它证明本族原新帧/追加顺序和窄故障分支，不是生产全部current-state/Housing或真实A21验收。原18强测试继续保护其余PVF/placement/creature/mirror/appearance事实。
- 第一次局部观察把explicit误作PreparedBatch、cargo实际ACK+Main+Cargo三消息及public identity fixture缺省产生诊断；首driver/raw保留，修正后才封before，不冒behavior RED。Warehouse旧helper TestGameplay包住新真实bus导致selfwait，按计划改真实wire；类型owner/using编译诊断亦保raw，非业务RED。
- 两个Committed explicit encoder出口（成功/正常拒绝）从oldraw变成既有默认PostCommitPublicationFailure innerSame，noWrite拒绝仍rawSame；三路实际rootchain/sourceclose/nofence/asset结果保持，外壳差异单列，不新增第四许可。
- restatement mutation只使positive identity guard对可达输入失效，物品真正从3逆向回65产生asset RED，合法fresh drag正控pass；finally原domainbytes恢复同2目标GREEN。原restatement判定是既有推导，非新增实机抓包事实。
- same-run rollback所有列精确；跨run仅items两SQL时间及account_cargo_state.updated_at实际CURRENT_TIMESTAMP按UID/account与run窗口校准，EventService三GUID每case跨phase双射且DeliveryFK/batch/origin保持；其它字段/帧/固定clock值不豁免。
- formal最终八freshfull、七actual controls、DFO74/7和旧期望，Golden15、Char10、原8991逐display/status/multiplicity N-D；suite时间不作单CMD≤2秒证明，T012/Astra/Spec最终核验后继。RecordedOutbound仅精确登记和关闭条目，非真实socket关闭。


## 原18测试源逐文件去向

所有原测试方法、InlineData/MemberData均由freeze原声明清单逐项核对；下表说明驱动/owner/必要断言变化，不声称所有assertion逐字未变。

| 原文件（项目内相对） | 保留意图与驱动/断言接缝 |
|---|---|
| Application/Game/MoveItemSpaceHandlerTests.cs | 原reverse、ACK、split/merge/swap/refusal/concurrency保持；直接业务用DirectInventoryMove+原Scope，协议用局部whole driver+原fixture事务，logger改whole类别。 |
| Application/Game/CreatureGoesBackOnTests.cs | Creature生命周期/重新装备、runtime与UID断言保；DirectInventoryMove取代fine bus，原scope仍实际参与。 |
| Application/Inventory/ItemUseFixtures.cs | Tutorial旧DirectCommandBus保；删除move ICommand分支，新增局部domain事务入口及whole单位/explicit转发adapter，不改公共Support。 |
| Application/Inventory/CharacterContainerDomainServiceTests.cs | 业务容器与物品结果保；direct domain owner/request替代fine，原事务能力不变。 |
| Application/Inventory/InventorySlotBoundaryArchitectureTests.cs | wire GameSlot仍只在Protocol；反射目标改whole.Input→wire、domain request→BusinessSlot，仍拒domain泄露GameSlot。 |
| Application/Inventory/WarehouseMoveSynchronizationTests.cs | 原真实仓库current同步保持；新真实decoder/dispatcher入口MoveWire.Send，移除外层TestGameplay自等待，源instance原样传。 |
| Application/Inventory/BusinessEquipmentSwapTests.cs | 两次wear同原显式Tx、双端与mirror/UID/rollback保；domain直接调用而非命令。 |
| Application/Inventory/AvatarAppearanceTests.cs | 原appearance字段/当前库存保；InventoryMoveTestAccess在实际fixture交易驱动domain。 |
| Application/Achievements/AchievementTitleBookMirrorTests.cs | title穿脱mirror原断言保；具名domain直接调用、调用方原Tx保持。 |
| Application/Achievements/AchievementTitleBookTests.cs | title穿脱commit/rollback与其它角色不变保；原显式Tx直接domain，status owner改Abstractions。 |
| Application/World/IllusionTowerDomainServiceTests.cs | Tower业务地址及不能逃出/不可占位保；direct domain而不硬造wire槽，原fixture transaction驱动。 |
| Protocol/Game/MoveItemSpace/TheCharacterChangesAsYouDressItTests.cs | 原真实PVF穿脱、appearance/mirror/两端、消息断言保；局部whole adapter驱动真实domain+原fixture事务，logger类别迁whole。 |
| Protocol/Game/MoveItemSpace/TheAvatarBagIsServedTests.cs | avatar容器/UID、Session=null能力保；原Fixture.RunAsync中直接domain，无假session协议入口。 |
| Protocol/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs | 原拒穿模板与日志字段/值保；局部whole adapter+原事务，logger迁whole类别，原业务日志内容保。 |
| Protocol/Game/MoveItemSpace/EitherEndMayBeHoldingItTests.cs | reverse/restatement、两端wear与asset UID保；domain/局部whole adapter沿原fixture实际事务。 |
| Protocol/Game/ItemPathFrameTests.cs | 纯frame bytes以及业务status映射原预期保；status owner改Abstractions。 |
| Protocol/Game/ItemRefusalCodeTests.cs | 原拒绝码映射字节预期保；status owner改Abstractions。 |
| Protocol/Game/CommandRegistrationTests.cs | 原其它注册断言保，新增此whole exactDirect/Protocol/NonEffect、fine缺席事实。 |
