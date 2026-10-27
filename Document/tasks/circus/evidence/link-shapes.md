
## dungeon/special/stationescape.dgn

[name]
斯洛特发电站-逃离
[explain]
    为了破坏最后一个发电设施， 冒险家们毅然决然地潜入到斯洛特发电站的烟囱下方。 在破坏发电机的瞬间， 巨大的火焰呼啸而至， 仿佛具有生命一般不停地追赶着冒险家。 为了躲避这奇异的火焰， 冒险家们不得不借助通风口向上逃跑……小心， 别被追上了！
[cutscene image]
Map/CutScene/PowerStation.img 0
[minimap image]
Map/MiniMap/Act6.img
[worldmap pattern info]
1 4 WorldMap/SelectdungeonSlot/powerstation.img 5
[minimum required level]
83
[basis level]
86
[experience increasing point]
4.15
[champion]
3 25 40 60 60
[pathgate object]
5801 5805 5757 5802 5806 5760 5803 5807 5804 5808
[maze info]
[size]
3 2
[greed]
BBMMAA
 AADDEE
[map specification]
map 1 0 17084 map 1 1 11075 map 2 1 11070 boss 0 0 17085
[/map specification]
[start map]
2 1
[/start map]
[boss map]
0 0
[/boss map]
[clear condition]
[hunt boss]
69257 1
[/clear condition]
[escape hell]
1
[special dungeon]
1
[difficulty]
1
[designate dungeon difficulty]
1
[special passive object item]
0 -1 1 10088439 10000 1 -1 1 3341 3000 2 -1 1 10099777 6000
[/special passive object item]
[use fatigue only start dungeon]
8
[/use fatigue only start dungeon]
[prohibit practice]
1
[ignore default dungeon clear]
[linked dungeon]
[prev]
2016
[/prev]
[/linked dungeon]

## dungeon/timegate/black_church.dgn

[background pos]
80
[name]
暗黑圣战
[explain]
一场激烈的交战后， 圣职者们终于艰难地击退了混沌之奥兹玛和暗黑骑士们。 因为是以神的名义战胜了黑暗势力， 所以这场战役在历史上被称作暗黑圣战。 但史书不会记载的是， 有一个小女孩， 正深深地思念着她在这场战斗中失踪的圣职者哥哥……
[cutscene image]
Map/CutScene/timegate.img 0
[minimap image]
Map/MiniMap/Act2.img
[worldmap pattern info]
1 4 WorldMap/SelectdungeonSlot/timegate.img 3
[difficulty level]
1 1 1 1 1
[recommend equipment]
0 2 0 2 0 3 12 3 12 6
[/recommend equipment]
[minimum required level]
73
[basis level]
76
[experience increasing point]
1.33
[champion]
3 25 40 60 60
[pathgate object]
5755 5756 5757 5758 5759 5760 5761 5762 5763 5764
[maze info]
[size]
6 3
[greed]
AAAAJJFFNNMM
 AAAALLMMLLOO
 BBFFGGDDGGCC
[map specification]
map 2 0 32386 map 3 0 32388 map 4 0 32387 map 5 0 32389 map 2 1 32382 map 3 1 32383 32391 map 4 1 32392 map 5 1 32393 map 0 2 32380 map 1 2 32381 map 2 2 32394 map 3 2 32395 map 4 2 32396 boss 5 2 32385
[/map specification]
[start map]
0 2
[/start map]
[boss map]
5 2
[/boss map]
[hell dungeon]
1
[seal door map index]
60059
[seal door pos]
3 0
[hit count]
50 50 50 50 50
[seal door appear rate]
10
[maze info]
[quest connection]
0 2414 -1
[clear condition]
[destroy object]
10601 1
[/clear condition]
[size]
1 2
[greed]
II
 CC
[map specification]
boss 0 0 15368
[/map specification]
[start map]
0 1
[/start map]
[boss map]
0 0
[/boss map]
[escape hell]
1
[maze info]
[quest connection]
0 2415 -1
[clear condition]
[destroy object]
10601 1
[/clear condition]
[size]
3 2
[greed]
IIAAAA
 DDFFEE
[map specification]
map 0 1 15370 map 1 1 15371 boss 2 1 15372
[/map specification]
[start map]
0 0
[/start map]
[boss map]
2 1
[/boss map]
[escape hell]
1
[maze info]
[quest connection]
0 2416 -1
[size]
3 2
[greed]
JJFFEE
 CCAAAA
[map specification]
map 0 0 15373 map 1 0 15371 boss 2 0 15374
[/map specification]
[start map]
0 1
[/start map]
[boss map]
2 0
[/boss map]
[escape hell]
1
[maze info]
[quest connection]
0 2417 -1
[size]
2 2
[greed]
JJEE
 CCAA
[map specification]
map 0 0 15375 boss 1 0 12030
[/map specification]
[start map]
0 1
[/start map]
[boss map]
1 0
[/boss map]
[escape hell]
1
[maze info]
[quest connection]
0 2420 -1
[clear condition]
[destroy object]
10606 1
[/clear condition]
[size]
1 2
[greed]
II
 CC
[map specification]
boss 0 0 15377
[/map specification]
[start map]
0 1
[/start map]
[boss map]
0 0
[/boss map]
[escape hell]
1
[event monster random map]
0
[event monster]
1 1000 21100 1 1000 1 1 5
[/event monster]
[event monster3]
1 2000 64744 1 7000 1 1 64745 1 3000 1 1
[/event monster3]
[special passive object item]
0 -1 1 3333 1500
[/special passive object item]
[linked dungeon]
[on clear add passive object]
18961 340 290
[next]
300 2 -1 301 2 -1 302 2 -1 303 2 -1 304 2 -1 305 2 -1 306 2 -1 -1 86 -1
[/next]
[/linked dungeon]
[recommend party]
> 1 > 2 > 2 > 3 > 4
[/recommend party]

## dungeon/dungeonsample.dgn

[cutscene image]
Map/CutScene/.img 0
[minimap image]
Map/MiniMap/.img
[worldmap info]
  WorldMap/Village/.img
[minimum required level]
[basis level]
[experience increasing point]
1
[champion]
6 12 18 24 26
[pathgate object]
[maze info]
[size]
3 8
[greed]

[map specification]
[/map specification]
[start map]
[/start map]
[boss map]
[/boss map]
[special passive object item]
[/special passive object item]
[name]

[explain]

[hell dungeon]
1
[designate dungeon difficulty]
0
[appendage destory object]
[object index]
1234 23454
[/object index]
[object count]
5
[gold count]
5
[independent drop weight]
10
[monster index]
1234 12345
[/monster index]
[/appendage destory object]
[special dungeon]
1
[hidden map]
1004 1005 1006
[/hidden map]
[designate dungeon difficulty]
0
[linked dungeon]
[next]
2016 50 -1 2008 30 -1 -1 20 -1
[/next]
[prev]
2014 2007
[/prev]
[/linked dungeon]
