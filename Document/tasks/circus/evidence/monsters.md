id 64683 -> NewMonsters/circus/circusMonkey/circusMonkeyVer2.mob
=== monster/NewMonsters/circus/circusMonkey/circusMonkeyVer2.mob ===
[floating height]
1
[name]
吃香蕉的猴子
[die effect]
0 1 1
[ability table]
0
[common champion elemental property]
[fire element] [water element] [dark element] [light element] [no element]
[/common champion elemental property]
[ability category]
[HP MAX] * 130 [EQUIPMENT_PHYSICAL_ATTACK] * 110 [EQUIPMENT_PHYSICAL_DEFENSE] * 120 [EQUIPMENT_MAGICAL_ATTACK] * 110 [EQUIPMENT_MAGICAL_DEFENSE] * 80
[/ability category]
[holdable off]
1
[is grabable]
0
[damage sound]
R_NORMALSILVER_DMG
[die sound]
NORMALSILVER_DIE
[category]
[human] [non attraction]
[/category]
[level]
35 60
[move speed]
1 1
[attack speed]
1000 1000
[hit recovery]
1000 1000
[weight]
60000 60000
[item]
[/item]
[common champion drop item]
[/common champion drop item]
[waiting action]
Action/Stay.act
[proc action]
Action/Proc.act
[move action]
Action/MoveVer2.act
[sit action]
Action/Sit.act
[damage action 1]
Action/Damage1.act
[damage action 2]
Action/Damage2.act
[down action]
Action/Down.act
[overturn action]
Action/Overturn.act
[lie frame]
1 2 3 3 4
[attack action]
Action/throw.act
[/attack action]
[attack info]

[/attack info]
[etc action]
Action/move_step1.act Action/move_step2.act Action/move_step3.act Action/GoalVer2.act Action/EscapeVer2.act
[/etc action]
[cooltime]
0
[/cooltime]
[vision]
1000
[think term]
500
[destination change term]
500
[keep distance with target]
800
[keep range distance with target]
280 0 50 200
[intelligence]
70 60 3000
[ai pattern]
[normal]
ai/Event.ai ai/DestinationSelect.ai ai/ActionVer2.ai ai/MoveMethod.ai
[/normal]
[master]
ai/Event.ai ai/DestinationSelect.ai ai/ActionVer2.ai ai/MoveMethod.ai
[/master]
[expert]
ai/Event.ai ai/DestinationSelect.ai ai/ActionVer2.ai ai/MoveMethod.ai
[/expert]
[king]
ai/Event.ai ai/DestinationSelect.ai ai/ActionVer2.ai ai/MoveMethod.ai
[/king]
[slayer]
ai/Event.ai ai/DestinationSelect.ai ai/ActionVer2.ai ai/MoveMethod.ai
[/slayer]
[/ai pattern]
id 64684 -> NewMonsters/circus/JumpBarrel_Cannon/Cannon.mob
=== monster/NewMonsters/circus/JumpBarrel_Cannon/Cannon.mob ===
[die sound]
DRGCANNON_BREAK
[category]
[machine] [fort armor] [range combat] [close-carefully] [fixture]
[/category]
[ability category]
[HP MAX] * 1 [EQUIPMENT_PHYSICAL_ATTACK] * 100 [EQUIPMENT_PHYSICAL_DEFENSE] * 100 [EQUIPMENT_MAGICAL_ATTACK] * 100 [EQUIPMENT_MAGICAL_DEFENSE] * 100
[/ability category]
[level]
60 60
[move speed]
0 0
[attack speed]
800 800
[cast speed]
500 500
[hit recovery]
800 800
[weight]
80000 80000
[sight]
400
[targeting nearest]
1
[warlike]
30
[attack delay]
1500
[stuckbonus on damage]
0 0 0 0
[lie frame]
1 2 1 2 3
[die effect]
0 19 0
[face image]
Monster/MonsterFace.img 330
[waiting action]
Action/Stay.act
[attack action]
Action/Launch_BlueBall.act Action/Launch_RedBall.act
[/attack action]
[attack info]

