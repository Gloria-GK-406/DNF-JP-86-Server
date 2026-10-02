---
kind: rks-implementation-report
spec: ./spec.md
spec_sha256: 958277A02E6AC3B67363D995E7728616939A57A752735B088D6EF2F08288EA0B
contract_revision: 1
---

# Spec005 实现交付

已实现按实际账号增加 Cera、TokenCera、HappyTokenCera 的后台模块，产品源文件已冻结供独立审查。此报告是生产者交付，不代表主控接受；主控实际 Chrome 证据见 [浏览器验证](verification/browser-verification.md)。

## Changed / Why

- 新增 GrantCera 独立权限下的窄账号选择、只读预览、整批提交、个人收据及查询入口。账号是唯一资产目标，离线、零角色和多角色账号均走同一账号钱包能力。
- 规则服务仅接输入及钱包 facts，生成不可变三币决定；命令固定完整账号范围，沿用事务前有序独占租约。事务内取得 SQLite 写边界、复权、读当前钱包，三币 CAS 必须恰好更新一行。余额、永久管理员范围 UUID 收据、一次成功审计共同提交，任一失败整批回滚。
- 收据保存规范化输入、提交时间及真实三币前后余额。原 UUID 同内容在并发、重启、账号后来删除及余额变化后仍恢复原事实；不同内容冲突。拒绝、确认回滚和未知状态分开，失败审计异常保持未知。
- 提交后使用中立 AccountWalletChanged 通知，投递边界解析当前精确选中会话；Protocol handler 仅注入领域服务，再读当前钱包并复核会话，复用现有 13B NOTI 0x0035。通知失败不改到账事实，不发送收据里的旧余额。
- 中文 `/gm/wallet` 页面包含跨页明确选择、三币编辑、预览确认、结果及个人记录。确认后固定 UUID 和输入，先成功保存 sessionStorage 再发送；刷新不自动 POST，未知结果保留原请求供查询/安全重试，401/403 及迟到响应不能恢复私有资料。
- [模块使用说明](../../../../gm-cera-grants.md) 提供限制、恢复步骤、fresh 隔离夹具与 A21 待验项；总体后台说明同步当前邮件/点券能力。NOTI 0x0035 文档仅追加 CurrentImplementation 接线，保留原历史证据。

持久实现保持 internal，建表仅在 Persistence/Schema；Application 不引用 Infrastructure/Persistence/Protocol/Networking。没有虚构角色 0、没有扩展 nullable 角色 effect，也没有改动其它消费流程。

## Checks

完整命令、失败与修复见 [生产者验证](verification/producer-verification.md)。真实点券 HTTP/SQLite 17/17；Application 相关回归 210/210；旧 GM HTTP 54/54；Protocol 相关 80/80；Persistence 初轮相关 27/27；全部前端行为 54/54；fresh 浏览器夹具 VERIFY_ONLY 1/1。上述运行 exit 0、skip 0。服务端解决方案构建 exit 0，0 warning/0 error；TypeScript/Vite 构建 exit 0，保留大 chunk 提示。

主控更宽回归发现旧 SchemaOwnershipTests 精确计数仍为54、实际新增收据表后为55（2 pass / 1 fail，原日志保留）。窄修复仅将计数更新55，并追加 gm_cera_operations 五列准确 shape 断言，未改其它产品。串行补跑目标 Schema 3/3 和完整 Persistence 396/396，均 exit 0、0 skip。原22文件清单保存为 [不可变 r1](verification/product-owned-manifest-r1.md)，当前清单23文件仅新增该测试 ownership。

TDD 价值门选择真实 HTTP 给零角色账号增款并同 UUID 重放，直接检查 SQLite 余额/收据/成功审计。实际 RED 为缺入口导致鉴权边界返回 403、预期 200 失败；随后最小实现修复编译问题并达到 1/1 GREEN。后续资产/页面测试属于扩展验证，不宣称所有代码均由 RED 驱动。早期失败日志完整保留。

所有运行在本 worktree 隔离 SQLite/端口；.NET 串行执行，PVF_ARCHIVE_PATH 明确指向只读 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。VERIFY_ONLY 夹具正常退出，最终检查 18084 无监听。未操作 Git、现服数据库、客户端或 PVF，未改合同、Spec、挑战集、根 map 或执行配置。

## Remaining

主控已实际使用本机 Chrome 完成 TokenCera 单账号+7、账号1/2批量 Cera 各+50、正常刷新/历史记录恢复及跨页1/21选择，只读核验2收据/2成功审计和准确余额，夹具正常 exit 0。该过程未制造浏览器断网；未知刷新恢复仍由前端行为测试提供证据。待主控完成独立漂移/完整结果审查与新鲜验证。

A21 三币展示、在线通知接收和重登余额仍是用户待验，自动证据仅为 CurrentImplementation。最终 CI、发布集成及整体验收表归后续 Spec006。当前 Vite 主 bundle 约 1.13 MB 的提示不阻断构建，可在后续集成评估。

完整产品边界及每文件 SHA256 见 [冻结清单](verification/product-owned-manifest.md)，不含 workflow 报告、旧 sealed 证据或派生产物。
