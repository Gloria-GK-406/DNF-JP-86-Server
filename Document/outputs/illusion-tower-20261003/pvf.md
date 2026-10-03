# Current PVF: illusion tower

| Floor | Map | Ordinary monster rows | Fixed APC rows | Random APC points |
|---:|---:|---:|---:|---:|
| 1 | 31001 | 3 | 0 | absent |
| 2 | 31002 | 7 | 0 | absent |
| 3 | 31003 | 0 | 0 | 100 |
| 4 | 31004 | 4 | 0 | absent |
| 5 | 31005 | 0 | 1 | absent |
| 6 | 31007 | 5 | 0 | absent |
| 7 | 31008 | 7 | 0 | absent |
| 8 | 31010 | 0 | 1 | absent |
| 9 | 31011 | 0 | 0 | 100 |
| 10 | 31006 | 6 | 0 | absent |
| 11 | 31019 | 8 | 0 | absent |
| 12 | 31015 | 1 | 1 | absent |
| 13 | 31020 | 2 | 1 | absent |
| 14 | 31024 | 2 | 1 | absent |
| 15 | 31025 | 9 | 1 | absent |

```text
=== dungeon/Towers/TowerOfIllusion.dgn ===
[background pos]
80
[cutscene image]
Map/CutScene/towerofdeath.img 0
[worldmap pattern info]
1 6 WorldMap/SelectdungeonSlot/Tower.img 1
[dungeon type]
tower of death
[minimum required level]
40
[basis level]
40
[required item]
4183 1 1
[/required item]
[recommended level]
40 40
[ai character appear rate]
100
[name]
迷妄之塔
[explain]
    穷尽一生追逐浮华的名利幻影， 到人生的最后一刻才明白这一切不过是虚无， 这就是人生给予每个生命最珍贵的礼物！ 那些在尘世中感到彷徨无助的人啊， 我已将你们心里的诸般迷妄在此地重现， 你们再也不用忍受欲望的煎熬， 直接来这里吧， 来取走你们人生中最珍贵的礼物吧！
[tower max clear item num]
4
[death tower map indexes]
15 1 31001 2 31002 3 31003 4 31004 5 31005 6 31007 7 31008 8 31010 9 31011 10 31006 11 31019 12 31015 13 31020 14 31024 15 31025
[/death tower map indexes]
[tower high skill initial cool time]
0 1
[tower high skill initial cool time rate]
0 1
[tower fp cubepiece]
0
[tower limit of stackable item]
0
[tower recovery]
0 10 100
[/tower recovery]
[tower recovery]
11 15 50
[/tower recovery]
[tower item drop]
0
[tower talk]
[floor]
1
[on start floor]
npc 1001 0 我是死神， 在绝望中寻求新生的人啊， 欢迎你来到迷妄之塔！ 5000
[/on start floor]
[on end floor]
npc 1001 0 时间就是生命， 不要浪费它。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
2
[on start floor]
npc 1001 0 怕了吗？ 死亡游戏才刚刚开始呢！ 5000
[/on start floor]
[on end floor]
npc 1001 0 我的欢迎仪式结束了， 你可以奔赴死亡的邀请了。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
3
[on start floor]
npc 1001 0 我要掷色子了…… 5000
[/on start floor]
[on end floor]
npc 1001 0 看来我这次掷的点数有点小啊。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
4
[on start floor]
npc 1001 0 灵活的脚步是战斗的基础， 我会在背后看着你的…… 5000
[/on start floor]
[on end floor]
npc 1001 0 你的步伐有点凌乱， 以后要多锻炼， 继续往上走吧。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
5
[on start floor]
npc 1001 0 从现在开始， 要玩命了， 你有心理准备了吗？ 5000
[/on start floor]
[on end floor]
npc 1001 0 看来我之前小看你了。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
6
[on start floor]
npc 1001 0 这是专门为你准备的……地狱之行！ 你可以开始了！ 5000
[/on start floor]
[on end floor]
npc 1001 0 哼哼……别以为亡者们只有这点力量。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
7
[on start floor]
npc 1001 0 战前逃跑会被人唾弃的， 希望你能坚持到最后一秒。 5000
[/on start floor]
[on end floor]
npc 1001 0 骄傲自满必然会带来失败， 这是无数人用自身实践证明的。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
8
[on start floor]
npc 1001 0 你现在就去会一会从天界来的亡灵们吧。 5000
[/on start floor]
[on end floor]
npc 1001 0 嗯， 表现马马虎虎， 上去吧。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
9
[on start floor]
npc 1001 0 凭你那慢吞吞的身法， 不用猜都知道你一定会吃亏的。 5000
[/on start floor]
[on end floor]
npc 1001 0 不管你用什么方法， 一定要找到让我开心的东西。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
10
[on start floor]
npc 1001 0 忠告你一句， 惹谁都不要惹那个家伙！ 5000
[/on start floor]
[on end floor]
npc 1001 0 绝望的乌云已经开始笼罩着你…… 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
11
[on start floor]
npc 1001 0 如果你在这里倒下， 就算以后成功了也还是失败， 希望你不是这样的人。 5000
[/on start floor]
[on end floor]
npc 1001 0 来者是客， 我好像怠慢你了， 嘿嘿。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
12
[on start floor]
npc 1001 0 我对你充满期待， 尽快让我开心起来吧。 5000
[/on start floor]
[on end floor]
npc 1001 0 你好像没听清我的话， 我再给你一次机会。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
13
[on start floor]
npc 1001 0 欢迎来到我的迷妄庭院！ 5000
[/on start floor]
[on end floor]
npc 1001 0 这……我的庭院变得一团糟了？！ 嘿嘿， 算算也值了。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
14
[on start floor]
npc 1001 0 那些滑腻腻的蜘蛛丝会慢慢的把你捆成人肉粽子。 5000
[/on start floor]
[on end floor]
npc 1001 0 我今天很高兴， 我已经很久没遇到能通过这里的人了。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
15
[on start floor]
apc 5602 0 无论你从哪个角度攻击， 我都能100%防御。
放马过来吧！ 5000
[/on start floor]
[on end floor]
npc 1001 0 原本以为你会输得很惨， 没想到你竟然破了绝对防御， 了不起！ 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
16
[on start floor]
npc 1001 0 这房间里到处都是爆弹和地雷， 稍有差错便会一命呜呼， 祝你好运啊。 5000
[/on start floor]
[on end floor]
npc 1001 0 我正在找色子， 你可以趁机休息一下， 我不会说你的。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
17
[on start floor]
npc 1001 0 如果你觉得恐惧已经让你无法前进， 我不介意你现在放弃。 5000
[/on start floor]
[on end floor]
npc 1001 0 我就当这是你的回答， 继续往上走吧。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
18
[on start floor]
npc 1001 0 如果你现在想回头， 我可以很负责任的说： 晚了！ 5000
[/on start floor]
[on end floor]
npc 1001 0 难道你从来不知道什么是恐惧？ 哈哈， 真是有趣的家伙啊。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
19
[on start floor]
npc 1001 0 还没开战就先示弱了三分， 你想不输也难。 5000
[/on start floor]
[on end floor]
npc 1001 0 嘿嘿， 你发现了吗？ 事情在不知不觉中变得有趣了。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
20
[on start floor]
npc 1001 0 我这里不欢迎没有战斗力的人， 你要好好把握这次机会。 5000
[/on start floor]
[on end floor]
npc 1001 0 嗯， 表现马马虎虎， 上去吧。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
21
[on start floor]
npc 1001 0 躲避或者反击？ 这是你将面临的问题， 但实际上并不是你想的那么简单。 5000
[/on start floor]
[on end floor]
npc 1001 0 笛子的声音又响起了， 你好自为之吧。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
22
[on start floor]
npc 1001 0 新一轮的战斗开始了， 祝你好运。 5000
[/on start floor]
[on end floor]
npc 1001 0 看来你是越战越勇了， 上去吧！ 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
23
[on start floor]
npc 1001 0 虽然我手气一直不好， 但这次的掷点却是个大数。 5000
[/on start floor]
[on end floor]
npc 1001 0 哼哼， 我会为你在此塔中留一个位置， 随时欢迎你再来。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
24
[on start floor]
npc 1001 0 在这里， 你会发现喘口气的时间都是宝贵的！ 5000
[/on start floor]
[on end floor]
npc 1001 0 有个人已经等你很久了， 快上去吧。 5000
[/on end floor]
[/tower talk]
[tower talk]
[floor]
25
[on start floor]
apc 608 0 现身吧， 我忠实的部下们！ 该是向客人展示九龙威力的时候了！ 5000
[/on start floor]
[on end floor]
npc 1001 0 你很强……不过你以后会遇到更艰难的挑战， 你有兴趣挑战死亡之塔吗？ 5000
[/on end floor]
[/tower talk]
[tower dialog]
1  <apc::-1>我是死神， 在绝望中寻求新生的人啊， 欢迎你来到迷妄之塔！ on start
[tower dialog]
1  <apc::-1>时间就是生命， 不要浪费它。 on complete
[tower dialog]
2  <apc::-1>怕了吗？ 死亡游戏才刚刚开始呢！ on start
[tower dialog]
2  <apc::-1>我的欢迎仪式结束了， 你可以奔赴死亡的邀请了。 on complete
[tower dialog]
3  <apc::-1>我要掷色子了…… on start
[tower dialog]
3  <apc::-1>看来我这次掷的点数有点小啊。 on complete
[tower dialog]
4  <apc::-1>灵活的脚步是战斗的基础， 我会在背后看着你的…… on start
[tower dialog]
4  <apc::-1>你的步伐有点凌乱， 以后要多锻炼， 继续往上走吧。 on complete
[tower dialog]
5  <apc::-1>从现在开始， 要玩命了， 你有心理准备了吗？ on start
[tower dialog]
5  <apc::-1>看来我之前小看你了。 on complete
[tower dialog]
6  <apc::-1>这是专门为你准备的……地狱之行！ 你可以开始了！ on start
[tower dialog]
6  <apc::-1>哼哼……别以为亡者们只有这点力量。 on complete
[tower dialog]
7  <apc::-1>战前逃跑会被人唾弃的， 希望你能坚持到最后一秒。 on start
[tower dialog]
7  <apc::-1>骄傲自满必然会带来失败， 这是无数人用自身实践证明的。 on complete
[tower dialog]
8  <apc::-1>忠告你一句， 惹谁都不要惹那个家伙！ on start
[tower dialog]
8 <apc::-1>绝望的乌云已经开始笼罩着你…… on complete
[tower dialog]
9  <apc::-1>如果你在这里倒下， 就算以后成功了也还是失败， 希望你不是这样的人。 on start
[tower dialog]
9  <apc::-1>来者是客， 我好像怠慢你了， 嘿嘿。 on complete
[tower dialog]
10  <apc::-1>凭你那慢吞吞的身法， 不用猜都知道你一定会吃亏的。 on start
[tower dialog]
10  <apc::-1>不管你用什么方法， 一定要找到让我开心的东西。 on complete
[tower dialog]
11  <apc::-1>还没开战就先示弱了三分， 你想不输也难。 on start
[tower dialog]
11  <apc::-1>嘿嘿， 你发现了吗？ 事情在不知不觉中变得有趣了。 on complete
[tower dialog]
12  <apc::5602>无论你从哪个角度攻击， 我都能100%防御。
放马过来吧！ on start
[tower dialog]
12  <apc::-1>原本以为你会输得很惨， 没想到你竟然破了绝对防御， 了不起！ on complete
[tower dialog]
13  <apc::-1>我这里不欢迎没有战斗力的人， 你要好好把握这次机会。 on start
[tower dialog]
13  <apc::-1>嗯， 做得好。 上去吧。 on complete
[tower dialog]
14  <apc::-1>在这里， 你会发现喘口气的时间都是宝贵的！ on start
[tower dialog]
14  <apc::-1>有个人已经等你很久了， 快上去吧。 on complete
[tower dialog]
15  <apc::608>现身吧， 我忠实的部下们！ 该是向客人展示九龙威力的时候了！ on start
[tower dialog]
15  <apc::-1>你很强……不过你以后会遇到更艰难的挑战， 你有兴趣挑战死亡之塔吗？ on complete
[revision table]
Dungeon/Towers/table/TowerOfIllusion.tbl
[recommend equipment]
0 2 0 0 0 0 0 0 0 0
[/recommend equipment]
[recommend party]
> 1 > 1 > 1 > 1 > 1
[/recommend party]
```

