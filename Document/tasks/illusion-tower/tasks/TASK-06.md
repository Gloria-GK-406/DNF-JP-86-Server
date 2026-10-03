# TASK-06 — 塔成员与队伍变更的一致接入

## Task Unit Contract

- Revision: 1
- Outcome and references: T-004/T-005 的队伍生命周期接缝，RQ-005、006、009、010。退队、踢人、换队长、放弃和断线后塔成员及权限与实际状态一致。
- Owned boundary: 独立塔领域的必要leader用例、Protocol endpoint队伍协调、现有Party/GiveUp入口的最小分流、对应测试。保留普通队伍路径，不把PartyService整体改造成事务服务。
- Dependencies: TASK-04 的共享投影和已提交离塔receipt接口；TASK-05完成并交还产品/.NET写入权。
- Required checks: 无权限不变更；结算SQL失败保留原party与塔状态；成功离队/踢人回城；取消与通知失败不遗留可继续获奖的幽灵成员；显式/自动leader、部分成员已回城、旧请求与并发断线。

## Detailed Plan

1. 在Game endpoint协调共享用例，塔相关入口先丢弃真实 `InboundOrdinal>0 && !SequenceIsFresh` 的旧帧，再识别涉及的塔并捕获精确session、RunId/RoomVersion和party身份。获取run投影gate后，按AccountId排序锁定塔成员、当前party成员及caller/target的并集。锁内重新校验party key/generation/revision/leader/槽位/成员和exact session，名单变化则释放后重试或拒绝，禁止持account再追加run gate。没有wire run/version字段，不能把这些检查描述成能辨识任意伪造fresh-sequence旧载荷。
2. 普通Party路径在取得账号锁后也须重检塔归属：等待期间进入塔时，释放普通锁后转到塔入口，不能继续只改party。保持现有普通退队/踢人/转移行为；不重构无关邀请/设置路径。
3. 在塔事务前完成全部可预见拒绝：Leave必须为真实party成员；Remove必须当前队长、同队目标且非本人；Transfer必须当前队长、同队有效目标，目标本人为空操作。不能先离塔后才发现权限/party状态不允许。
4. 正常变更顺序：在上述锁内先dispatch Transactional的塔离开/leader同步CMD，成功提交后再dispatch现有Direct Party CMD，第二阶段使用CancellationToken.None并且没有新的业务读取或可取消等待。这样已知资产/存储失败发生在party修改之前；不把持久化写入塞入新的Direct复合CMD，也不在CMD内嵌套dispatch。
5. PartyService为即时内存变更；其ProtocolPartyClientProjectionService包装器可能在内存修改后因wire ID投影而抛异常，不能把异常当成“party未改”。完整成员锁保护已验证的第二阶段条件，意外提交后失败需记录并在释放所有gate后隔离精确受影响连接，由cleanup解除归属，不重发奖励或宣称跨两套状态有数据库原子性。
6. 成功离塔使用TASK-04的 `ProjectCommittedDepartureAsync` 消费已提交结果，不再调用Leave第二次。公开方法会获取run→account锁，因此必须先释放本协调入口的锁；在锁内捕获原party通知精确受众，投递party通知后再投递塔剩余成员和回城。发送失败后的连接关闭一律在释放gate后执行。
7. 塔自动继任按实际成员的最小party slot，与PartyService保持一致。显式转移时目标仍在同塔才同步塔leader；目标已回城时保留实际塔host，不能创建幽灵leader。既有PartyLeadershipChanged通知会把UDP host发给全队，必要时随后给塔内精确成员发实际塔host更正。领域方法表达同步已有队伍变更的用例，不绕过领域直接改snapshot。
8. GiveUpGame的塔分支须保留当前普通放弃会退队的用户可见行为：真实party中走同一已验证的离塔+退队流程；单人虚拟party可直接离塔返回，不因普通LeaveParty的拒绝卡住。BackToVillage/结果返回只回城是否保留party沿现有行为，不能把两种语义混同。断线已在SelectedSessionCleanup先离塔后清party，检查其继任/Ready放行与本单元没有重复通知或反向锁。
9. 使用真实塔领域/事务/party的组合测试，至少覆盖：SQL结算失败后party/tower/资产不变且重试一次成功；非法踢人/过期槽/虚拟退队不动塔；塔提交后请求取消仍完成party阶段；三人中第三人断线并发能结束；自动继任/转移后旧leader不能start/next，新leader能；部分成员回城后转移不产生幽灵成员；发送失败和重复动作不重复结算、不投递给替换session。
10. 执行相关Party、塔、普通副本及架构测试，所有.NET串行。只用隔离测试环境，不操作在线Host、客户端或玩家数据库；不提交Git。结果记录实际命令/数量、失败处理的边界和未验证事项。

