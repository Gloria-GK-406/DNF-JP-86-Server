# A21 马战协议清单（阶段1）

2026-10-06；以本次固定原始dump及上轮20261006重分析为当前依据。**ClientStatic，未升级WireCaptured/ClientVerified。** 本阶段仅完成目录，不提前构造模拟时间线。

## 方向与身份

同一opcode不同方向/command不能合并为同一个包。这里有11个direction entries：3 C2S CMD、3 S2C CMD ACK、4马战NOTI，以及1个通用活动NOTI006C。C2S CMD006C为REPORT_4_HACK，与S2C NOTI006C无关。0190为补丁插件编号，不是新发现的原生opcode。

| ID | 方向 | command | body长度B | 主要锚点 RVA / VA |
|---|---|---:|---:|---|
| C2S-CMD-04E6 | C2S | 1 | 14 | 0x000D21A0 / 0x004D21A0 |
| C2S-CMD-04E7 | C2S | 1 | 23 | 0x000F2B10 / 0x004F2B10 |
| C2S-CMD-04E8 | C2S | 1 | 14 | 0x000F4B10 / 0x004F4B10 |
| S2C-CMD-04E6-ACK | S2C | 1 | 5 | 0x000D2030 / 0x004D2030 |
| S2C-CMD-04E7-ACK | S2C | 1 | 5 | 0x000D20B0 / 0x004D20B0 |
| S2C-CMD-04E8-ACK | S2C | 1 | 3505 | 0x000D25B0 / 0x004D25B0 |
| S2C-NOTI-04B8 | S2C | 0 | 3 | 0x000D2690 / 0x004D2690 |
| S2C-NOTI-04B9 | S2C | 0 | 90 | 0x000D2330 / 0x004D2330 |
| S2C-NOTI-04BA | S2C | 0 | 46 | 0x000D23E0 / 0x004D23E0 |
| S2C-NOTI-04BB | S2C | 0 | 31 | 0x000D24A0 / 0x004D24A0 |
| S2C-NOTI-006C | S2C | 0 | 变长 | 0x01019E30 / 0x01419E30 |

ACK5/3505B指success非0路径；success0 reader不读后续payload，不能由此宣称任意失败帧总长。history即使result非0仍固定读3504B后判断，完整body需3505B。

## Payload、边界与副作用

### C2S-CMD-04E6：查询资料

- opaque_prefix：offset 0，14B，bytes；Unknown。
  局部writer未赋值；最终wire内容未闭合。不能称作frame头、认证token或随机垃圾。

锚点：writer_function RVA 0x000D21A0 / VA 0x004D21A0；begin_cmd_callsite RVA 0x000D21BE / VA 0x004D21BE。

- client_request_trigger：打开结果UI的初始化会先发送，再同步检查manager state；state为0拒绝继续。；已显示的结果UI进入scene0/1会再查询。
- server_effects：本静态目录不定义服务端业务副作用

未知/约束：首14B最终wire内容和真实server返回顺序Unknown。

### C2S-CMD-04E7：确认投注

- opaque_prefix：offset 0，14B，bytes；Unknown。
  局部writer未赋值；最终wire内容未闭合。不能称作frame头、认证token或随机垃圾。
- knight_index：offset 14，1B，u8；确认对象保存的所选骑士index。
- material_slot_index：offset 15，4B，i32le；确认对象保存的材料物理槽index；业务容器转换不是本字段本身。
- amount：offset 19，4B，i32le；确认对象保存的投注数量。

锚点：writer_function RVA 0x000F2B10 / VA 0x004F2B10；begin_cmd_callsite RVA 0x000F2B4D / VA 0x004F2B4D。

- client_request_trigger：确认callback参数==0且amount!=0才发送；消费客户端已有UI选择和库存投影。；state!=1时刷新函数禁用两个控件；不能说函数立即退出。
- server_effects：本静态目录不定义服务端业务副作用

未知/约束：首14B最终wire内容和真实server返回顺序Unknown。

### C2S-CMD-04E8：查询历史

- opaque_prefix：offset 0，14B，bytes；Unknown。
  局部writer未赋值；最终wire内容未闭合。不能称作frame头、认证token或随机垃圾。

锚点：writer_function RVA 0x000F4B10 / VA 0x004F4B10；begin_cmd_callsite RVA 0x000F4CE1 / VA 0x004F4CE1。

- client_request_trigger：投注UI显示初始化虚表+0x18调用004F4B10，构建材料UI后发送；get-or-create并不等同此显示初始化。
- server_effects：本静态目录不定义服务端业务副作用