## etc/deathtower.etc

```text
=== etc/deathtower.etc ===
[reward item prob]
50 49 100
[reward gold weight]
11
[reward exp weight]
1.987 2.03 2.078 2.136 2.202 2.276 2.356 2.443 2.535 2.633 2.736 2.844 2.957 3.074 3.196 3.321 3.451 3.585 3.723 3.864 4.009 4.158 4.31 4.465 4.624 4.786 4.951 5.119 5.291 5.465 5.642 5.823 6.006 6.192 6.381 6.572 6.766 6.963 7.163 7.365 7.57 7.777 7.986 8.199 8.413 8.63 8.85 9.071 9.296 9.522 9.751 9.982 10.215 10.45 10.688 10.928 11.17 11.414 11.66 11.909 12.159 12.412 12.666 12.923 13.182 13.442 13.705 13.97 14.236 14.505 14.775 15.048 15.322 15.598 15.877 16.157 16.438 16.722 17.008 17.295 17.584 17.875 18.168 18.462 18.758 19.056 19.356 19.658 19.961 20.266 20.572 20.881 21.191 21.502 21.816 22.131 22.447 22.766 23.086
[/reward exp weight]
[illusion reward exp weight]
4.752 4.495 4.9703 5.1086 5.2668 5.4427 5.6347 5.8415 6.0624 6.2964 6.5428 6.8009 7.0704 7.3507 7.6414
[/illusion reward exp weight]
[reward card num]
1 1 1 1 1 1 1 1 1 1 2 2 2 2 2 2 2 3 3 3 3 3 4 4 4 4 5 5 5 5 6 6 6 7 7 7 8 8 9 9 9 10 10 11 11 12 12 13 13 14 14 15 15 16 16 17 18 18 19 19 20 21 21 22 23 24 24 25 26 26 27 28 29 30 30 31 32 33 34 35 36 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 52 53 54
[/reward card num]
[illusion reward card num]
1 1 1 1 1 2 2 2 2 2 3 3 3 4 4
[/illusion reward card num]
[apc exp weight]
3
[monster exp weight]
1
```

