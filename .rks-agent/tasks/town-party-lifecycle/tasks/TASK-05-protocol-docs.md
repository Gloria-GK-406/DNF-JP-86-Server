# TASK-05 — 同步 A21 组队协议资料库

## Task Unit Contract

- Revision: 1
- Outcome and references: 归档当前真实 C2S 样本并使协议页、索引、catalog 与实际实现和验证等级一致，覆盖 Spec RQ-012，Plan T-005。
- Owned boundary: `protocol/` 下 `0x0002/000A/000B/000D`、`NOTI 0x0007/0008/0009/000B/0099` 的记录和相邻 samples，以及 `protocol/game/index.md`、`protocol/catalog.md`；只记录 `0x01A3/0122` 和 `0x000A type=1` 的范围外观察，不实现它们。
- Required checks: 所有文档链接与样本可导航；实现映射对应当前代码；搜索不再留下错误的未观察/未实现状态；未实机确认项目保持 `Implemented` 或待验证，不得标为 `ClientVerified`。
- Dependencies: TASK-02、TASK-03、TASK-04 已稳定的字段、实现位置和生命周期行为。

## Result — Round 1

- **Changed:** 为 C2S `0x0002/000A/000B/000D` 与 NOTI `0x0007/0008/0009/000B/0099` 增补 A21 人工记录，逐项写明当前长度、字段、投递 audience/顺序、代码映射与待实机边界；删除 `0x0002` 已被 A21 新证据替代的 A12 人工块。更新 `game/index.md` 与 `catalog.md`，全部新组队能力保持“实现服务端功能/待 A21 双客户端实机验证”，未升为 ClientVerified。归档 2026-09-11 的两份 `0x0002`、两份 `0x000A` 真实 body，并把同一 UI 操作链中的 `0x01A3/0122` 样本和 `0x000A type=1` 明确记录为范围外观察；没有为未捕获的 C2S `0x000B/000D` 或任何新 S2C 通知伪造样本。
- **Why:** 资料库必须同时表达真实 wire 证据、静态/参照实现推导与自动化编码证据的不同强度，否则服务端“已实现”会被误读成客户端“已接受”。范围外帧只保留相邻时序，不把相关性升级为组队前置因果。
- **Checks:** 6 个新增 `.hex` 均为合法大写十六进制，长度依次为 31/31/9/7/11/8 字节；所改 13 个索引/协议文档的本地 Markdown 链接全部可解析；目标页搜索不再残留“尚无观察记录/本服务端不实现”的错误状态；`check-controls.py` 为 0/14 failure；`check-acceptance.py` 仅报告基线已有的两个无 census identity 文档（`0x0075.md`、`CMD-0x0040.md`），与本变更无关；`git diff --check` 通过。
- **Remaining:** 等待独立 quick review；通过后进入 TASK-06 完整验证与 A21 实机验收准备。

## Independent Quick Review — Round 1

```yaml
kind: rks-review
verdict: changes_required
```

- **Reviewed deliverable:** `Result — Round 1` 与当前 `protocol/` diff；限定 TASK-05 Task Unit Contract Revision 1、Spec RQ-012、Plan T-005。证据源为 `server-20260911-121053.stdout.log`、六份新增 `.hex`、当前 Party decoder/domain/notification encoder 与投递实现。
- **Confirmed evidence:** 六份新增样本均与日志逐字节相同：两条 `0x0002` 对应日志行 126/190，两条 `0x000A` 对应行 212/214，`0x01A3`/`0x0122` 对应行 208/210；长度为 31/31/9/7/8/11 字节。`0x0002` 两端点解为 NAT 5、内外网 `172.17.208.1`、LE 端口 30976/63752、LE MTU 1472，身份尾部为长度 12 与 ASCII `dab32f5d9d4f`。除下列 finding 外，C2S 允许长度、五种 S2C 长度/字段、建队与离队 audience/阶段顺序均与当前实现一致；`0x01A3`、`0x0122`、`0x000A type=1` 清楚标为只观察且 scope-out；新增状态均保持 Implemented/待验证，没有伪造 `ClientVerified`。
- **Mechanical evidence:** 13 份改动文档内共检查 1822 个本地 Markdown 链接，0 个缺失；`check-controls.py` 为 0/14 failure；`check-acceptance.py` 只报告基线已有的 `0x0075.md` 与 `CMD-0x0040.md` 两个无 census identity 文档；`git diff --check` 通过。

### Blocking Findings