未知/约束：首14B最终wire内容和真实server返回顺序Unknown。

### S2C-CMD-04E6-ACK：查询ACK

- success_flag：offset 0，1B，u8；公共CMD success参数；为非0才走此reader的payload消费门。
- result：offset 1，4B，u32le；0表示成功；其余按reader跳表处理。

锚点：reader RVA 0x000D2030 / VA 0x004D2030。

- ui：result0调用get-or-create(1FB)，不show；B9/BA同样ensure此对象。
- state：不写manager state
- announcement：成功无直接公告；错误可显示UI209

未知/约束：success0分支不读后续payload；其实际线帧总body长未由reader单独规定。；字符串ID正文/服务端失败业务意义Unknown。。

| Result | 动作 | string ID |
|---:|---|---|
| 0 | ensure_ui_1fb | 无 |
| 1 | show_ui_209 | 0x12BC0 |
| 2 | return | 无 |
| 3 | show_ui_209 | 0x12BC0 |
| 4 | show_ui_209 | 0x12BC0 |
| 5 | return | 无 |
| 6 | show_ui_209 | 0x12ACA |

其它值静默返回；不猜string对应的服务端业务理由。

### S2C-CMD-04E7-ACK：投注ACK

- success_flag：offset 0，1B，u8；公共CMD success参数；为非0才走此reader的payload消费门。
- result：offset 1，4B，u32le；0表示成功；其余按reader跳表处理。

锚点：reader RVA 0x000D20B0 / VA 0x004D20B0。

- ui：result0 ensure 1FB后调用004F4300重新计算材料输入状态
- state：不写manager state
- announcement：成功无直接公告；错误可显示UI209

未知/约束：success0分支不读后续payload；其实际线帧总body长未由reader单独规定。；字符串ID正文/服务端失败业务意义Unknown。。

| Result | 动作 | string ID |
|---:|---|---|
| 0 | refresh_bet_controls | 无 |
| 4 | show_ui_209 | 0x12BC0 |
| 5 | return | 无 |
| 6 | show_ui_209 | 0x12BC2 |
| 7 | show_ui_209 | 0x12BC4 |
| 8 | show_ui_209 | 0x12ACA |

其它值静默返回；不猜string对应的服务端业务理由。

### S2C-CMD-04E8-ACK：历史ACK

- success_flag：offset 0，1B，u8；公共CMD success参数；为非0才走此reader的payload消费门。
- result：offset 1，4B，u32le；0表示成功；其余按reader跳表处理。
- history_records：offset 5，3500B，array；Unknown；count=500，stride=7。
- history_records.round_marker：offset 0，2B，u16le；记录首u16；0终止投影循环。
- history_records.knight_index：offset 2，1B，u8；Unknown。
- history_records.value_raw：offset 3，4B，bytes；Unknown。
  现服务端/文档称f32 Odds，本reader仅原样转交7B记录，语义不据此升级。

锚点：reader RVA 0x000D25B0 / VA 0x004D25B0。

- ui：success非0时先固定读3504B，然后result0清UI+230历史容器，装最多500条、整理并更新滚动控件
- state：不写manager state
- announcement：result非0或success0静默返回，无错误弹窗分支

未知/约束：success0分支不读后续payload；其实际线帧总body长未由reader单独规定。；字符串ID正文/服务端失败业务意义Unknown。。

| Result | 动作 | string ID |
|---:|---|---|
| 0 | project_history | 无 |

其它值静默返回；不猜string对应的服务端业务理由。

### S2C-NOTI-04B8：状态及事件消费

- round_marker：offset 0，2B，u16le；轮次UI标记；不是已证的服务端去重门或动画时钟。
- raw_state：offset 2，1B，i8；signed读取原始state并保存manager+4；原生没有high-bit silent标志。

锚点：reader RVA 0x000D2690 / VA 0x004D2690；manager_setter RVA 0x000D2150 / VA 0x004D2150；ui_consumer RVA 0x000FCBD0 / VA 0x004FCBD0。

- state：setter无条件保存原始signed state；state1还重置部分投注UI资料。
- ui：ensure结果UI1FC并保存round；按rawState消费scene。
- announcement：state1..4直接聊天，未比较旧state/round；UI显示只抑制浮动提示。；state5没有直接公告，但counter2→scene11→12会触发冠军公告。

未知/约束：state值对应业务阶段名称仍是ServerPolicy/Unknown；原服state5发送时点未抓包。；构造counter=-1；尚未BB设置时state5不轮转。范围外state仍保存，但不走正常scene/直接公告表。。

