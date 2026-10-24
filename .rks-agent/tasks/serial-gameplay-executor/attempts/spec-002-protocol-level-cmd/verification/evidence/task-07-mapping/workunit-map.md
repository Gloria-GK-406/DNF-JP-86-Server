# TASK-07 四名册协议的真实边界

原源码快照取 d2e07630；original/baseline 不改。四旧命令只有 IAccountCommand，没有 selected-session/effect marker；不新增已删除账号门，也不将初始化资产误作效果请求。全部当前读取/写入均在同一个协议CMD作业内，CMD不调用另一个CMD。

|协议|旧边界|新边界|提交后登记|
|---|---|---|---|
|Roster|Direct GetAccountCharacterRosterCommand→GetAccountRosterWithAppearanceAsync|GameAccountRosterCommand直接同领域读取，无Write/ReadOnly单元|NOTI roster(query=true)→CMD hidden→NOTI honor，原序|
|CheckName|Direct CheckCharacterNameCommand→CheckNameAsync|GameCheckCharacterNameCommand直接同领域；null不入领域，无事务|唯一CMD，原日志/raw bytes/status|
|Create|Transactional CreateCharacterCommand→CreateCharacterAsync|GameCreateCharacterCommand内唯一ordinary create-character Write；null不开单元|提交→原日志→ACK登记完成→仅Created直接同领域带外观读取→NOTI roster(false)|
|Delete|Transactional DeleteCharacterCommand→DeleteCharacterAsync|GameDeleteCharacterCommand内唯一ordinary delete-character Write；null不开单元|提交→原日志→唯一ACK，回显请求ushort index，无刷新|

## 真实参与者与失败

`Participants=[]`仅为selected-session recipients空。创建实际经过CharacterDomainService133–175、Application/Characters/CharacterRosterService248–302及初始化能力；人物/技能/初装/复活币/模板资产使用同规范化数据库连接串，在SqliteDatabase83–104和SqliteItemDomainStore复用一个CommitCanFail=true的SqliteDatabaseParticipant。真实commit/rollback在该participant178–182；没有虚构每领域一个可失败者。

物品创建还可加入ItemLifecycleScope、InventoryContentsEventDomainService.Batch、EventService.EventBatch，均CommitCanFail=false；库存/等级事实及delivery写回同一SQLite参与者，commit后唤醒。模板、初装与注册决定动态加入条件，未宣称一个fixture的观察集合恒定穷尽所有配置。SetLevel/GrantAllPersistent不是AddExp(source)的ApplyEffects，不加效果marker/主结算。

删除有效目标只MarkDeleted→同人物SQLite表；不套用创建的item/event批次。按原始确认名字字节定位本账号目标，index仅拒绝分类和回显。早拒绝分支可不触及实际数据库。正常拒绝原未RequireRollback，保原事务正常结束，不私添回滚。

原Host初始化两PvfFact保Status/异常/等级20/货仓24/物品20/账号资产不重复及回滚SQL，驱动改真实认证dispatch→新协议CMD。原失败另比较items身份和创建主题event/delivery身份集合，避免混入先前账号事实/后台状态变化。新增Host真实create提交后仅appearance读取故障，独立非池SQLite连接仍看见身份/模板资产；App同provider真实bus/WorkUnits/GameplayOutboxes/typed适配守commit→ACK→read→NOTI及flush，失败已ACK后关闭原来源。既有骨干按PostCommitPublicationFailure typed exception保原Inner cause，不称ConfirmedRollback，不重开创建写段。

## caller 与旧 API

旧Roster生产caller是Roster handler和Create handler，现分别直接已有ICharacterDomainService.GetAccountRosterWithAppearanceAsync；旧Check/Create/Delete各一个handler caller已吸入对应新CMD。Host GmInitializationHttpTests105/112/141通过新协议输入保旧业务断言，不增加生产setup内部CMD。Protocol原Create/Delete的Transactional类型断言改为新4个Protocol绑定+实际显式写段证据，剩余名单原样。旧4个API全Server符号引用为零；冻结证据的原文不视为生产残留。

## 范围与行为证据

未认证保持原异常文本、无领域/消息登记（来源失败关闭按既有骨干）；原metadata/physical dispatch、纯payload codec和消息/编码器字节均保持。ACK提前读取坏变异：真实frame登记点trace在pos2把ACK前的roster读取拒绝，2pass/1fail；恢复3/3，源SHA相同。初始fixture编译/包装类Equals/新typed cause oracle前置错误保留，不当实现RED。未改auth/registry/规则/数据库/执行骨干/OtherDetail/效果/Compose。
