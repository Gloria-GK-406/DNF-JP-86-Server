# 镜像阿拉德等级与经验计算

2026-10-10；历史调查基准 `ba4ffc46`，等级修复基于 `af7b1ee8`。本次修复等级输入，沿用现有经验公式与倍率。
证据为 CurrentImplementation、真实 PVF 的 ContentStatic，以及角色 1123 历史运行日志；不是完整 A21 原生数值对齐结论。

## 本次实现（CurrentImplementation，尚未部署或实机验收）

用户授权将镜像等级策略放在入场与房间规则层，复用 Cell、掉落及经验消费者。

- `MirrorDungeonEntryDomainService` 在共享入场事务中收集已校验成员的持久化进度等级，交给 `IMirrorDungeonRuleService.ResolveRunLevel`。
- 单人取角色等级；组队取成员最高等级。这是本次服务端策略，尚未作为 A21 原生组队规则确认。
- 返回值同时冻结到本局 `DungeonLayout.FixedMonsterLevel` 与 `BasisLevel`。共享 PVF 定义不变；换房不重查角色等级。
- `DungeonRoomRuleService` 只在 Cell 创建时对普通怪物（type 0..3，Monster / ConditionalMonster 声明或无声明）应用固定等级，覆盖旧 MAP 的绝对值和相对偏移，并清除已消费的偏移。
- APC、type 4、dummy、ConditionalSummon、SpecialPassiveObject 及后续追加的深渊模板不应用这个固定覆盖；其已有绝对/相对/模板规则继续生效。相对规则仍使用本局基础等级，不能理解为它们一律保留历史 DGN 的基础等级。
- `DungeonCellFactory` 保存最终 Actor.Level 并以同级生成掉落；房间协议、击杀识别和击杀账本读取同一等级；通关读取本局 BasisLevel。不在这些消费者中再增加镜像分支。
- 历史图的经验权重、难度选择及经验公式保持现有规则，未擅自套用包装图8000的经验权重。

验证：Application 相关230项、Protocol相关54项全部通过，无失败/跳过；`git diff --check` 通过。覆盖普通图绝对/相对/默认等级、两房间 Cell→掉落→击杀账本、入场后角色进度改变仍保留本局等级、APC与特殊模板排除、协议最终等级，以及真实 PVF 四张历史图4/17/23/60的本次迷宫全部候选房间。此前测试只改level列未改exp而失败，已根据ProgressAsync的经验权威语义修正测试数据，不改角色进度实现。

实机待验证：1123重新进入镜像后怪物/击杀经验/通关经验表现；本次没有修改客户端或PVF，也没有切换运行中的服务。新规则只影响后续新建场次。

## 修复前等级来源（历史调查）

镜像入场用客户端提交且经合法性校验的历史地下城/任务组合，创建历史图的普通迷宫；Mirror 标记仅记录包装图、worldmap 与任务身份。
结构服务把历史 DGN 的 BasisLevel、ExperienceWeight 写入 Layout，没有根据角色等级替换。
房间规则把 MAP 中的绝对等级或 BasisLevel+相对偏移解析为 Actor.Level；下发房间怪物、生成掉落、击杀结算均使用 Layout.LevelOf(Actor.Level)。
因此当时并非“下发86级而经验偷偷用旧等级”的两套服务端算法：服务器整个镜像房间链路尚未套用镜像等级缩放。客户端观察到86级的具体显示来源还需独立取证。

真实修复 PVF 中地下城 4/17/23/60 的 BasisLevel 均为55；图4经验权重约1.3，其余三张为1.5。包装图8000的 BasisLevel=55、经验权重约1.2，但当时没有将包装图经验规则覆盖到历史图。
MAP 仍可声明6、26、55等混合等级；非零绝对等级优先，不会自动统一成55或角色等级。

## 击杀经验

入口：MonsterDeath 的击杀认领及参与者去重 → ApplyMonsterKillCommand → PlayingCharacter.AddExpAsync → ExperienceCalculationDomainService → ExperienceRuleService → MonsterExperienceRules。
角色等级从事务内持久化进度读取，不采用客户端上报的等级。

默认基础曲线（服务端调参，不是 PVF 字段）：

```text
W(L) = round(10 × (L+2) × (100+3×max(0,L-15)) / 100)
S = floor(W(L)/2 × 组队倍率 × 难度倍率 × 图权重
          × 怪物阶级倍率 × 绿名倍率 × 全局击杀倍率)
基础经验 = floor(S × 等级差倍率 / 队伍人数)
最终奖励 = 基础经验 + 各项加成分别向下取整
```

阶级倍率默认普通/精英/超级精英/Boss为1/2/3/4；独立绿名标记再乘3。
当前 PVF 难度倍率为约1.3、2、2.5、3、4；组队共享倍率为1、2、3、4，最后再除人数；全局击杀倍率为1。
等级差取“怪物等级－角色等级”：-3..0为100%，1..3为112%，低于角色至少7级降至5%；怪物高出至少10级也为5%。其余分档见 ExperienceTuning。
基础曲线、阶级倍率和部分等级差分档来自参考实现/服务端配置，不能当作 A21 已实机确认的全部原生常量。
成长契约、频道及其他已配置权益按基础经验各自算增量，不互相复利；经验药水当前只用于通关加成。

## 通关经验

通关使用 Layout.BasisLevel，乘本次实际击杀怪物数，再乘组队倍率、难度倍率、图权重与全局通关倍率；两次取整和等级差惩罚沿用击杀顺序。
不对每个击杀追加阶级/绿名倍率。评级奖励按 PVF 评级表给通关基础经验加成；成长契约按通关基础经验加成，黑钻按“通关基础+评级”加成。
结算页的普通/精英/Boss击杀经验分类是已发奖励的汇总，不再发第二次。满级后的经验溢出走统一经验胶囊处理。

## 已记录的实例

历史日志 `artifacts/mirror-layout-rotation-20261010/activation-stdout.log`：
- 7614行：图4，Adventure难度，单人。
- 9446行：角色2（1123，86级）杀55级普通怪，基础81，成长契约16，总97。
- 9550行：55级Boss基础326，成长契约65，总391。
- 9608行：本局击杀86只，通关基础7009、评级1401、成长契约1401、黑钻420，通关奖励10231。

普通怪计算为 `floor(floor(1254/2 × 2 × 1.2999999523162842) × 0.05)=81`，与日志相符。
这说明少量经验确实能由当时旧怪物等级和5%惩罚解释，不能通过只扩大经验倍率修复镜像等级问题。
本次已实现入场冻结与最终等级共享；包装图经验权重的覆盖关系仍未作原生对齐结论。

源码入口：
- `Server/src/Dfo.Server.Application/World/MirrorDungeonEntryDomainService.cs`
- `Server/src/Dfo.Server.Application/World/DungeonRoomRuleService.cs`
- `Server/src/Dfo.Server.Application/World/DungeonCellFactory.cs`
- `Server/src/Dfo.Server.Abstractions/World/MonsterExperienceRules.cs`
- `Server/src/Dfo.Server.Abstractions/World/DungeonClearExperienceRules.cs`
- `Server/src/Dfo.Server.Application/World/ExperienceCalculationDomainService.cs`
