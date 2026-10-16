# 普通婚礼实机反馈的并行调查

日期：2026-10-16。调查起点：`6576a2842317d8bf62d14b03b295e97832e8683c`，分支 `task/next-round-20261013`。

本轮范围为调查及协作资料整理，没有修改服务端、客户端、PVF、数据库或运行服务。三位子代理分别调查原生开场入口、关闭与重入、商店与礼炮使用。另已准备[给 Opus 的独立调查 prompt](Opus-WeddingInvestigation-Prompt-20261016.md)，Opus 的结果尚未收到，不计入本报告证据。

## 用户观察与待解决项

| 观察 | 调查状态 | 当前处理方向 |
|---|---|---|
| 点击“开始仪式”只有“恭喜！”和确定按钮 | 窗口按action选正文；当前action2应有正文，根因尚未闭合 | 先取得实际开窗参数和点击链；不能改outcome试错 |
| 暂离教堂后没有返回图标 | 原生清理逻辑与现实现发送点已闭合 | 准备期恢复装扮后补发原预约上下文 |
| 大司祭商店礼炮等商品无法购买 | 日志明确ShopUnavailable，PVF空位被通用解析器拒绝 | 修通用商品表解析，再核教堂内共享商店 |
| 关闭请柬窗是否应跳到最后一分钟 | 静态路径不支持此解释 | 保留原开场时间；60秒是派送截止边界 |

## 空“恭喜”窗：已排除一个错误解释

`ClientStatic / Confirmed`：UI1C1构造`RVA0088EA20`调用`RVA0088D460`设置dstr72755“恭喜！”作为标题；OnOpen `RVA0088A6B0`按参数1/2/7填正文，参数0不填。

继续追调用方后确认，该参数来自S2C **action**：NOTI03CE handler `RVA00891960`在`008919E3`附近传action；CMD03F1 handler `RVA00891FD0`在`00892009`附近同样传action。不是末尾outcome。主代理和子代理中途提出的“接受0导致空白正文”假设已撤销。

当前`WeddingResponseAck`/`WeddingResponseNotification`编码和预约响应调用传action2、outcome0；按上述原生路径，应出现dstr72975“夫妻双方必须在仪式开始前进入礼堂才能举行婚礼。”，而不是空白。因此现有证据不足以证明问题出在这两个回包，更不能将outcome0改成1或2试错。运行日志只有帧类型和长度，不能证明本次实际body或窗口实际收到的参数。

菜单跳表`RVA021AC338`以menuId-1005为索引。1014对应`021AB558`，可打开预约UI1C2；其确定回调`RVA0088ECF0`发03F0/action2。1026对应祝词UI1C6。1019对应另一泛用UI1CE；不能将附近的“准备好了”文案或历史错配的菜单标签当作婚礼提前开场证据。

下一步最小证据是：实际点击的菜单编号/调用栈，UI1C1 OnOpen实参，实际03F1/03CE正文及当前UI资源初始化。Opus应重点独立核这条链；当前没有足够证据支持隐藏按钮、删除窗口或改回包的生产修复。

## 请柬窗口关闭与一分钟的关系

`ClientStatic / Confirmed`：派送窗口vtable `VA031A5214 + 0x78` 经 `VA0248CD10` thunk 跳转到通用Close `RVA01FB6EB0`。secondary event `RVA008856D0` 中控件6/事件0x0D走Close，控件5/事件0x0D才调用03F6派送构包器 `RVA00885590`；另一相关关闭入口 `RVA00886010` 也调用同一Close。

通用Close只处理UI移除和注册项清理，没有婚礼类回调、婚礼manager时间写入或协议构包/发送。右上×、底部关闭、Esc的静态共用路径仍需分别实机抓包确认，不能以静态分析冒充逐动作验收。

`RVA00885D50` tick通过 `RVA00893B70` 计算当前婚礼绝对开场秒数减当前秒时钟；剩余≤60秒时只显示dstr72953一次，原文为“婚礼邀请函派送时间截止到婚礼开始前1分钟。”当前PVF `etc/weddingsystem.etc` 的 `[noti end]` 为60，属于当前PVF静态内容，不能标成70server参考。

因此目前没有证据支持“关闭就压缩倒计时”。应保持预约绝对时刻；若未来要实现提前开场，应另查双方确认流程，不能利用普通关闭动作猜测业务含义。

## 离场后的图标与真实装扮

`CurrentImplementation / Confirmed`：`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs` 在暂离教堂后发送 `03CF(op=1)`。该包最初按恢复真实装扮使用。

