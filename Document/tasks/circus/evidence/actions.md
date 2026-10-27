
## passiveobject/actionobject/circus/master/action/master.act

[MOTION]
[BASE ANI]
../Animation/Master.ani
[/MOTION]
[TRIGGER]
[IF]
time
[+]
1000
[<=]
[GET TIME]
[/IF]
[DO BEHAVIOR]
[ME]
0
[/TRIGGER]
[BEHAVIOR]
[DIALOG]
<npc::20058>欢迎来到我们马戏团。 来欣赏下我们的马戏团的绝技——喝下午茶的狮子王吧！！ 马戏团狮子王非常强大， 但它好像有个弱点…嘿嘿， 就看你能不能找的到了！ 7000
[DIM]
time
[=]
[GET TIME]
[/DIM]
[TRIGGER]
0
[OFF]
[/BEHAVIOR]

## passiveobject/actionobject/circus/master/action/master1.act

[MOTION]
[BASE ANI]
../Animation/Master.ani
[/MOTION]
[TRIGGER]
[IF]
time
[+]
1000
[<=]
[GET TIME]
[/IF]
[DO BEHAVIOR]
[ME]
0
[/TRIGGER]
[BEHAVIOR]
[DIALOG]
<npc::20058>欢迎来到我们马戏团。 来欣赏下我们马戏团的绝技之一的会滚球猴子和滚动的橡木桶吧！ 7000
[DIM]
time
[=]
[GET TIME]
[/DIM]
[TRIGGER]
0
[OFF]
[/BEHAVIOR]

## passiveobject/actionobject/circus/master/action/master2.act

[MOTION]
[BASE ANI]
../Animation/Master.ani
[/MOTION]
[TRIGGER]
[IF]
time
[+]
1000
[<=]
[GET TIME]
[/IF]
[DO BEHAVIOR]
[ME]
0
[/TRIGGER]
[BEHAVIOR]
[DIALOG]
<npc::20058>欢迎来到我们马戏团。 来欣赏下我们马戏团的可爱的猴子们吧！ 第一个猴子擅长拆除炸弹， 但第二个大猩猩却最喜欢制造炸弹， 希望你们小心。 7000
[DIM]
time
[=]
[GET TIME]
[/DIM]
[TRIGGER]
0
[OFF]
[/BEHAVIOR]

## passiveobject/actionobject/circus/master/action/master3.act

[MOTION]
[BASE ANI]
../Animation/Master.ani
[/MOTION]
[TRIGGER]
[IF]
time
[+]
1000
[<=]
[GET TIME]
[/IF]
[DO BEHAVIOR]
[ME]
0
[/TRIGGER]
[BEHAVIOR]
[DIALOG]
<npc::20058>欢迎来到我们马戏团。 来欣赏下我们的马戏团的淘气宝贝皮埃罗吧！ 黑色皮埃罗会反射伤害的哦， 希望你们小心。 7000
[DIM]
time
[=]
[GET TIME]
[/DIM]
[TRIGGER]
0
[OFF]
[/BEHAVIOR]

## passiveobject/actionobject/circus/catch_pierrot_small/action/pierrot_check.act

[TRIGGER]
[WHICH]
[MONSTER]
[CHECKUP]
[IS INDEX]
64694 64695 64696
[/IS INDEX]
[/CHECKUP]
[CHECKED NO]
[<]
1
[DO BEHAVIOR]
[ME]
0
[/TRIGGER]
[BEHAVIOR]
[TRIGGER]
0
[OFF]
[TRIGGER]
1
[ON]
[/BEHAVIOR]
[TRIGGER]
[ENABLE]
[OFF]
[WHICH]
[MONSTER]
[CHECKUP]
[IS INDEX]
64697 64698 64699
[/IS INDEX]
[/CHECKUP]
[DO BEHAVIOR]
[CHECKUP OBJECT]
1
[/TRIGGER]
[BEHAVIOR]
[DESTROY]
[TRIGGER]
1
[OFF]
[/BEHAVIOR]

## passiveobject/actionobject/circus/catch_pierrot_big/action/pierrot_boss_check.act

[TRIGGER]
[WHICH]
[MONSTER]
[CHECKUP]
[IS INDEX]
64689
[/IS INDEX]
[/CHECKUP]
[CHECKED NO]
[<]
1
[DO BEHAVIOR]
[ME]
0
[/TRRIGER]
[BEHAVIOR]
[TRIGGER]
0
[OFF]
[TRIGGER]
1
[ON]
[/BEHAVIOR]
[TRIGGER]
[ENABLE]
[OFF]
[WHICH]
[MONSTER]
[CHECKUP]
[IS INDEX]
64694 64695 64696 64697 64698 64699
[/IS INDEX]
[/CHECKUP]
[DO BEHAVIOR]
[CHECKUP OBJECT]
1
[/TRIGGER]
[BEHAVIOR]
[DESTROY]
[TRIGGER]
1
[OFF]
[/BEHAVIOR]

## passiveobject/actionobject/circus/jumpbarrel/action/destroyobject.act

[TRIGGER]
[WHICH]
[PASSIVE]
[CHECKUP]
[IS INDEX]
54313 54327 54329
[/IS INDEX]
[DISTANCE]
[<=]
100
[/CHECKUP]
[DO BEHAVIOR]
[CHECKUP OBJECT]
0
[/TRIGGER]
[BEHAVIOR]
[DESTROY]
[/BEHAVIOR]

## passiveobject/actionobject/circus/ringring/action/ring_basic.act

[MOTION]
[BASE ANI]
../Animation/boss_ring_stand_01.ani
[/MOTION]
