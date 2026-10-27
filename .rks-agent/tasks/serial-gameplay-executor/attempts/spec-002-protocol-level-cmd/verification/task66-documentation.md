# TASK-66 文档整理与变长字段判据裁定

本文件记录主控拥有的文档工作；结构实现和最终全量／测量尚未据此通过。AGENTS 的入口图、注入面、执行器、
总线、handler、选角、通知接受事实及新增协议步骤已按当前 typed CMD 改写；ArchitecturePhilosophy 不再描述
细命令内联、请求 AsyncLocal 或 handler 多阶段编排。新增 `Document/ProtocolCommands.md` 提供实际注册／单元
边界及 LOGIN MId／Ticket 示例。旧 ProtocolSliceArchitectureNotes 明确历史阅读边界，不把旧 observer／选角
描述继续冒作现状，也不声称其其它发现已逐项解决。未修改 A 封存任务证据。

## F-128①

核读 GameLoginRequest、GameLoginPayloadCodec、GameLoginHandler、GameLoginCommand、
AuthenticationDomainService、LoginSessionRegistry 及 HttpApiApplicationExtensions：MId 是随机六位会话键，
Ticket 是16随机字节的十六进制文本，来自实际 HTTP 登录响应。Game body 为两个 int32-LE 长度前缀 ASCII 值，
每段解析上限256；它们不是账号名／密码。兑换的普通 Write、内存参与者、提交后当前身份复验、设置投影和拒绝关闭
按真实代码说明，不引入新的失败 ACK。所有新增文档链接已检查目标存在；最终结构落定后再核一次。

## 无编号事项：是否新增通用变长字段上限判据

裁定：保留现有具名边界与真实 codec／线级测试，不增“所有 string／byte[] 都有统一上限”的伪通用判据。
是否流向其它连接、是否只在收件人自己的 CMD 读取、编码后的字节增长以及人群累积，均是具体协议语义；
仅看属性类型／名字或一个 Maximum 常量不能证明安全。新增协议步骤继续要求写明该路径的限制及证据等级。

只读准备比对以 A seal `2079d98d` 为原点：生产注册源码中的175个 decoder 类型集合未增删，173个声明全文相同；
另外两个仅为 MonsterDeath 注入既有诊断 LabelProbe、UseRevivalCoin 注入既有 AckFlag 配置，不增加客户端变长输入。
原始声明提取／SHA对照在 `/private/tmp/task66-doc-audit/decoder-compare.json`，脚本明确是声明文本比较，
不是自动数据流证明；最终冻结后重新运行绑定源码。当前六个有关 decoder 的字段路径另核如下。

| 输入 | 当前限制／流向 | 实际位置 |
|---|---|---|
| 队伍标题 | decoder拒绝超过256字节，不截断；名单帧80＋T，设置帧37＋T；会给队员／区域观察者 | PartyCommandSlice.SetPartyInfoDecoder；PartyNotificationSlice；PartyCommandCodecTests、PartyTitleFanOutWireTests及PartyNotificationTests已有边界断言 |
| 创建角色名 | 写角色前规则2～18字节；之后进入他人名牌 | CharacterCreationRuleService／CharacterDomainService |
| 宠物名 | <=12字节且拒绝控制字节；改名通知与名牌使用该已存值 | PetInstance.RenameAsync；特殊消耗品规则与效果校验；CreatureRuntimeProjection |
| 单封／多物品邮件正文 | 两个decoder共用MailSendWire，正文<=512字节、整请求<=1024；正文由收件人在自己的邮箱请求读取；即时提醒只有固定计数 | MailboxSendWire；StateSynchronizationPayloadCodec.Entry；MailboxQueryDomainService；MailDeliveredHandler |
| 婚礼留言 | decoder及领域规则<=20字节，最多100条；他人在自己的读取CMD取书，作者名固定30字节 | MarriageGuestbookSlice；MarriageGuestbookRuleService／Guestbook；WeddingCommentEncoder |

此裁定不消除 F-113 的人群数量无上限、F-114 的导入／直接写库字段、F-116 的多条积压与 F-117 的客户端标题上限
证据缺口；256字节仍是既有服务端策略，不冒称 ClientStatic 已证。它们按 A 的原交接边界保留，不能用本轮声明相同
或合成负载最多21个角色证明任意人群规模安全。最终正式八项目应保留上述已有强例，报告不得把此次裁定写成新增
通用运行期防护。没有修改线上字段／上限、未运行游戏客户端。

