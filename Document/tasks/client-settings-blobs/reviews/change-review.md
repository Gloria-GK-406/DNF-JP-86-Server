---
kind: rks-review
verdict: changes_required
---

# 设置 blob 完整结果独立审查

日期：2026-10-13。目标：`task/next-round-20261013` 当前未提交成果，基线 `314bd46356a02c18e6d6f3ccb4354413f630fe1a`；排除 `Tool/WireSmoke/__pycache__/`。本审查未修改产品代码、未运行构建或测试。

依据：TaskContract revision 1、spec、acceptance；`reviewing-work/SKILL.md` 及 `references/code.md`、`references/deliverable.md`。已独立读取完整源代码、测试和协议差异、新增源文件与测试，以及相关命令管线、选角生命周期、在线身份和既有 C0 实现。测试执行与部署证据由后续 completion verification 判断，当前结论只覆盖交付结果审查。

## 需要修改

### F-1 [P2] 断线删除与 profile 激活交错后留下永久失效条目

证据：`Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs:75` 在取得生命周期门与在线用户门（98–103）之前调用 `ForgetConnection`，之后结束断线时没有再次清理。`Server/src/Dfo.Server.Application/Accounts/ClientSettingsDomainService.cs:49–53` 的 `IsCurrent` 检查与 `profiles[...]` 赋值是两个独立动作；`profiles` 为该单例服务的强引用字典（17–18）。

具体时序：激活命令取得执行门并通过 `IsCurrent` → 断线观察者执行 `ForgetConnection` 并等待该门 → 激活命令写回 profile 并释放门 → 观察者完成断开。字典会重新保存已经断开的身份，后续没有删除入口。每次遇到该交错都会累积常驻条目。`IsCurrent` 仍能拒绝这些身份的上传，因此本发现不是跨账号数据库污染。

最小修正：让精确连接断线结算完成后仍能清掉并发激活留下的条目，并避免误删替代连接的 profile；保留或等价实现断线期间阻止上传的保护。为激活与断线交错添加可控制时序的回归即可，不需要扩大到客户端字段语义。

## 已核对的关键边界

- C5/C6/170/C0 注册为 Transactional，账号与 online 执行门保护写入，C0 增加 `ISelectedSessionCommand` 后受到选角生命周期门保护。事务提交位于命令返回之前，成功日志在命令返回后；没有新增 ACK 或即时 NOTI。
- 解包严格拒绝不一致长度、额外尾部、非法 bank；150B 主设置、两个 300B bank 与最长 65536B 的键位原始 bytes 分别保存。SQLite UPSERT 仅更新对应列或账号＋profile 行；旧表、旧键位列保留，没有将二进制槽数据翻译成文本。
- AD 为三个长度前缀 blob；1C7 为类别＋完整变长 bytes，没有截 2B、添加尾 NUL 或发送旧默认槽表；187 沿用完整 516B。无记录和显式空 profile 在存储模型中可区分，空回放采用已有静态证据中的客户端缺省入口。
- 登录发送普通 profile，选角发帧类别与激活类别使用同一职业策略。职业10→1只作为服务端策略；当前 A21 空文本加载默认的资料明确标为 ClientStatic，未夸大成 ClientVerified。
- 连接身份 `GameSessionIdentity` 在认证、选角和返回选角时保持稳定，`Generation` 单独随转换变化。返回选角时允许 null selected context 沿用最后已下发 profile，但保存命令仍校验捕获的当前 Generation；新的非 null selected session 必须匹配已激活上下文。旧 token、旧 generation 和旧 selected session 不能排队串写。
- C6 上行不携带 profile 关联字段，服务器无法识别在新类别激活后才收到的旧类别上传；该限制已有静态记录并列入实机边界，不作为本次实现阻断。早到上传被拒绝及真实客户端恢复也应由实机验收判定。
- 新测试覆盖真实 SQLite 重开、提交／回滚、两个 profile、两个 bank、不同账号、显式空、旧 token／generation、生产 handler 保存与入场读取；TCP 测试增加上传后重连 AD/1C7 原始字节比较。已审查新增选角 transcript hash 和两个既有命令注册判据调整，未用实施方的通过报告替代代码检查。

本轮未发现其他需要阻断的实质缺陷。F-1 修正后应独立复核受影响的清理和激活路径；当前审查不宣布任务完成、部署就绪或实机恢复成功。

## Round 2：断线清理整改复核（当前有效裁定）

```yaml
kind: rks-review
verdict: approved
```

目标仍为基线 `314bd46356a02c18e6d6f3ccb4354413f630fe1a` 上的当前完整成果。按同一 Code／Deliverable 判据复核受影响源码、接口与新增测试，并重新核对完整变更清单；其余不变路径沿用首轮独立检查证据。没有修改产品代码、运行构建或测试。

F-1 已解决：`OnlineUserConnectionObserver.cs:110–118` 在取得 online 执行门、完成匹配身份的断线后，再调用 `ForgetConnection(observed.SessionIdentity)`。激活命令同样取得该 online 执行门，因此先前通过检查的激活只能在末次清理之前完成；之后的新激活会因在线身份已移除而被拒绝。

`ClientSettingsDomainService.cs:67–70` 先比对不可变 `GameSessionIdentity`，再以键和值进行条件原子删除；并发写入不同 token 或替代身份时，不会通过末次清理误删新 profile。早期 ID 清理仍保留，末次删除补上首轮指出的交错窗口。

新增 `ClientSettingsBlobTests.cs:139–160` 按早期清理→激活完成→断线→精确清理的顺序调用，另验证旧身份的晚到精确清理不影响相同连接 ID 的新 profile。该测试对新身份保护有直接断言；旧条目最终移除的结论同时依据上述持门结算与条件删除源码，不把它夸大为已执行的并发压力证明。

当前完整成果没有未解决的阻断发现，可进入后续 completion verification 与用户实机验收。首轮 `changes_required` 和 F-1 保留为历史；本节 `approved` 为当前审查裁定。执行方报告的全量／专项通过结果需在完成验证阶段保留原始证据，本审查不代替测试、部署身份检查或 ClientVerified。

## Round 3：最终 schema 判据差异确认（当前有效裁定）

```yaml
kind: rks-review
verdict: approved
```

有界复核 `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs:58–59` 的最终两行测试变更：总表数 77→78 与本任务仅新增 `account_hotkey_options` 一张表一致；新增列形状断言与 `CharacterDatabaseSchema.cs:305` 的 `account_id INTEGER`、`key_type INTEGER`、`payload BLOB` 声明一致。既有总数量与其他表断言继续保留，没有放宽判据。产品代码未变化，本轮未运行构建或测试；Round 2 的其余审查结论保持有效，当前仍为 `approved`。