- **IQR-05-F1 — `0x000A type=1` 真实样本的 `peerValue` 解码算术错误。** `protocol/game/client-to-server/samples/0x000A-2026-09-11-02.hex` 与日志行 214 都是 `02 00 01 2B 62 01 00`；按文档自己声明的 offset 3 `i32 LE`，值为 `0x0001622B = 90667`，但 `protocol/game/client-to-server/0x000A.md:65` 写成 `90795`。这使真实样本记录不再可复算，违反本 Task 的 source fidelity。请更正为 90667，并搜索同步任何沿用该错误数值的位置。
- **IQR-05-F2 — 文档把当前不存在的端点更新通知写成已实现行为。** `PartyDomainService.UpdateEndpoint` 写入端点并可递增 party revision，但在 `PartyDomainService.cs:39-42` 始终返回不带 snapshot 的 `PartyOperationResult.Success()`；`SetUdpIpPortHandler` 随后交给 `PartyNotificationDelivery` 的是空计划，因此不会发布 `0x0099/0x000B/0x0009`。与此相反，`protocol/game/client-to-server/0x0002.md:74-75` 声称队内更新会向当前队员投影 `NOTI 0x000B` 与状态刷新，`protocol/catalog.md:39,296` 声称 `0x0002` 可推/会推组队 NOTI，`protocol/game/server-to-client/NOTI-0x0099.md:71-72` 又把“端点更新”列为 `0x0099 → 0x000B → 0x0009` 刷新来源。该矛盾会让 TASK-06 等待服务端根本不会发出的帧。请按已批准实现边界统一修正文档为“端点值供后续形成/成员刷新快照使用”，或由 controller 另行决定是否先改变生产行为；两者不能同时保留。

## Result — Round 2

- **Changed:** 将 type 1 样本 `2B 62 01 00` 的 `i32 LE` 值更正为 `90667`；按 `PartyDomainService.UpdateEndpoint` 的真实返回值统一修正 `0x0002`、NOTI `0x000B/0099` 与 catalog：端点更新本身不广播，最新端点只进入后续建队或成员变化产生的 snapshot。
- **Why:** Round 1 把十六进制算术写错，并把未来可能的端点即时刷新误写成当前行为；两者都会使实机验收等待错误结果。
- **Checks:** 全树搜索不再出现 `90795`，目标文档不再声称单独 `0x0002` 会推 `0x0099/000B/0009`；`git diff --check` 通过。
- **Remaining:** 等待 Round 2 独立复核。

## Result — Round 3

- **Changed:** 将 `PartyCommandHandlerTests` 中取自同一 type 1 真实样本的 `peerValue` 从残留误算 `90795` 同步为 `90667`。
- **Why:** 即使该测试只断言范围外请求不改队伍，不读取 peer 值，样本化构造也必须保持可复算，不能与协议页产生第二套值。
- **Checks:** 排除历史 review 记录后，源码与协议树搜索不再出现 `90795`；聚焦 PartyCommand 测试重跑通过；`git diff --check` 通过。
- **Remaining:** 等待 Round 3 独立复核。

## Independent Quick Review — Round 2

```yaml
kind: rks-review
verdict: changes_required
```

- **Reviewed remediation:** 仅复核 IQR-05-F1、IQR-05-F2 及直接回归。`0x000A.md` 已把 `2B 62 01 00` 正确写为 `i32 LE 90667`；`0x0002.md`、NOTI `0x000B.md`、NOTI `0x0099.md` 与 `catalog.md` 已统一为“单独端点更新不广播，最新值只进入后续建队或成员变化 snapshot”，与 `PartyDomainService.UpdateEndpoint` 返回空通知计划的当前代码一致。Round 1 两项文档 defect 均已消解。
- **Evidence:** 目标协议文档中未再发现错误的即时广播表述；13 份目标文档的 1822 个本地 Markdown 链接全部存在；`check-controls.py` 为 0/14 failure；`check-acceptance.py` 仍只报告基线已有的 `0x0075.md`、`CMD-0x0040.md` 两个无 census identity 文档；`git diff --check` 通过。

### Blocking Findings

- **IQR-05-R2-F1 — 错误样本数值仍残留在活动测试源码，且 Round 2 的“全树搜索不再出现”检查声明不成立。** 排除必须保留原 finding 的 TASK-05 历史评审文字后，`Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs:85` 仍以 `requestType=1, peerValue=90795` 构造与本次真实 type-1 请求相同的目标/type 场景；真实 body `02 00 01 2B 62 01 00` 的值是 90667。该测试只断言非组队类型不派发，所以错误值不会使测试失败，但它仍是原算术错误在代码侧样例中的残留，并与已纠正的协议记录不一致。请将该测试输入同步为 90667（或明确改成与真实样本无关、不会被误认为证据的任意哨兵值），再由 fresh reviewer 复核全树搜索；Round 1 评审中的旧值作为历史证据无需删除。

## Independent Quick Review — Round 3

```yaml
kind: rks-review
verdict: passed
```

- **Reviewed remediation:** 仅复核 IQR-05-R2-F1 及 Round 1/2 阻塞项的最终状态。`PartyCommandHandlerTests.cs:85` 已将真实 type-1 样本对应的 `peerValue` 修正为 `90667`；活动 `Server/` 与 `protocol/` 范围内只剩测试构造和 `0x000A.md` 两处 `90667`，不再存在 `90795`。Round 1/2 的历史评审文字保留旧值作为缺陷证据，不构成活动实现或协议资料残留。
- **Evidence:** 聚焦命令 `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter FullyQualifiedName~PartyCommand` 新鲜通过 33/33；`git diff --check` 通过。复核 `protocol/catalog.md`、NOTI `0x000B.md` 与 NOTI `0x0099.md` 后，端点更新语义仍统一为“更新本身不广播，最新值进入后续建队或成员变化 snapshot”，Round 1/2 三项阻塞均已关闭，无新增阻塞 finding。
