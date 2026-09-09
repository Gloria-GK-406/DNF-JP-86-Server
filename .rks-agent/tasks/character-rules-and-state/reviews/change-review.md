---
kind: rks-review
verdict: approved
scope: change
reviewer: /root/review_complete
date: 2026-09-09
---

# 角色静态规则与动态事实：完整独立审查

结论：approved。完整变更满足 TaskContract SC-1—SC-6、spec RQ-001—RQ-006 的已授权边界，未发现需要阻塞交付的缺陷。本结论来自完整产品变更、依赖路径及实际验证证据的独立检查，不以三个 Task 的 quick-review 结论代替整体判断。

## 审查输入与身份

工作树：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-rules-and-state`；分支：`refactor/character-rules-and-state`。基线及现场 HEAD 均为 `8b19eaac9863e23da4703fa644bfd0cb882b6cd6`，审查对象为其上的完整未提交产品变更。

读取当前 `sdd-execution.yaml` review inputs：`TaskContract.md` revision 1、`spec.md`、`plan.md`，并遵循适用 AGENTS.md 与 reviewing-code 技能。没有配置需要另行裁定的 challenge set。

`reviews/change-review-input.json` 包含 75 个产品路径，SHA-256：
`F245E64E8F70E3D6E1428DC193F14C3E906E3EFD6B62BA699E3E3CDBF3A0247E`。
逐文件现场 SHA-256 全部匹配，删除项确实不存在；将 manifest 路径与基线 git diff 加全部未跟踪产品文件（排除任务工件）独立比较，集合完全一致。审查包含新文件正文、已有文件完整差异和删除测试的迁移去向；`git diff --check` 通过。

## 实现判断

- **职业与基础属性（SC-1/2）**：CharacterCareer 集中处理低四位转职与高四位觉醒，结构合法性和档案支持性分开。内容源保留 Single/Int32 精度；规则服务只发布完成后的表，支持取消后重试，并返回共享不可变值。1—86 级的未转职、转职及所选择醒分段和最终定点转换与独立算式一致。创建通过同一属性规则获取 1 级值；Host 在存在档案时于监听启动前初始化，缺档案仍保留原启动语义。
- **技能与账本（SC-3/4）**：逐段检查 maximum 多值、正觉醒上限优先及转职回落、零上限、required level/range、前置条件、固定等级标记与免费成长。累计费用重复末项，SP/TP 分开，实际投入减去最高免费基线；账本保留 long 负值及高额奖励，仅协议转换夹取 ushort。技能目录与规则缓存只以 byte 职业索引；生产内容集合为只读或 Frozen，规则复制并封装集合，不缓存角色等级、已学组合或余额。未知技能保留事实，显式标记不完整并记录警告，没有新增静默免费路径。删除的 Infrastructure 测试已迁至 Application 的真实规则测试，旧“按已学问题缓存”断言被共享静态实例断言取代。
- **当前事实与事务（SC-5）**：PlayingCharacter 只持 ID 与取数依赖；基础属性由一次 standing 读取决定职业与等级，技能状态读取当前普通页和奖励。沿 SqliteCharacterTable、CharacterSkillTables、CharacterEntryStateTables 到 SqliteDatabase 核对，读取仍加入现有环境事务，不增加第二个状态源。临时库用例证明同一角色对象可见未提交变化、回滚恢复、提交及新容器重载；slot、extra_values、活动页索引和未建模 stat 字段仍保留。
- **消费者与兼容（SC-6）**：检查创建、选角、技能对账与 PvP 初始页兼容接缝，以及全部迁移的副本选择、进本、击杀、结算、接任务、卡牌、新手升级消费者。副本选择先局部刷新当前基础事实再发帧；没有授予结果的经验发帧重新查询当前进度，有结果时继续使用本次写入回值。教程等级检查位于原有事务命令内，不能由旧会话等级把高等级角色降级；卡牌仍保留首次结算公布的价格。未修改存储 schema、网络编解码、协议注册或引入新技能协议，Application 项目依赖边界保持。
- **声明边界**：成长算法与相关技能语义为 PVF 及参考代码推导，并非客户端确认。普通页账本不代表自由决斗无限点数策略；事务外连续查询不承诺一个全局原子快照；会话整体聚合、装备/Buff 合成及部署均不在此批准范围。文档明确这些实际边界，未用其包装额外完成项。

## 独立核验的证据

逐条读取 `evidence/task03-tests` 中实际 `UnitTestResult`，最终各项目证据如下：

| 项目 | Passed | 条件跳过 | 采用记录 |
|---|---:|---:|---|
| Application | 1162 | 0 | Dfo.Server.Application.Tests-task03-full.trx |
| Infrastructure | 260 | 2 | Dfo.Server.Infrastructure.Tests-task03-full.trx |
| Networking | 52 | 0 | Dfo.Server.Networking.Tests-task03-full.trx |
| Protocol | 656 | 0 | Dfo.Server.Protocol.Tests-task03-full.trx |
| Pvf | 60 | 0 | Dfo.Pvf.Tests-task03-full.trx |
| Persistence | 333 | 0 | Dfo.Server.Persistence.Tests-task03-full.trx |
| Host | 6 | 0 | Dfo.Server.Host.Tests-task03-corrected.trx |

Passed 合计为 1162 + 260 + 52 + 656 + 60 + 333 + 6 = **2529**。Host 首轮实际失败为组合测试未注册 ILogger；现场夹具已加入 AddLogging，修正后的完整 6 项重跑通过。Infrastructure 的两条缺档案场景在真实档案环境中明确 NotExecuted，随后 `Dfo.Server.Infrastructure.Tests-task03-no-archive.trx` 中相同两条均 Passed。没有把跳过或旧失败当成通过，也未重复计入同一个用例。作者最终 `task03-final-build.log` 为 0 warning / 0 error。

本 reviewer 另作现场核验，均使用只读真实档案 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`：

1. 运行现有 Application 测试，筛选 CharacterStatArchiveTests、CharacterStatRuleServiceTests、CharacterSkillArchiveTests、CharacterSkillRuleServiceTests、CurrentCharacterStateTests；`--no-build --logger "console;verbosity=detailed"` 返回 0，**31/31 Passed**。其中完整属性 oracle 覆盖 174 职业组合、14,964 行与每行 19 个属性；本次初始化 388.139 ms，百万查询 63.901 ms，校验和 20186173950。临时 SQLite 的回滚、提交、重载三项也实际通过。
2. 在独立 PowerShell 进程加载现有构建程序集，通过生产内容源和规则实例只读遍历 job 0—13 的全部技能目录及档案声明职业。每个技能/职业组合调用等级 1、25、50、86 的 GetMaxLevel、GetFreeLevel、CanLearn，并调用累计 SP/TP；全部目录解析和组合调用完成，无异常。此探针补充覆盖整个技能目录的可执行性，不把“无异常”当作每个技能语义的独立证明；代表语义由上述真实档案与合成边界断言支持。
3. 直接读取真实十字斩 token，核对 required level=15、range=2、purchase cost=15 及 maximum/growtype 声明；[type] 的 [active] 为 QuotedString，与新解析器分支一致。

以上现场工具输出随本次审查保留在任务会话中；未新增探针项目或改变源码、测试、数据库结构和控制文件。审查写入仅限本文件。完整审查已结束并释放写入权；控制器仍须执行配置的 fresh completion verification，之后才能接受提交。