## etc/deathtowerclient.etc

```text
=== etc/deathtowerclient.etc ===
[dead tower slave hp equalization]
0.2
[dead tower skill damage equalization rate]
0.75
[dead tower high skill initial cool time rate]
1 1
[dead tower active status user to user damage rate]
0.5
[dead tower recovery]
1 5 20 0 30 30 30 60 25 60 90 20
[/dead tower recovery]
[dead tower recovery]
6 10 10 0 30 20 30 60 15 60 90 10
[/dead tower recovery]
[dead tower recovery]
11 40 0 0 30 9 30 60 6 60 90 3
[/dead tower recovery]
[dead tower recovery max stair]
40
[dead tower gunner hit delay rate]
0.7
[dead tower gunner stuck rate]
10
[dead tower weapon grade]
10
[dead tower armor grade]
0
[dead tower apc active property]
30 0 30 0 30 0 0 30 0 0 0 0 0 0 0 0 0
[dead tower apc stuck rate]
-40
```

## Entry item 4183

```text
id 4183 -> quest/invitation_drapyus.stk
=== stackable/quest/invitation_drapyus.stk ===
[name]
死神的邀请函
[explain]
相当于进入[死亡之塔]的入场券。
[flavor text]
只有接到死神德莱弗斯邀请函的人， 才拥有挑战死亡之塔的权利。
[grade]
1
[attach type]
[trade]
[rarity]
3
[usable job]
[all]
[/usable job]
[minimum level]
1
[icon]
Item/stackable/material.img 156
[stackable type]
[quest] 0
[move wav]
PAPER_TOUCH
```