| state | scene | counter依赖 | 直接公告ID |
|---:|---|---|---|
| 0 | 0 | 无 | 无 |
| 1 | 1 | 无 | 0x12BCB |
| 2 | 2 | 无 | 0x12BCC |
| 3 | 保持/条件 | 无 | 0x12BBD |
| 4 | 13 | 无 | 0x12BBE |
| 5 | 保持/条件 | 依UI+220：0→scene5且counter1；1→scene8且counter2；2→scene11且counter0；其它值不匹配。 | 无 |

counter构造初值-1；BB stage0/1/2/3分别设counter0/1/2/2。state5 counter2可间接触发冠军公告。

### S2C-NOTI-04B9：八名骑士资料投影

- round_marker：offset 0，2B，u16le；轮次UI标记；不是已证的服务端去重门或动画时钟。
- knights：offset 2，88B，array；固定8条11B记录；count=8，stride=11。
- knights.knight_index：offset 0，1B，i8；投注资料key、比赛参赛者index；FF按signed消费为-1。
- knights.condition_image_option：offset 1，1B，u8；conditionImg图像index=byte+3；没有0..3范围检查。
- knights.value_raw：offset 2，4B，bytes；Unknown。
  11B完整保存；现人工段称f32Odds，但本轮没有完成此4B全部消费链。
- knights.count_a：offset 6，2B，u16le；格式12BC7第一个计数，百分比的分子。
- knights.count_b：offset 8，2B，u16le；格式12BC7第二个计数，百分比分母=count_a+count_b。
- knights.conceal_flag：offset 10，1B，u8；精确值1且当前比赛scene<3时遮蔽资料；值2不触发此分支。

锚点：reader RVA 0x000D2330 / VA 0x004D2330；bet_ui_record_consumer RVA 0x000F5C10 / VA 0x004F5C10；selected_record_ui RVA 0x000F5130 / VA 0x004F5130；race_ui_participant_consumer RVA 0x000FA090 / VA 0x004FA090。

- ui：ensure1FB与1FC；保存完整骑士记录，重建参赛者投影并刷新当前选择。；count_a/(count_a+count_b)*100转整数显示；分母0时显示0。；conceal_flag==1且scene<3：四个文字使用12AC9，condition/attack两icon隐藏；scene>=3正常显示。；conceal_flag复制到比赛参赛者entry+5，BB晋级复制整条参赛者时保留此字段。；黄色attackTypeImg来自PVF provider+64，不来自本包；图像index=attack_value+8。
- state：不写manager+4
- announcement：未找到本reader的直接公告路径

未知/约束：condition具体中文状态、attack类别含义、两个计数的胜/负业务命名Unknown。；FF是否在所有后续路径安全视为空槽未完成证明；不能只凭ServerPolicy称通用缺席标志。；本reader读首u16，但不以它做轮号去重。。

### S2C-NOTI-04BA：投注数值投影

- round_marker：offset 0，2B，u16le；轮次UI标记；不是已证的服务端去重门或动画时钟。
- personal_total：offset 2，4B，i32le；个人投入总值进入投注UI。
- bets：offset 6，40B，array；固定8条5B记录；count=8，stride=5。
- bets.knight_index：offset 0，1B，i8；所对应骑士index。
- bets.personal_bet：offset 1，4B，i32le；对应骑士个人投入值。

锚点：reader RVA 0x000D23E0 / VA 0x004D23E0；round_total_consumer RVA 0x000F2BC0 / VA 0x004F2BC0；record_consumer RVA 0x000F5100 / VA 0x004F5100。

- ui：ensure1FB，刷新轮次/总值并装8个骑士投注值。
- state：不写manager+4
- announcement：未找到直接公告路径

未知/约束：服务端业务槽映射、币种和扣除规则不由本payload证明。；实际回包相对ACK/B9的顺序未取得原服实帧。。

### S2C-NOTI-04BB：结果、轨迹和场景消费

- round_marker：offset 0，2B，u16le；轮次UI标记；不是已证的服务端去重门或动画时钟。
- stage：offset 2，1B，i8；signed读取后unsigned0..3分派；不是B8 state。
- results：offset 3，28B，array；第一组4条、第二组2条、第三组1条；count=7，stride=4。
- results.winner_index：offset 0，1B，i8；匹配参赛者设entry+4=1。
- results.winner_aux：offset 1，1B，u8；轨迹资料两级lookup选项；不是padding/比分。
- results.loser_index：offset 2，1B，i8；匹配参赛者设entry+4=0。
- results.loser_aux：offset 3，1B，u8；轨迹资料两级lookup选项；不是padding/比分。

