# Diagnosis

1. `TryClaimSettlement` 只检查参与者自己的 `IsSettled`，尚未检查共享 `ClearOpen`。
2. 卡牌奖励按参与者保存是正确的，但 claimed 也完全按参与者保存，缺少共享列占用与 seat 快照。
3. `SelectCardResponse` 只携带本次 kind/column，codec 每次重建“仅该列、seat 0”的瞬时画面，组队时会丢失其他成员选择。
4. `ScoreScrollStateHandler` 立即附带 layout；当前没有结算后的 2 秒/4 秒 timer，也没有手动选择使旧 auto callback 失效的版本门。
5. `EplpCommandHandler` 与 `GiveUpGameHandler` 已按参与者 `End`，可复用 run id 与 frozen delivery registry 收缩语义；需要把 card timer/projection 也接到同一过滤规则。

上游 `ServerS4A21/Docs/ClockService.md` 与 `CardRewardCoordinator` 证明兼容节奏为结算后 2 秒 layout、layout 后 4 秒自动免费翻牌，timer 只触发真实翻牌业务并在回调前校验 run/phase/version。