[/attack info]
[cooltime]
1900 4700
[/cooltime]
[vision]
1000
[think term]
500
[destination change term]
500
[keep distance with target]
300
[ai pattern]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/ai pattern]
[name]
马戏团特制大炮
id 64685 -> NewMonsters/circus/JumpBarrel_Monkey/Monkey.mob
=== monster/NewMonsters/circus/JumpBarrel_Monkey/Monkey.mob ===
[floating height]
200
[die sound]
DRGCANNON_BREAK
[category]
[machine] [fort armor] [range combat] [close-carefully] [fixture]
[/category]
[ability category]
[HP MAX] * 1 [EQUIPMENT_PHYSICAL_ATTACK] * 100 [EQUIPMENT_PHYSICAL_DEFENSE] * 100 [EQUIPMENT_MAGICAL_ATTACK] * 100 [EQUIPMENT_MAGICAL_DEFENSE] * 100
[/ability category]
[level]
60 60
[move speed]
0 0
[attack speed]
800 800
[cast speed]
500 500
[hit recovery]
800 800
[weight]
80000 80000
[sight]
400
[targeting nearest]
1
[warlike]
30
[attack delay]
1500
[stuckbonus on damage]
0 0 0 0
[lie frame]
1 2 1 2 3
[die effect]
0 19 0
[face image]
Monster/MonsterFace.img 330
[waiting action]
Action/Stay.act
[attack action]
Action/Throw.act Action/Die.act
[/attack action]
[attack info]

[/attack info]
[cooltime]
1100 0
[/cooltime]
[vision]
1000
[think term]
500
[destination change term]
500
[keep distance with target]
300
[ai pattern]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/ai pattern]
[name]
大炮猴
id 64686 -> NewMonsters/circus/JumpBarrel_Boss/Barrel.mob
=== monster/NewMonsters/circus/JumpBarrel_Boss/Barrel.mob ===
[name]
超大橡木桶
[face image]
Monster/MonsterFace_JP.img 43
[attack sound]

[appear sound]

[damage sound]
R_GOBLIN_DMG
[die sound]
R_GOBLIN_DIE_01
[ability table]
0
[ability category]
[HP MAX] * 20000 [EQUIPMENT_PHYSICAL_ATTACK] * 150 [EQUIPMENT_PHYSICAL_DEFENSE] * 40 [EQUIPMENT_MAGICAL_ATTACK] * 150 [EQUIPMENT_MAGICAL_DEFENSE] * 40
[/ability category]
[category]
[machine]
[/category]
[fix direction]
[left]
[HP regen speed]
0 0
[MP regen speed]
0 0
[level]
60 80
[dark resistance]
100 100
[light resistance]
100 100
[slow resistance]
100 100
[freeze resistance]
100 100
[poison resistance]
100 100
[stun resistance]
100 100
[curse resistance]
100 100
[blind resistance]
100 100
[lightning resistance]
100 100
[stone resistance]
100 100
[sleep resistance]
100 100
[bleeding resistance]
100 100
[piercing resistance]
100 100
[confuse resistance]
100 100
[hold resistance]
100 100
[move speed]
800 800
[attack speed]
700 700
[cast speed]
1000 1000
[hit recovery]
800 800
[weight]
70000 70000
[lie frame]
2 3 2 3 4
[waiting action]
Action/Stay.act
[last action]
Action/Last.act
[attack action]
Action/Run.act Action/Run_UnderHP66.act Action/Run_UnderHP33.act Action/Reach.act
[/attack action]
[attack info]

[/attack info]
[cooltime]
0 0 0 0
[/cooltime]
[stuckbonus on damage]
0 0 0 0
[vision]
1000
[warlike]
100
[think term]
500
[ai pattern]
[normal]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/normal]
[expert]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/expert]
[master]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/master]
[king]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/king]
[slayer]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/slayer]
[/ai pattern]
id 64688 -> NewMonsters/circus/boss_lion/boss_lion.mob
=== monster/NewMonsters/circus/boss_lion/boss_lion.mob ===
[name]
狮子王-简单
[face image]
Monster/MonsterFace_JP.img 44
[category]
[beast] [fixture]
[/category]
[ambient sound]

[attack sound]

[appear sound]