锚点：reader RVA 0x000D24A0 / VA 0x004D24A0；stage_consumer RVA 0x000FCCA0 / VA 0x004FCCA0；match_consumer RVA 0x000F7A70 / VA 0x004F7A70。

- ui：保存round，清理/重建第二、第三组集合，按stage调用match consumer；赢者的参赛者完整记录复制到下组，保留B9 conceal flag。；Win/Loss轨迹资料存在交叉消费，不能简单叫赢家Win资源/输家Loss资源；当前参赛者index匹配决定胜负。
- state：不写manager+4；会写UI counter与scene
- announcement：stage2在非13/9场景直接进入12；stage3无条件12；scene12产生12BBF聊天且无去重。；决赛本地tick同样能11→12公告，无须等待服务端下一包。

未知/约束：原服aux选择策略、未匹配index/缺席占位全部安全性Unknown。；loader容量5组选项；实际可用选项取决于PVF，不能把capacity当合法index范围。；无动画已播放毫秒字段；隐藏窗口tick调度、实际server阶段顺序仍Unknown。。

| BB stage | 实际调用解析组 | 条数 | counter | scene gate | gate时scene | 否则scene |
|---:|---|---:|---:|---|---:|---:|
| 0 | [0, 1] | 6 | 0 | [13] | 3 | 5 |
| 1 | [0, 1, 2] | 7 | 1 | [13, 6] | 6 | 8 |
| 2 | [0, 1, 2] | 7 | 2 | [13, 9] | 9 | 12 |
| 3 | [0, 1, 2] | 7 | 2 | None | 12 | 12 |

stage范围外仍保存round/清集合，不走上述counter/scene赋值。B9隐藏flag随参赛者晋级复制，不能当BB中的新字节；BB本身没有hiddenflag字段。

### S2C-NOTI-006C：通用活动列表依赖

- entry_count：offset 0，2B，u16le；主活动项数量。
- entries：offset 2，variableB，array；主活动项；count=entry_count，stride=variable。
- entries.event_id：offset sequential，2B，u16le；活动ID；马战入口存在门key093D。
- entries.unknown_u32：offset sequential，4B，u32le；Unknown。
- entries.start_notice：offset sequential，variableB，dstring；Unknown。
- entries.end_notice：offset sequential，variableB，dstring；Unknown。
- entries.has_detail：offset sequential，1B，u8；非0时读取detail。
- entries.detail：offset sequential，variableB，object；条件详情；count=conditional，stride=variable。
  条件：has_detail != 0。
- entries.detail.flag_a：offset sequential，1B，u8；Unknown。
- entries.detail.flag_b：offset sequential，1B，u8；Unknown。
- entries.detail.title：offset sequential，variableB，dstring；Unknown。
- entries.detail.short_name：offset sequential，variableB，dstring；Unknown。
- entries.detail.third_text：offset sequential，variableB，dstring；Unknown。
- entries.detail.start_unix：offset sequential，4B，u32le；活动开始时间值；时区/活跃规则另由manager消费。
- entries.detail.end_unix：offset sequential，4B，u32le；活动结束时间值。
- entries.detail.link_key：offset sequential，variableB，dstring；Unknown。
- entries.detail.description：offset sequential，variableB，dstring；Unknown。
- entries.detail.enabled：offset sequential，1B，u8；随后用于事件数据的启用状态。
- event9_tail：offset sequential，variableB，object；条件前置尾段；count=conditional，stride=variable。
  条件：tail reader检查当前EventManager+18的map含event9；不简单等于新包主表含9。
- event9_tail.count：offset sequential，1B，u8；前置u16项数量。
- event9_tail.values：offset sequential，variableB，array；Unknown。
- extra_count：offset sequential，1B，u8；额外表项数量。
- extras：offset sequential，variableB，array；额外表；count=extra_count，stride=50。
- extras.event_id：offset 0，2B，u16le；活动ID。
- extras.values：offset 2，48B，array；Unknown。

锚点：reader RVA 0x01019E30 / VA 0x01419E30；entry_parser RVA 0x010196B0 / VA 0x014196B0；tail_reader RVA 0x01017650 / VA 0x01417650；event_map_apply RVA 0x01016BD0 / VA 0x01416BD0；event_change_apply RVA 0x0101A1E0 / VA 0x0141A1E0；registration RVA 0x0101A0CD / VA 0x0141A0CD。

