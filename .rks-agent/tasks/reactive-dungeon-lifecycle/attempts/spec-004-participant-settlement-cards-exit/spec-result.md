---
kind: rks-spec-result
state: accepted
contract:
  path: ./TaskContract.md
  revision: 1
review: ./reviews/change-review.md
evidence: ./verification/completion-evidence.md
---

# Spec Result: 参与者结算、共享牌面与独立离场

结算、牌面和离场现在归属于同一个共享 `DungeonRun`：共享实例持有全队牌面占用与 revision，每名参与者仍独立结算、领取自己的奖励和离场。协议投影只处理当前 frozen generation，并把完整 seat 快照发给有效参与者，私有库存/金币只发给领取者。

牌面节奏统一为结算后 2 秒打开、再 4 秒自动免费翻牌。客户端主动打开、手动翻牌与自动回调复用同一领域占位和命令路径；瞬态列竞争会有界改选，离场、replacement connection 和新 run 会使旧回调失效。

翻牌的共享运行时事实与持久化奖励处在同一个事务边界：持久化成功后才发布 `DungeonCardSelectionChange`，失败不占牌。独立审查提出的事务、离场占位、双人自动竞争和可控计时问题均已修正并复审通过。
