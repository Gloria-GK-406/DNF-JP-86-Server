---
kind: rks-review
target: tasks/TASK-30.md
verdict: approved
---

# TASK-30 结果快速审查（T-009 第 1 单元，独立审查者）

- 审查对象：`tasks/TASK-30.md`，Contract Revision 1，主控方裁定（执行前）1～7，Result 第 1 轮；相对 HEAD `a9c79e98` 的未提交差异。评审者只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit、Code、Deliverable 准则。
- 评审者自查：Application.Tests 小过滤（4 个判据类）79 条全过；`cr-at-eol` 口径 `diff --check` 无输出，`HousingState.cs` 仍为 CRLF；用 scratchpad 探针对 Release dll 做反射与 IL 扫描，寻找普查规则能被绕过的形状。

## Round 1 — `changes_required`

1. **F1（中，需改）**：`WeaknessDomainService` 私有嵌套类 `Weakness` 的 `SetWeakness`、`Recover`、`SetRecovery`（`PauseRecovery`／`ResumeRecovery` 走 `SetRecovery`）三处私有断言，只有 `Recover` 由运行期反例守着。句柄经接口 `IWeakness` 交出，分类表看不到它的成员。删掉另两处的 `Writing` 时全量不变红，句柄新增写成员也无判据要求挂断言。`t009-criteria.md:54`、Result Remaining、普查日志 V7 段的说法对这两处不成立。修法：补反例调用；或者把句柄列进 `Types` 与 `StatelessEntries`（更彻底）；最低限度改正记录。
2. **F2（低）**：普查规则 (1) 假定主构造器捕获参数从不被写，这一点没有判据守着；`class X(int next) { int Next() => next++; }` 可以绕过。今天 IL 扫描在构造器之外对 `<x>P` 字段的 `stfld` 只有 `GameplayOutbox+OrderedScope.set_Delivery` 一处，它是每作业一份的对象，不是单例。
3. **F3（低，信息）**：藏在其他程序集类型后面的状态普查认不出，remarks 已声明这条边界；探针显示今天没有漏掉的玩法状态。
4. **F4（低，信息）**：包装形状多为整段放进作业块，被包住的都是读成员或纯读断言，语义不变。`SelectCharacterProductionStateTests`、`SelectCharacterEntryLifecycleServiceTests` 用了不传 services 的 `RunAsync`，偏离「有容器时传容器」的约定，但不影响结果。
5. **F5（低，信息）**：`CharacterContainerDomainService` 的 Find／Get／GetContainers 在事务内登记参与者，却按读归类，理由已写明（只暂存、不写不提交、执行器外走加锁直读）。可以接受，TASK-33 对账时须知悉。
6. **F6（低，信息）**：`SendExplicitAsync` 中 `ValidateExplicitDelivery` 排在位置检查之前；整份投递为空或全部被跳过时不做位置检查。这是基准行为，不在 F-97 范围内。

第 1 轮已核实无问题：
- **普查判据**：与 remarks 一一对应；4 行反例各自恰报一条；加法 73 ＝ 40 ＋ 33，单例 53（37 ＋ 16）＋ 静态 17 ＋ 组件 2 ＋ 句柄 1，E1 7 ＋ E2 21（6 ＋ 9 ＋ 6）＋ E3 4 ＋ R061 1。
- **33 项豁免**：E2 的规则缓存只写一次、写入 Frozen 或不可变内容、有预热；15 张常量表没有元素写入；编码常量以 `ReadOnlyMemory`／`CopyTo` 进入帧；引用的 5 个附录 B 行都存在。
- **分类表**：新增 89 行，抽查 40 多行正确。
- **`PartyDeliveryStream`**：`field` 访问器语义不变，执行器外不误报。
- **测试包装**：抽查 15 处以上，断言文本不变。
- **F-76**：三项加固与 3 行反例都在，闭包判据从 `Dfo.Server.Host` 出发。
- **F-97**：三个入口都改为先取 `outboxes.Current` 再编码；`SelectCharacterHandler` 的预验证不受影响。
- **F-124**：断言由 TASK-23（`50d7bce4`）删除；泛型约束加上全仓唯一实现，风险很小，登记交主控方即可。
- **Spec 附录 B／F**：5 处与实现一致。
- **数字与边界**：8301 ＝ 8281 ＋ 2 ＋ 18，取证 71 ＝ 41 ＋ 27 ＋ 3，`Types` 41 ＝ 14 ＋ 27，行尾符合要求。

主控方处置见 `tasks/TASK-30.md`「主控方处置（第 1 轮评审后）」。

## Round 2 — `approved`

```yaml
kind: rks-review
verdict: approved
```

