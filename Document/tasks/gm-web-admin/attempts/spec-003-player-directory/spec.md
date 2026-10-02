---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 服务概览与账号角色查询

## Why and Outcome

管理员能在中文后台找到全服账号和角色，以稳定 ID 打开关联详情，查看账号三币余额及当前在线、频道、位置。页面让管理员分清当前运行事实与保存资料，为后续明确选择操作对象提供只读查询能力。

## Scope

包含受 `QueryPlayers` 保护的服务概览、账号与角色列表、详情、搜索、在线筛选和刷新；覆盖隔离 SQLite、模拟游戏生命周期、真实 HTTP 与页面验收。沿用已接受的管理员登录、注销、权限和访问边界。

不包含任何玩家资产修改、邮件/点券发放、管理员管理、多服监控、实时推送或现服切换。测试使用隔离库和端口，不触碰 source checkout、现服、真实玩家库或客户端；本阶段证据不构成 `ClientVerified`。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 全部私有查询由服务器事实核验管理员 `QueryPlayers` 权限，沿用已有默认封闭、独立 GM 监听、远程 HTTPS 和 `no-store` 边界，不扩张公共健康接口。 |
| RQ-002 | 提供下述有界、可组合的全服账号/角色 HTTP 查询与 ID 详情契约；在线筛选在分页前执行，返回筛选后总数和稳定 ID 升序结果。 |
| RQ-003 | 只返回必要管理投影：账号身份、三币余额、角色数量/在线；角色身份、归属、职业/成长型/等级/删除标记、在线、频道和位置。无角色账号也能正常查询。 |
| RQ-004 | 角色 `name_bytes` 是姓名权威；严格 UTF-8 解码，异常或缺失有明确诊断，不用替代字符或存储的 `name` 冒充正确姓名；关联及后续目标始终使用 ID。 |
| RQ-005 | 账号在线以当前活跃、已认证的游戏连接为准，包含未选角账号，按账号去重；角色在线以当前有效且已发布的精确入场会话为准，旧连接、准备中与已退出会话不能冒充当前在线。 |
| RQ-006 | 在线角色当前位置从位置领域实时读取；离线保存位置明确标记来源。频道由实际 Game 监听与频道目录映射，人数由当前会话计算，不能使用静态 `CurrentUsers` 或把公网端口当本地监听端口。 |
| RQ-007 | 服务概览显示本进程游戏网络启用状态、游戏就绪状态、全服账号/角色总数、在线账号/已入场角色数、频道及各频道已入场角色数和采样时间。GM-only 且游戏关闭时查询正常，不谎报游戏就绪。 |
| RQ-008 | HTTP 经 CommandBus/领域服务取得只读投影；全服持久化查询和必要运行态枚举由既有状态所有者通过窄只读接缝提供。查询不创建账号、不执行选角恢复、不保存位置、不通知玩家，也不修改玩家或运行态。 |
| RQ-009 | 中文页面完成概览、账号/角色切换查询、搜索/在线过滤、分页、ID 关联详情和手动刷新；加载、空结果、输入错误、无权、会话失效、不存在和服务失败均有明确反馈，过时响应不能覆盖新查询或恢复已失权资料。 |

## HTTP 与投影契约

所有路径以 `/api/gm/v1` 为前缀，使用 `GET`、JSON camelCase，绑定 `QueryPlayers`。仅支持下表的查询字段；重复已知字段、无法解析值或越界输入返回 `400`。未知字段不改变查询含义。

| 路径 | 输入 | 成功返回 |
|---|---|---|
| `/overview` | 无 | 下述概览投影 |
| `/accounts` | 公共分页参数；可选 `accountId`、`q`、`onlineOnly` | 账号分页投影 |
| `/accounts/{accountId}` | 正整数 ID | 单账号详情，含角色数量；关联角色经角色列表 `accountId` 查询 |
| `/characters` | 公共分页参数；可选 `characterId`、`accountId`、`q`、`onlineOnly` | 角色分页投影 |
| `/characters/{characterId}` | 正整数 ID | 单角色详情及其账号必要投影 |

