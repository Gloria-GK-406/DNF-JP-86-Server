# A21 物品期限

## 存储语义

所有物品的到期事实只有一种读法，由 [ItemLifetime](../Server/src/Dfo.Server.Data/Inventory/ItemLifetime.cs) 唯一负责：

| 存储值 | 含义 |
|---|---|
| 0 | 永久 |
| 其他值 | 截止时刻（Unix 秒）；小于等于判定时刻即已到期，因此负值一律已到期 |

`int.MaxValue` 只是 2038-01-19 的普通截止时刻，不是永久标记：客户端按剩余秒数显示约 11 年，
早期把它当永久占位写入的物品同样按截止时刻处理，不做读取兼容。新物品只写 0 表示永久，
有限期限超出表示范围时封顶为 `int.MaxValue`。装扮的截止字段是 `AvatarDetail.ExpireDate`，
其余种类是核心 `ExpireTime`；按 `LegacyKind` 选择字段只在 `ItemLifetime.Of` 中出现一次。
调用方以 `item.Lifetime.IsPermanent`、`IsExpired(at)` 判断，不直接比较原始字段；
`ItemLifetimeSourceTests` 扫描生产源码守护这一点，名单内只剩租赁截止和人偶期限校正这类非到期判断。
SQL NULL 表示没有装扮事实，与期限无关。

## 线上编码

背包快照／增量、角色穿戴与装扮合成回执的剩余时间是 A21 线上编码，由 Protocol 层的 `ItemLifetimeOnTheWire.RemainingSeconds` 从 `ItemLifetime` 统一换算；`ItemLifetime` 本身只回答期限、永久与过期：

| 存储值 | 线上值 |
|---|---|
| 0 | 0，永久 |
| 截止时刻晚于投影时刻 | 剩余秒数 |
| 截止时刻小于等于投影时刻（含负值） | -1，已到期 |

不能把到期差值钳制为 0，否则会把已到期物品显示成永久。投影时刻必须显式传入，避免一批消息跨边界取值不一致。
物品到期清理的领域行为见 [物品过期](ItemExpiration-20261014.md)，不能由显示标记推断已删除物品。

## 客户端依据

固定A21镜像身份同 [商城取证](../protocol/patterns/data/traces/a21-cera-feedback-20260909.md)，以下为RVA。客户端资源dstr.dat SHA-256为 `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`，FILE/102资源以SC01链式XOR解密后：1059为“剩余时间 : 无期限”，3030为“使用期限已到， 下次登录时将会消失”。

- `0126E840..0126E850` 读取物品+FD0后执行test eax,eax / sete al，只有零返回true；tooltip `020232F9..02023307` 据此选择资源1059。
- `0126E860..0126E870` 对同一值执行test / sets al，负数返回true；tooltip `0202332F..0202333D` 据此选择资源3030。
- 因而线上剩余时间为：0永久、正数剩余秒数、负数已到期（本实现发送-1）。置信度 Confirmed（读取与显示指令）；该证据不等同于当前部署的实机验收。

## 验证边界

回归需覆盖永久 0、负值、`int.MaxValue`、已过期、刚好到期、剩余 1 秒与正常有限期限，
以及背包、支援角色、角色穿戴的实际帧字段。静态指令证据、自动回归和当前部署实机显示分别报告。
