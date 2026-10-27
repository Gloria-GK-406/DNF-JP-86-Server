
## dungeon/event/circus/circus.dgn

[prohibit practice]
1
[background pos]
80
[name]
疯狂马戏团
[explain]
    马戏团的秘密武器狮子王正在悠闲地喝着下午茶等待着冒险家， 你有勇气挑战它吗？ 若灵活使用特殊投掷物品， 则可更顺利地通关。
    每个角色每天仅限进入3次。 通关地下城时， 有一定概率会出现凌凌的天幕剧场。 在凌凌的天幕剧场可获得的马戏团金币及团员印章奖励为普通疯狂马戏团地下城的2倍左右。
[revision table]
Dungeon/Towers/table/TowerOfDeath.tbl
[designate dungeon difficulty]
0
[entering title]
Title/pirateonthetrain.ani
[cutscene image]
Map/CutScene/circus.img 0
[minimap image]
Map/MiniMap/Act2.img
[worldmap pattern info]
1 3 WorldMap/SelectdungeonSlot/CircusDungeon.img 3
[mob level charac level replace flag]
1
[schedule]
2226
[/schedule]
[minimum required level]
60
[basis level]
65
[recommended level]
60 86
[limit party count]
4
[experience increasing point]
35
[champion]
3 6 9 12 9
[pathgate object]
54373 54372 54371 54370 54369 54368 54367 54366 54365 54364
[maze info]
[size]
8 1
[greed]
BBFFFFFFFFFFFFEE
[map specification]
map 0 0 25458 map 1 0 25459 map 2 0 25508 map 3 0 25494 25496 25497 map 4 0 25495 map 5 0 25492 map 6 0 25491 boss 7 0 25500 25501 25502
[/map specification]
[start map]
0 0
[/start map]
[boss map]
7 0
[/boss map]
[hell dungeon]
1
[seal door map index]
60050
[seal door pos]
8 0
[no fatigue]
[enter without fatigue]
1
[prohibit practice]
1
[hit count]
50 50 50 50 50
[seal door appear rate]
10
[special passive object item]
[/special passive object item]
[linked dungeon]
[on clear add passive object]
54645 543 125
[next]
4114 50 -1 -1 50 -1
[/next]
[/linked dungeon]
[recommend party]
> 1 > 2 > 2 > 3 > 4
[/recommend party]

## dungeon/event/circus/circus_monkey.dgn

[prohibit practice]
1
[background pos]
80
[name]
疯狂马戏团入门-追猴子
[explain]
   喜欢放炸弹的猴子挡着你的去路， 请追上并消灭它！ 下个房间可能有一个巨大的猩猩等待着你……
    该地下城是消耗疲劳的地下城， 同时无法获得[马戏团硬币]。
[revision table]
Dungeon/Towers/table/TowerOfDeath.tbl
[designate dungeon difficulty]
0
[entering title]
Title/pirateonthetrain.ani
[cutscene image]
Map/CutScene/circus.img 0
[minimap image]
Map/MiniMap/Act2.img
[worldmap pattern info]
1 3 WorldMap/SelectdungeonSlot/CircusDungeon.img 0
[schedule]
2226
[/schedule]
[minimum required level]
60
[basis level]
60
[recommended level]
60 85
[limit party count]
1
[experience increasing point]
1.3
[champion]
3 6 9 12 9
[pathgate object]
54373 54372 54371 54370 54369 54368 54367 54366 54365 54364
[maze info]
[size]
3 1
[greed]
BBFFEE
[map specification]
map 0 0 25475 map 1 0 25476 boss 2 0 25477
[/map specification]
[start map]
0 0
[/start map]
[boss map]
2 0
[/boss map]
[hell dungeon]
1
[seal door map index]
60050
[seal door pos]
8 0
[prohibit practice]
1
[hit count]
50 50 50 50 50
[seal door appear rate]
10
[special passive object item]
[/special passive object item]

## dungeon/event/circus/circus_ball.dgn

[prohibit practice]
1
[background pos]
80
[name]
疯狂马戏团入门-躲避球
[explain]
    请躲避前方滚来的障碍物向前移动。 最后会有超大橡木桶等待着冒险家。
    该地下城是消耗疲劳的地下城， 同时无法获得[马戏团硬币]。
