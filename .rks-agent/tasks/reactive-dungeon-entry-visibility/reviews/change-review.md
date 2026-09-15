---
kind: rks-review
verdict: approved
contract:
  path: ../TaskContract.md
  revision: 1
target:
  baseline: 44cde9a7fe74928835757687961802a29d29af8d
  revision: working-tree-candidate
---

# Change Review: 响应式地下城入口可见性

## Round 1 — changes_required

- `CR-001`：领域参与者携带 `WireUserId`，违反领域与传输身份隔离。已改为只保留 `AccountId`、`CharacterId`、稳定槽位与队长标记，wire 身份在投影边界解析。
- `CR-002`：`FINISH_LOADING` 未冻结并复核 selected-session generation。已建立统一加载投影协调器，在投影 gate 与 session transition gate 内验证冻结代际、提交 CMD 并发送直接响应。
- `CR-003`：非请求成员投影失败被吞掉，可能形成部分成功。已将所有入口参与者改为 required delivery，并把失败纳入精确回滚。
- `CR-004`：旧修订失败可能销毁同一 run 的较新状态。已新增按 `RunId + EntryRevision + Recipients` 原子匹配的 `TryAbortEntry`。
- `CR-005`：`FinishLoading` 与 `DungeonEntry` slice 形成依赖环，且命令总线为可选依赖。已抽出共享 `Game.Dungeons` 协调层并将 `ICommandBus` 设为必需。

## Round 2 — changes_required

- `CR2-001`：`DungeonVisit` 失败仍按 `RunId` 粗粒度清理。已统一使用准备结果中的 run、修订和接收者做精确回滚，并覆盖 replacement run 与同 run 新修订保留。
- `CR2-002`：单人兼容路径在最新实例参与者为空时仍查找 participant，可能崩溃。已令单人最新快照明确使用 `Participant=null`，并补齐选图回归。
- `CR2-003`：required delivery 顺序发送时，后续成员失败仍会让已发送客户端停在半入口状态。已注入必需的 `INetworkConnectionCloser`，失败时隔离已发送与当前失败连接，再精确回滚；城镇成员不会提前移除。

## Round 3 — approved

独立审查确认上述八项问题全部关闭，无新增阻断项。领域参与者不再含 wire 身份；入口和加载完成都通过共享投影协调层；精确回滚不会破坏 replacement run 或较新修订；单人兼容和部分发送隔离已有回归测试。

## Residual Risk

自动化只证明实现达到双客户端实机验收条件。真实客户端是否最终创建并显示远端角色仍需合并部署后的双客户端验证，本审查不将其标记为 `ClientVerified`。
