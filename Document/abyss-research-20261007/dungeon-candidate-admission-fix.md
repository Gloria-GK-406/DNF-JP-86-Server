# 同格候选与入场配置拒绝

## 故障与裁定

2026-10-07 21:24:23，测试服在处理卡勒特之初（地下城 72）入场 CMD 0x0010 时，读取 `dungeon/timegate/formacartel_r.dgn` 抛出重复候选异常，异常逃出 handler 后连接断开。

该文件任务 2389 maze 在 `(1,2)` 分别声明 `32984` 和 `32986`。这是同格资源候选，不能当成重复配置错误。用户授权“按你的想法实现后部署”。

## CurrentImplementation

- 候选按 maze、坐标、普通/Boss 类型合并，保留声明顺序并去重；重复 `[map specification]` 节也逐项完整解析。
- 文件语法、类型、截断、非法 ID 仍全局拒绝。几何问题记录在 maze 的 `ConfigurationError`，只有选中该 maze 才拒绝，不删除 maze 或重排协议索引。
- `ResolveTopologyAsync` 选择 maze、起点、Boss 并校验几何；`PrepareLayoutAsync` 检查最终资源。普通入口由 `ResolveAsync` 组合两步；主动深渊先加入深渊替换，再检查最终资源。
- 显式候选要求完整门布局与到达入口在同一 connection pair 中匹配。未声明 greed 的格不伪造四门约束。显式列表全部不兼容时，不回退到无关地图池。
- 入场前检查从起点可达的合法进门路径；Boss 是终点。检查不创建 Cell、不扣票或疲劳。实际 MAP 仍在首次访问时选定，同次 run 重访保留原 MAP。
- 仅在纯配置读取/规则调用处捕获 `InvalidDataException`，转换为普通 `ConfigurationInvalid` 或深渊 `HellConfigurationInvalid`。存储、库存、Cell 创建等意外异常继续传播。
- 协议层恢复选图意图，发送既有 CMD 0x0010 拒绝 ACK（`DungeonUnavailable=0x01`），日志记录具体配置诊断。没有新增客户端补丁或修改 PVF。

## 参照边界

**UpstreamImplementation / High**：`ServerS4A21/Tool/PvfLib/Models/DungeonFile.cs` 的 MAP 解析累加声明；`DungeonMapResolver.cs` 按坐标聚合候选、去重，并参考门布局。上游部分 fallback 行为未照搬，当前明确拒绝不兼容资源。

**70Reference / High**：`df_game_r` 的 `ImportDungeonScript`（VA `0x088E5FAA`）向 Grid 的普通/Boss vector 添加候选。70 一个 tag 表示一个格，重复完整 tag 会 clear 后重写；A21 一个 tag 含多行格声明，不能机械套用 70 的覆盖规则。两者都支持单格多个候选。

## 验证与实机待验

- 新断言先确认 RED，再实施 GREEN：重复候选、所选 maze 几何、完整门匹配、配置拒绝、深渊替换后预检。
- 真实客户端 PVF 的 Infrastructure 回归：802 通过、2 跳过、0 失败。
- 真实 PVF 地下城/深渊领域回归：312 通过、0 跳过、0 失败；包含地下城 72/77 各 20 个种子的最终深渊布局、门票与疲劳、房间重访。
- 入场协议回归：99 通过、0 失败；Release 构建及部署状态以本次部署记录为准。

以上为服务器自动验证，不是 ClientVerified。实机重点复测此前断线的卡勒特之初主动深渊入场、清房换房、返回已访问房间，以及正常缺票拒绝。部署保留旧 runtime 与 SQLite 一致性备份，运行身份、端口和健康检查写入 `artifacts/dungeon-candidates-20261007-<commit>/deployment.json`。
