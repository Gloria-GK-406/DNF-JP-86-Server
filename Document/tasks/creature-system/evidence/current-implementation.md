# 当前实现与已复现缺陷
基线 b8d2a4dc；2026-09-29。CurrentImplementation / 日志复现，不作ClientStatic证据。
- ICharacterBusinessInventoryDomainService只暴露容器；宠物详情在items八个creature列，ICharacterCreatureTable按items UID给NOTI0069。
- SqliteItemDomainStore创建宠物时已将core.Value回填为新item_uid；所有生产创建需沿用该路径。
- 上轮副本导入样本顺序改变UID：948/949/950携带core.Value938/939/940；对应等级1、饱食度100。不是DB NULL，而是客户端关联错配。精确修复只改该三件的core.Value，并核对模板63000/63003/63010及拥有角色2。
- 21:18:21选角报Required inventory synchronization value SupportEquipment.Noti2CreatureMarker[slot25] unavailable。SelectCharacterPreparationService仅对supportInventory执行CompleteSupportCreatureMarkerAsync；当前characterInventory未执行。
- 登录后复活在SelectCharacterPetEntryService，用ICharacterDomainStore.Creatures仅写Field04=1；应迁入宠物本体服务，避免状态由角色和宠物两个入口各自决策。
- 商城/开箱/AddAsync都能生成宠物明细；移动沿world item身份，更新/进化须保证不重建丢明细。
- 70核心已定位：CCreature/CCreatureMgr/CStomach/TimerCreature；规则与A21明细见相邻研究文件，研究可继续细化。