`ClientStatic / Confirmed`：`03CF` handler `RVA00898220` 调 `RVA00897C00`；非0分支在 `RVA00897F95` 对manager+4调用 `RVA00888C00`，清掉当前婚礼peer、频道、area、名字、开场时间及有效标志。返回图标0x41C在 `RVA00F9CF40..00F9D0BE` 依赖state2及 `RVA00895570` 能取到有效记录；清理后自然隐藏。

最小修复方向（`ClientStatic / High`）：在领域层确认仍Preparing、now<ReadyUntil、本人仍有重入许可的主动暂离路径，严格在同一连接中发送 `03CF(op=1) → fresh 03D4`。后者保持原绝对开场时刻、正确配偶编号、原频道/area及最新剩余额度，不重新扣费或续时。

`03D4` handler通过 `RVA008954C0` 重建婚礼记录；它只查询并刷新已存在的派送UI 0xAE，没有从此路径创建已关闭窗口。不能简单省略03CF，否则真实装扮恢复仍可能错误。Completed、Cancelled或已过开场时刻的终结离场不得补03D4，客户端的开场时间+60容忍检查也不能用于延长服务端重入权限。

## 商店与礼炮：不能只修购买

`CurrentImplementation / Confirmed`：`PvfNpcShopDefinitionSource.cs:124-127`对商品表中任何≤0整数拒绝整店。shop1208有10个`-1`空位，直接解释本次ShopUnavailable。应只识别精确`-1`为空位，保留对0、≤-2、非整数及未知售卖条件的拒绝。扫描发现15个商店含非正项，其中1206另有未闭合的`-2`，不能一概删除所有负数。

教堂内NPC20014的自身role明确声明shop1208，商品表canonical owner为20013；现解析器要求`[NPC]==请求NPC`，修空格后它仍失败。可通过“请求NPC与canonical owner各自明确声明同一shop”认定合法共享店。若涉及限购，alias必须共用同一计数主体，不能把配额拆成两份。现有NPC位置授权边界是另一个通用问题，不是本次购买失败原因，不在此凭空放宽。

两种礼炮10002625/10002626均价1000、shared effect1。通用购买/背包路径存在；当前002C规则能消费匹配物品，但不能据此断言原生会发002C。C2S CMD00FB（共享效果物品）的6B构包已静态识别为u32+u16，成功ACK还读u16+u8；S2C NOTI00DB至少读u16+u32+u8。对应现服decoder/handler及broadcast未接入，实际扣量责任与广播字段仍需补查，避免002C和00FB各扣一次。

独立ClientHack的0170只修晶之契约CMD0012 operation3扣料，0200只覆盖称号簿/婚礼邀请超时，不负责礼炮共享效果。两种礼炮PVF未给出只有婚礼进行中使用的限制，不应凭主题随意新增此权限。

建议将后续修复验收扩展到：广场/教堂共用商店正常买入；一次使用只扣一次并正确冷却；本人及同场另一客户端都看到动画；不向其它房间错误广播。共享效果布局可以继续静态补证及小步实机验证，不能假装已取得当前尚未实现的S2C真实样本。

## 本轮日志能证明的范围

本地日志 `artifacts/marriage-flow-20261016/runtime.stdout.log` 记录：

- 22:07:59及22:15:31，角色8从2:12教堂移至2:2广场，前后有03CF发送元数据。
- 商店请求npc20013、shop1208、10002625/10002626/10002616被拒绝为ShopUnavailable。
- 两次预约最终均Reached Cancelled，未验证成功仪式。
- 多次收到未处理的03FA（宾客祝词读取）请求。这是另一个已观察的缺口，不能自动归因于只有“恭喜”的提示窗。

上述为服务端消息类型、顺序和业务结果记录，没有本次完整正文级抓包。只有用户直接观察的症状属于该范围ClientVerified；修复建议尚未实施或验收。

## 原始证据与协作分工

- 原生A21 EXE SHA256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 内存镜像SHA256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，路径见Opus prompt，ImageBase00400000。
- 服务端PVF SHA256：`91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4`。
- 关闭/重入子代理完整报告：[本地证据报告](../artifacts/wedding-investigation-20261016/close-reentry/report.md)。artifacts内容为本地取证材料，不随Git提交。
- 商店/礼炮子代理完整报告：[本地证据报告](../artifacts/wedding-investigation-20261016/shop-effects/report.md)。
- 原生开场入口子代理完整报告：[本地证据报告](../artifacts/wedding-investigation-20261016/start-native/report.md)。