[damage sound]
R_KERBEROS_DMG
[die sound]
KERBEROS_DIE
[ability category]
[EQUIPMENT_PHYSICAL_ATTACK] * 50 [EQUIPMENT_PHYSICAL_DEFENSE] * 30 [EQUIPMENT_MAGICAL_ATTACK] * 50 [EQUIPMENT_MAGICAL_DEFENSE] * 30 [HP MAX] * 2000
[/ability category]
[level]
200 200
[dark resistance]
30 30
[light resistance]
30 30
[slow resistance]
30 30
[freeze resistance]
30 30
[poison resistance]
30 30
[stun resistance]
30 30
[curse resistance]
30 30
[blind resistance]
50 50
[lightning resistance]
30 30
[stone resistance]
30 30
[sleep resistance]
30 30
[bleeding resistance]
30 30
[piercing resistance]
30 30
[confuse resistance]
30 30
[hold resistance]
30 30
[move speed]
900 900
[attack speed]
1000 1000
[cast speed]
1000 1000
[hit recovery]
1000 1000
[weight]
90000 90000
[hp regen rate]
0 0
[stuckbonus on damage]
0 0 0 0
[waiting action]
Action/sleep.act
[move action]
Action/move.act
[damage action 1]
Action/damage1.act
[damage action 2]
Action/damage2.act
[down action]
Action/down.act
[last action]
Action/Last.act
[attack action]
Action/attack_swing.act Action/attack_bite_ready.act Action/attack_bite.act Action/attack_dash1.act Action/attack_dash2.act Action/attack_jump1.act Action/attack_jump2.act Action/attack_smash.act Action/attack_smashdown.act Action/attack_cry.act Action/attack_cry_summon.act
[/attack action]
[attack info]
attackinfo/attack.atk attackinfo/grab.atk attackinfo/grab.atk attackinfo/dash_attack.atk attackinfo/dash_attack.atk attackinfo/jump_attack.atk attackinfo/jump_attack.atk attackinfo/attack.atk attackinfo/attack.atk attackinfo/attack.atk attackinfo/attack.atk
[/attack info]
[cooltime]
4000 2000 1000 5000 5000 4000 4000 4000 35000 10000 5000
[/cooltime]
[think term]
300
[etc action]
Action/wakeup_start.act Action/drunken_lion.act Action/etc_summon.act
[/etc action]
[ai pattern]
[normal]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/normal]
[expert]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/expert]
[master]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/master]
[king]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/king]
[slayer]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/slayer]
[/ai pattern]
[vision]
1000
[lie frame]
1 2 1 2 3
id 64689 -> NewMonsters/circus/catch_pierrot_big/catch_pierrot_big.mob
=== monster/NewMonsters/circus/catch_pierrot_big/catch_pierrot_big.mob ===
[name]
皮埃罗队长
[face image]
Monster/MonsterFace_JP.img 42
[die effect]
0 3 1
[category]
[human] [range combat] [close-carefully] [fort armor] [fixture]
[/category]
[fix direction]
[right]
[ambient sound]
VENTING_AMB
[attack sound]

[appear sound]
VENTING_MEET
[damage sound]
R_VENTING_DMG
[die sound]
VENTING_DIE
[ability category]
[HP MAX] * 1000 [EQUIPMENT_PHYSICAL_ATTACK] * 50 [EQUIPMENT_PHYSICAL_DEFENSE] * 50 [EQUIPMENT_MAGICAL_ATTACK] * 50 [EQUIPMENT_MAGICAL_DEFENSE] * 50
[/ability category]
[level]
100 100
[dark resistance]
30 30
[light resistance]
30 30
[slow resistance]
30 30
[freeze resistance]
30 30
[poison resistance]
30 30
[stun resistance]
30 30
[curse resistance]
30 30
[blind resistance]
50 50
[lightning resistance]
30 30
[stone resistance]
30 30
[sleep resistance]
30 30
[bleeding resistance]
30 30
[piercing resistance]
30 30
[confuse resistance]
30 30
[hold resistance]
30 30
[attack speed]
1000 1000
[cast speed]
1000 1000
[hit recovery]
1200 1200
[weight]
90000 90000
[stuckbonus on damage]
3 3 3 3
[waiting action]
Action/Stay.act
[damage action 1]
Action/Damage1.act
[attack action]
Action/Throw_Manager.act Action/8Throw1.act Action/8Throw2.act Action/16Throw1.act Action/4Throw1.act Action/4Throw2.act
[/attack action]
[attack info]
AttackInfo/base.atk AttackInfo/base.atk
[/attack info]
[cooltime]
6000 1 1 1 1 1
[/cooltime]
[think term]
500
[ai pattern]
[normal]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/normal]
[expert]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/expert]
[master]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/master]
[king]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/king]
[slayer]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/slayer]
[/ai pattern]
[warlike]
1000
[vision]
1000
[is direction to move]
0
[lie frame]
1 2 3 3 4
[level of difficulty]
[normal]
-40 2000
[expert]
-20 1000
[master]
0 0
[king]
0 0
[slayer]
0 0
[/level of difficulty]
id 64694 -> NewMonsters/circus/catch_pierrot_small/catch_pierrot_smalla1.mob
=== monster/NewMonsters/circus/catch_pierrot_small/catch_pierrot_smalla1.mob ===
[name]
皮埃罗
[face image]
Monster/MonsterFace_JP.img 40
[die effect]
0 3 1
[category]
[human] [range combat] [close-carefully] [fort armor] [fixture]
[/category]
[ambient sound]
VENTING_AMB
[attack sound]