## 迁移账当前路径维护

结构拆分后更新 canonical ledger 的34行、179处当前证据／目标字段。原六个共享文件的证据分别指向
具体 CMD 的 Stages 或当前小型投影算法；旧五条两塔 fine 命令的目标类别据实际 owner 改为
internal-cmd 或 protocol-cmd，不再把已删除的共享 work-unit 文件称作 application-service。
原始条目、ID及顺序、classification、state、completion_task 均逐项比对未变，不重开或新增迁移行。
历史 removed_paths／introduced_artifacts 与 original 保持原样。

当前 strict 验证通过，五账 pending 均为0；八个原坏例均按原原因拒绝。计数仍为消息159 closed＋14 retained、
旧命令204 closed、来源73 closed＋12 retained、保护5 closed、过渡8 closed。原始路径差分与验证输出在
`/private/tmp/task66-doc-audit/ledger-current-path-changes.json`、`ledger-validate.json`、`ledger-selftest.json`；
最终结构冻结后仍需再核路径，不将本次路径维护当作正式测试完成。

## 退场组件夹具的虚弱帧覆盖裁定

主控核实 `a7f47c1` 的 `GameDungeonExitCommandHandler` 构造器传入非空虚弱投影，
`TownReturnCommandPhases.TownProjectOwnersAsync` 在镇帧前／后按原标记调用；
旧 `DungeonExitDriver.ReturnTownAsync` 则转交另一个 `TownAreaProjectionDelivery`，其测试 factory
未传可选虚弱协作者。旧四帧组件观察因此没有覆盖完整生产 CMD。

批准先补合法城镇在场／选择意图准备，再把确属上述省略的非 Golden／Characterization 组件用例扩为真实
CMD 覆盖：原四条镇帧字段与顺序保留，另验虚弱帧恰一次、正确前后位置与当前值。不能仅改 Count 或过滤
多出帧，不加生产开关／缺依赖回落。
裁定时聚焦 r3 的无 run 用例实际仍为 Expected4／Actual0，原因是旧准备没有城镇在场，尚未观测到第五帧；
最后验证记录须另给修正后的实际结果及逐文件断言说明。此处只批准测试覆盖精度修正，不声称实现或测试已通过。

随后沿真实 DI 再核，修正此前“DungeonSelection 不投影虚弱”的初判：同一基线该构造器虽将 weakness
声明为可选，`ApplicationModule` 已注册真实领域服务，组合根会传入非空实例。因此 BackToVillage 的旧
四帧夹具也有相同省略，适用上述增强裁定；不能把生产投影误改成关闭。TowerReturn 的旧构造器从未传入
虚弱投影，其固定不投影语义保持。每个 owner 的选择据真实构造与 DI 核定，不能据测试的 null 参数推断。
共享协议阶段可以接收这个真实路径差异；不准给生产 TownAreaProjectionDelivery 增加仅为旧夹具保四帧的可变开关。

允许在现有 TownArrivalProjection 收拢固定的 Prepare→CommitAdmissions→Capture→Owners→Previous→Peers
顺序，产品与 test-only TownAreaProjectionDriver 共同调用。它含既有城镇在场状态调用，准确称作
“城镇到达协议阶段”，不称纯函数／只读投影；不能取得 execution／request／units、开事务、补偿或接任意
callback。具体 CMD 仍显式拥有整个协议前后业务及各单元顺序。阶段可接收现有 IPartyTownProjectionDelivery
固定能力，复用真实非空实现；不新增仅为 spy 的生产转发类、接口或注入豁免。测试 false 只表达原组件未观察
虚弱的范围，真实产品策略由前述 owner 固定，真实 CMD 的增强用例另守完整帧序。

## 执行模型的容量回退

2026-10-26 用户明确：“不行就换gpt-6-astra medium，优先用sol 6.1 high”。原执行代理在同一任务多次
因容量限制中断，重试后已完成上述局部回归及 Entry／Selection 组合改写，再次容量中断后由新的
GPT-6-astra medium 执行代理接续当前工作区。执行范围、验收线和所有权不变，不重跑已完成工作；
最终完整结果仍由另一位 fresh 独立 Astra 评审，执行代理不充当自己的评审。

## 旧投影组件的结算结果接缝

