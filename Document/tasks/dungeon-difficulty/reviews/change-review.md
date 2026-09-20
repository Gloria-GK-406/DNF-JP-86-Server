## Round 1 — 独立完整改动审查

```yaml
kind: rks-review
verdict: approved
```

目标：`task/dungeon-difficulty` 工作树中相对 `026c4f9f` 的未提交生产代码、测试及任务交付内容；审查时 `HEAD=026c4f9f`。依据 `TaskContract.md` revision 1、`spec.md`、仓库 `AGENTS.md`，按 `reviewing-work/references/code.md` 与 `deliverable.md` 审查。结论仅表示本轮代码与相关交付物未见阻塞缺陷，不表示整体验收完成。

关键核对：

- `SelectDungeonDecoder` 对已确认的 A21 普通和教学 15B 请求读取 u32 地图编号、offset 4 的枚举难度，拒绝未定义值及不足 15B 的请求；`EncodeDungeonPermissions` 使用 u32 地图编号和 u8 状态。测试覆盖高位地图编号、普通/冒险样本与截断请求。
- `CharacterDungeonDifficultyDomainService` 按角色和地图返回缺省 Normal、聚合旧重复行最高值，并拒绝非法旧状态。`SqliteCharacterDungeonPermissionTable.RaiseMaximumAsync` 在同一个 `SqliteDatabase` 环境事务内保持旧行、只升不降；持久化测试覆盖隔离、重载、重复/较低解锁、旧重复行及回滚。
- 单人难度上限在布局、首房和 Run 创建前判断；拒绝不留下 Run。通过后布局、首房、换房和通知从同一 `DungeonDifficulty` / Run 状态取值，组队旧路径未附加新准入政策。
- 0005 由角色难度服务装配的权限列表编码；选图前 `currentViews.TryLoadAsync → LoadCurrentAsync → characterEntries.LoadAsync` 重新读取持久化状态，新通知先于选图状态通知发送，并非沿用登录快照。DI 注册与读取链一致。

已检查的现有运行证据：解决方案构建零警告零错误；Application 真实 PVF 测试 1639/1639、Persistence 361/361、Protocol 987/987；`git diff --check 026c4f9f` 无输出。上述测试日志由执行方生成，本次审查独立检查代码和日志，未以测试通过代替代码审查。

仍待后续验收：规格 AC-005 的普通/冒险客户端实机操作、0010 与 001C 抓包和服务端实例难度对照尚未执行，不能标为 `ClientVerified`，也不能据此宣称 TaskContract 全部完成。通关自动解锁、组队新准入政策和疲劳均保持在本契约范围外。
