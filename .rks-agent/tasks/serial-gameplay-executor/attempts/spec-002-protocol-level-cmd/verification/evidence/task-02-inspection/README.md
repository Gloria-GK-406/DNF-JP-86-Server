# TASK-02 执行证据与复现边界

本单元只新增守护测试，四处产品原保护均恢复为 HEAD e0604195 的原始字节。F-132仍是风险机制未修复、生产禁止引用该延迟API；没有执行坏API或宣称提交后组装风险修好。各轮实际命令、cwd、HEAD、环境、UTC起止、退出码和children_maxrss在B的verification/logs/task-02-*.json，对应.log原样保留。Darwin maxrss单位bytes，只代表所取子进程观测，不代表机器/完整树峰值。

## Test-value gate

| 保护 | 可观察合同 / 现实漏失成本 | 原覆盖不足与本次oracle |
|---|---|---|
| Recover handler IsInWorld | 仍有selected会话但未在场时静默忽略，不能发拒绝回包或改金币/虚弱 | 原旧CMD替换会话测试不能证明handler提前返回；真实selected未EnterWorld输入，移除门产生error4消息，精确静默断言红 |
| Teleport Owns(previous) | 当前角色不可借另一generation的town lease启动物品用例 | 正常/重放/下层拒绝不能守该调用前置；真实town/currentView配合只记录命令边界，不伪造无效租约，删Owns使0次入CMD变1。其余资产/位置/冷却/无回包保持 |
| Safety DiscardExact | 不确定结算不能留下旧runtime、待奖励/投影或离场保存缓存；旧代封锁不得清新代 | 原直接调用runtime.DiscardExact只守实现，不能守Safety调用点；真实SQLite/runtime/Safety，删调用后DueSession仍在；恢复后旧缓存不落库且新代保留 |
| Ledger二次DemandCurrent | Capture后物理连接变旧，必须在handler/ordinal登记前拒绝；同ordinal恢复后仍首次接受 | 原物理context独立ledger测试没造Capture与登记间窗口；局部query装饰器首查真、二查替换物理对象，真实Safety/Ledger执行，不经后续pipeline掩盖 |
| F-132禁用判据 | 生产不能启用已知未修复的事务内延迟组装路径 | 仅grep同名会漏别名/async并误禁其他composer；同一IL检测器扫描实际12生产程序集，解析method身份，ctor/async坏例命中，同名非目标不命中；坏调用不执行 |

四行为例只依赖保留的当前性/静默/精确代数/序号语义，后续CMD迁移可适配调用入口继续保护同一行为，不冻结旧构造器或旧pipeline。Teleport的命令边界计数是本次明确守护的“无权租约不得启动结算”，不是任意内部协作者偏好。F132是可重复的架构策略，使用静态IL判据而非伪装成游戏业务测试。

## 真实发现与非RED尝试

- guards-setup-01是TeleportRequest目标类型编译修正，未运行，不算行为RED。
- guards-setup-02发现原规划候选不成立：Fence不改变OnlineStateRegistry.InWorld（它检查phase/owner，fence另有安全语义）。因此Assert.False setup失败，不算保护被杀RED。
- 经主控continue，在同一白名单用真实ReleaseExact→Select同session但不EnterWorld建立Selected phase；FindByConnection仍是同session、IsInWorld=false。guards-setup-03四例4/4通过。未改产品来迎合测试。
- 正式首轮自检发现Safety输入还应确保产生实际变化对应的pending projection；最终加强只在新例中推进后再次Advance并要求changes非空，再验证Block清空，重做Safety单点变异及App全量。原轮保留。

## 变异与方法身份

`../task-02-preflight/mutations.json`列四个原字节SHA、单点移除内容、RED/GREEN日志及恢复结果。`scripts/task02-mutations.py`是最初四项逐一变异的实际执行器；不得在其它任务写入/构建时重跑，它会暂改四产品文件，finally恢复。后续Safety加强轮另有独立脚本和日志。`scripts/task02-edit.py`仅是最初测试插入记录（含随后已诊断的Fence候选），不是最终测试生成器，不应重跑覆盖当前源码。`scripts/task02-run.py`为日志/元数据runner，正式test轮用独立新鲜DFO目录。

Host IL判据读取全部DeclaredOnly方法、构造器/类型初始化器及编译生成MoveNext/闭包，按operand长度推进（含switch），解析InlineMethod/InlineTok，比较真实GameplayOutbox.ComposeAsync泛型definition的Module/MetadataToken。解析失败带程序集/方法/token报错，不吞潜在引用。实际程序集：Dfo.Pvf、Abstractions、Application、Data、GameServer、GmWeb、Host、HttpApi、Infrastructure、Networking、Persistence、Protocol。Utilities当前仅csproj、没有源码且未引用，不在闭包；未来生产集合变化必须扩展覆盖，不能据此宣称扫了未加载程序集。

## 证据索引

- products-before.json及mutations.json：`../task-02-preflight/`。
- source-checks.json：四产品恢复SHA、最终产品diff0、各测试前后行尾、无新全清、diff-check。
- owned-tests-sha256.json：最终5测试精确身份；tests.diff保留已有测试文件增量，新Host文件本身为权威源。
- test-accounting.json与Task Result：最终实际全量数量、退出码和追加/删除账。
- 原始正式DFO目录和violations.py partial核验日志：只要求每轮实际运行程序集control，独立重跑不复制/混合jsonl；exe0仅为观测边界。
