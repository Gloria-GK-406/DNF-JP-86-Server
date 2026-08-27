# A21 教学第二间房:四个 actor 都自称第 0 个(2026-08-27)

**症状(玩家报告)。** 新手副本第二间房分上下半场。**上半场本该有两只怪,打死才爆牛皮护肩;
实测上半场没有怪,护肩已经在地上了。** 玩家用同一个客户端连上游跑过一遍,上游的表现是对的。

## 一、上游怎么做的

`Server/DfoServer/Network/Builders/Dungeon/DungeonNotificationBuilder.cs`,`BuildStartMap`
的 actor 循环:

```csharp
bool isApc = monster.Type >= 5;
var packetIndex = monster.PacketIndex.HasValue
    ? monster.PacketIndex.Value
    : (isApc ? apcIndex++ : normalIndex++);

writer.WriteUInt16(monster.TemplateOrder);
writer.WriteInt32(packetIndex);
writer.WriteUInt16((ushort)(firstMonsterSequence + i));
...
```

**两个独立计数器**,以 `Type >= 5` 分界。房间的种子与序号基数也是每间房各取一次
(`DungeonMapHandler.cs:405`):

```csharp
var roomSeed = (uint)(ServerRandom.Next() & ~0x40000);
var firstActorSequenceId = (ushort)ServerRandom.Next(1, 60001);
```

## 二、上游发的是什么

2026-08-27 07:59–08:13,上游(pid 20136,7001/10011)跑完整套鬼剑士教学,
`packet_log.txt` 四帧 `NOTI 0x001D`:

| 行 | 格子 | 地图 | actor | 掉落 |
|---:|---|---:|---|---|
| 254 | (0,1) | 61000 | 61670/l1/t0 · 461/l80/t5 | — |
| 328 | (1,1) | 61001 | 61670 · 61359 · 61670 · 61359(全 l1/t0) | idx0 seq1 item14400 耐久25 |
| 430 | (2,1) | 61002 | 61356 ×5 | — |
| 530 | (2,0) | 61005 | 62527 · 61357 · 61360 · 61361 | — |

四间房的种子分别是 `0x4d02fd48` / `0x30b22343` / `0x7008d819` / `0x6119dc14` ——
**每间房一个**。

## 三、档案里的「上下半场」

`map/Tutorial/tutorial(1,0)_normal.map`(即地图 61001):

```text
[monster]
61670 1 0 517 426 0 1 1 "[fixed]" "[normal]"     ← y=426 下半场
61359 1 0 518 222 0 1 1 "[fixed]" "[normal]"     ← y=222 上半场
61670 1 0 608 426 0 1 1 "[fixed]" "[normal]"     ← y=426 下半场
61359 1 0 654 229 0 1 1 "[fixed]" "[normal]"     ← y=229 上半场
[/monster]
[special passive object]
37183 736 219 0 1 "[item]" 0 1 -1 -1 -1          ← y=219 箱子也在上半场
[/special passive object]
```

玩家说的「上半场两只怪」就是那两只 **61359**,箱子和它们在一起。

## 四、本仓读到的内容是对的,发出去的字节不对

用本仓的 `PvfDungeonRoomActorSource` / `PvfDungeonRoomDropSource` 读同一份档案
(`DNFClient-A21-protocol/Script.pvf`,副本 10000):

```text
map 61000: actors=2 [61670/l1/t0, 461/l80/t5]                       drops=0
map 61001: actors=4 [61670/l1/t0, 61359/l1/t0, 61670/l1/t0, 61359/l1/t0]
                                                                     drops=1 [idx0/item14400/dur25]
map 61002: actors=5 [61356 ×5]                                       drops=0
map 61005: actors=4 [62527, 61357, 61360, 61361]                     drops=0
```

**与上游逐条相同。** 把这份内容喂给本仓的 `EncodeStartMap`,替换掉两侧各自取的种子、序号
基数与实例值,再与上游第 328 行那 126 字节逐字节比:

```text
diff @ 42: 上游=01  本仓=00
diff @ 63: 上游=02  本仓=00
diff @ 84: 上游=03  本仓=00
```

**只差三个字节**,正是第 1、2、3 条 actor 的包内索引。本仓每条都写 0。

## 五、为什么之前没暴露

第一间房 61000 是「一只怪 + 一个 AI 角色」——两者各是**自己那类的第 0 个**,写 0 恰好正确。
第二间房是第一次能把两种写法分开的房间。四条 actor 都声称自己是第 0 个,客户端只留下一条;
教学里「打完上半场再开箱」那一步没有可等的东西,于是直接推进到发物品。

这是本次会话第六次踩同一个坑:**参照会话在那条路上什么也没做,于是我们的错误答案与它逐字节
相同。** 前五次分别是空 actor 表、缺可破坏物掉落、复活重访、延迟的 `NOTI 0x001B`,以及比对
帧的形状而不是请求的内容。

## 六、改了什么

- `EncodeStartMap` 按 `ActorType >= 5` 分两路计数,写各自的位次。
- 房间种子改为由本局种子与房间格子推导(重访重发同一间房得同一个值),并照抄上游的
  `& ~0x40000` 掩码。**那一位对客户端意味着什么没有读出来**,只知道上游从不置位。
- `DungeonEntryCodecTests` 增加一条把上游第 328 行那 126 字节整帧钉住的回归测试。

## 七、顺带闭合:选图列表为空不是缺帧

同日玩家用同一客户端连上游,**选图界面同样一个副本都没有**。能选的副本挂在任务进度上,
这个账号还没推到解锁任何一张图。不再当协议缺口追;要让列表出东西,需要的是
`CMD 0x001F ACCEPT_QUEST` 之后的任务状态。

## 八、附:客户端 handler 注册表可以整表扫出来

追这条时给客户端(`DNF.unpacked-b1-debug.exe`)写了个注册点扫描:客户端用一段重复的
`6A 00 / 68 <handler> / 6A <type> / 8B Cx / E8 <register>` 逐个注册 handler,按这个模式全图扫
得到 **1265 条**,分成若干「场景」组(城镇、副本、选人界面各一组)。

`NOTI 0x001D` 在副本组里落到 `0x00D57B30`,与机器区记的入口一致。这张表对「全量模拟」有用:
它能直接回答「客户端到底为哪些 type 装了 handler」。扫描脚本没有入库,重写只要十几行。