[revision table]
Dungeon/Towers/table/TowerOfDeath.tbl
[designate dungeon difficulty]
0
[entering title]
Title/pirateonthetrain.ani
[cutscene image]
Map/CutScene/circus.img 0
[minimap image]
Map/MiniMap/Act2.img
[worldmap pattern info]
1 3 WorldMap/SelectdungeonSlot/CircusDungeon.img 2
[schedule]
2226
[/schedule]
[minimum required level]
60
[basis level]
60
[recommended level]
60 85
[limit party count]
1
[experience increasing point]
1.3
[champion]
3 6 9 12 9
[pathgate object]
54373 54372 54371 54370 54369 54368 54367 54366 54365 54364
[maze info]
[size]
3 1
[greed]
BBFFEE
[map specification]
map 0 0 25472 map 1 0 25473 boss 2 0 25474
[/map specification]
[start map]
0 0
[/start map]
[boss map]
2 0
[/boss map]
[hell dungeon]
1
[seal door map index]
60050
[seal door pos]
8 0
[prohibit practice]
1
[hit count]
50 50 50 50 50
[seal door appear rate]
10
[special passive object item]
[/special passive object item]

## dungeon/event/circus/circus_pie.dgn

[prohibit practice]
1
[background pos]
80
[name]
疯狂马戏团入门-抓皮埃罗
[explain]
    将会随机出现很多藏着皮埃罗的箱子。 黑色的皮埃罗会反射伤害， 请多加小心。 注意， 皮埃罗队长请在第一时间消灭。
    该地下城是消耗疲劳的地下城， 同时无法获得[马戏团硬币]。
[revision table]
Dungeon/Towers/table/TowerOfDeath.tbl
[designate dungeon difficulty]
0
[entering title]
Title/pirateonthetrain.ani
[cutscene image]
Map/CutScene/circus.img 0
[minimap image]
Map/MiniMap/Act2.img
[worldmap pattern info]
1 3 WorldMap/SelectdungeonSlot/CircusDungeon.img 1
[schedule]
2226
[/schedule]
[minimum required level]
60
[basis level]
60
[recommended level]
60 85
[limit party count]
1
[experience increasing point]
1.3
[champion]
3 6 9 12 9
[pathgate object]
54373 54372 54371 54370 54369 54368 54367 54366 54365 54364
[maze info]
[size]
3 1
[greed]
BBFFEE
[map specification]
map 0 0 25469 map 1 0 25470 boss 2 0 25471
[/map specification]
[start map]
0 0
[/start map]
[boss map]
2 0
[/boss map]
[hell dungeon]
1
[seal door map index]
60050
[seal door pos]
8 0
[prohibit practice]
1
[hit count]
50 50 50 50 50
[seal door appear rate]
10
[special passive object item]
[/special passive object item]

## dungeon/event/circus/circus_new.dgn

[prohibit practice]
1
[background pos]
80
[name]
凌凌的天幕剧场
[revision table]
Dungeon/Towers/table/TowerOfDeath.tbl
[designate dungeon difficulty]
0
[cutscene image]
Map/CutScene/circus.img 1
[minimap image]
Map/MiniMap/Act2.img
[worldmap pattern info]
1 3 WorldMap/SelectdungeonSlot/CircusDungeon.img 2
[mob level charac level replace flag]
1
[minimum required level]
60
[basis level]
65
[recommended level]
60 86
[limit party count]
4
[experience increasing point]
15
[champion]
3 6 9 12 9
[pathgate object]
54373 54372 54371 54370 54369 54368 54367 54366 54365 54364
[maze info]
[size]
1 4
[greed]
II
 KK
 KK
 CC
[map specification]
map 0 3 25561 map 0 2 25562 map 0 1 25563 boss 0 0 25564
[/map specification]
[start map]
0 3
[/start map]
[boss map]
0 0
[/boss map]
[hell dungeon]
1
[seal door map index]
60050
[seal door pos]
8 0
[no fatigue]
[enter without fatigue]
1
[prohibit practice]
1
[hit count]
50 50 50 50 50
[seal door appear rate]
10
[special passive object item]
[/special passive object item]
[linked dungeon]
[prev]
4103
[/prev]
[/linked dungeon]
