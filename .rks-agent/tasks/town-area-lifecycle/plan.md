---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 城镇区域生命周期与主动退本归一

## Planning Choice

采用 `direct`。全部改动围绕一条共享状态链：区域实体提交事实，现有会话目录只在应用/协议边界解析当前连接，协议投影使用实体事件中的权威快照。拆成独立 Task Unit 会迫使中间版本同时维护两套 presence 真相，增加而不是减少协调成本；一份按依赖顺序维护的详细计划可以完整指导本次迁移。

## Approach and Shared Decisions

- 在 `Dfo.Server.Abstractions/World` 定义不含网络字段的区域值对象、成员资格代次、成员/快照、过滤器及变更结果；`Dfo.Server.Application/World` 实现进程级惰性区域 store 和实体。使用服务级同步边界原子维护 `区域键 → 实体` 与 `角色 → 当前成员` 反向索引，先捕获不可变结果再离开锁。
- 区域身份严格使用逻辑 `ChannelId + TownId + AreaId`。listener port 只属于当前连接解析，不再参与城镇成员分区。私有 gate 成员保存在同一实体但 `SharesTownPresence=false`，任何可见快照或移动听众只含自身。
- 成员资格代次由应用层从当前 selected-session generation 映射为不含网络含义的 `TownAreaMembershipLease`。领域 compare-and-remove/compare-and-move 只看 `CharacterId + Lease`，解决 replacement 安全而不保存 session。
- 实体变更返回领域事件/变更结果，不直接持有发送器。协议侧建立单一城镇区域投影协作者，负责把事件成员映射为当下仍匹配代次的 exact session、加载 runtime，并完成加入、离开、跨区和移动通知。各 CMD handler 只调用生命周期入口和投影协作者，不再自行查询听众。
- 初次选角保留现有“完成标记后再公布”的协议顺序：发布命令把实体变更及权威快照带到 SelectCharacter 协议层，由现有完成后通知点投影。其他城镇生命周期可在各命令的业务变更提交后立即投影。
- `ICharacterPositionDomainService` 继续保存回城锚点；角色在城镇时的实时位置同时写入区域实体。离开选图/进本只移除当前成员，不忘记锚点；真实 teardown 最后按原规则持久化/清理锚点。
- Party directory 由区域 store 在成员提交后以 `PartyDirectoryScope` 更新，离开城镇或私有 gate 对应 `null`。协议 Party 的区域听众查询改读区域快照，再通过 selected-session service 解连接。
- 主动退本仅在 `GiveUpGame` 确实结束一个活跃 run 时调用 `IPartyService.LeaveParty` 并沿用统一 Party 通知投影；`EPLP` 正常离开和 `BackToVillage` 取消不调用退队。

## Work and Dependencies

### T-001 建立区域领域模型与 store（RQ-001..RQ-004、RQ-008）

1. 先增加 Application 单元测试，覆盖惰性唯一实体、过滤返回快照、加入/替换、旧代次迟到离开、幂等离开、同区移动不改变成员修订、跨区双快照、私有 gate 隔离、空实体回收和 Party directory 镜像。
2. 在 Abstractions 定义最小公开契约，在 Application 实现实体与 store；不引入 Protocol、Infrastructure 或网络类型。
3. 以 immutable snapshot/result 作为所有投影输入，排序固定按 CharacterId，确保测试与协议输出确定。

### T-002 迁移 presence 查询消费者和会话桥（RQ-002、RQ-007、RQ-008、RQ-010）

1. 将通知 audience、Party directory/Party town projection、商店/技能/角色行为中的“是否站在城镇”和区域过滤改读新 store；需要发送时显式通过 `ISelectedSessionDomainService` 得到 exact session。
2. 重写选角发布与 cleanup：选角先发布 runtime/位置，再以 lease 加入区域；cleanup 先按 exact lease 捕获/提交离开结果，再执行其他资源清理。仅真实 teardown 的投影保留 `USER_LEAVE`。
3. 删除生产代码对 `SessionPresence`、`SessionPresenceScope` 和 `ISessionPresenceDomainService` 的依赖；更新 DI 与架构测试，避免保留双写兼容层。