- ui：活动存在表中的093D允许通用入口尝试打开结果UI；calendar及其它系统也刷新。
- state：已解析006C→类型工厂/特殊分派链未给马战manager+4写非零state。
- announcement：不是已证的纯静默快照；各活动通知及全局UI副作用必须按整个活动集合核验。

未知/约束：event9条件检查早于新表同步；parser对旧map潜在间接修改未作完全排除。；编码取决于原生转换和补丁；本JSON只固定dstring长度计数字节，不宣称原始dump就等于补丁后的UTF-8。；不能从006C无马战state赋值推断全程序不存在其他间接写入。。

长度公式：`2 + sum(entry: 15 + bytes(start_notice,end_notice) + (has_detail ? 31 + bytes(title,short_name,third_text,link_key,description) : 0)) + (event9_tail_enabled ? 1+2*n : 0) + 1+50*extra_count`。

## 通用依赖、候选及陈旧记录

协议目录的scope必须区分直接包、共享依赖与候选。

- event093d_gate：EventManager活动map中含093D，与manager state两道独立门。
- manager45：构造未显式写state；reset可清0，登录必reset未动态证明。
- pvf_rules：材料、上限、attack icon、Win/Loss轨迹是PVF内容，不是新opcode。
- inventory_mail：本地库存刷新参与投注UI；结算/邮件是领域共享依赖，不捆成已证马战专属消息。

### 邻近opcode附录

- CMD 04EC, 04ED, 04EE, 04EF, 04F0：邻近另一模块；未证明。
- NOTI 04BC, 04B0, 04B1, 04BF：邻近/全局注册；未证明。
- CMD 04DF, 04E0, 04E9：邻近/全局注册；未证明。
- NOTI/CMD 02B0, 044A, 0197, 01D5：EventManager本体/全局活动邻接候选；未证明直接依赖；不是006C替代品。
- C2S CMD 006C：REPORT_4_HACK，同号但不同方向/type用途；与S2C NOTI006C活动表无关。
- plugin 无opcode：0190_JoustStateSync + B8 high-bit0x80；自定义补丁扩展；0190是插件编号。

### 现有人工记录差异

- `0x04E6.md`：writer D21BE是构包callsite不是第二函数；注册ACK不证明当前server实际已回ACK；首14B不应推断最终wire无业务身份。
- `0x04E7.md`：前14B构包未初始化只能作为局部writer观察；最终wire/槽转换需独立证据。
- `NOTI-0x04B8.md`：多轮CurrentImplementation记录并列，入场marker/完全不返回入场互相过期；state5不是未解析备用，已确counter0/1/2轮转。
- `NOTI-0x04B9.md`：展示选项已具体化condition图片+3；hidden精确1且scene<3；+2的f32Odds业务命名未完成全部消费链。
- `NOTI-0x04BB.md`：stage累计4/6/7/7是server填槽策略，native实际消费6/7/7/7；Stage3 sceneUnknown已过期，现已确无条件scene12；aux存在交叉Win/Loss取轨迹。
- `NOTI-0x006C.md`：机器区静态下界/A12空列表不是A21丰富布局；尾段依旧manager event9条件，不应推广3B空表到所有状态。

旧protocol文件及自动生成区本阶段未修改。上述新目录优先使用重分析的指令/消费链，对当前实现历史记录不作原生证明。

## 证据与阶段交接

dump SHA256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`；image base `00400000`；所有JSON锚点同时给RVA/VA。三份完整上一轮报告：[协议调用链](../joust-client-reanalysis-20261006/protocol-set.md)、[状态和场景](../joust-client-reanalysis-20261006/scenes.md)、[初始化](../joust-client-reanalysis-20261006/initialization.md)。本次针对字段补读：[原始指令](../joust-client-reanalysis-20261006/protocol-set-stage1-fields-check.txt)。

结构数据：[protocol-catalog.json](protocol-catalog.json)，可供HTML按ID消费。每条都有fields/effects/unknowns/anchors/sources；state5和BBstage矩阵额外结构化。目录没有给原服timeline顺序，不包含自定义high-bit分支作为native能力。

- 未有本轮原服WireCaptured/ClientVerified。
- 原服首次入场顺序、state5发送时点、ACK与库存/B9/BA先后Unknown。
- UI隐藏时tick是否持续及任意阶段完全静默精确恢复Unknown。
- 字符串正文和错误业务原因Unknown，不从截图反猜。