- 复核对象：`tasks/TASK-30.md`「主控方处置（第 1 轮评审后）」第 1～4 条，以及 Result 中「第 1 轮评审后修改」一节（执行第 2 轮）。改动仍是相对 HEAD `a9c79e98` 的未提交差异。本轮只复核 Round 1 的各项发现，以及修改可能直接带来的回归。
- 评审者自查：
  - 构建产物比返工源文件新（`WeaknessDomainService.cs` 01:23:34，`Application.Tests.dll` 01:23:46）。
  - Application.Tests 小过滤（普查、分类、`GameplayWriteAssertionTests`、`WeaknessTests`、`WeaknessGoldRecoveryTests`）124 条全过。
  - `cr-at-eol` 口径的 `diff --check` 无输出。
  - `Server/src` 仍是 27 个文件。工作区里 `Server/src`、`Server/tests` 和任务目录之外没有改动。
  - `WeaknessDomainService.cs` 是 LF，`HousingState.cs` 是 CRLF。

### 逐项结论

1. **F1：已解决。**
   - `WeaknessHandle` 按嵌套类型名取到 `Weakness`，并在找不到时抛出，所以改名会立刻失败。它已列入 `Types`，`StatelessEntries` 共 2 项，两处都写了理由。
   - `Table` 新增 6 行：`get_Value`、`RecoveryGoldCost` 是读；`SetWeakness`、`Recover`、`PauseRecovery`、`ResumeRecovery` 是写。对照源码，读写判定正确。
   - `PauseRecovery`、`ResumeRecovery` 各自以自己的名字断言。私有的 `SetRecovery` 只经这两个成员进入，不再断言是对的，否则一次调用会记两条取证。
   - 反例断言了 `Recover`、`SetWeakness`、`PauseRecovery` 三条，用的是 `Assert.Equal` 精确序列，并逐条要求类型是 `write-off-executor`。
   - MU11a、MU11b 都让分类判据与句柄反例两条变红；`cmp` 还原后重建为 0 警告 0 错误（`logs/task-30-rework.txt`）。
   - 文档已按实情改写：`t009-criteria.md` §2.1、§2.4、§4、§8、§10，以及普查日志 V7 段。`violations.sh` 的值是 `GameplayWriteAssertionTests=53`，日志记为 `expected 53, recorded 53 [ok]`。
   - 这个修法比 Round 1 的首选修法更彻底：句柄以后新增的写成员，也会被分类判据要求断言。
2. **F2：已解决。**
   - `IsReassigned` 扫描类型本身及全部嵌套类型的方法（`GetMethods` 不含构造器），找 `stfld` 指向同一字段的地方。闭包和异步状态机里经 `<>4__this` 写回捕获参数的情形也在扫描范围内，规则写法正确。
   - 反例 `FakeCounter(int start) { Next() => ++start; }` 在 MU12 下变红（passed 5, failed 1）。
   - remarks 写明 `OrderedScope.set_Delivery` 属于每个作业一份的对象，不是单例。
   - 普查仍是 73 个持有者、0 违规，这条规则没有认出新的生产持有者。与 Round 1 探针的 IL 扫描结果一致。
3. **F4：理由成立，维持不改可以接受。**
   - 两个文件里都没有 `IServiceProvider`，被测服务全是手工 new 出来的。同文件原有的 `TestGameplay` 调用也都不传 services。
   - `TestGameplay` 的约定是「有容器时传容器」，这里不适用。Round 1 把它说成偏离约定，是评审的误判，在此更正。
4. **F3、F5、F6：§10 的补记准确**，与 Round 1 的原意一致。
5. **F-125：判断为与本单元无关。**
   - 失败栈的根是 `PvfEquipmentDefinitionSource.cs:160`，在 Infrastructure 项目里，本单元没有改动 Infrastructure。
   - 栈经过 `IllusionTowerProjectionDelivery.KillAsync:248` 和 `MonsterDeathSlice.cs:403`。前一个文件本单元只改了文件末尾的离场队列类，不在这一帧上。
   - 测试文件 `IllusionTowerPublicFlowTests.cs` 也不在本单元的改动里。
   - 失败的成因是随机奖励选中了一件 PVF 缺字段的装备，与执行器内外无关；`illusion-reruns.txt` 记录 5 次复跑全过。
   - 补充一点供主控方立项参考：这实际上是一个生产健壮性缺口，内容缺字段时奖励结算会直接抛错，不只是测试偶发。
6. **未重跑正式全量：可以接受。**
   - 本轮唯一的生产改动只把句柄上已有的断言从私有 `SetRecovery` 移到它的两个公开入口。会在执行器外抛错的路径集合不变，只有断言名字变了。
   - 第 1 轮的正式全量已证明这些路径在生产与测试中都没有执行器外调用。
   - 重跑 Application 与 Protocol，加上取证汇（61 与 1，类外 0，`exe:` 0），足以覆盖本轮改动面。折算数字 8302 与 73 的加法成立。

### 不阻塞的记录

- **复跑次数说法不一**：Result「第 1 轮评审后修改」Checks 3 写「单独复跑 5 ＋ 5 次」，而 `t009-criteria.md` §10、`follow-ups.md` F-125 与 `illusion-reruns.txt` 都是 5 次（每次 2 个用例）。不影响结论，提交前可统一为 5 次。