主控读取原 GetItem 的 `PickupHandlerDriver.SettleAsync` 与 Card 的 `AwardAsync`／`GrantAsync`：
它们本就由 override 直接供应结果或转给测试 bus，没有执行所替代的生产结算。
准许这些具名 test-only 夹具改组合真实 CMD，在既有 `ICommandWorkUnits` 接缝以精确单元名和结果类型
供应同一结果；未声明单元／方法必须拒绝。不得更改 `ProtocolAlgorithmFixture.Units` 的默认真实事务
执行，也不得将此 spy 用于真实事务、real-wire、Golden 或 Characterization 验证。
旧输入、调用次数、顺序及失败注入断言保留；CMD 的入场、收件人与投影继续执行真实算法，不以
`Admitted=true` 或复制整段协调替代世界准备。最终验证须列具体夹具及现存真实结算覆盖，并明确这些
组件只证明结果后的协议行为，不证明领域结算、生产 request scope 或发件箱事务边界。

## 计划状态说明

`plan.md` 顶部补当前 TASK-66／B10 已批准与用户第二阶段的位置，将旧 P11／P10 待审及 P7 下一单元
段落明确标为历史；T-012 的变化清单引用同步为已批准 Spec 的 VC-B1～VC-B40。
这是文档事实同步，不改簇顺序、跨簇共享契约或方法，不创建新 Plan 修订或重复方法审查门。

## Scroll 组件的空服务回落

主控直接读取 `a7f47c1` 的 GameCard 构造器、ScrollAsync 及 ProtocolModule：cardPresentation 本来就是
非空构造依赖，真实 DI 注册原 coordinator 的控制接口。无 run／无 session 时生产只登记 Scroll ACK；
旧 `CardWireDriver` 用 `control!` 传 null 才额外造出 layout 帧。删除这个不可达空服务回落不改生产策略。

因此 `The_scroll_state_is_acknowledged_without_a_published_session` 改验恰一个 Scroll ACK，并同步注释；
另外两个有 session 的顺序用例补真实已结算 run／已固定 cards 准备，经一次 ScoreScroll 请求进入实际
OpenLayout，继续守 ACK→layout 的两帧原顺序，不把它们都降为单 ACK，不直接另调 OpenLayout 或伪造
第二个请求。这是夹具覆盖修正；最终记录须给真实测试结果，不能凭本裁定记通过。

## MoveMap 组件的疲劳投影覆盖

主控核实 `a7f47c1` 的 GameMapLoading 构造器要求 fatigue，ApplicationModule 注册真实领域实现；
OrdinaryLoading 先登记完整 explicit 房间帧，再逐个 currentRecipients 调 MoveFatigue，方法内还检查
IsInWorld。旧 MoveMapAlgorithmDriver 在未给 fatigueProjection 时直接跳过，未观察这段生产行为。
对于实际聚焦确认属于这个省略的非 Golden／Characterization 用例，允许补真实疲劳领域准备，保所有
原房间帧字段、次序和收件人断言，并另验每个仍 current 的收件人恰一次当前疲劳、位于 explicit 之后。
不得机械加 Count、过滤额外消息或给拒绝／过期目标增帧；最终验证另列实际受影响用例、值与结果。

## 构造器之外的委托能力复核

主控源码扫描发现 `CreatureVisibleRecipients.DeliverAsync` 仍将 send／close 两个 Func 作为能力，实际由
宠物时钟和重命名 CMD 调用；这既非纯值变换也非显式单元 action，纳入同一 B10 结构收口。
共享受众 Resolve 可保留，各 CMD 可显式拥有自己的小登记／关闭循环；不能以新转发接口或依赖袋藏回去。
宠物时钟的 ProjectionPhase.Registering 失败分类、逐目标隔离和关闭先后，以及重命名按在线查询取
physical／时钟按原 ResolveAndRequestClose 的差异都须保留。Marriage／TowerLoading 的显式单元 action
与 coordinator 队外启动 timer 的 Func 另按真实职责分类，不把所有函数参数一律视作依赖能力。

两塔入场的 `Action restoreIntent` 同样回调当前会话判断和入场意图领域写，而同一具体 CMD 已拥有这些
依赖，应改为直接具名方法，不保外传能力。选角 `Action<Exception> failed` 若仅捕获本次原 cause
用于既有失败阶段辨识，则按局部事实观察单独说明；不为词法清零改变原异常行为或引入新的权限对象。

## 最终注册、账本及残余词法核对（正式全量前）

