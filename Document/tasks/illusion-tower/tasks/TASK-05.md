# TASK-05 — 任务材料与公共能力的塔内接入

## Task Unit Contract

- Revision: 1
- Outcome and references: T-004/T-005 的任务与公共能力接缝；RQ-007、009、010。塔内击杀可获得当前任务材料，物品使用与城镇限定操作识别塔归属，必要角色变化只投递给当前塔成员。
- Owned boundary: Application/Abstractions 的任务掉落和公共能力最小接入，Protocol 的击杀库存投影及宠物/外观受众，对应测试。Party离队/踢人/队长变更留给下一单元。
- Dependencies: TASK-04 已验收后的稳定投影入口；当前奖励事务与真实物品领域能力。任务计划已准备，尚未授权并行产品写入。
- Required checks: 真实任务950规则的材料生成、数量上限、满包、重复UID和事务回滚；合法消耗品可用，城镇限定能力拒绝塔成员；宠物/转职外观通知受众精确；普通副本/城镇相关回归。

## Detailed Plan

1. 使用 `IQuestEnemyDropDomainService.DrawAsync` 和现有物品发放能力，在塔 Kill 的同一事务内处理任务掉落；每成员独立按其有效任务和持有量抽取。不要转入普通地面掉落或把普通 MazeCell 引入塔。若请求类型仅为随机盐携带 Cell，将其最小改为显式位置盐，普通适配器仍传原 x/y，塔传 floor/0，保持普通结果稳定。
2. 当前PVF任务950索要10089420×10；APC15402/20604/5602/10600每次配置2个、608配置3个，概率30，地下城11001、难度-1。测试保留真实30%并使用可重现命中种子，不把概率改100再宣称验证真实规则。保留现有共享任务材料等能力的语义。
3. 发放结果处理现有容器的实际放置数量和槽位，满包可保留无掉落结果，非满包未知失败必须回滚。击杀receipt携带实际库存更新，合并 `QuestHoldingsChanged` 与敌人击杀任务事实后生成一次最新任务快照。网络仅投影receipt，不再次抽取/发放。重复同一UID不新增材料。
4. 采用既有真实SQLite夹具，覆盖无任务、匹配APC、持有9至不超过10、已满10、任务栏满、SQL提交失败后演员/经验/材料/任务共同回滚且重试成功。任务10089420使用Quest物理页的真实业务映射；不能沿用夹具默认Consumable映射伪装测试。
5. `PlayingCharacter.ItemUseFactsAsync` / `PlaceAsync` 识别塔为地下城语境。当前PVF StackableItemLimit=0表示允许沿普通使用规则，不是禁止消耗品；保持等级、职业、冷却等校验。使用已调查的规则依据，不新增无依据白名单。注入路径避免Character/Tower DI构造循环，可沿现有延迟character工厂。
6. 检查仓库转移、仓库金币/扩容、恢复疲劳、仓库整理等现有城镇操作的守卫，使塔内请求同普通地下城一样拒绝；不禁用携带背包整理。传送物品与NPC商店现有town条件已能拒绝时不重复改写，也不把塔结算期开放为普通Boss商店。
7. 宠物运行变化及任务转职外观的同伴通知增加当前塔精确session受众。只使用同一run的当前成员，避免向已经回城、离塔、旧连接或仅仍在party的成员投递；不得把“当前party”直接当塔成员。
8. 用既有架构检查核对Protocol/CMD/Domain边界，测试真实公开能力和生产DI。所有.NET串行；不启动用户Host/客户端、不操作玩家库、不提交Git。完成时记录实际命令、通过/跳过数量、实现边界与后续Party接口需要。

## Evidence

研究见 `reward-and-projection-evidence.md` 的公共能力与任务部分。测试输入来自当前PVF与本地实现；本单元不提升为ClientVerified。Task04网络投影仍须维持结算可重投影但资产一次提交的约束。

## Result — round 1, 2026-10-03

- Changed: 塔内击杀为每个run成员调用既有 `IQuestEnemyDropDomainService`，塔请求以 `floor/0` 作为显式位置盐，普通副本仍传原始格子x/y。发放在击杀同一领域事务内重新检查当前任务、`GrantLimit` 与真实持有量；普通任务物读取Main容器，共享材料读取账户SharedMaterial容器，并统一调用既有 `GrantAsync` 保留Quest、QuickSlot及SharedMaterial placement语义。receipt只携带实际变更槽位，网络只投影receipt；敌人击杀与持有量变化合并为一次任务进度报告。
- Changed: 任务950使用当前PVF的30%概率与可重现种子42验证，未改成100%。真实物品10089420按kind=4映射到Quest页；覆盖无任务、命中、持有9→10、已持有10、Quest页与QuickSlot均满、重复actor UID、SQL提交失败共同回滚并成功重试。另用现有账户材料3033覆盖SharedMaterial持有9→10及GrantLimit，避免把950的Quest页结论泛化到所有DirectQuestItem。
- Changed: `PlayingCharacter` 将塔归属视为Dungeon场所，继续使用原有等级、职业、场所、冷却和物品规则。仓库转移、仓库扩容/金币读取、恢复疲劳、仓库整理在塔内与普通副本一致拒绝；携带背包整理不受影响。Character→Tower依赖通过延迟解析避免构造循环。
- Changed: 宠物可见通知和任务转职外观增加塔内受众。塔分支只保存同一run中捕获的 `SelectedSessionIdentity`，每个宠物通知目标发送前重新确认发起者、收件者仍是当前exact session且仍属于同一RunId；不会按CharacterId重新绑定替换连接。转职外观在构造单次显式投递前执行同样的exact session/RunId检查。城镇旁观者、仅party成员、已回城成员和旧连接均不进入塔受众。
- Why: 塔内击杀应复用现有任务物与库存语义，只改变击杀上下文和事务归属；950证明其自身使用Quest页，不能据此为所有任务物新增强制Quest接口。塔运行成员记录包含连接世代，投影若降为CharacterId会在并发换线时误投递给新连接，因此身份必须保留到发送边界。
- Checks: [TASK-05 bounded verification](../verification/TASK-05-verification.md)。Application全项目2880通过、0失败、0跳过；Protocol宠物/任务完成24通过；Protocol塔集合8通过；CommandHandlerArchitecture 13通过；`git diff --check`通过。所有PVF命令均显式使用 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
- Remaining: 本单元没有执行A21实机，任务物UI、宠物与转职外观的客户端表现仍保留在 [human test list](../human-test-list.md)，不能提升为ClientVerified。Party离队、踢人、队长变更和已提交离塔投影由TASK-06处理。未启动生产Host/客户端、未部署、未访问玩家数据库、未提交Git。

### Stable public-capability contract

- 塔内DirectQuestItem发放必须尊重当前active quest、`GrantLimit`、账户共享材料持有量与既有 `GrantAsync` placement；不得新增仅为塔或仅为950强制某一物理页的发放路径。
- 塔内物品使用的场所事实为Dungeon；其余资格仍由现有物品规则判断。城镇限定能力必须同时排除普通DungeonRun和IllusionTower归属。
- 塔内共享通知的受众键为捕获的exact `SelectedSessionIdentity` + `RunId`；发送边界不得把它降为CharacterId后再查当前连接。