## Current generic daily refill declarations

```text
[refill item]
2680738 2 2030-01-01 06:00:00 0 2680755 0 2030-01-01 06:00:00 0 2749110 3 2030-01-01 06:00:00 0 10002520 3 2030-01-01 06:00:00 0 10093393 3 2030-01-01 06:00:00 0 10003235 1 2030-01-01 06:00:00 0 10004114 0 2014-10-16 06:00:00 0 690001556 1 2030-01-01 06:00:00 1 10004573 0 2015-02-10 06:00:00 0 10004698 0 2015-03-12 06:00:00 0 10005005 0 2015-05-07 06:00:00 0 10002627 0 2030-01-01 06:00:00 0 10002628 0 2030-01-01 06:00:00 0 490002365 3 2016-08-11 06:00:00 0
```

## Floor 1, map 31001

```text
id 31001 -> IllusionTower/001F.map
[dungeon]
11001
[monster]
4 1 0 458 184 0 1 1 [fixed] [normal] 4 1 0 360 236 0 1 1 [fixed] [normal] 4 1 0 473 283 0 1 1 [fixed] [normal]
[NPC]
1001 [left] 447 150 0
[passive object]
5108 220 -60 0 5104 51 277 0 5105 846 277 0
```

## Floor 2, map 31002

```text
id 31002 -> IllusionTower/002F.map
[dungeon]
11001
[monster]
4 1 0 364 186 0 1 1 [fixed] [normal] 50004 1 0 425 187 0 1 1 [fixed] [normal] 4 1 0 488 203 0 1 1 [fixed] [normal] 50004 1 0 314 243 0 1 1 [fixed] [normal] 4 1 0 497 277 0 1 1 [fixed] [normal] 50004 1 0 432 283 0 1 1 [fixed] [normal] 4 1 0 371 316 0 1 1 [fixed] [normal]
[NPC]
1001 [left] 447 150 0
[passive object]
5108 220 -61 0 5104 51 277 0 5105 846 277 0
```

## Floor 3, map 31003

```text
id 31003 -> IllusionTower/003F.map
[dungeon]
11001
[apc random point]
100
[monster spawn pos]
2 350 280 25 450 240 25
[NPC]
1001 [left] 447 150 0
[passive object]
5108 220 -61 0 5104 51 277 0 5105 846 277 0
```

## Floor 4, map 31004

```text
id 31004 -> IllusionTower/004F.map
[dungeon]
11001
[monster]
12 1 0 777 181 0 1 1 [fixed] [normal] 12 1 0 288 200 0 1 1 [fixed] [normal] 12 1 0 789 298 0 1 1 [fixed] [normal] 12 1 0 280 303 0 1 1 [fixed] [normal]
[NPC]
1001 [left] 565 149 0
[passive object]
5108 220 -61 0 5105 1072 255 0 5104 51 277 0
```

## Floor 5, map 31005

```text
id 31005 -> IllusionTower/005F.map
[dungeon]
11001
[ai character]
15402 480 250 0 [monster] [normal] 0 0
[NPC]
1001 [left] 447 150 0
[passive object]
5108 220 -61 0 5104 51 277 0 5105 846 277 0
```

