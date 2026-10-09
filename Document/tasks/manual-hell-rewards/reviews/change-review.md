---
kind: rks-review
verdict: approved
---

# 主动深渊奖励独立结果审查

日期：2026-10-07。目标：基线 `9c6ff160817faa1e15e8999848bcbd6b548fc619` 上第 1 轮完整未提交改动，含审查期间的稀有度端点修正。审查时 HEAD 仍为该基线。18 个改动/新增 C# 文件的路径与 SHA-256 清单排序后合成指纹：`3EF6B379B3CEC6A6CF5D93F4950BA9D882882C7CDBE1C61C4589E2BE0C4F1813`。

依据：仓库 AGENTS.md、TaskContract revision 1、spec.md、implementation.md；采用 reviewing-work 的 Code / Deliverable 判据。独立读取完整 git diff、新增源码和测试，并追踪现有独立掉落、死亡 claim、地面编号、死亡投影和 GET_ITEM 持久授予路径。未修改源码、未运行构建或测试。

## 结论与关键证据

没有剩余阻塞缺陷，可进入最终验证与部署阶段；此结论不代表部署完成或 ClientVerified。

| 检查 | 独立检查结果 |
|---|---|
| 冻结与重复组 ID | DungeonCellFactory.cs:71、101 在 Cell 创建时冻结演员与组奖励；按 GroupIndex 合并，不按波次另建一份。读取已发布 Cell 不调用抽奖服务。 |
| 实际末怪资格与一次结算 | DungeonRunDomainService.cs:1799 以所有组成员死亡事实判断末怪，并用本次演员 EligibleForHellGroupReward 决定释放。1819 无论有奖、空奖或不合资格都标记 IsSettled 并清空 PendingLoot。 |
| 原子提交与重复死亡 | CommitActorDeathChange 持既有同一把锁；先生成/校验地面草稿并整体分配编号，再标记演员和组。无效奖励 Count 与编号空间耗尽在死亡/组更新前失败。重复 claim、重复死亡均不能再次释放。新增组成员与奖励数组在 adoption/snapshot 时复制。 |
| 普通路径 | 非深渊演员保留原 GenerateGroundAsync / GeneratePersonalAsync 路径；新增分支没有将普通 Boss 成长契约加成套到深渊整组。 |
| PVF 与奖励规则 | 内容源读取奖励难度次数、七列累计阈值、grade 窗口和 epicpieceinfo；创建率候选复用现有真实池；空池不改稀有度或重抽等级。演员资格读取各自 monster/APC 脚本的 [hell monster]。 |
| 地面/拾取/协议 | 新装备与碎片进入 Cell 已有地面集合并由 NOTI0026 投影。GET_ITEM 仍调用 ItemGrantDomainService，授予与 StageGroundDrop 沿已有事务路径收口，没有新增 wire 格式。 |
| 测试含义 | 新增测试实际断言不同死亡顺序、实际末怪资格、独立组、空奖、重复死亡、claim 释放和无效奖励不能半结算；工厂测试断言重复 GroupIndex 只冻结一份组奖。真实 PVF 测试检查 A/B 次数、七列、演员标记、真实池与生成物的 placement。 |

## 已闭合的审查发现

原 HellRewardRuleService 将 `0..1000000` 稀有度抽样与严格 `<` 累计比较组合，使 A 的最大端点进入 Chronicle、B 的最大端点进入 Epic，与 70-reference.md 的 `C[i] >= r` 比较不同。实现方已改为 HellRewardRules.cs:11 的 inclusive 比较，并在 HellRewardRuleService.cs:36 接线；HellRewardRuleServiceTests.cs:10 新增零值和各累计端点回归。源码复查确认该偏移已消除，无未闭合 finding。

## 验收边界

- 本仓尚无深渊 LuckPoint / 史诗药领域事实，未捏造这些加成。基础倍率 100 和碎片次数是明确的服务器/上游策略，稳定随机源不声称还原官方 RNG；文档保留这些限制。
- 调用方提供的 83 条定向测试和 5 条真实 PVF 检查是修正前证据指针；本审查没有独立重跑。最终验证应针对上述最终源码重新执行并保存结果，不能把已有 TRX 中的 80 条旧结果当作最终 target 的通过证据。
- 地面装备/碎片实机显示、实际拾取和客户端碎片表现仍需用户验收；已有 placement 可用性和通用拾取路径不等同于碎片书册的 ClientVerified。
- TaskContract 的发布构建、回退输入、进程/程序集身份、监听及 HTTP 健康属于后续完成验证，不由这份代码审查批准替代。
