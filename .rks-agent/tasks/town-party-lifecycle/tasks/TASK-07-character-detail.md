# TASK-07 — A21 当前角色详情查询

## Task Unit

- Outcome and references: 在 TASK-06 的 HP 修复完成后，实现 Spec RQ-011/RQ-012/RQ-015、AC-013 与 Plan T-007 的 `GET_USERINFO mode=3` 安全详情闭环。
- Scope boundary: 只覆盖当前请求会话对同一 Game listener 上唯一当前在线目标的详情查询、最小 A21 响应及协议文档；不实现离线查询、跨频道查询、社交关系、装备评分或写入行为。
- Inputs and dependencies: TASK-06 完成；当前 `GET_USERINFO` mode=2 角色列表路径；A21 客户端解析证据与同版本参照实现；当前会话目录、presence、角色运行时投影和协议资料库。
- Required checks: TDD 固定三字节 mode=3 分流；成功、离线、跨 listener、歧义 uid、请求者/目标代际变化；逐字节 A21 响应；mode=2 无回归；生产 DI 与分层测试。
- Rollback note: 角色详情新增类型、Command、Notification、codec、注册、测试和文档形成独立可识别改动；失败时回退该集合，不触碰已接受的组队/HP 路径。

## Detailed Plan

待 TASK-06 完成后，先从当前源码、A21 客户端解析记录与同版本参照实现确定请求/响应最小 wire 形状；再以失败测试固定安全查询与字节向量，依次实现 Data/Abstractions/Application/Protocol 边界和生产注册；最后运行定向回归并同步协议页。发送前必须再次验证请求者与目标的精确会话代际，任何拒绝路径不发送旧详情。

## Task Unit Execution Result

- **请求分流**：`GET_USERINFO` 保留 mode=2 的 `FFFF` 角色列表语义，并把非 `FFFF` 的 mode=3 三字节 body 解码为目标在线角色详情请求；没有扩大到离线或跨 listener 查询。
- **领域边界**：Application 领域服务验证请求者和目标的当前精确会话、目标 wire uid 的唯一当前映射、同 listener 以及当前 selected-session runtime，并在读取 runtime 后再次校验双方代际；拒绝结果不携带投影。
- **协议闭环**：Protocol handler 按 account 顺序取得双方 transition gate，调用 Query Command；只对成功投影向请求者角色发布 Notification。发送前 Notification handler 再次校验请求者、目标与 runtime 角色 identity，随后编码单个 `NOTI 0x0002` subtype=3 响应。
- **A21 wire 形状**：响应 codec 根据当前 A21 parser/同版本实现固定 subtype=3、角色基础信息、88B combat block、装备、clone/avatar、skill tree、两页技能、宠物、工会和 quest tail；空装备向量为 165 字节。`NOTI` header checksum/query answer 固定为 1。
- **回归证据**：Release 定向 Application TRX 为 5 Passed、0 Failed、0 Other；Protocol 角色详情 TRX 为 6 Passed、0 Failed、0 Other，另有拒绝结果不发布详情通知的显式覆盖。`git diff --check` 通过。
- **资料同步**：`0x0008.md`、`NOTI-0x0002.md`、catalog/index 已记录 mode=3 与 subtype=3 的当前实现及证据边界；在 TASK-08 当前 A21 客户端成功消费前保持 `Implemented`。
- **Result**：TASK-07 实现与自动化子结果完成，进入 TASK-08；这不提前满足 AC-013，也不代表 Spec/根任务完成。