## Floor 6, map 31007

```text
id 31007 -> IllusionTower/007F_6F.map
[dungeon]
11001
[monster]
55001 1 0 677 200 0 1 1 [fixed] [normal] 55001 1 0 548 205 0 1 1 [fixed] [normal] 9 1 0 454 239 0 1 1 [fixed] [normal] 55001 1 0 678 275 0 1 1 [fixed] [normal] 55001 1 0 553 275 0 1 1 [fixed] [normal]
[NPC]
1001 [left] 447 158 0
[passive object]
5108 220 -61 0 5105 842 268 0 5104 51 277 0
```

## Floor 7, map 31008

```text
id 31008 -> IllusionTower/008F_7F.map
[dungeon]
11001
[monster]
700 1 0 633 195 0 1 1 [fixed] [normal] 701 1 0 454 214 0 1 1 [fixed] [normal] 700 1 0 636 257 0 1 1 [fixed] [normal] 701 1 0 721 258 0 1 1 [fixed] [normal] 700 1 0 548 259 0 1 1 [fixed] [normal] 701 1 0 458 294 0 1 1 [fixed] [normal] 700 1 0 640 310 0 1 1 [fixed] [normal]
[NPC]
1001 [left] 444 149 0
[passive object]
5108 220 -63 0 5105 847 263 0 5104 51 277 0
```

## Floor 8, map 31010

```text
id 31010 -> IllusionTower/010F_8F.map
[dungeon]
11001
[ai character]
20604 450 250 0 [monster] [normal] 0 0
[NPC]
1001 [left] 447 150 0
[passive object]
5108 220 -61 0 5104 51 277 0 5105 846 277 0
```

## Floor 9, map 31011

```text
id 31011 -> IllusionTower/011F_9F.map
[dungeon]
11001
[apc random point]
100
[monster spawn pos]
3 350 280 25 450 240 25 350 200 25
[NPC]
1001 [left] 447 150 0
[passive object]
5108 220 -61 0 5104 51 277 0 5105 846 277 0
```

## Floor 10, map 31006

```text
id 31006 -> IllusionTower/006F_10F.map
[dungeon]
11001
[monster]
55003 1 0 498 213 0 1 1 [fixed] [normal] 55003 1 0 595 216 0 1 1 [fixed] [normal] 50001 1 0 693 219 0 1 1 [fixed] [normal] 50001 1 0 706 280 0 1 1 [fixed] [normal] 55003 1 0 513 289 0 1 1 [fixed] [normal] 55003 1 0 607 292 0 1 1 [fixed] [normal]
[NPC]
1001 [left] 447 154 0
[passive object]
5108 218 -61 0 5105 844 264 0 5104 51 277 0
```

## Floor 11, map 31019

```text
id 31019 -> IllusionTower/019F_11F.map
[dungeon]
11001
[monster]
55028 1 0 712 184 0 1 1 [fixed] [normal] 55028 1 0 184 187 0 1 1 [fixed] [normal] 55028 1 0 183 219 0 1 1 [fixed] [normal] 55028 1 0 712 220 0 1 1 [fixed] [normal] 55028 1 0 183 255 0 1 1 [fixed] [normal] 55028 1 0 712 257 0 1 1 [fixed] [normal] 55028 1 0 181 297 0 1 1 [fixed] [normal] 55028 1 0 712 303 0 1 1 [fixed] [normal]
[NPC]
1001 [left] 447 142 0
[passive object]
5108 220 -61 0 258 76 176 0 258 802 179 0 5105 846 262 0 5104 51 277 0 258 819 328 0 258 81 332 0
```

## Floor 12, map 31015

```text
id 31015 -> IllusionTower/015F_12F.map
[dungeon]
11001
[monster]
50074 1 0 513 237 0 1 1 [fixed] [normal]
[ai character]
5602 480 250 0 [monster] [normal] 0 0
[NPC]
1001 [left] 450 150 0
[passive object]
5108 220 -61 0 5105 845 270 0 5104 51 277 0
```

## Floor 13, map 31020

```text
id 31020 -> IllusionTower/020F_13F.map
[dungeon]
11001
[monster]
50001 1 0 445 177 0 1 1 [fixed] [normal] 50001 1 0 451 311 0 1 1 [fixed] [normal]
[ai character]
10600 480 250 0 [monster] [normal] 0 0
[NPC]
1001 [left] 447 150 0
[passive object]
5108 220 -61 0 5104 51 277 0 5105 846 277 0
```

## Floor 14, map 31024

```text
id 31024 -> IllusionTower/024F_14F.map
[dungeon]
11001
[monster]
421 1 0 658 200 0 1 1 [fixed] [normal] 421 1 0 665 299 0 1 1 [fixed] [normal]
[ai character]
5601 572 243 0 [monster] [normal] 0 0
[NPC]
1001 [left] 434 152 0
[passive object]
5108 220 -61 0 5105 845 271 0 5104 51 277 0
```