原生导出 `/private/tmp/task66-final-runtime-r1` 成功：173 条消息＝159 业务＋14 遥测，172 条 CMD／137 个处理器均为 explicit-work-units，实际源码注册与运行清单逐项一致。导出 HEAD 为 a7f47c1，产品尚未提交；随后仅清理新增尾空白，最终源码 hash 另由正式构建绑定，不用该旧源码 hash 冒充最终字节。账本 strict 验证无 pending：消息159 closed／14 retained、旧命令204 closed、来源73 closed／12 retained、保护5 closed、过渡8 closed；八种坏账本全部被拒。原始结果在 `/private/tmp/task66-final-ledger-{validate,selftest}.json`，不是语义等价的替代证明。

主控读取实际结构判据：生产 Application／Protocol 两程序集 ICommandHandler 实现均被扫描；另从 ProtocolModule 的真实注册反向检查覆盖，含 Internal 与 Protocol。新增反例直接拒绝共用基类、nullable 构造器、默认 null 及委托注入；允许 nullable 业务值，不把语法零命中当架构结论。三份测试 csproj 变化仅链接真实算法 fixture，无类并行／期限／skip 配置变化。

`Game` 范围剩余 `null!` 两处是 `TryCapture`／`TryFindRun` 的失败 out 值，不是缺服务填空。剩余 Func 中，Marriage.WriteAsync、Boss/IllusionLoadingUnitAsync 是具体 CMD 内的显式工作单元 action；CardPresentationCoordinator.StartDetached 是原生产者抑制流的计时启动机制，不向 CMD 注入或传入业务协作能力。注释中的 virtual 不算虚方法。Creature 发送/关闭 Func 能力已改为各具体 CMD 内的小循环，固定 Resolve 仍是共享只读选择。

与 A 对照的175个 decoder 声明仍在：173个逐声明相同；MonsterDeathDecoder 与 UseRevivalCoinDecoder 两个原有诊断配置变化继续单独列明，未添加通用字段限长或修改线上字段。正式八项目、DFO、Golden／表征以及 G1 此时仍待最终证据。

## 主控消费正式全量与本地检查点边界

最终产品/测试源码清单为 `logs/task66-manifests/task66-final-r4-source-manifest.json`，SHA `2cd7b909da3d58d1166df489ba20e25b360ef9dae47c3c287a4c06f403b5c67c`。正式八项目显式组合：Application取 `logs/task66-final-r2-application`；其余七项目取 `logs/task66-final-r4-{project}`，不复制或改名原日志。主控独立核2862项r2/r4清单，唯一差异为Protocol.Tests的InitialConnectionDeliveryFailureTests；Application自身、共享Support、全部生产和项目/构建输入逐字节相同，其csproj无该测试文件引用，故不机械重跑它。r4 fresh Release `--no-incremental --no-restore`自然退出0，日志0警告0错误；r1/r2/r3曾失败的夹具轮次仍保留，未抹成通过。

最终9817例＝9797通过、2精确原Dimension失败、18精确原skip；失败消息仍是7100/maze0/cell(1,0)/Left，逐TRX身份和消息与task65-final-r4一致。相对Task65仅增加7个结构case，无删除；相对A8350的显示实例账为+1510/-43，包含重命名与驱动改变，不能称新增1510业务覆盖。43个消失display实例与此前已逐项核对的Task65去向完全相同，完整原始对照位于 `/private/tmp/spec2-final-measurement/a-to-task66-final-r4-inventory.json` 和 `a-to-task65-removal-map-prep.json`。

10个Characterization全部通过、0跳过；15份Golden与A seal逐bytes一致。主控读取A至当前Characterization完整diff，仅PartyTowerCharacterizationTests的注释和mask说明字符串改名两处，偏移、宽度和断言均不变。DFO沿原解析器8组通过：App62＋Networking6＝68个预期负例，7程序集各1 control；预期类/对照外、exe及无test frame均0。exe0仍是观测边界，不证明所有被吞错误可见。

正式套件runner累计405.955秒，对A同口径321.639秒为约1.262倍；新增实例及环境不同，此数字不作性能停止门。B进程wall/RUSAGE_CHILDREN高水位另留 `final-suite-resources.json`，不把子进程高水位当并发RSS之和，也没有A同口径内存对照。静态检查、Golden、注册、账本、DFO、用例增减的实际报告都在日志和 `/private/tmp/spec2-final-measurement/`；原始证据不进git。此处只接受Task66，不能代替最终G1、完整挑战/独立结果审查或B封存。

原始源码清单、差异和焦点结果已按原字节移入既有忽略目录 `logs/task66-manifests/`，只提交叙述性验证记录和迁移账，不提交这些原始证据。
