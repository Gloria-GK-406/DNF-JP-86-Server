---
kind: rks-review
verdict: approved
---

# 规格审查

- 日期：2026-09-10。
- 审查者：独立子代理 `spec_reviewer`；未参与规格生产或挑战生成。
- 合同：`../TaskContract.md` revision 1。
- 规格：`../spec.md`，实读 SHA-256 `5CAC0B29FE1B4C5033B3D2FB7D23828F9DE5C56C821A0C04B202460C48A00E30`。
- 挑战：`./spec-challenges.md`，目标摘要与实读规格一致。
- 源码基准：`e567c803b99260786aaddb0d2d4612dc3334b6f4`。以下代码路径均相对任务工作树根目录。
- 证据限度：只读核验当前源码、测试及协议记录；没有执行真实客户端、故障注入或修改数据库。以下是规格缺口及可达的静态失败路径，不宣称已发生运行事故。

## 挑战裁定与最小修正

### CH-1 — confirmed，阻塞

`Server/src/Dfo.Server.Application/Users/UserDomainServices.cs:29` 调用 `GetOrCreateAsync`，`Server/src/Dfo.Server.Application/Accounts/AccountProvisioning.cs:24` 之后仅校验长度并转交写入，`Server/src/Dfo.Server.Persistence/Accounts/AccountTables.cs` 的 `InsertIfAbsentAsync` 原样持久化 `password_hash`，没有证明账号所有权的步骤。合同 Assumptions 已明确这批账号没有可信的已验证密码语义。RQ-007/AC-007 却把所有匹配 MD5 形状和值的行作为自动迁移凭据；格式验证不能提供缺失的来源信任。多个历史账号采用同一已知默认摘要时，这个策略会允许同一默认密码取得多个既有 AccountId 的票据。

最小修正：RQ-007/AC-007 必须把“不可信历史行”与“经受控方式明确允许迁移的行”分开，默认不得仅凭 32-hex 与摘要匹配自动认证或升级。可以采用默认拒绝旧认证、提供保持 AccountId 的受控初始化说明，也可以定义逐账号的可信迁移授权边界；无需新增管理后台。验收至少包含格式合法且密码匹配、但没有可信授权的历史行仍被拒绝且数据不变，以及受控初始化/迁移保留 AccountId 和角色关联。不能把整个旧库的一次性全局开关本身当作账号来源证明。

此项必须在实现前明确，因为它直接决定旧账号登录资格；不能交由实现者在互相矛盾的合同假设与 RQ-007 之间自行选择。

### CH-2 — confirmed，阻塞

`Server/src/Dfo.Server.Application/Users/UserDomainServices.cs:34` 在领域调用返回前执行 `state.Authenticate`；`Server/src/Dfo.Server.Application/Users/OnlineStateRegistry.cs:36` 与 `:207` 在普通锁内直接发布 AccountId、Authenticated 状态和新代次。`Server/src/Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs:34` 之后才提交事务。登录命令在 `Server/src/Dfo.Server.Protocol/ProtocolModule.cs:156` 注册为 Transactional + OnlineUser Exclusive，但独占执行门不会把未登记的内存写入变为事务参与者。

`Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs:65` 的先持久提交、后其他参与者提交机制是可用的实现基础，不构成现有认证发布已受保护的证据；`Server/tests/Dfo.Server.Application.Tests/Transactions/UnitOfWorkBehaviorTests.cs` 验证的也是登记参与者。RQ-009/RQ-012/RQ-013 和 AC-008 目前能约束普通并发消费，却未覆盖这条具体的发布早于提交路径。提交失败后连接是否立即被清理，不能替代未提交身份不可用的要求。

最小修正：明确兑换成功提交以前，不得发布可被后续业务使用的认证身份，也不得发送成功帧；提交失败或取消必须保留匿名/关闭状态且无成功通知。消费已提交而身份发布失败时必须拒绝该连接，票据保持不可重放。补充提交失败、提交前取消、提交后连接消失/发布失败的故障验收，检查在线身份和另一连接的兑换结果。只规定这些可观察不变量；事务参与者、发布顺序、执行门组合等机制留给 Plan，不要求新分布式事务或全局运行态重构。

此项应现在修正，因为“沿用当前链路”的自然实现会直接保留上述提前发布副作用，而普通成功/并发验收可以全部通过。

### CH-3 — confirmed（频道发现缺口）；downgraded（附加的真实客户端验证要求）

阻塞的是从 HTTP 入口到 Game 的广告链覆盖缺失。`Server/src/Dfo.Server.Host/appsettings.json:11`、`:21` 分别保留独立的 `Channel:PublicServerIp` 与 `Game:EndpointHost`；`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs:134` 起按频道配置产生 Host/Port，`Server/src/Dfo.Server.Protocol/Channel/Info/ChannelInfoResponseEncoder.cs:21` 将它们写给客户端。`Server/src/Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs:34` 还会独立投影 Game 成功响应端点。HTTP 仅配置正确初始频道地址时，并不保证后续广告地址正确。

`Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs:71` 直接使用测试持有的 Game 端口连接；规格 AC-011 同样没有要求消费 Channel Info，AC-016 又只要求 loopback 联调，因此“HTTP 入口正确、频道却返回客户端自身 loopback”的配置能逃过当前验收。该问题直接影响合同的实际客户端可访问端点及后端联动，属于需要补齐的接受边界。