## Floor 15, map 31025

```text
id 31025 -> IllusionTower/025F_15F.map
[dungeon]
11001
[monster]
60030 1 0 524 176 0 1 1 [fixed] [normal] 60030 1 0 680 180 0 1 1 [fixed] [normal] 60030 1 0 726 210 0 1 1 [fixed] [normal] 60030 1 0 482 215 0 1 1 [fixed] [normal] 60030 1 0 526 229 0 1 1 [fixed] [normal] 60030 1 0 491 266 0 1 1 [fixed] [normal] 60030 1 0 729 271 0 1 1 [fixed] [normal] 60030 1 0 690 307 0 1 1 [fixed] [normal] 60030 1 0 526 310 0 1 1 [fixed] [normal]
[ai character]
608 560 225 0 [monster] [normal] 0 0
[NPC]
1001 [left] 463 152 0
[passive object]
5108 220 -61 0 5105 844 275 0 5104 51 277 0
```

## Fixed APC 608

```text
id 608 -> swordman/sajin/sajin.aic
=== aicharacter/swordman/sajin/sajin.aic ===
[minimum info]
九猛龙刹秦 0 1 0 0 40 0 0 1 0 0 0 no creature 0 0 0
[additional character status]
0 0 0 0 0 0 0 0 10000 0 0 0 0 0 0 0 20 20 20 30 30 50 30 50 30 30 90 30 30 30 30 30 0 0 30 30 30
[/additional character status]
[quick skill]
9 1 38 1 8 1 68 1 72 1 67 1
[/quick skill]
[skill]
27 1 33 1 37 1 26 1 1 5 3 6 12 9 48 1 7 4 46 10 5 1 181 2 182 2 184 1 173 1 179 5 172 1 169 1 170 1 186 1
[/skill]
[quick item]
[/quick item]
[equipment]
27069 4 0 10871 1 0 14870 2 0 12871 4 0 18871 3 0 16870 2 0 20116 2 0 22110 2 0 24119 2 0 39109 2 0 39532 2 0 39861 2 0 41143 2 0 41829 2 0 40274 2 0 41503 2 0
[/equipment]
[think term]
400
[destination change term]
500
[keep distance with target]
0
[warlike]
0
[vision]
600
[attack damage rate]
0.15
[appearance point]
0
[far attack reaction rate]
100
[skill correction]
72 0.1
[/skill correction]
[targeting bonus]
50 100 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 [down] 0 0 [stun] 0
[ai pattern]
Event.ai DestinationSelect.ai Action.ai MoveMethod.ai
[/ai pattern]
[key stream]
bal a.key over s.key slash d.key charge f.key dragon g.key li h.key upper z.key step step.key x x.key li2 li2.key li4 li4.key auto auto.key random_dragon random_dragon.key
[/key stream]
[speech on situation]
[on stand]
呀啊！ 30
[on die]
我的龙…… 50
[on appear]
战斗吧！ 50
[on disappear]
逃到哪里去了？ 50
[on attack]
轰隆！ 70 4
[on attack]
轰隆隆！ 70 12
[on attack]
升龙！ 50 3
[on attack]
发！ 50 0
[on attack]
里！ 50 5
[/speech on situation]
```

## Fixed APC 5601

```text
id 5601 -> fighter/ivy/ivy.aic
=== aicharacter/fighter/ivy/ivy.aic ===
[minimum info]
邪毒希特尔 1 3 0 1 40 0 0 1 0 0 0 no creature 0 0 0
[additional character status]
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 10 10 30 10 30 50 150 50 30 30 60 30 30 30 30 30 30 0 30 30 30
[/additional character status]
[quick skill]
14 1 46 20 38 7 75 6 76 1 58 6
[/quick skill]
[skill]
32 1 33 1 45 6 2 1 74 1 17 5 5 1 181 3 182 5 184 1 172 1 179 5 73 1 169 1 170 1 190 1 186 7 178 5
[/skill]
[quick item]
[/quick item]
[equipment]
29690 4 0 10887 2 0 14878 3 0 12887 3 0 18884 2 0 16878 3 0 22069 1 0 24120 1 0 20117 1 0 43426 2 0 43805 2 0 44680 2 0 45080 2 0 45964 2 0 44304 2 0 46202 2 0
[/equipment]
[think term]
400
[destination change term]
500
[keep distance with target]
0
[warlike]
0
[vision]
500
[appearance point]
0
[attack damage rate]
0.2
[far attack reaction rate]
100
[skill correction]
[/skill correction]
[targeting bonus]
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 100 [down] 0 100 [bleeding] [poison] 0
[ai pattern]
Event.ai DestinationSelect.ai Action.ai MoveMethod.ai
[/ai pattern]
[key stream]
mount a.key hammer s.key poison d.key sting f.key mine g.key zilpung h.key stingx3 stingx3.key combo combo.key jumpx jumpx.key
[/key stream]
[speech on situation]
[on stand]
哈啊！ 30
[on damage]
哈！呃？ 10
[on damage]
呼~唔！ 20
[on die]
啊啊~~啊啊啊！！！ 50
[on die]
给我陪葬吧！ 50
[on appear]
咳！啊啊啊…… 100
[on disappear]
哈啊！哈啊！ 100
[on attack]
啊啊！啊啊啊……呃！ 70 0
[on attack]
呼啊！呼啊！ 35 2
[on attack]
咳呀！ 50 5
[on attack]
哈~喝呀！ 50 8
[/speech on situation]
```

