# 给 Opus 的协作调查 Prompt

请独立协助调查 DNF 日服 A21 客户端的普通婚礼流程。当前先调查，不修改服务端、客户端、PVF或数据库，不重启服务、不恢复存档、不提交或推送代码。不要预设现实现或主代理的结论正确；请优先寻找能推翻现有解释的证据。

## 用户实机反馈

当前普通婚礼预约接受后，可以自动进入教堂。但有以下问题：

1. 在教堂点大司祭的“开始仪式”，只出现“恭喜！”和确定按钮，没有其它内容。用户明确确认没有礼金输入或祝福列表。
2. 新郎通过大司祭离场后，底部没有重返教堂的图标，无法回来。
3. 大司祭商店的两种礼炮等商品不能购买。
4. 请柬派送窗口既有底部“关闭”，也有右上角 ×。用户怀疑结束派送后应该将倒计时压缩到开场前1分钟；需确认原版实际语义。

最需要你独立查清的是第1和第4项：原生“开始仪式”的菜单、窗口、动作、协议和双方确认流程到底怎么连接；关闭窗口是否有提前开场或结束派送的业务副作用。

## 工作区与证据

- 工作区：`E:\Project\DNF`，遵守顶层和仓库 `AGENTS.md`。
- 服务端仓库：`E:\Project\DNF\DNF-JP-86-Server`，当前提交 `6576a2842317d8bf62d14b03b295e97832e8683c`，分支 `task/next-round-20261013`。请现场核对，不覆盖既有修改。
- 现实现说明：`Document/MarriageWeddingFlow-20261016.md`。
- 静态证据说明：`Document/MarriageChurchEntryEvidence-20261016.md`。其中历史菜单解释可能有误，不作为原生行为权威。
- 当前服务日志：`artifacts/marriage-flow-20261016/runtime.stdout.log`；stderr同目录。不要为了取证停止服务。
- 当前实现：`Server/src/Dfo.Server.Protocol/Game/MarriageProposal/`、`Server/src/Dfo.Server.Application/Marriage/`。
- A21 内存镜像：`E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin`。
- 镜像 SHA256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。对应客户端 EXE SHA256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- ImageBase：`0x00400000`。镜像按RVA布局；capstone以RVA地址反汇编时，数据中的原始VA/vtable指针仍应减ImageBase定位，注意区分。
- 客户端PVF：`E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 和 `E:\Project\DNF\DNFClient-A21-protocol2\Script.pvf`；两份文件不完全相同，重要段应对比。
- 服务端PVF：`artifacts/sao-ticket-20261014/Script.server.pvf`，SHA256 `91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4`。
- PVF只读探针：设置 `PVF_ARCHIVE_PATH`，运行 `dotnet Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll dump <path> <token-limit>`；另有 `lst <list-path> <id>`、`files <substring>`。
- 字符串只读解码：`Tool/ClientStringPatch/patch_sao_strings.py` 的 `resources/decode`，客户端 `dstr.dat` 中 `('FILE',102,1042)` 资源。可import，但不要调用patch；设置 `sys.dont_write_bytecode=True`。
- Python capstone 可用；中文stdout建议 `sys.stdout.reconfigure(encoding='utf-8')`。

## 已有线索：需要复核，不是答案

### A. 空“恭喜”窗 / 提前开场

- dstr72755（0x11C33）原文就是“恭喜！”，唯一push引用在 RVA0088D4A3；窗口初始化函数0088D460，由0088EA87调用；0088EA20构造UI 0x1C1。
- NPC分支021AB558是婚礼预约；021AB5FF调用00891880后打开UI 0x1C6。但0x1C6是宾客祝词列表：构造008778C0，CMD03FA响应00895070查找同一UI。因此不能凭附近的婚礼命名把021AB5FF认作开场。
- dstr72919/72920/72921是“准备好了，请开始吧！”、“有点紧张，等会再开始吧！”、“让我再考虑下，请等等吧！”，引用分别00890E88/00890EDD/00890F32。这可能是关键的原生准备确认流程，需要追它的创建条件、选择回调和出站协议。
- 当前两份客户端PVF的 `etc/popupmenu.etc`：1019实际是“巨龙排行 npc 5”，历史文档写它是开始婚礼不可信。“开始仪式”可能来自另一硬编码入口、NPC或资源，必须从实际路由追。
- 当前服务端只做固定开场timer，成功时NOTI03D7启动动画；没有独立手动开场命令。03D7原生handler00897B00读取68B，调用00897770，第一u32为上下文key，第二u32为area，后面两个30B名字。不能把服务端timer当原生完整状态机证据。

### B. 离场与装扮

- 当前SetUserAreaHandler离场后发送03CF operation1，原意只是恢复婚服。
- 原生03CF handler00898220读取67B；非0分支00897C00会在00897F95附近调用00888C00清除manager+4的婚礼record。
- 底部返回图标0x41C依赖state2与00895570返回有效record，逻辑00F9CF40..00F9D0BE；这解释了暂离后图标丢失。
- 请核对如何正确恢复服装而保留预约上下文，避免简单删包留下服装错误，或盲目补发准备包产生其它副作用。

### C. 请柬关闭与60秒

- 派送窗口vtable VA031A5214；底部关闭event008856D0及00886010均调用vtable+0x78，当前映射到通用Close RVA01FB6EB0。
- OnOpen00885910，tick00885D50，派送构包00885590（03F6）。仍需查Close的回调、右上×、Esc与底部按钮是否完全同义，不能仅凭没有直接call sender就断言绝无业务副作用。
- 当前PVF `[noti end]60`；dstr72953明确为“婚礼邀请函派送时间截止到婚礼开始前1分钟”。它是否仅控制派送权限，还是有其它阶段意义，需要沿消费者确认。
- 当前观察没有对应提前开场请求；但日志的未出现不能独立证明没有原生路径。

### D. 商店

- 日志记录npc20013、shop1208、item10002625/10002626/10002616，拒绝原因ShopUnavailable。
- PVF `itemshop/chn_father.shp` 商品表包含上述真实商品与多个 `-1` 空格。
- `PvfNpcShopDefinitionSource.ReadShop` 当前拒绝item list中的所有<=0值，使整店返回null，可能是共享解析bug。
- 两礼炮价格1000；10002625为 `[only effect]0`，10002626为 `[random reward item]0`，均有shared effect。若提出修复，还应标明购买后使用、消费和双端特效的覆盖情况。

## 交付要求

请输出中文调查报告，优先回答：

1. “开始仪式→空恭喜窗”的完整调用链、正确窗口内容及根因；是否缺回包、参数、原生状态，还是菜单/资源映射错误？
2. 原生有没有双方准备/提前开场流程？若有，协议、action/outcome、请求与响应字段、窗口和状态转换各是什么？若尚不清楚，明确缺证据。
3. 底部关闭、右上×、Esc是否发CMD或改变开场时刻？“关闭就跳到最后一分钟”的假设是否成立？
4. 对主代理的离场03CF、商店-1解释给出独立支持或反例。
5. 给出最小修复方案和双端实机回归步骤，特别保护重登恢复、重复扣款/奖励、缺席退款与多场婚礼隔离。

每项关键结论带源文件/行号、RVA/指令、PVF字段或日志时间，明确区分 `ClientStatic`、`70Reference`、`CurrentImplementation`、`WireCaptured`、`ClientVerified`、`Unknown`，并标注Confirmed/High/Hypothesis。70server或历史资讯只能作对照，不能替代A21证据。

如果能写文件，只写 `Document/Opus-WeddingInvestigation-Report-20261016.md`，必要原始证据放 `artifacts/wedding-investigation-20261016/opus/`，避免与其它子代理冲突；否则直接返回报告供用户转交。请不要擅自修复、改客户端、恢复数据库或部署。

## 发出后新增的待独立复核线索

子代理完整追踪已纠正一个中途假设：UI1C1的OnOpen RVA0088A6B0按参数1/2/7写正文（dstr11D0E/11D0F/11CDD），0不写正文；但NOTI03CE与CMD03F1传入的是S2C action，不是响应末尾outcome。两handler分别在008919E3和00892009附近传action。当前服务端编码及调用明确为action2、outcome0，理论上应显示dstr72975“夫妻双方必须在仪式开始前进入礼堂才能举行婚礼。”，所以不能把空窗归因于接受0。该症状根因仍未闭合。需要实际UI1C1 OnOpen参数、用户点击的调用栈、真实03F1/03CE正文及客户端UI资源对照，不能改枚举试错。

另，NPC菜单跳表RVA021AC338以menuId-1005为索引，1014→021AB558→UI1C2，1026→021AB5FF→祝词UI1C6；1019→021AB680→UI1CE属于另一个泛用菜单分支；UI1C2确定回调0088ECF0发送03F0 action2婚礼预约，不能凭“准备好了”的文案就认定它是婚礼提前开场。请独立验证上述线索和实际用户入口。
