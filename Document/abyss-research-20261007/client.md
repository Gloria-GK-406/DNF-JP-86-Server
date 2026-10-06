# A21 深渊客户端静态调查（2026-10-07）

只读前置信息研究，不启动客户端/服务、不修改二进制/PVF/数据库。本页为此次研究报告；canonical 协议页仍须按该页人工区的证据边界阅读。

## 二进制与地址

- 直接反汇编目标：`E:/Project/DNF/DNFClient-A21-protocol2/DNF.unpacked-b1-debug.exe`，实算 SHA256 `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`。
- 同目录壳 `DNF.exe` 实算 SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`，后者是既有协议页的 identity，不能把两文件 hash 混写。
- PE Machine `014C`，ImageBase `00400000`，SizeOfImage `07475000`。**全文入口均为 RVA；VA=RVA+00400000。** 例如 NOTI02A7 RVA `00D483C0` 对应 VA `011483C0`。
- 辅助现成 A21 dump `DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin` SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。六处初始 code128B与本PE逐字节相同；后续结论直接取本PE，不依赖把dump数据误称本PE。
- `evidence/client-scan.py` 用 pefile 映射 RVA、capstone x86反汇编、按实际字符串解码函数 RVA `023BAB90/023BA5A0` 重现 UTF16字面量。解码是反馈 XOR，不是从上游代码猜名称。
- ClientStatic/Confirmed表示本文件读长/分支/常量；High表示由代码与本地名称推导作用；Hypothesis是待补链条。**本次无新增 WireCaptured/ClientVerified；全部深渊真实时序未知。**

## 协议消费闭合表

消费长度表示该handler读取的正文，不证明原服务端不会附加未消费尾部。CMD公共success/error参数由外层dispatcher提供，表中ACK长度不含公共success字节。

| 族/type | 本PE RVA | 直接读写 | ClientStatic裁决 |
|---|---|---|---|
| NOTI00CF | `00D4B910..00D4BA55` | readU32一次，4B | 全局hell蓄积标量；读取值存global VA03A5CEC0；队伍人数<=1时立即置0。精确概率/蓄积单位未知 |
| NOTI02A7 | `00D483C0..00D48453` | u32 count + count×(u32,u32)，4+8nB | **不是partyId**；count控制循环，日志明确`hell index(%d) level(%d)`。每对传RVA0134B4A0 |
| NOTI030E | `00D48580..00D48599` | **0B入口消费** | global VA03A5C9EC存在时派发事件0x27，五实参为0x27,0,0,1,1；不存在直接return。本handler入口没有读取正文；事件helper下游未完整闭合 |
| NOTI0431 | `00570010..005700B9` | 固定8B | 两u32传RVA0056DD70，分别写booster对象+360与+358的加密scalar；首/次字段精确单位未知 |
| NOTI049C | `001E0780..001E0806` | 固定32B | ensure UI0x35D，调用RVA001E01E0复制数据、001E0340刷新。32B可分8×u32，但不同字段含义未闭合 |
| C2S CMD0413 | `0233BB5B/0233BBFD` | raw-copy18B | copy源首14B本地没有赋值；+14是u32状态0或1。**18B是本地writer尺寸，首14B最终wire不能由该点闭合** |
| S2C CMD0413 | `00D05B10..00D05C08` | 外层success非0时固定12B | 三u32；首字段result，第二字段零/非零决定checkbox setter传1/0，第三字段写UI值。result13/16/B3分别显示string5726/5727/5728 |
| C2S CMD04C3 | `001E0290..001E0332` | raw-copy18B | 控件event kind=0xD；遍历checkbox，checked项发送+14=u32 index。首14B未赋值；支持一次多项多包，并非函数本身强制单选 |
| S2C CMD04C3 | `001E0190..001E01D6` | 外层success非0时固定4B | 一个u32，随后ensure UI35D；本函数不应用result字段，也不显式show |
| C2S CMD0010 | `0232DF3A..0232E067` | 4+1+1+1+2+4+1+1=15B | 第5偏移取模块+3EC的u8（特定模式改写3）；第6偏移取模块+3F8的加密scalar低字节；详见下面 |
| C2S CMD00E4 | `015F50A0` | 0B本地正文 | 原生命令名称表按opcode索引：CREATE_GUILD_AGIT；不能套70版HELLPARTY_START |
| C2S CMD00EB | `012F82C4..012F8363` | u16+u16+u8，5B | 原生命令名称表：REQUEST_DISJOINT_ITEM；已读writer到Finalize，无额外正文写入 |

既有catalog在02A7标题写DISCONN_DUNGEON_INFO而身份表写HELL_PARTY_MONSTER_INFO，且把首字段套上上游partyId；本次本PE解码的原生日志与循环共同支持后者。030E不能因通用提取器遇到外部UI调用便宣称只能靠运行时才能得到结构。

02A7下游RVA0134B4A0仅在index>0、level>0时将level写入global VA03A8FD1C的index键。此处证明的是正值index→level表，没有把index resolve为怪物code，因此保留原生日志的hellIndex名称。

0413/04C3公共发送边界：BeginCMD RVA0234D264先单独构造14B头，头+0=CMD byte1、+1=opcode u16，RVA0234D2AF向底层write传14，再清正文计数。RawWrite RVA0234CEF5将传入长度原样加到正文计数，随后把原pointer和长度传同一底层write VA05790FE9。因此**不能直接因业务值位于+14就裁定正文4B**；可证明的是raw18及计数+18，底层保护转接及Finalize尚未闭合，wire正文18/4裁决保留Unknown。对照00EB走独立WriteU16/WriteU8，正常字段不会预留14B。证据见`client-send-boundary.txt`。

## 地图通知真实前缀

- 001B handler RVA00D90190：offset0读取u32（RVA00D90202）写global VA03A5CEC0，与00CF共享；offset4读取u32（00D90358）写VA03A5CEC8。之后u8 countA、countA×u16，再u8 countB、countB×u16。该共享状态证据不能自动给两个列表命名深渊权限/任务阻挡；既有`Document/outputs/issue-101/client/noti-001b-static-evidence.md`保留37case动态结构调查，本次没有重新宣称整包已闭合。
- 001C handler RVA00D51E10：前缀u32（00D51F68）+四u8（00D51F76/84/92/A0）=8B；再两u8（00D523AE/BC）、一u8（00D524E2）、u8 groupCount（00D52522），每group读u8 pairCount再pairCount×(u8,u8)。后续还有u16/u16/u8/u8/u32/多个u8以及条件分支，不应依据上游模式写定长32。前四u8的hellRoom/boss坐标业务名未直接证明。
- 001D handler RVA00D57B30：三u8（00D57C85/93/A1）+u32（00D57CAD）=7B；offset7/8分别读u8存global VA03A5C8F9/03A5C8FA，offset9读u32，offset13读u8。**offset7是真实u8；“默认2”“枚举2=深渊”等不能由读长证明。** 后续actor/物品记录与条件分支未完整闭合。摘录见`client-map-prefix.txt`与`client-send-boundary.txt`。

## 主动选择与入口

普通CMD0010完整序列是`u32 dungeonId | u8 difficulty | u8 moduleMode | u8 derivedMode | u16 moduleField | u32 0 | u8 moduleFlag | u8 dungeonFlag`。difficulty入参限制0..4。

- moduleMode来自对象+3EC；对象+8C==1时发送3。该字段不能仅按历史上游名称直接命名为bool hellMode。
- derivedMode读取对象+3F8加密标量后缩为u8；代码显式比较该标量==1，参与地下城重映射条件。因此它也不能随意变成bool。
- moduleField来自+57EADC，moduleFlag来自+57EAD8；这些状态必须追写入方才能命名。
- Hell checkbox确实存在，原生控件`IDC_WORLDMAP_HELLPARTY_BUTTON`、`IDC_WORLDMAP_HELLPARTY_EVENT_BUTTON`、`IDC_WORLDMAP_HELLPARTY_MODE_BACKGROUND`；资源`UI/HellParty/HellPartyReleaseWindow.xui`。存在资源不证明当前PVF配置自动开放入口。
- 额外活动checkbox资源为`Interface2/event/chn_hellparty_difficulty_event/chn_hellparty_difficulty_event.img`，活动存在检查key0x86F；checked状态变化才发0413。它是活动开关，不能把它当所有主动深渊必备CMD。
- 0413错误字符串已定位，但本次尚未从dstr.dat解文字，不能把5726/5727/5728猜成门票/等级/权限错误。
- 入场费用、免费券优先顺序、全队扣费时机、错误分支实际server使用条件未由本PE闭合，归上游/PVF线索并等待真实客户端差分。

## 随机深渊、房间与战斗

00CF读取服务端标量并在人数<=1时清0，这证明至少这条原生蓄积投影有队伍人数门；**不证明任何单人随机深渊永远不发生**。客户端包含`Etc/RandomHell.etc`与`Etc/HellParty.etc`加载路径，但具体触发概率、是否由server先决定还须追地图/状态消费及PVF表。

本PE能解出原生类名`CNSealedGateOfHellDungeon::procAppend`（字面量RVA03003950），其代码位于RVA019A8250附近。已真实阅读：对象+1214为本地状态0→1→2；+121C为阶段计数，下一阶段函数RVA019A2FC0自增并封顶9；+1220/+122C用于阶段无剩余敌人/已触发判断。阶段函数从两组20B本地record读取阶段相等项，分别调用RVA019A2B70/019A2D60；有多个object/animation虚调用，无法把阶段号直接映射为柱子耐久/深渊波数。动画/背景本地资源包括`HELLGATE_OPEN`、`Map/HellMap/bg_hell_overgrowth.img`、`bg_hell_vapor.img`、`bg_hell_braze.img`。

该procAppend已读范围没有直接CMD00EB/00E4发送。下一阶段函数对游戏角色/manager写状态与调用本地特效，不是已证明的专用HELLPARTY_START网络opcode。所有间接调用未穷尽，不能把“未见”升格为不存在。

## 00EB/00E4编号反证

- 00EB writer RVA012F82D2，写u16对象参数、u16另一参数，scalar解密分支得到值后写u8，随后Finalize，**5B本地正文闭合**。CMD handler RVA00D3A120有拆解NPC/摊位路径，当前没有另一个空正文深渊writer证据。canonical历史“未进入摊位时上游路由HELLPARTY_START”只是**server同号分支**，不是A21客户端双用证明。
- 00E4 writer RVA015F50A0明确push00E4、BeginCMD后直接Finalize（0B本地正文）；旁边00E5与00E7共享公会UI状态对象，CMD handler RVA00D1B8A0的错误映射也属该路径。
- 原生名称表绑定已独立闭合：RVA0123C055读word[packet+1]，0123C059用opcode×4索引VA03B53230；初始化0项为CHECK_CONNECTION、1项LOGIN。解密CREATE_GUILD_AGIT后存VA03B535C0，(03B535C0−03B53230)/4=00E4；REQUEST_DISJOINT_ITEM存VA03B535DC，对应00EB。**A21名称ClientStatic/Confirmed：E4=CREATE_GUILD_AGIT、EB=REQUEST_DISJOINT_ITEM。** 见`client-native-enum-binding.txt`，不能再把两者命名HELLPARTY_START。
- 解密后的A21命令名字普查中有0413/04C3，却未找到HELLPARTY_START名字；这只是被检索字符串形状的阴性证据，尚无权威A21专用start opcode裁决。

## 掉落与品质

本PE包含深渊掉落/拾取日志`[Hell][Drop]`、`[Hell][SEND][GetItem] Uid : %d`、`[Hell][RECV][GetItem][%s] Uid : %d, idx : %d fsIdx : %d`。这说明深渊物品仍进入地面对象→GET_ITEM申请→接收拾取投影链，不能据此断言客户端有掉落权威。

NOTI001D/0026必须逐条核对item记录与本地品级渲染调用；client可按item静态品级选择光柱/名字色/声音，不等于client随机决定server生成哪件史诗。现有itemId/掉落对象uid消费证据支持服务端指定对象身份；品质随机源、品级展示阈值与是否特定深渊事件覆盖尚未在本轮完整闭合。

## 后续真实验收缺口

1. 主动按钮0/1与CMD0010偏移5/6的真实值、是否先0413、特殊模式3语义；NOTI001B权限阻挡列表的hell专属语义。
2. NOTI001C的hell房坐标与NOTI001D偏移7的枚举实值（历史页“默认2”仅上游构造，不能当ClientStatic）。
3. 人数变化00CF清0行为；随机触发时谁更换地图、是否先00CF再001C/001D。
4. 柱子破坏与战斗开始究竟使用普通object/monster事件、脚本还是专用CMD；00EB双用未证。
5. 02A7的index→组/怪ID映射、level实际语义、030E清场发生在掉落前后。
6. 0431两u32单位/上限与booster图层；049C的32B字段及活动专属入口。
7. 门票扣除/回滚、重连恢复、组队切图、普通boss通关与深渊清场的独立边界。

完整大型asm只存任务工作区`artifacts/abyss-client/`（不提交）；可提交的关键摘录在`evidence/client-*`，复现脚本可直接对上述PE运行。