## Fixed APC 5602

```text
id 5602 -> fighter/anastatia2/anastatia.aic
=== aicharacter/fighter/anastatia2/anastatia.aic ===
[minimum info]
不起者安娜斯塔 1 0 0 1 40 0 0 1 0 0 0 no creature 0 0 0
[additional character status]
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 10 10 10 30 30 50 150 50 30 30 60 30 30 30 30 30 30 0 30 30 30
[/additional character status]
[quick skill]
12 16 180 9 176 9 69 6 42 8 15 3
[/quick skill]
[skill]
46 1 35 1 29 1 7 10 8 10 5 1 181 3 182 3 184 2 172 1 179 5 187 1 190 1 169 1 170 1 188 3
[/skill]
[quick item]
[/quick item]
[equipment]
39370 4 0 12888 4 0 10888 4 0 14879 4 0 18114 4 0 16107 4 0 24103 4 0 20102 4 0 22101 4 0 43496 2 0 44652 2 0 45050 2 0 45828 2 0 44262 2 0 45411 2 0 46205 2 0
[/equipment]
[think term]
400
[destination change term]
500
[keep distance with target]
0
[warlike]
0
[vision]
500
[appearance point]
0
[far attack reaction rate]
100
[attack damage rate]
0.5
[skill correction]
[/skill correction]
[targeting bonus]
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 100 [down] 0 100 [bleeding] [poison] 0
[ai pattern]
Event.ai DestinationSelect.ai Action.ai MoveMethod.ai
[/ai pattern]
[key stream]
nentan a.key will s.key memory d.key illusion f.key guard g.key gal h.key combo combo.key jumpx jumpx.key
[/key stream]
[speech on situation]
[on stand]
呃，竟然打倒我了？ 30
[on damage]
呼呼，一点也不痛…… 10
[on damage]
没有感觉…… 20
[on die]
呜哇！ 50
[on die]
为了特里斯家族！ 50
[on appear]
喂！斯扎克！ 30
[on disappear]
这是我的宠物！ 30
[on attack]
特里斯！ 70 5
[on attack]
别躲了！ 25 0
[on attack]
看来这招不好用啊…… 100 4
[on attack]
被我的贵族面孔迷住了？ 25 7
[on attack]
哼，无知的家伙！ 25 6
[/speech on situation]
```

## Fixed APC 10600

```text
id 10600 -> gunner/hesus/hesus.aic
=== aicharacter/gunner/hesus/hesus.aic ===
[minimum info]
交叉火力赫伊斯 2 4 0 1 40 0 0 1 0 0 0 no creature 0 0 0
[additional character status]
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 20 20 20 20 30 50 30 50 30 30 90 30 30 30 30 30 0 0 30 30 30
[/additional character status]
[quick skill]
2 3 48 1 169 1
[/quick skill]
[skill]
4 1 47 1 38 1 49 3 181 2 184 1 185 1 179 5 61 1 170 1 186 7 178 7 190 1 52 5
[/skill]
[quick item]
[/quick item]
[equipment]
32286 4 0 10475 1 0 14474 2 0 12475 1 0 18474 3 0 16473 2 0 20106 2 0 22111 1 0 24121 1 0 47090 2 0 47533 2 0 47828 2 0 48616 2 0 49018 2 0 49806 2 0 48221 2 0 49448 2 0 50204 2 0
[/equipment]
[think term]
500
[destination change term]
500
[keep distance with target]
0
[warlike]
0
[vision]
600
[attack damage rate]
0.3
[appearance point]
0
[far attack reaction rate]
30
[skill correction]
48 0.7
[/skill correction]
[targeting bonus]
50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 [down] 0 100 [freeze] 0
[ai pattern]
Event.ai DestinationSelect.ai Action.ai MoveMethod.ai
[/ai pattern]
[key stream]
cold a.key cross s.key step d.key spike z.key sliding sliding.key xxstep xxstep.key jumpcannon jumpcannon.key
[/key stream]
[speech on situation]
[on stand]
嘿呀！喝！ 30
[on damage]
呃！呃…… 10
[on damage]
呼唔…… 20
[on die]
卡尔扎克啊…… 50
[on die]
我会回来的！ 50
[on appear]
OK！ 100
[on disappear]
你肯定会死在我的交叉火力下！ 100
[on attack]
交叉火力！ 70 1
[on attack]
哒哒哒噹！ 25 1
[on attack]
GOGO！ 50 6
[on attack]
还不够猥琐…… 50 6
[on attack]
让你结成冰块！ 30 0
[/speech on situation]
```

