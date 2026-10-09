# 实现记录

基线：9c6ff160817faa1e15e8999848bcbd6b548fc619；2026-10-07。

新增 HellRewardDomainService / HellRewardRuleService / PVF 内容源。Cell 工厂冻结演员独立与个人任务掉落；重复 GroupIndex 的演员共享一份组奖励。Cell 聚合在实际末怪死亡时按该演员资格释放一次，空奖励与不合资格也清空 PendingLoot 并标记 IsSettled。死亡提交继续使用现有锁、claim 和地面编号。

当前 PVF 核验：20444 是带 `[hell monster]` 标记的第一波演员，440 无此标记；不能把所有 APC 当成奖励组。装备奖励次数 A=8、B=6 从 hellparty 读取。稀有度按本资源的七列累计阈值读；等级上界按70的 exclusive upper 转换为 inclusive band；装备候选复用当前真实 `[creation rate]` 池。

装备门沿用70的 0..1000 inclusive 比较；零概率显式关闭。基础倍率为服务器策略 `HellRewards:EquipmentRatePercent`，默认100，参考上游，不声称是官方概率。随机源复用本仓稳定 DropRandom，不声称是客户端/70 的 RNG。碎片映射读取 epicpieceinfo，尝试数采用上游普通难度5/6/8/9/10，A加1；空匹配池不换稀有度或等级重抽。

当前仓库未提供史诗药效果/角色 LuckPoint 的深渊加成领域事实，所以没有凭药剂ID或70偏移捏造这些加成。已有演员独立掉落会读取当前玩家等级/任务事实并冻结；普通掉落/成长契约的 Boss 加成不错误套到深渊整组奖励。`[item drop rarity control]` 保留未消费边界，官方 Luck 修正未还原。

自检：新增测试是实现后的回归证据，不声称 TDD RED/GREEN。验证与部署证据见 verification/completion-evidence.md 和外部部署记录。