最小修正：说明服务端部署者负责协调 HTTP 下发的频道入口、Channel Info 广告的 Game 端点及本登录流程实际需要的后续端点，使它们到达可兑换该票据的认证后端；说明相应配置和缺失/无效值的处理。增加 HTTP → 返回频道入口 → 消费 Channel Info → 使用所广告 Game 端点完成票据登录的集成验收，并用非 loopback 广告值验证配置投影，防止默认地址偷偷回落。无需规定这些 Host 字符串必须相同，也无需声称启动校验能证明外部网络可达，更不要求部署范围外的公网环境或未实现的附属服务。

挑战最后将“必须真实 A21 客户端完成本链路”一并列为最小修正，这部分降级为交付时的兼容性证据建议，不作为新增规格批准门槛。现有合同 SC-6 选择参数构造证据、SC-7 选择协议/身份集成证据，并未承诺真实游戏客户端烟雾；`protocol/catalog.md:282` 与 `protocol/game/server-to-client/CMD-0x0001.md:139` 已记录请求携带启动参数凭据。可复用现有客户端/逆向证据，实施中若出现票据槽被变换或截断的实际反证，再回交控制器处理，不在本次审查凭空扩大验收。

## 独立规格判断

其余主要接口已足够指导实现：INI 单 BaseUrl、HTTP 字段及失败码、35 槽参数、票据过期和原子消费、六条登录通知顺序、WPF 恢复状态与可视检查均有对应接受条件。成功通知顺序与现有 `GameLoginHandler.SendAccountSettingsAsync` 一致；现有 `GameLoginPayloadCodec` 支持两个最多 256 字节的 ASCII dstring 及尾部数据，为短票据接入提供直接接缝。没有发现需要改变 WPF + HTTP + Game 一体交付边界的独立阻塞项。

本次修正为 `bounded → spec`：三项均保留合同的原始结果与既定安全依据，补正旧身份信任条件、消费发布失败语义和已在范围内的频道发现验收。无需修改合同、添加新业务能力或重新追求更多形式证明。修正后复审上述未解决项及其直接回归即可；具体实现机制由后续 Plan 确定。

## Revision 2 有界复审

- 日期：2026-09-10；同一独立审查者 `spec_reviewer`。
- 复审规格：`../spec.md` revision 2，实读 SHA-256 `6970C36B34E013C91563F3AEF220D3AF5633FB9912EBDDD3EDBBE9F45F4890FE`；合同仍为 revision 1。
- 范围：原 CH-1—CH-3 的修正及直接回归。上文是首版审查的历史证据，当前门禁结论以本节及 frontmatter 为准。
- Verdict：`approved`。原三项阻塞均已在规格层关闭；本结论不代表尚未实现的功能或故障测试已经通过。

| 原挑战 | 复审裁定 | 关闭依据 |
|---|---|---|
| CH-1 | confirmed，已修正关闭 | Definitions、服务端旧账号迁移配置与 RQ-007 明确旧行默认不可信，只允许系统外核实历史凭据可作为迁移证明后逐 AccountId 授权；缺省空、禁止全局放开。AC-007 覆盖未授权但密码匹配仍拒绝且行不变、授权后错误密码拒绝、成功迁移保留身份关联、未知格式不迁移，RQ-015/AC-016 要求操作说明。保留外部账号核实责任是明确的信任边界，没有凭空把格式变成所有权证明。 |
| CH-2 | confirmed，已修正关闭 | 新不变量及 RQ-009/RQ-010/RQ-012 将持久消费提交设为身份发布的前提；提交失败/提交前取消不发布身份，提交后发布或投递失败关闭且不恢复票据。AC-009 与 AC-018 分别检查两侧失败及第二连接兑换结果，AC-008 同时保留并发至多一次提交要求。机制仍留给 Plan，没有要求改造全部运行态或实现分布式事务。 |
| CH-3 | confirmed 的核心已修正关闭；真实客户端附加要求维持 downgraded | 三类端点的定义、独立配置校验和 RQ-016 补齐 HTTP → Channel Info → 广告 Game 端点 → 同一票据兑换路径。AC-011 明确消费频道响应，后续端点逐值投影；RQ-015/AC-016 要求部署说明。规格明确配置校验不证明公网/NAT/防火墙可达，并维持公网部署和附属服务实现不在任务范围。 |

### AC-011 可实现性与修订直接回归

“受控测试网络”使非 loopback 广告值与实际测试机网卡配置可以分离：测试可用隔离网络，或以完整广告 host/port 为键的显式测试路由/转发，把已广告的逻辑端点接到本地真实 Channel/Game socket。测试必须分别断言线上返回的非 loopback 值及不同主机值，并且下一跳严格取自 HTTP/Channel Info 的解码结果；若广告端点缺失或被替换，路由/断言应失败。这样的路由只属于测试装置，不能进入启动器生产行为，也不能把“不论响应是什么都连接预先持有的 Game 端口”称为消费频道发现。

因此 AC-011 不需要公网部署、向外部地址发包或改变用户网卡，第三类端点只验证登录响应的配置投影，无需新增现有范围外的监听服务。这是该条“受控测试网络”和“证明配置投影与协议链闭合”的可实现读法；Plan 可选具体装置，真实 socket 握手、频道解码及票据兑换仍必须执行。该读法与 AC-016 单独要求的发布目录 loopback 联调不冲突。

逐项检查修订对身份保留、错误码、默认拒绝、事务提交、通知顺序和配置说明的直接影响，未发现新的阻塞性矛盾。无需继续增加规格条款或重新展开对抗；可推进后续计划阶段。