## Fixed APC 15402

```text
id 15402 -> magician/lie/lie.aic
=== aicharacter/magician/lie/lie.aic ===
[appearance point]
0
[minimum info]
萝耶 3 1 0 1 25 0 0 1 0 0 0 no creature 0 0 0
[additional character status]
0 0 0 0 0 0 0 0 300 0 0 0 0 0 0 0 10 10 10 10 30 50 50 50 30 30 50 30 30 30 30 30 30 0 30 30 30
[/additional character status]
[attack damage rate]
1
[add equipment status from level]
1
[custom skill level]
[/custom skill level]
[far attack reaction rate]
70
[quick skill]
11 5 12 5
[/quick skill]
[skill]
37 5 3 5 41 5 38 10 19 10 61 10 62 10 63 10 64 10 181 3 182 3 184 2 187 1 179 5 188 10 189 10 178 10 186 10
[/skill]
[quick item]
1085 100 8 568
[/quick item]
[equipment]
10083 1 0 14080 1 0 12082 1 0 18068 1 0 16067 1 0 20088 1 0 22086 1 0 24088 1 0 51126 2 0 51527 2 0 51868 2 0 52294 2 0 52750 2 0 53139 2 0 53503 2 0 53907 2 0
[/equipment]
[think term]
600
[destination change term]
800
[keep distance with target]
249
[warlike]
20
[vision]
1000
[targeting bonus]
99 50 0 0 0 0 0 0 0 0 0 0 0 0 60 0 0 0 0 0 0 [stand] [dash] 0 0 [slow] [confuse] 0
[ai pattern]
Event.ai DestinationSelect.ai Action.ai MoveMethod.ai
[/ai pattern]
[key stream]
 a.key  s.key  g.key  h.key  z.key  1.key  phase_shift.key  combo.key
[/key stream]
[speech on situation]
[on damage]
呀啊！！ 30
[on damage]
咳呃！！ 30
[on die]
呃！气死我了！ 100
[on attack]
魔法导弹！！ 30 0
[on attack]
接我一招！！ 80 1
[/speech on situation]
```

## Fixed APC 20604

```text
id 20604 -> priest/deius/deius.aic
=== aicharacter/priest/deius/deius.aic ===
[minimum info]
血手狄乌斯 4 0 0 1 15 0 0 1 0 0 0 no creature 0 0 0
[additional character status]
0 10000 0 0 0 0 0 0 0 0 0 100 0 0 0 0 10 10 30 10 30 50 30 50 30 30 60 30 30 30 30 30 30 0 30 30 30
[/additional character status]
[quick skill]
1 1 169 1 64 1 63 3 8 6 190 1
[/quick skill]
[skill]
51 1 181 1 182 1 184 1 179 5 173 1 188 7
[/skill]
[quick item]
[/quick item]
[equipment]
35323 4 0 10119 2 0 14104 4 0 12117 2 0 18099 2 0 16093 2 0 20053 3 0 22052 3 0 24060 3 0 55449 2 0 55817 2 0 56609 2 0 56637 2 0 57013 2 0 57831 2 0 56228 2 0 57427 2 0 58206 2 0
[/equipment]
[think term]
500
[destination change term]
1000
[keep distance with target]
200
[warlike]
0
[vision]
800
[appearance point]
0
[attack damage rate]
0.3
[far attack reaction rate]
100
[skill correction]
64 0.7
[/skill correction]
[targeting bonus]
50 100 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 [stand] [dash] 0 0 [slow] [confuse] 0
[ai pattern]
Event.ai DestinationSelect.ai Action.ai MoveMethod.ai
[/ai pattern]
[key stream]
 a.key  s.key fist d.key  f.key  g.key  h.key 1 1.key 2 2.key 3 3.key 4 4.key 5 5.key 6 6.key z z.key x x.key c c.key 10step 10step.key 50upper 50upper.key 90hold 90hold.key fist_or_xx fist_or_xx.key
[/key stream]
[speech on situation]
[on stand]
咳~咿呀！ 30
[on damage]
啊啊…… 10
[on damage]
咿呀！ 20
[on die]
咳~呵呃！ 50
[on die]
呃…… 50
[on appear]
嘿嘿~~嘿！ 50
[on disappear]
咳呃！ 50
[on attack]
邪拳盛宴！ 30 2
[on attack]
排山倒海的神力！ 30 2
[on attack]
龙飞虎袭！ 50 4
[on attack]
撕裂异空间的空斩打！ 50 0
[/speech on situation]
```