公共参数：`page` 默认 1、范围 1..1,000,000；`pageSize` 默认 20、范围 1..100；ID 范围 1..2,147,483,647。`q` 最多 128 个 UTF-16 code unit，去首尾空白后为空则无文本条件；`onlineOnly` 默认 false，仅接受 true/false。条件按 AND 组合，账号 `q` 匹配登录名、角色 `q` 匹配合法解码的权威姓名，均为区分大小写的字面子串；`%`、`_` 和引号不是通配符或 SQL。异常姓名可通过 ID 找到。

分页返回 `{ items, total, page, pageSize, sampledAt }`：total 为同次查询所有条件过滤后数量，items 为过滤结果的当前页；越过末页返回空 items、保留 total。无数据变化时重复请求及相邻页无漏项/重项；跨请求发生资料或在线变化时不承诺冻结名单。数据库资料与各运行态采样不承诺全局原子快照，`sampledAt` 为本次采样的 UTC 时间，页面显示本地时间及“可刷新”。

| 投影 | 必要内容及含义 |
|---|---|
| 账号 | `accountId`、`loginName`、`balances: { cera, tokenCera, happyTokenCera }`、`characterCount`、`isOnline`、当前有效已入场 `activeCharacterId`（无则 null）。三币为账号余额，保留 0..int.MaxValue 精确整数，不缩到客户端小整数或从某角色读取。 |
| 角色 | `characterId`、`accountId`、`loginName`、`name`、`nameDecodeStatus: valid/invalid/missing`、`nameBytesHex`（异常字节的十六进制诊断；合法/缺失时 null）、`job`、`growType`、`level`、`isDeleted`、`isOnline`、`channel`、`position`；单角色详情另带账号三币余额。已有持久化角色记录均可查询，软删除者显示删除标记。 |
| 姓名显示 | valid 使用严格解码的字符串；invalid 使用“姓名编码异常（hex:…）”；missing 使用“姓名字节缺失”。保留诊断和 ID，不将异常姓名匹配到另一角色。 |
| 频道 | 已知时含 `channelId` 和目录 `name`，无法映射时为 null、页面显示“未知频道”；离线角色无当前频道。按本地 GamePorts 与目录的既定顺序映射，尊重已有部署的回退语义，不凭 public port 相等猜测。 |
| 位置 | `source: current/saved/unavailable` 和 `townId`、`areaId`、`x`、`y`、`direction`（unavailable 时位置值为空）。current 只来自有效已入场角色的位置领域；saved 来自离线角色持久化资料；在线暂时缺少有效当前位置时 unavailable，不把保存坐标称为当前位置。 |
| 概览 | `networkEnabled`、`gameReady`、`totalAccounts`、`totalCharacters`、`onlineAccounts`、`onlineCharacters`、`channels`（目录 ID/名称及本次 `onlineCharacters`）、`unmappedOnlineCharacters`、`sampledAt`；目录不涵盖的会话计入 unmapped，静态人数不参与计算。只展示已有真实事实，不预设吞吐、负载或内存等指标。 |

详情和概览也包含 `sampledAt`。合法但不存在的 ID 返回 `404`；未认证/会话失效返回 `401`；缺权限返回 `403`。错误返回 `{ message }` 中文说明，失败不能包装为空列表/零余额/离线事实。服务器内部异常可返回通用 `500`，不得暴露 SQL、堆栈或秘密。

HTTP 返回字段采用明确白名单；不序列化原始 AccountRecord、OnlineUserSnapshot 或连接上下文。不公开玩家密码散列、管理员密码记录、玩家 Bearer、任何会话/连接秘密、连接 ID、IP、UDP 地址或不必要的私人资料。页面把姓名/登录名作为文本渲染。

## 运行态与边界

查询以 `ISelectedSessionSnapshotQuery.Snapshot`、精确发布状态与当前在线归属组合验证入场会话，在读取位置前后确认该会话仍有效，失效条目弃除或标记不可用。账号已认证连接枚举若需扩展，只由 OnlineStateRegistry 的领域所有者提供最小只读快照；不在 GM 模块建立第二份在线注册表。`SelectedSessionIdentity.ConnectionGeneration` 是发布前的不可变值，绑定角色后 online-user generation 会增长，二者不以数值相等作为已发布身份判据。

当前位置使用 `ICharacterPositionDomainService.Find`，不复用选角 entry 的位置。替换旧会话、断线及退出后再次采样须反映新事实，旧会话清理不能带走新会话的位置。概览与在线过滤使用相同在线定义，账号去重，频道计数只统计已入场角色。网络关闭/未就绪时仍能查持久化资料，在线数字由实际运行态回答；`networkEnabled=false` 与 `gameReady=false` 分别表达关闭和未就绪，不因 GM HTTP 可用而合并为 ready。

