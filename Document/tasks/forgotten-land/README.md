# 遗忘之地：A21 入口与专用玩法取证

对应 [#77](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/77)、
[#170](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/170)，2026-10-27 读取最新正文及评论；两项均无评论。
源码已变基至主干`b9ffc750`，分支`codex/77-forgotten-land`，版本保持主干`0.2.10`；已测主玩法与森林晶石修复通过用户验收，进入PR准备。原始取证基线为`63fb3993`。

## 当前结论

已实现原生入场选图、限时探索与热血、胶囊／变身药水／修理券、退出总结及本服击杀产出，已安排的实机内容通过用户确认，见[主玩法验收](Human-Test.md)与[击杀产出](DropPolicy.md)。当前服务保留20分钟，已关闭不限次并恢复真实额度；[配置](AcceptanceOverrides.md)是运行策略的维护位置。变基后使用主干的`DungeonRunMode.LostLand`保存唯一入场计划，经统一`IDungeonEntryRoutingDomainService`路由；不恢复旧的并行nullable玩法计划或协议层入场路由。

**当前客户端原生入口及选图流程已由用户确认。** 用户2026-10-27反馈：乔安处有“入场”按钮，
点击后打开地图选择页，页内可选择遗忘之地；界面显示1/1，并提示每消耗100疲劳增加一次次数。
这将入口及选图页记为用户实机观察，不扩大为入图、准入拒绝、计时、奖励或恢复的ClientVerified。
当前 `worldmap/worldmap.lst` 将 101 绑定到
`golden_road.wdm`，其 `[dungeon]` 为 `4000 -1 94 -1 4103 -1`，显示名「黄金梦」。
当前 `town/town.lst` 引用的 `Underfoot.twn` 在区域 2、3 声明 `[dungeon gate] 101`。
本仓实际 `ITownDefinitionSource` 也读出了这两处绑定。
资源绑定与用户观察共同确认乔安到选图页的路径；碰触边界或其他入口方式未核实。

区域 2 的 `Underfoot_Event.map` 包含 NPC146 乔安，坐标 `(988,186,0)`，
以及 NPC1055 乔安的百宝箱 `(1080,187,0)`。`npc/JoAnn.npc` 的角色是
`[golden commerce] 0` 和 `[item shop] 71`；不能把商贸角色本身当作遗忘之地按钮。
任务文字提示经乔安进入，但任务文字也不是当前菜单的执行证据。
区域 2、3 的 `[town movable area]` 未见 `(-1,-1)` 本地选图出口，因此不能把上述区域绑定
转换成“走到某个已知坐标就会打开”的操作保证。
「黄金梦」是静态选图资源名，不要求NPC菜单同名。

普通入场不能直接承接这张地图；已新增专用探索准入，普通路径保持原校验。对本仓真实内容源及规则服务执行
`ResolveAsync(4000, DungeonEntryFacts.None, 77)`，稳定得到：

```text
Dungeon '4000' maze '0': boss cell (9,0) is unreachable from the start.
```

`[greed]` 被解析为 86 个占用格；Boss 格存在，问题是普通相邻门遍历无法到达它。
不能通过删掉可达性检查或强制普通 Boss 通关来宣称玩法完成。

## 来源身份与边界

| 来源 | 当前身份 | 证明范围 |
|---|---|---|
| ClientStatic / 内容 | `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA256 `3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B` | 本次客户端入口绑定、DGN、追击者和 ACT 声明；不是实机行为 |
| ClientStatic / 指令 | A21 `DNF-memory-rva.bin`，SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，imagebase `00400000` | 模块调用、符号与空体退出构包；本文指令地址用 VA |
| 客户端 EXE | `DNF.exe`，SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` | EXE 身份；反汇编使用上行内存映像 |
| 其他服务资源候选 | `E:/Project/DNF/Script-purple-187-312-no-random-20261022.pvf`，SHA256 `246300A2C796B0C0F5B6B0C1204D130B6D058DEEBA42F121C9DF6B1B36DE16B3` | DGN4000 token 文本与客户端一致，普通规则拒绝相同；未核实为当前运行 Host 所选资源 |
| 70Reference | 指定 `Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7` | 97,021 个不同函数地址的符号及全文件 ASCII 有界检索未命中；不证明全部版本都没有 |
| UpstreamReference | `E:/Project/DNF/ServerS4A21`，本次读取 `master` 为 `04e04b43c65c464a9f48833dcaa5d06725342919` | 未找到 LostLand 专用领域实现；通用地下城和对象奖励仅供复用调查 |
| 入口探查时Host资源快照 | 当时进程29936指定上方3822A9ED档案 | 历史样本，不能当作当前服务身份；当前掉落内容身份见[击杀产出](DropPolicy.md) |
| 当前运行客户端 | `E:/Project/DNF/DNFClient-A21-protocol2`；EXE同上，PVF SHA256 `D45CF58B1A2B3D16D6B35ABE0D4E302E052F0B6BC43E9D3E98D815FED54AEF84` | DGN4000、追击者路线、钥匙和银箱ACT token文本与取证资源相同；不推断整个PVF一致 |
| WireCaptured / ClientVerified | 原生入口、已安排探索及道具／区域路线、1分钟自动结束、掉落与森林修复均有用户反馈；0204有原始请求body | 实机范围统一见[验收](Human-Test.md)，未逐项覆盖者保留，不由静态样本补造 |

内存映像路径：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`。
静态探查用 mmap 与有限目标解码，通常每目标最多800条指令；NPC菜单限4,000条，死亡接收函数限2,600条；RSS超过384 MiB中止。
不进行全镜像反汇编对象保留。`literals-a21.csv` 只用来定位，本次引用的字符串已从上述映像重新解密。

## 已确认声明与未知语义

| 主题 | 确认内容 | 尚未证明 |
|---|---|---|
| DGN 身份 | 4000 → `dungeon/Fun_test/Fun_test2.dgn`，类型 `lost land` | `forgotten_land.dgn` 是「失落的神殿」，不能按文件名错接 |
| 准入 | 最低25级、单人、禁练习、不耗疲劳、可零疲劳进入、无放弃惩罚、难度0 | 完整菜单前置条件、活动开关与真实拒绝体 |
| 次数 | incount 行 `0 4000 1 0`；用户确认当前显示1/1及每消耗100疲劳加1次提示 | 基础次数的具体周期、累积边界、重置与跨日；不能直接称“每天固定1次” |
| 地图 | 10×10；起点(9,6)绑定 MAP11200；普通解析得到86占用格 | 各种特殊门实际请求与目标授权条件 |
| 传送 | 50组 source/dest 声明，存在同源多目标及 `(-1,-1)` 来源 | 通配来源是否对应道具、使用条件；不能直接放行任意目标 |
| 时间 | `[lost land parameters] 120000 120000`；`[limit time] 600 1200`；发送原始参数后用户确认初始10分钟 | 第二个值、延时物品、暂停/断线计时与退出判定；任务20分钟文案不能替代实机 |
| 钥匙 | ACT `Box_key.act` 对 `treasure key` 加1；银箱要求大于0、减1并销毁；无钥匙显示提示 | 服务端可观察的增减链、重放/伪造校验和跨房间权威状态 |
| 追击者 | `etc/lostland/chaserpattern.etc` 38组路线；夜间条件，常见 move term=8000；创造ACT生成59822 | 各区域精确生成/停止、动态实体身份、重入与重复死亡 |
| 退出 | 专用更新函数 VA `00C4E850` 内会构造空体 CMD002A，详见协议页 | 不等于已证明其所有触发条件；退出、超时、放弃与断线结算需分开 |
| 奖励 | DGN有9组 `special passive object item`；客户端有专用奖励界面与本地符号 `lostland clear` | 奖励实际投递、20分钟完成记录、总结界面与真实资产到账关系 |

## 本仓复用与实现接缝

| 现有 owner / 接点 | 可复用能力 | 专用缺口 |
|---|---|---|
| `GameSelectDungeonCommandHandler.RouteEntryAsync` | typed CMD、精确会话、显式入场工作单元、拒绝后恢复 intent | 没有 LostLand 分流；不能让普通流程代替探索 |
| `IDungeonAdmissionDomainService` | 等级、材料、疲劳、计数的事务化准入 | 专用次数已接入，使用本服疲劳周期和ConsumedToday；官方日切尚待实机 |
| `IDungeonRunDomainService` / Cell | 同局/房间版本、怪物身份、对象领取保留、已有房间重访与退出 | 运行聚合需自然承接探索截止时间、门授权、区域进度；不能平行复制 run/Cell |
| `DungeonStructureRuleService` | DGN/MAP内容、显式种子、候选选择与入场前校验 | 专用探索校验已接入显式MAP与明确source/target传送；特殊条件及完成策略待探查 |
| `PvfDungeonRoomDropSource` / `ObjectDropRuleService` | 对象奖励池及有限实例 | 本次没有验证宝箱实例与钥匙消费的完整链 |
| 现有物品结算 / UsageQuota / 任务日志 | 原子资产、幂等领取、持久计数与任务进度 | 不应再建资产镜像或在专用普通通关奖励中重复发放 |
| 普通退出和精确连接清理 | 回城投影、释放运行、已有持久资产保留 | LostLand正常结束/放弃/断线恢复语义需独立核定 |

已实现 `ILostLandContentSource` / `PvfLostLandContentSource`，读取现有DGN owner的迷宫、
时间原始参数、基础次数、50组传送声明及38组巡逻路线。保留同源多目标与 `(-1,-1)` 通配声明，
拒绝截断、混合负坐标和越界位置；不把读取到的传送表转换成运行期授权。
专用准入已接入选图CMD，复用UsageQuota事务和既有疲劳ConsumedToday计算额外次数，
不另建累计疲劳或资产副本。日切使用本服疲劳owner的既有游戏日边界，这是当前实现规则，未称官方实机已验收。
探索布局保留86个greed格与无MAP的Boss标记，逐个验证85个显式MAP；运行拒绝进入无MAP标记。
普通Boss通关保持隔离；只接DGN明确source/target的传送，`(-1,-1)`不产生任意来源许可。
传送表只覆盖静态拓扑，特殊门/道具授权仍需实机核对。

拟定职责：Infrastructure只读取明确的DGN/ETC/ACT声明；规则服务解释内容及显式facts；
运行事实由既有地下城聚合拥有；CMD划分准入/移动/结算事务并登记投影。
传输、执行器、发件箱不加遗忘之地分支。实现方案仍以补齐以下证据为前提：
钥匙可观察链、专用限时结束/奖励与计数恢复。

## 复现与验证

只读取证小程序不启动 Host、监听端口或注册 Persistence；两次运行均没有打开玩家库。

```powershell
dotnet build Tool/PvfProbe/PvfProbe.csproj --nologo -v quiet
python Document/tasks/forgotten-land/collect-static.py
python Document/tasks/forgotten-land/probe-client.py
dotnet build Document/tasks/forgotten-land/probe/Probe.csproj --nologo -m:1
dotnet Document/tasks/forgotten-land/probe/bin/Debug/net10.0/Probe.dll E:/Project/DNF/DNFClient-A21-protocol/Script.pvf
```

取证程序的 PASS 只表示声明与普通路径拒绝已复现，不是玩法测试。构建成功，但本任务小程序仍有
NU1900（NuGet漏洞数据服务不可达）警告；没有关闭审计、降低仓库的 warnings-as-errors 或修改共享设置。
完整解决方案构建通过（0警告、0错误）；仓库Unit分类1286项通过，另有37项次数投影/计时编码测试和6项遗忘之地准入边界测试通过。Host架构4项通过。没有把这些结果当作完整玩法或实机验收。
新增内容契约测试先以 `NotSupportedException` 运行失败，完成读取后通过；又运行新增边界及
Heras、地下城定义、对象奖励邻近回归，共52项通过。测试命令如下；日志保留在本任务
`evidence/content-red.log`、`content-green-isolated.log`、`content-regression.log`（未纳入Git）。

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter 'FullyQualifiedName~LostLandContentTests|FullyQualifiedName~HerasContentTests|FullyQualifiedName~PvfDungeonDefinitionSourceTests|FullyQualifiedName~PvfDungeonRoomDropSourceTests' --nologo -m:1 --logger 'console;verbosity=normal'
```

普通沙箱测试曾在VSTest连接testhost阶段90秒超时；同一已构建测试在正常工具执行权限下1秒内通过，
随后包含重新构建的52项回归也通过。没有因此改游戏代码或VSTEST全局超时配置。
协议对账检查报告18项失败；使用HEAD原catalog做只读对照，失败诊断与HEAD的源代码及catalog逐行相同，未新增失败；新025C编码登记已单列覆盖。
见[对照结果](evidence/documentation-comparison.txt)。新增本地文档链接及 `git diff --check` 通过。
准入行为测试先复现Boss不可达拒绝（`entry-red.log`），接入专用规则后通过。
准入/次数、普通布局与Heras相关回归44项通过（`entry-regression.log`）；覆盖99→100疲劳边界、
返还不抹累计、次数耗尽、单人/等级/练习拒绝及事务回滚。尚未证明钥匙、追击者、限时退出和奖励完整玩法。
已发布本任务入图探查产物到忽略的`private/host/`，使用真实玩家库的一致备份副本验证了新Host启动、零TCP/UDP监听及10个角色/物品相关表行数不变；原0.2.8 Host也能读取升级后的副本并启动。
副本验证首次因继承主服HTTP配置而发生18080端口冲突，随后关闭正确的`LauncherAuth`开关后通过；未停止端口占用进程。
所有临时副本Host均已结束。用户授权独占后，已启动真实玩家库入图探查，见下方当前实机状态。
尚无功能版本递增、功能提交、PR、tag或Release。

核心工件：

- [实际内容源和普通入场结果](evidence/current-parser.json)、[服务资源候选复现](evidence/server-candidate-parser.json)。
- [当前DGN](evidence/client-dungeon4000.txt)、[世界地图101](evidence/client-worldmap101.txt)、[暗黑城区域](evidence/client-town2.txt)。
- [乔安声明](evidence/client-joann.txt)、[区域2](evidence/client-area2.txt)、[区域3](evidence/client-area3.txt)。
- [钥匙ACT](evidence/client-key-action.txt)、[宝箱ACT](evidence/client-chest-action.txt)、[追击者ACT](evidence/client-chaser-action.txt)、[巡逻路线](evidence/client-chaserpattern.txt)。
- [A21有限反汇编](evidence/client-disassembly.txt)、[重新解密的符号](evidence/client-decoded-literals.json)。
- [70符号检索](evidence/70-symbols.json)、[70 ASCII检索](evidence/70-strings.json)。

## 入图探查决定

当前按用户确认的每局20分钟策略，由Host配置`LostLand:LimitSecondsOverride=1200`提供首字段及服务端截止；第二字段保持PVF的1200，具体用途仍未知。首次加载冻结截止，换房不重置。[NOTI001F](../../../protocol/game/server-to-client/NOTI-0x001F.md#2026-10-27-遗忘之地专用完成界面)到点触发原生总结并回城，1分钟候选已通过用户验收，20分钟本轮实机待确认。配置、默认端口就绪、备份和数据库保持证据见[验收配置](AcceptanceOverrides.md)，其余进度见[验收结果](Human-Test.md)。

## 本服热血循环

用户已同意以下本服规则；70参照和上游未提供可用涨幅，不称为原版复原。

| 已确认敌人分类 | 单次击杀增加 |
|---|---:|
| Ordinary 普通怪 | 1 |
| Champion 精英怪 | 3 |
| SuperChampion 高级精英 | 5 |
| Boss | 10 |

100点触发热血，溢出舍弃。持续时间读取DGN `[fever time]` 的首项，当前为60秒，
与客户端原生计时保持同一参数；其余七项原样保存，尚不在服务端赋予推测语义。
热血期间不积累、不叠加、不续时；截止后归零，再次击杀从零蓄能。
只取怪物当前身份的一档，不因泛用命名怪标记重复加分。

`DungeonRunDomainService` 拥有当局蓄能和热血截止时间，随既有run释放；换房不重建。
只有成功提交的敌对怪物死亡推进，重复上报、回滚、友军、APC、破坏物、dummy和清场连带移除不推进。
击杀提交结果携带本次进度变化，CMD在原死亡投影之后发送[0264](../../../protocol/game/server-to-client/NOTI-0x0264.md)。
热血期间不重复发送100。加载只恢复未激活的进度，原生热血计时跨房表现仍须实机验证。
500毫秒定时源只提交typed CMD；CMD按原run及预期截止时间登记事务化归零，
提交后仅向当前且已加载的精确会话发送0。旧周期、旧run、回滚和重复到期任务不能清掉新进度。

四档通知测试先观察到缺失0264的四项失败，再接通后通过；到期测试先观察到到期扫描为空的三项失败，
实现后通过。实际定时CMD、SQLite击杀回滚重试和CMD架构约束66项通过；
击杀、加载、次数投影与计时编码相关86项通过。日志为本目录忽略的`evidence/fever-*.log`。
自动证据证明本仓循环和投影边界；用户已确认热血状态正常。60秒精确计量、各档涨幅和所有跨房组合未逐项反馈，不扩展为全玩法验收。

## 剩余验收顺序

入口、主循环、两类药剂及到期原生收尾已获用户确认，保留各自的证据边界。
剩余机制按[顺序验收清单](Remaining-Acceptance.md)分局执行：钥匙银箱与持久奖励→昼夜追击者→区域条件门→最终区域与账目→中断清理→自然次数边界。
A组已获用户“全部OK”反馈；当前从B组昼夜与追击者开始。关键前置失败时先反馈，不要求用户继续消耗资源跑依赖项。
延时道具尚未确认具体名称和效果，列为遇到后再安排的条件项，不要求按猜测寻找或消耗。
