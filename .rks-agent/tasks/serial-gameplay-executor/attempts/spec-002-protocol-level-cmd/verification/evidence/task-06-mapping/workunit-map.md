# TASK-06 原新 LOGIN 边界

原三文件快照取 HEAD 276a4338；original/baseline 不改。生产入口旧 Redeem 的唯一调用者为 GameLoginHandler；原 Activate 的另一调用者 SelectCharacterHandler 保留。新 LOGIN 不 dispatch 任何旧命令。

|阶段|原实现|本单元|
|---|---|---|
|入口|旧 handler 解析 connection 后 Send Redeem|薄 handler 以原 request/context 一次 Send GameLoginCommand；ProtocolNonEffect、精确 Game metadata|
|兑换|Redeem Transactional 总线边界，AuthenticateSessionAsync|game-login-redemption 普通 Write 只调同一个 domain；selected-session 声明为空，实际正常兑换事务只有 LoginSessionRegistry、OnlineStateRegistry 两个内存参与者，二者 CommitCanFail=false；SQLite fixture 不等于兑换段 SQLite enlist|
|提交|Prepare 两个运行态参与者→运行态 Commit→afterCommit 顶替身份移除/关闭|同 domain 算法和参与者不改；WorkUnits 等提交结束才返回|
|后段|重查 OnlineUserId/Account/Authenticated→LoadSettings→listener/env/endpoints→CMD+六 NOTI|同序；没有第二认证 Write，后段不加入兑换事务|
|profile|第二 hotkeys NOTI 后旧 Activate 细命令|同一点直接 domain.ActivateHotkeyProfile(origin,0)；Select 旧 caller/登记保留|
|拒绝|原 Warning 类别，移除 online identity，close None|同类别及顺序，ICommandConnectionClosures 登记完整原 NetworkConnectionContext；WorkUnits fault 粘性仍由既有 executor 报原 cause|

提交后设置/配置/编码故障不撤销已提交认证。旧 Registry 有意允许同票据 reconnect/supersede；原 TCP LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect 的成功重连断言未修改。Task r3 更正了局部不可复用误记，不是产品行为变更。

原细段交易两例仍 Connected 匿名、原 cause、Available 和第二连接可用：测试专用 fixed-payload Internal probe 用实际 WorkUnits/auth；不把它改整LOGIN。两塔及 GM 的旧 Redeem 仅 setup，改 TestGameplay +真实 DomainTransactionScope.Begin/Commit，不增加 LOGIN 帧。Net 仅为此增加测试 IVT；不公开事务类。

频道 service 直接吸收原 Resolve/Unknown/KindOf/Refuse；IChannelEntryPolicy 同 singleton alias 暂供其它协议。typed Infra 持同 IServerConfigService snapshot，ctor 无 Get；Environment 只有 null 回退时读取，后续顺序 ConnectionPort→Host→PortA→PortB，保持原转换且不引 strict endpoint 规则。

原观察性 unit 用例保原7消息/同连接/类别/secret/关闭断言，驱动直接调新CMD（非真实票据证据）。整flow使用真实注册/launcher SQLite fixture 和真实内存兑换路径，另对发布身份和提交后事务空作外部观测；测试用一过性 settings/config fault，真实 Disconnected 后同票据成功重新登入。真实 Net 预提交故障与 TCP 编码失败/关闭/重连保原断言。source保护／effects／主结算／T011 清零均未在此扩展。

## r4 取证纠正

Task 初稿将 SQLite fixture/声明 Transactional 推成正常兑换 SQLite 参与，现按 owning source 明确纠正：AuthenticationDomainService.BindSessionAsync 只校验格式并调用 LoginSessionRegistry.TryBind；GameLoginAuthenticationDomainService 再调用 OnlineStateRegistry.PrepareAuthentication。正常兑换只有这两个运行态参与者，均 CommitCanFail=false；不存在 SQLite 连接/enlist。真实 SQLite 用于注册、launcher issuance、后续 settings 查询，不能冒称兑换的 durable commit。

旧 DurableCommitFailureLeavesTheTicketAndIdentityReusableByASecondConnection 的可失败阶段来自测试显式 TicketParticipant，保原 cause/Available/匿名/重试断言；Net prepare-fault 是额外测试 PrepareFaultParticipant。两者保真实阶段与夹具，但都不是生产 SQLite 兑换故障。生产 auth、registries、旧 fault/重连算法均未更改。

首轮完整 Net 唯一失败是 LifecycleExecutorWireTests 的祖先 label 仍期待旧 inbound:GameLogin，而真实 Protocol CMD 已为 GameLoginCommand。r4 仅将该一个 expected 改为完整类型 nameof，保持 FIN、executor、Source、Depth、SourceConnection、single Ancestor 及已有 RootId/ParentId 断言；不更改生产链或关闭逻辑。
