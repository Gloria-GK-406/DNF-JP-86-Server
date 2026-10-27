
## stackable/throw/event_circus_stone.stk

[name]
麻醉剂
[explain]
用来麻醉大型狮子的特制麻醉剂。 使用后， 可以使马戏团的狮子王沉睡5秒钟。 狮子沉睡时， 防御力大幅减少。
[grade]
15
[attach type]
[free]
[rarity]
0
[weight]
1
[usable job]
[all]
[/usable job]
[minimum level]
14
[icon]
Item/stackable/consumption_cn.img 965
[field image]
Item/field_material_cn.img 165
[stackable type]
[throw] 1
[price]
150
[cool time]
1000
[move wav]
BONE_TOUCH
[int data]
54352 0 0 450 0 100
[/int data]
[string data]
PassiveObject/ActionObject/throwitem/Particle/sleep_pill.ptl
[/string data]

## monster/newmonsters/circus/boss_lion/action/drunken_lion.act

[MOTION]
[BASE ANI]
../Animation/drunken.ani
[/MOTION]
[TRIGGER]
[FRAME]
0 0
[DO BEHAVIOR]
[ME]
0
[/TRIGGER]
[BEHAVIOR]
[CHANGE STATE SKIP]
[ON]
[SET SPEED]
[X AXIS]
0
[Y AXIS]
0
[Z AXIS]
0
[/SET SPEED]
[/BEHAVIOR]
[TRIGGER]
[FRAME]
20 20
[DO BEHAVIOR]
[ME]
1
[/TRIGGER]
[BEHAVIOR]
[CHANGE STATE SKIP]
[OFF]
[/BEHAVIOR]
