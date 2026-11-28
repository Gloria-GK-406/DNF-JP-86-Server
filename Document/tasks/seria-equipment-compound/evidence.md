# 赛利亚物品合成证据

## 客户端与版本

- `ClientStatic / High`。A21 b1 E5A34BBF 的赛利亚单底材窗口从 RVA `0x00666EB0` 进入确认构包。取源装备槽位（`0x12EF980`），选择模式（`0x00666FF4` 之后的布尔值），由底材分类助手 `0x00666930` 取得分类（1～15、未知哨兵 16），三个 `WriteU16`（`0x0234D310`）后发送 CMD `0x0197`。底材若是传承且稀有度为 2，客户端在费用分组设为 99。
- `ClientStatic / High`。同号 RVA `0x021300F6` 前从两个 UI 对象分别取得槽位，随后写两项 u16 参数，共八字节。界面与字段含义待证。单底材结算入口只接受六字节。
- `ClientStatic / High`。响应 `0x00665FF0` 成功读取 u16 结果槽并立即查主背包来展示。失败入口把错误字节交给通用提示。上游发送的成功 body 是 01 + 槽位，失败是 00 11。上游记录同槽换模板时需先清除旧槽再写新槽。
- `ContentStatic`。开发 PVF 的 `npc/seria.npc` 含 `[random option operate]`，另有 `ui/randomoptionregeneration/` 布局。日服 2015 年 [7 月 8 日公告](https://arad.nexon.co.jp/news/update_view.aspx?no=6361) 加入合成，2016 年 [1 月 27 日公告](https://arad.nexon.co.jp/news/update_view.aspx?no=6975) 确认旧传承可通过合成产出。
- `70Reference`。70 版符号考察见 [镇魂武器参考](../requiem-weapon/reference-survey.md)，含 `Dispatcher_Compound_ExtreamItem`，属于极限装备合成。70 时代的[原版装备合成器](https://df.nexon.com/community/news/magazine/120702) 使用两件底材和道具，版本与入口均有差异。其等级、配方和扣料规则不能用于此协议。

## 数据和规则

- `ContentStatic / Confirmed`。`etc/randomoption/regenerationrandomoption.etc` 在开发通道与 A21 参照 PVF 的探针输出相同。分类、排除模板、随机与定向材料及按级倍率分别有表。合成等级使用 `etc/randomoption/randomizedoptionoverall1.etc` 的 `[regen level limit]`。基础材料类型为 2 稀有、3 神器、6 传说、99 传承。
- `ContentStatic / Confirmed`。普通／高级／稀有／神器／传说灵魂 ID 分别为 `10100115/10100116/10099773/10099774/10099775`，五种由本服账号材料容器承接。无色小晶块 `3037`、上级元素结晶 `3167` 属普通材料。
- `UpstreamReference`。ServerS4A21 的 EquipmentRegeneration 按底材品质、等级、装备组、兑换封装类型和创建条件筛选结果，按等级权重抽候选档，再按装备权重抽一件。普通稀有允许传承，传承底材只出传承。定向的精细装备组与底材相同。
- `UpstreamReference`。上游保留底材的封装标志，结果由新实例生成，原有强化、附魔等打造随旧实例消耗。上游把普通传承候选的权重另外乘以 0.01。PVF 的 `[probability legacy]` 本体值为 1，二者是独立输入。实际日服的传承总概率缺服务端证据。
- `CurrentImplementation`。主干未接入 0197。分解已有灵魂产出，账号材料已有固定槽。既有材料成本规划和物品生命周期效果可以同时处理底材与多容器材料的事务，再用现有库存投影将已提交状态发给当前精确会话。

## 后续 A21 检查

检查随机与定向实际发包、各档材料窗口、成功结果槽、原槽状态、账号灵魂投影、失败提示以及重登数据。当前证据等级为静态和上游参考。
