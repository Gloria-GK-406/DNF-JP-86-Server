# Spec003 规格挑战

- Target: `Document/tasks/gm-web-admin/attempts/spec-003-player-directory/spec.md`，`state: ready`，绑定 child TaskContract revision 1。
- Target revision: 工作区 HEAD `416f26c9919383b58b0bd0e7179a99734fe53b48`；规格 SHA-256 `4D4349E0C10F6C90C4A1ACB13A082EEDB4896BDCC25BAA007B50891B74695169`。
- Governing sources: 根 `Document/tasks/gm-web-admin/TaskContract.md` revision 1（SC-2/SC-3、权限、领域边界）；child `TaskContract.md` revision 1（全部成功条件、在线/保存来源、历史姓名诊断）；工作区 `AGENTS.md`（分层、命令注入、只读查询）；`C:/Users/Gloria/.agents/skills/generating-adversarial-challenges/SKILL.md`。
- Attack scope: QueryPlayers 私有查询与最小投影；在线归属、当前位置、监听频道映射；筛选与分页；姓名字节；只读边界。只读源码核对，未运行共享 .NET、现服或客户端。
- Retained: high-risk 0，low-risk 4。以下是交给独立审查者的具体问题，不是审查结论。规格已给出结果约束和相应验收，剩余局部实现选择不构成要求新增规格证明的理由。记录创建后保持不可变。

## CH-1: 发布标记出现但位置尚未放入时，是否把保存位置误报为 current？
- Risk: low-risk
- Failure and impact: `activeCharacters.Publish` 和运行时发布标记已完成、`positions.Place` 尚未执行时，账号和精确会话可以满足在线定义，当前位置却还没有。若查询把非空位置视为在线的必要条件，会漏掉已发布角色；若从保存坐标补齐并标 current，会把历史位置呈现为正在使用的位置。这里只需让该状态按既定语义呈现在线和 unavailable；规格 RQ-005/006 与 AC-005/006 已要求，局部实现和可暂停发布的隔离测试能廉价控制，无需改变中央方案。
- Evidence: `spec.md:66-68` 要求精确发布、在线归属及读位置前后复核；`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs:243-244` 先绑定在线归属，再发布；`Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:357-359` 先标发布，再放位置。`CharacterPositionDomainService.cs:40-46` 的 Find 仅按角色返回坐标。需同时核对生产约束：`SelectCharacterPhaseCommands.cs:88-106` 在替换选择前完成旧会话清理，`SelectCharacterLifecycleServices.cs:150-154` 清除旧位置及旧发布，因此不能仅因 Find 不带 owner 就宣称存在生产可达的旧 owner 坐标混入。可复现探针：在新会话运行时发布后、Place 前暂停，请求详情/onlineOnly/概览，再恢复发布检查 current；退出后检查 saved。
- Minimum correction: 不要求修改规格；实现不得以保存坐标掩盖发布窗口，可在现有 AC-006 内纳入这一确定性交错。

## CH-2: 花名册的 COALESCE 是否抹掉历史姓名字节缺失这个事实？
- Risk: low-risk
- Failure and impact: 旧行 `name_bytes=NULL`、`name` 为合法文本时，若管理查询复用花名册 Name 表达式，返回的合成字节会被判 valid，并进入 q 的姓名匹配，管理员看不到缺失诊断；冲突行则可能被按非权威文本寻址。规格已明确选择字节权威、缺失诊断及 ID 目标，不应把保留 legacy name 的兼容性偏好反过来当成规格缺陷。typed 独立原始列投影与 AC-004 的小型隔离库场景即可控制。
- Evidence: `spec.md:28,47,54-55,79` 明确权威姓名、匹配和 missing 语义；`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:289` 的 name_bytes 可空；`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs:35-41,74-86` 明确旧行只有文本并使用 `COALESCE(name_bytes, CAST(name AS BLOB))`。可复现探针：同一隔离库放 NULL 字节/合法文本、字节与文本冲突、非法 UTF-8 三行；无 q 的列表与 ID 详情仍可打开，只有严格合法权威字节进入 q 匹配。
- Minimum correction: 不要求修改规格；新的管理持久化投影须保留真实 NULL/原始字节，不能借花名册兼容结果推断 valid。

## CH-3: 在线集合在计数与取页之间变化时，是否生成不属于同一筛选结果的 total/items？
- Risk: low-risk
- Failure and impact: onlineOnly 先取一次在线集合计算 total，取页或详情补全时又重新枚举/剔除失效会话，就可能返回 total=2、第一页空而第二页有项；反向顺序可能漏掉位于持久化前页之外的在线对象。跨请求变化和全局非原子采样是规格允许的边界，但不能借它为同一次分页的先分页后筛选、或各阶段换筛选集合开脱。RQ-002 和 AC-002 已直接控制，局部决定采样/失效处理方式并加入交错用例即可，风险无需阻塞规格。
- Evidence: `spec.md:26,49,77` 要求筛选前分页及同次查询过滤后总数；`:66` 允许失效条目弃除或标记不可用，实施时必须与分页含义协调；`Server/src/Dfo.Server.Application/Sessions/SelectedSessionDomainService.cs:14-17` Snapshot 是当次复制；`OnlineStateRegistry.cs:74-100,121-147` 能随后清理活跃归属。可复现探针：低 ID 页全离线、高 ID 两对象在线，pageSize=1；分别在在线枚举、计数、取页之间断开或替换一对象，核对 total/items 使用同一处理后结果，并确认复刷最终反映新事实。
- Minimum correction: 不要求全局原子快照或修改规格；在查询内部为筛选与分页选择同一名单/失效策略，沿用 AC-002 与 AC-006 验证。

## CH-4: 为省接缝复用现有账号查询/原始快照，是否意外写库或泄露非必要字段？
- Risk: low-risk
- Failure and impact: 现有账号入口 GetOrCreate 会尝试写库，AccountRecord 与 OnlineUserSnapshot 又携带密码散列和连接资料。若 GET handler 为搜索复用入口、或把原始 record 包到响应中，即使 QueryPlayers 门已正确，也会违反只读与最小投影，向后台暴露未授权范围资料。规格已明确禁止这两条路径并要求 typed 窄只读接缝，AC-001/003/008 的实际 HTTP 字段和隔离库前后对比可以在交付前廉价发现；不需要扩大本阶段到重做认证模块。
- Evidence: `spec.md:25,32,62,70,76,78,83` 已有权限、禁露、typed 投影、只读与验收要求；`Server/src/Dfo.Server.Application/Accounts/AccountProvisioning.cs:20-41` 查询入口实际先 InsertIfAbsent；`Server/src/Dfo.Server.Data/Accounts/AccountModels.cs:12-16` AccountRecord 包含 PasswordHash；`Server/src/Dfo.Server.Data/OnlineUsers/OnlineUserModels.cs:33-40` OnlineUserSnapshot 含完整 Connection。`Server/src/Dfo.Server.GmWeb/GmAccessBoundary.cs:21-46` 为 `/api/gm/v1` 执行监听边界、no-store、服务器身份和权限。可复现探针：无权限身份对各列表/详情/概览仍被拒；授权查询无对象/异常时账号数不增加；成功 JSON 白名单递归检查不含密码、连接 ID、IP、UDP/会话秘密。
- Minimum correction: 不要求修改规格；实施使用明确投影和真正只读的持久化接缝，所有新 GET 保留 QueryPlayers 元数据及既有访问边界。