GM HTTP 不直接引用 Persistence/Protocol/Networking 或查询 SQL；命令 handler 只注入领域服务/领域 store，持久化分页与详情通过 typed 只读 Persistence 接缝由拥有查询的领域服务取得，可使用独立投影 record。既有账号花名册不能充当全服查询。保持一个 Host/DI，不复刻玩家身份或资产状态，不改变既有认证、审计和游戏流程。

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 对五类 GET 的实际 HTTP 请求逐项证明匿名/过期 401、已认证无 QueryPlayers 403、玩家票据不授权、允许管理员 200；远程明文拒绝、GM 监听隔离及 no-store 保持成立，概览不经 public health 绕过。 |
| AC-002 | RQ-002 | 隔离 SQLite 放入超过一页的账号/角色且在线对象分布于数据库前页之外；组合 ID/归属/q/onlineOnly 后分页和 total 均按筛选全集计算，重复及相邻页稳定，末页外与空库行为正确。page/pageSize/ID/布尔/q 的边界、重复和无效输入返回 400，合法不存在详情 404。 |
| AC-003 | RQ-003 | 含无角色账号、多角色账号、软删除角色，分别查列表及关联详情，身份/归属/职业/成长型/等级/删除标记与隔离库一致；三币覆盖 0、普通值及 int.MaxValue，准确来自同账号，查询不自动创建对象。检查 JSON 不含规定禁露字段。 |
| AC-004 | RQ-004 | 姓名字节与 name 文本冲突时以前者为准；中文、literal %/_、引号及含 HTML 的合法姓名按字面搜索/文本展示，非法 UTF-8 和缺失字节显示诊断，可按 ID 打开而不误寻址另一角色。 |
| AC-005 | RQ-005 | 用真实运行态领域服务模拟匿名连接→登录未选角→准备→发布入场→退出/断线，逐次请求证明匿名不算在线账号、登录计账号、准备不计在线角色、发布同时计账号/角色、清理后移除；同账号多连接去重，绑定后的 generation 差异不误判离线。 |
| AC-006 | RQ-005, RQ-006 | 入场后 StepTo/CrossInto 改当前位置、在另一频道发布新会话并清理旧会话，查询反映步行/换区/新频道及新位置；旧 entry 与保存位置故意不同，不能冒充 current。替换/采样交错不把旧会话位置归给新会话，在线无位置显示 unavailable，离线显示 saved。 |
| AC-007 | RQ-006, RQ-007 | 隔离配置使 static CurrentUsers 与实际人数不同、public port 与 local port 不同，实际两频道及无法映射会话的统计仍正确；概览总量与库、在线口径一致。GM-only 网络关闭时 GET 200、networkEnabled=false/gameReady=false；网络启用且就绪状态变化时两字段分别准确。 |
| AC-008 | RQ-008 | 检查依赖/命令注入与只读注册；对成功、无结果、失败查询比较玩家持久化资料及运行态前后无变更，未触发通知或选角恢复。已有管理员访问/审计及相关游戏生命周期回归通过。 |
| AC-009 | RQ-009 | 前端行为验证搜索/过滤重置页码、翻页、账号↔角色按 ID 关联详情、关闭详情及刷新；覆盖加载/空数据/400/401/403/404/500 和网络失败。快速连续查询及权限/登录身份变化时旧响应不覆盖当前结果，失权或退出后清除私有资料。 |
| AC-010 | RQ-009 | 在隔离 GM Host 上浏览实际构建的中文页面，使用概览和账号角色页完成搜索、分页、在线过滤、查看详情与刷新，观察中文标签/数值/位置来源/采样时间及成功或失败反馈；前端检查与构建通过，不以 mock 页面截图代替 HTTP 与运行态证据。 |

## Success Evidence

实施结果按 AC-001..AC-010 记录自动验证、构建与隔离页面检查的证据位置及未验证边界；SQLite/运行态/API 判据与浏览器页面观察分别列明。真实客户端仅保留根任务最终人工验收边界，本子任务不启动客户端或现服。上述结果交独立挑战与完整审查后，由主控决定接受；本规格不自行开启实施。
