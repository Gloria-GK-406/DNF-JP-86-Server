# 阶段2纯模型独立评审

2026-10-06。审查`simulator.mjs`与`scenarios.json`对上轮raw证据/coverage边界的遵守情况。不是原生等价测试；没有网络、部署、数据库或产品修改。本次Node纯模型检查9项通过、2项失败，失败用于具体复现下列缺陷，不应删掉断言掩盖差异。

## 必要修复

1. **P2：投注包预览与实际请求存在不同默认值。** `simulator.mjs:121`的`confirmBet`缺knight时使用当前`selectedKnight`，但`:190`的`payloadPreview(confirmBet)`默认knight0。复现：`selectedKnight=3`，payload只有slot7和amount10；派生request.knight=3，预览14B之后的knight却是00。HTML从未解析UI输入生成payload时会显示错误body。应使用同一个request resolver，或直接从`trace.requests`的已解析payload生成预览；不能为预览偷偷改玩家选择。

2. **P2：stateSnapshot允许不属于已描述扩展的值，模型state与合成字节不一致。** `simulator.mjs:73`没有验证snapshot value是整数0..5。启用扩展、value128时模型保存managerState128，而preview的byte是80；这不再是模型所称的“raw80..85解除高位恢复0..5”。缺失值亦静默默认0。applyEvent与payloadPreview应共用快照值验证，拒绝缺失、非整数、范围外输入。普通`state`输入若允许0..255 raw byte，也需要统一signed i8解码（例如128在原生保存-128而当前模型保存128）；否则明确限制为模型已解析的0..5，不模拟范围外字节。

## 已通过的有界检查

- `queryInfo/queryHistory`与成功ACK不会改serverBetTotal/serverInventory/settlementCount/mailCount。
- 04E6成功ACK仅ensure投注对象，不显示、不赋state、不自动产生04E8。
- snapshot1禁用插件时manager=-127且不走scene1；启用配套扩展时恢复state1且无B8直接公告。
- 重复stage3两次进入scene12产生两条冠军公告，没有模型去重。
- scene5与8的localAdvance不自动进入6或9。
- inventoryProjection仅更新客户端库存版本；其后ackBet才读取该版本，不扣除serverInventory。
- eventList取消093D只改活动存在门；模型不擅自reset manager/scene或关闭已显示窗口。

## HTML必须保留的抽象说明

`initialState()`默认state0不能介绍成原生构造/登录保证。场景文件顶部`resetApplied=true`是明确模拟前提：假定一次reset已经发生、service45存在、网络ready；HTML应直接显示这项前提，不能仅藏在JSON。构造本身未赋state、登录reset时点未知。

`openUI/openBetUI`选取已识别显示初始化分支，重复打开每次执行show、自动04E8是模型选择；受保护通用open、重开回调和网络/level条件不由模型完整实现。ensure与show必须保持分别可见；打开动作本身不要重放当前scene去生成额外查询或冠军公告。

`eventList.enabled`在模型表示093D map项存在/消失，**不是承诺只改真实006C的enabled字节就等价**。完整字符串、条件tail、其他UI刷新、关闭行为均未模拟。

`canBet`中的infoReady/库存>0等属于页内示例规则；客户端state!=1禁用有静态证据，但模型没有重建所有PVF材料上限、网络、等级和物品条件。初始库存100、level20/要求17、材料槽等是fixture前提。

`localAdvance`是动画完成/轨迹耗尽的一步抽象，默认隐藏UI暂停是Unknown的模型选择；T+编号不是秒，9100不应在页面升级为固定实机总时长。scene12从fixture第7场winner形成文本，只适用于该页合法0..7骑士与已解码结果；未重建真实winner flag、轨迹对象和所有无效数据行为。

DOMAIN下注、扣物品、结算与邮件数是显式业务示例；与读包函数没有自动调用关系。查询投影可以更新UI但不等于领域写入；库存ACK读取当前版本不证明原服ACK/库存通知的相对顺序。

首14B仍为wire Unknown，占位预览不是有效请求。B8高位方案只能标CompatibilityExtension；其抑制B8直接公告并不抑制scene12、BB或scene0/1已显示查询。

## 复现

`node coverage-model-review-test.mjs`。该脚本只导入纯model，不发网络、不部署。修复后的复核应保留上述反例，并检验既有9项不会回归。时间线阶段/scene映射由时间线代理独立评审，本报告不替代其结果。

## 主代理修复与复核

两处P2已修复：包预览接收before状态，继承当前selectedKnight；stateSnapshot在模型与预览共用整数0..5校验。普通raw state128..255按signed i8读取。独立反例脚本调用新的预览context接口后，11项全部通过；完整模型验证367个断言通过。

另外修正Stage0消费集合：保留raw七槽用于展示，但第七槽不进入已消费赛果；追加回归证明它不会被state5链伪造成冠军。

通用缺093D的12BC0争议已查原始分支：VA025ABDDC跳025ABE0F，VA025ABE11压入12BC0，VA025ABE25压UI209后打开。此ID保留Confirmed调用证据，正文及实际显示成功仍未知，未由manager state0分支类推。