## Evidence and constraints

前置静态调查定位：`PartyCommandSlice`现有Leave/Remove仅清普通run；`PartyService`同步修改、按最小slot继任；`PartyClientProjectionService`在内存变更后投影wire ID。TASK-04返回可能使party与塔的成员集合不同，不能以party名单覆盖塔事实。现有工作序列和返回能力见TASK-04结果；只有本单元回归完成后才能主张队伍接缝闭合。

## Result — round 1, 2026-10-03

- Changed: 新增 `IllusionTowerPartyCoordinator`，统一协调Leave、Remove、Transfer和GiveUp的塔/Party双状态变更。入口先按真实序列元数据丢弃旧帧，再从 `IIllusionTowerDomainService` 的run事实识别塔归属；锁顺序固定为run投影gate，再按AccountId排序获取塔成员、party成员、caller与target的账号gate。锁内重新核对party key/generation/revision/leader/slot/member、exact selected session与RunId/RoomVersion，变化则释放后重试。
- Changed: 所有可预见权限拒绝都在塔事务前完成。塔成员离开或leader同步先走Transactional CMD；提交成功后立即用 `CancellationToken.None` 调用现有Direct Party CMD，中间不增加业务读取。请求在塔提交后取消仍完成Party变更；若Party包装器已写内存后抛错，协调器在释放全部gate后关闭仍为exact的受影响连接，避免把部分提交误报为未写。
- Changed: Party Leave/Remove/ChangeHost与GiveUp接入协调器，普通路径在账号gate内复查塔run，发现等待期间入塔时先释放普通gate再重路由。真实party GiveUp同时离塔与退队；单人虚拟party GiveUp只离塔。普通副本路径、普通party通知与Remove拒绝帧保留原行为。
- Changed: 塔leader同步成为明确领域用例。塔内目标接任时同步塔host；目标已回城时保持实际塔leader，并在party通知后向塔内exact成员发送实际 `UdpHostNotification` 更正。外部party leader离开、塔成员按实际slot自动继任、第三成员并发断线后的重新捕获均有测试；旧leader失去start/next权限、新leader获得权限由真实领域测试验证。
- Changed: 已提交离塔结果只由 `ProjectCommittedDepartureAsync` 投影一次。party通知、剩余塔host与回城投影全部在协调gate外执行；发送失败只关闭仍匹配捕获身份的exact session，替换连接不会被误关。真实双人入场后踢人用例确认目标离塔、离party并重新进入TownArea。
- Changed: 独立review发现的R1同时修复：塔party成功入场提交后、任何角色Refresh或发送前，消费所有仍为exact的非requester `DungeonEntryIntent`；失败路径仍保留原intent。真实双人入场测试确认requester预先消费、attached成员提交后消费且不会恢复。
- Why: 塔结算含持久化资产写，PartyService则是进程内即时状态。先完成塔事务可以让已知SQL失败发生在Party修改前；Party第二阶段无法与数据库组成原子事务，因此意外的写后失败只能隔离当前连接并让既有cleanup收敛，不能安全重放。
- Checks: [TASK-06 bounded verification](../verification/TASK-06-verification.md)。Application塔领域/规则51通过；Protocol塔、Party、选图、普通离场、死亡/击杀159通过；Networking塔帧1通过；Application Architecture 39通过；`git diff --check`通过。所有PVF命令显式使用 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
- Remaining: 本单元未执行A21实机，也没有把无wire run/version的请求描述为可识别任意伪造fresh-sequence旧载荷。最终全解决方案、全PVF与独立累计review由根流程执行；未启动Host/客户端、未部署、未访问玩家数据库、未提交Git。

### Stable party/tower coordination contract

- 塔相关party变更的锁顺序只能是run投影gate到排序账号gate；普通party路径若在账号gate内发现塔run，必须释放后重路由，不能反向追加run gate。
- 可预见拒绝必须发生在塔事务前。塔已提交后的Party CMD使用 `CancellationToken.None` 且不得插入新的业务读取；包装器写后失败按部分提交处理并在门外隔离exact连接。
- 塔成员资格与实际host只能来自 `IIllusionTowerDomainService` 的run事实。Party只提供party身份、权限、slot与通知计划，不能覆盖塔成员或把已回城target设为幽灵host。
- `ProjectCommittedDepartureAsync` 消费已提交结果，不再次调用Leave。传输失败关闭连接前必须重新确认捕获的 `SelectedSessionIdentity` 仍为当前身份。