[appear sound]
VENTING_MEET
[damage sound]
R_VENTING_DMG
[die sound]
VENTING_DIE
[ability category]
[HP MAX] * 30 [EQUIPMENT_PHYSICAL_ATTACK] * 150 [EQUIPMENT_PHYSICAL_DEFENSE] * 150 [EQUIPMENT_MAGICAL_ATTACK] * 150 [EQUIPMENT_MAGICAL_DEFENSE] * 150
[/ability category]
[level]
62 62
[dark resistance]
30 30
[light resistance]
30 30
[slow resistance]
30 30
[freeze resistance]
30 30
[poison resistance]
30 30
[stun resistance]
30 30
[curse resistance]
30 30
[blind resistance]
50 50
[lightning resistance]
30 30
[stone resistance]
30 30
[sleep resistance]
30 30
[bleeding resistance]
30 30
[piercing resistance]
30 30
[confuse resistance]
30 30
[hold resistance]
30 30
[attack speed]
1000 1000
[cast speed]
1000 1000
[hit recovery]
1200 1200
[weight]
90000 90000
[stuckbonus on damage]
3 3 3 3
[waiting action]
Action/Stay.act
[damage action 1]
Action/Damage1.act
[attack action]
Action/Jump.act Action/Throw.act
[/attack action]
[attack info]

[/attack info]
[etc action]
Action/Hide.act Action/Etc.act
[/etc action]
[cooltime]
10000 10000
[/cooltime]
[think term]
2000
[ai pattern]
[normal]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/normal]
[expert]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/expert]
[master]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/master]
[king]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/king]
[slayer]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/slayer]
[/ai pattern]
[warlike]
1000
[vision]
1000
[is direction to move]
0
[lie frame]
1 2 3 3 4
[level of difficulty]
[normal]
-40 2000
[expert]
-20 1000
[master]
0 0
[king]
0 0
[slayer]
0 0
[/level of difficulty]
id 64697 -> NewMonsters/circus/catch_pierrot_small/catch_pierrot_smallb1.mob
=== monster/NewMonsters/circus/catch_pierrot_small/catch_pierrot_smallb1.mob ===
[name]
皮埃罗
[face image]
Monster/MonsterFace_JP.img 40
[die effect]
0 3 1
[category]
[human] [range combat] [close-carefully] [fort armor] [fixture] [unbeatable]
[/category]
[ambient sound]
VENTING_AMB
[attack sound]

[appear sound]
VENTING_MEET
[damage sound]
R_VENTING_DMG
[die sound]
VENTING_DIE
[ability category]
[HP MAX] * 30 [EQUIPMENT_PHYSICAL_ATTACK] * 150 [EQUIPMENT_PHYSICAL_DEFENSE] * 150 [EQUIPMENT_MAGICAL_ATTACK] * 150 [EQUIPMENT_MAGICAL_DEFENSE] * 150
[/ability category]
[level]
62 62
[dark resistance]
30 30
[light resistance]
30 30
[slow resistance]
30 30
[freeze resistance]
30 30
[poison resistance]
30 30
[stun resistance]
30 30
[curse resistance]
30 30
[blind resistance]
50 50
[lightning resistance]
30 30
[stone resistance]
30 30
[sleep resistance]
30 30
[bleeding resistance]
30 30
[piercing resistance]
30 30
[confuse resistance]
30 30
[hold resistance]
30 30
[attack speed]
1000 1000
[cast speed]
1000 1000
[hit recovery]
1200 1200
[weight]
90000 90000
[stuckbonus on damage]
3 3 3 3
[waiting action]
Action/Stay_reflect.act
[damage action 1]
Action/Damage1_reflect.act
[attack action]
Action/Jump_reflect.act Action/Throw_reflect.act
[/attack action]
[attack info]

[/attack info]
[etc action]
Action/Hide_reflect.act Action/Etc_reflect.act
[/etc action]
[cooltime]
10000 10000
[/cooltime]
[think term]
2000
[ai pattern]
[normal]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/normal]
[expert]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/expert]
[master]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/master]
[king]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/king]
[slayer]
ai/Event.ai ai/DestinationSelect.ai ai/Action.ai ai/MoveMethod.ai
[/slayer]
[/ai pattern]
[warlike]
1000
[vision]
1000
[is direction to move]
0
[lie frame]
1 2 3 3 4
[level of difficulty]
[normal]
-40 2000
[expert]
-20 1000
[master]
0 0
[king]
0 0
[slayer]
0 0
[/level of difficulty]
