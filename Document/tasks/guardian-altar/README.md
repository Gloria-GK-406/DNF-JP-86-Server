# #168 守护者祭坛：完整玩法候选与验收边界

适用：A21 守护者祭坛（Advance Altar），DGN3511–3523。它与无尽／极限祭坛11006／11007的状态、调度和协议分别归属。

本工作区：`codex/168-guardian-altar`，基线 `63fb399345383f0d42d76e4289d8d9f967444acf`。
用户授权独立会话实现 #168，入口由本会话探查并提出解决方案。关联需求：
[#168](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/168)、
[#170](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/170)，2026-10-27已读取正文；#168无评论。

## 入口方案

**优先使用 A21 已存在的底栏按钮／主菜单守护者祭坛项，按原生通用调度状态控制开放。**
不以在城镇补放克雷泽NPC作为第一方案。NPC资源存在、当前城镇固定放置和原生按钮可达是三个不同事实。

1. Content：`npc/npc.lst`的161映射`npc/AdvanceAltar.npc`，名称克雷泽，role包含
   `[item shop] 76`、`[start advance altar] 0`及`[upgrade item quickslotequipment] 0`。
   但对档案211张`map/town/*.map`及当前town索引引用的104个区域地图的检查，未找到NPC161固定放置。
   这是固定地图声明的否定结果，不排除客户端动态生成NPC。
2. ClientStatic / Confirmed：底栏鼠标处理`VA013531D5–01353201`检查`this+330`按钮、城镇模块及当前角色，
   调用`VA01335D40`。主菜单处理`VA013AAC69–013AAC8B`也调用同一个函数。
   源码名字字面量可定位为`CNRDBaseInterface::startAdvanceAltar`。
3. ClientStatic / Confirmed：开始函数检查角色等级至少50（`VA01335EE6`），成功分支
   `VA0133619E–013361B1`发送空正文CMD0392。另两处0392发送点属于祭坛结果后的再次进入，不应误当首次入口。
4. Content：`etc/chn_schedule.etc`含`[common schedule]`类别4、内容10；它没有把3511–3523逐项列入
   `[dungeon]`调度列表。不能机械照搬领主之塔的`category=0,contentId=3505`方案。
5. ClientStatic / High：NOTI033F读取四个u32，由`VA01192C7D–01192D21`重排为调度记录并交给
   `VA011C2670`。通用调度处理`VA011C3370`在`VA011C368A/011C3693`、`VA011C379B/011C37A0`
   明确识别内容10／类别4，`VA011C385D`调用底栏按钮状态函数`VA013362D0`。
   后者操作同一`this+330`按钮的显示／可用状态。部分显示路径还受本地化feature flag约束；尚未实机证明最终显隐。
6. 70Reference：`advancealtar::isOpenAdvanceAltar`（`0812CFEA`）检查`GetScheduleState(4,10)==1`；
   `GM_OpenAdvanceAltar`（`08126D60`）也使用类别4／内容10。此处仅作独立语义对照，不作为A21 opcode依据。

实现使用NOTI033F正文：四个u32 `4,1,10,0`，十六进制
`04000000 01000000 0A000000 00000000`；关闭明确投影状态0。
该投影已接线，**客户端最终效果待实机验证**，不是已有WireCaptured或ClientVerified。
协议事实维护在[033F](../../../protocol/game/server-to-client/NOTI-0x033F.md)和
[0392](../../../protocol/game/client-to-server/0x0392.md)。

默认开放策略沿既有受管活动采用常驻可用、GM可独立关闭；不搬历史周末时段。
开关与0392准入必须由同一个活动owner裁决，选角和在线变化均从当前事实重建状态。
活动119是70原生`isOnEvent`所查身份，尚未证明A21首次入口必须额外收到EVENT_INFORMATION119，
不能直接把“70存在119”当作A21必发条件。本实现受管配置使用119；A21入口的已知调度身份仍为4／10。

## 输入与证据来源

| 标签 | 当前输入 | 身份与边界 |
|---|---|---|
| ClientStatic | `E:/Project/DNF/DNFClient/DNF.exe` | SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` |
| ClientStatic | `E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin` | SHA256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`；本目录反汇编行使用VA，工具参数使用RVA，基址00400000 |
| Content | `E:/Project/DNF/DNFClient/Script.pvf` | SHA256 `573388731C394A71D5E75ED7DD215124B3D04383631D93116625ED353E06ACAB`；常用客户端副本 |
| Content | `E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf` | SHA256 `D45CF58B1A2B3D16D6B35ABE0D4E302E052F0B6BC43E9D3E98D815FED54AEF84`；研究副本，不能与常用客户端混称同一输入 |
| Content | `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` | 当前SHA256 `01F1BE685D46647961DF65207A36304777DB20EF5A3D03C68EFAA92F8B0C5189`；2026-10-28默认端口验收输入，85项祭坛相关和2项内容补测通过；初版哈希与原检查记录保留在部署快照 |
| 70Reference | `E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r` | SHA256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7` |
| UpstreamReference | `E:/Project/DNF/ServerS4A21` | HEAD `04e04b43c65c464a9f48833dcaa5d06725342919`；存在未跟踪参考备份目录。当前源码检索仅找到协议枚举，未定位到守护者祭坛玩法实现，不把缺少检索命中扩大为所有历史版本从未实现 |
| CurrentImplementation | 本工作区 | 领域、持久化、协议及调度已接入；按最新用户授权维护默认端口验收实例，实际运行身份见deployment |
| WireCaptured | 本任务无 | 不以静态机器区代替真实双向样本 |
| ClientVerified | 有限玩法范围 | 玩家确认入口、进图、友军召唤、时间条及HG增长；存在首局敌军数值异常、再次入场无可见敌军和回城快捷栏残留，修复候选待复验，完整玩法未通过 |

常用客户端与研究副本的哈希不同，二者都已独立运行内容测试及入口地图扫描，所列守护者内容检查均通过。
旧探索文档中的2026-10-11 PVF与上游哈希不再作为本次当前输入身份。

静态工具使用mmap读取、仅反汇编选定函数；私有内存超过512MiB则终止。
`--xrefs`输出的是字节扫描候选，只有重新解码调用者函数确认后的调用路径才能作为证据。
未从有效函数边界起始的候选反汇编片段也不作为证明；正文只引用已核实路径。
原始摘录见本目录`client-*.txt`、`70-*.txt`，每份均带输入哈希。
入口PVF扫描由[探针](probe/Program.cs)生成，分别保存`entrance-pvf-current.txt`和`entrance-pvf-scan.txt`。

## 当前实现

守护者祭坛独立领域owner已接入：原生大厅、12关三档难度、99轮生存、单位／技能／塔升级、槽位管理、
能量升级与召唤、暂停恢复、中心失败、奖励与成就、金币洗星、主动退出及50秒结果超时回城。
所有生产注册已加入，数据不再停留在内容解析器。

| 职责 | 实现与复用 |
|---|---|
| 活动入口 | `GuardianAltarEntranceEventDomainService`受管119默认常驻；选角及在线变化投影4／10；准入读取同一开关 |
| 内容／规则 | `PvfGuardianAltarContentSource`只读DGN3511–3523、实际MAP索引、时间线、能量、商店、洗星与成就；规则经`IGuardianAltarRuleService` |
| 大厅／本局 | `GuardianAltarDomainService`与事务参与者`GuardianAltarRuntimeStore`，精确SelectedSession、run、roomVersion及generation归属 |
| 入场／地图 | 复用Admission／Visit／Run；独立难度进度；不消耗疲劳、不施加放弃虚弱；中心及已购塔使用真实地图actor，level0未购塔不生成 |
| 战斗／调度 | 真实PVF各阶段时间线、存活数动作选择、动态敌人；typed Internal CMD生产者，旧代数失效；普通副本清房及掉落不接管此模式 |
| 召唤／技能 | 服务端验证STK、槽位、能量、等级和冷却；0396成功由客户端执行STK友军效果，技能由原生变身／技能配置消费，禁止重复创建友军 |
| 结算／资产 | 每成功领取一轮奖励扣一次，先免费后额外；失败／放弃不扣；星数、最高纪录、实际物品与防重账本同一SQLite事务；背包满走既有邮件兜底 |
| 生存 | 保留中心、能量、冷却与同一roomVersion；3秒轮间间隔后切换PVF时间线，无重新Loading；99轮上限或无奖励次数后终局 |
| 升级／重置 | 三类升级验证原生声明等级区间与真实星数；装卸／交换校验归属和旧ID；金币洗星返回余额并重建槽位；成就只能领取一次 |
| 退出／回城 | 复用精确会话拆除与DungeonParticipantLeft；结果超时复用既有TownArrival投影；普通团灭计时器不抢先结束此模式 |
| 持久化／日切 | Schema唯一owner新增character_guardian_altar与guardian_altar_settlements；revision CAS；每日按现有06:00规则补足3次免费，额外次数保留；打开／读取及操作时刷新 |

协议字节与发送顺序统一见[祭坛流程](../../../protocol/patterns/flow-guardian-altar.md)及各协议页。
0392–039A已注册请求与响应；035B/C/D、0360/1/2已注册通知。035E无明确字段证据不构造，035F是原生测试路径，
不冒充每秒能量同步。0392头offset28的4B仍Unknown，保持0并列入实机观察。

## 当前档案的边界

- 单位7000银钩菲力四行资源2749201–2749204，数字等级为`1,2,3,3`，最后名字含Lv4。
  本实现保留原始数据，按声明等级校验；请求升到不存在的声明Lv4会明确拒绝，不扣星、不篡改PVF。
- 普通NPC161的item shop76在当前常用PVF的`itemshop/AdvanceAltar.shp`中商品列表为空。
  本实现沿既有真实NPC商店链，不凭空补出装备／兑换商品。祭坛自身三类升级商店有完整内容并独立实现。
- 本次实现未接入外部商城的额外次数／付费星购买，未取得可消费洗星道具的完整请求身份；进度保留这些字段，
  039A类型1明确拒绝。免费玩法、金币洗星和成就奖励不依赖这些外部商城功能。
- 中心先被毁判败；必须全部阶段、待生成项及敌方都结束才判胜，瞬时空场不提前通关。
- 自动测试与服务端状态不能证明客户端按钮、本地技能效果、敌友碰撞或结果UI已经通过。

## 验证及部署

本目录[验证记录](verification.md)列出最终自动检查、失败身份和实机边界；
[实机验收](acceptance.md)给出编号步骤；[部署准备](deployment.md)记录隔离产物及切换前置条件。
目前没有部署本功能，没有改动常用客户端、PVF、主检出、其他功能工作区、共享运行产物或玩家库。

用户实机通过后才创建功能PR。云端当前提交明确审核通过且CI通过后才能合并；本地自动测试不能代替这些门。