### T-003 集中城镇投影并迁移移动/跨区（RQ-003、RQ-004、RQ-006、RQ-007、RQ-010）

1. 为区域变更建立协议侧投影协作者：从变更快照解析匹配 lease 的当前 session/runtime，分别生成 newcomer、existing、old-area roster 和 movement 投影；发送使用 exact-session audience。
2. 将 `SetUserPositionHandler` 改为 `MoveExact` 后交给协作者，保留原请求者直接响应语义并确保自己和同区可见成员各一份位置通知。
3. 将 `SetUserAreaHandler` 改为领域跨区，再由协作者按“请求响应/新旧区域成员投影/Party 刷新”的已验证约束发送；普通跨区不使用 `USER_LEAVE`。
4. 保留 gate 规则解析，但只把 `SharesTownPresence` 作为目标成员属性提交给区域 store。

### T-004 补齐选图、进本和回城生命周期（RQ-005、RQ-006、RQ-007）

1. `EnterSelectDungeonHandler` 对本次附着成员提交离开城镇并投影旧区域最终 roster，再记录 intent/发送选图帧；实际 `SelectDungeon`/开始地图路径执行幂等离开兜底。
2. `BackToVillageHandler` 清理对应 intent 后让每名返回者以各自锚点加入区域；权威快照包含区域原有玩家而非仅返回队员，并用同一回城投影同时发送直接响应与同区通知。
3. `EplpCommandHandler` 与 `GiveUpGameHandler` 回城同样调用区域加入；移除 `TownReturnProjection` 的 self-only fallback 或使其只消费权威区域结果，防止再次绕开 store。
4. 选角入口把区域变更交给既有 completion-marker 后发布点，维持客户端初始化顺序。

### T-005 实现主动退本等价退队（RQ-009）

1. 为 `GiveUpGame` 添加先失败的回归测试：非队长主动退出后成员被移除且其余成员收到新快照；队长沿用解散；无活跃 run 不误退；发送目标使用 captured exact sessions。
2. 在成功结束 run/清理 delivery registry 后调用现有 `LeaveParty`，复用 Party 通知计划与发送协作者，不复制协议组包。
3. 明确验证 `EPLP` 和 `BackToVillage` 路径保持 Party snapshot 不变。

### T-006 收口、删除旧模型并补生命周期矩阵（RQ-005、RQ-010）

1. 更新受影响测试 fixture，新增一个矩阵型集成测试覆盖“选角 → 移动 → 跨区 → 选图 → 取消 → 进本 → 正常回城/主动退出 → teardown/replacement”。
2. 搜索并清除生产代码中旧 SessionPresence 类型及按 listener port 划分城镇区域的残留；保留真正只属于 PvP listener 分区的逻辑。
3. 运行格式/编译反馈，处理 nullable、warnings-as-errors 和依赖注入启动验证；检查完整 diff 只覆盖本规格。

## Verification

- T-001 后运行 `Dfo.Server.Application.Tests` 中新区域 store 测试，预期全部通过，且领域项目无 Networking/SelectedSession 类型引用。
- T-002/T-003 后运行 Notification、Party、TownProjection 定向测试，预期已有跨区“不发 USER_LEAVE”、party projection last、replacement exact-session 测试保持通过。
- T-004/T-005 后运行 DungeonSelection、DungeonEntry、DungeonClear、SelectCharacter 定向测试，预期回城看到区域现有玩家、进入选图无幽灵、GiveUp 退队而 EPLP/BackToVillage 保队。
- 完成实现后运行 `dotnet build Server/86JP.Server.slnx -c Release`，再运行完整 `dotnet test Server/86JP.Server.slnx -c Release --no-build`；预期零 warning、零失败。
- 审查后重新执行受影响定向测试和完整 Release 测试，逐项把 AC-001..AC-009 的命令与结果记录进 `verification/completion-evidence.md`。
