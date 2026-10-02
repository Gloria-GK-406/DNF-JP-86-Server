---
kind: rks-review
verdict: changes_required
---

# GM Web 完整根交付独立审查

审查目标为根合同修订1的完整中文 GM 后台：独立启动与发布、独立管理员和审计、全服查询及当前会话、官方邮件及附件领取、账号三币发放、React 页面与最终验收交接。审查的是整个原基线 `5fbe2dad35bf871a048ce4667055751e973bd844` 到接受 HEAD `a4195c310a3b11eb5e8c1798cc0680ccdec35a44` 的集成产品，分支 `feature/gm-web-admin`，唯一工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`。

producer 为 root；审查者 `/root/gm_root_review` 未参与产品实施。本轮按 `reviewing-work/SKILL.md`、`references/code.md` 和 `references/deliverable.md` 检视，读取工作区 AGENTS、根 TaskContract、multi-spec-map 和六份 Spec。没有另行生成挑战、委派代理或扩张合同。child 的既有审查仅作为追溯指针，未据其 verdict 代替当前源码判断。当前三处未提交工作流游标文档与接受源码 HEAD 分开核对，不作为产品缺陷。

**判定：changes_required。** 发现一处具体邮件安全恢复缺陷，阻塞 SC-4 和完整集成验收 SC-6；其余已授权范围未发现另一个 material defect。当前自动验证通过不能覆盖下面这个缺失的组合场景。

## F-001：身份拒绝会删除先前未知邮件请求的防重编号

定位：`Web/gm-admin/src/OfficialMailView.tsx:76-77`，以及该文件第23行的 `forgetPending`。

`send` 捕获失败时，将所有非 unknown 的 HTTP 4xx 归入确定拒绝分支，先 `forgetPending()` 删除当前管理员的 `sessionStorage` 待确认 UUID/冻结输入，再调用 `refused` 处理401/403。`GmAccessBoundary` 的身份/权限/CSRF拒绝响应只有 message，没有 outcome，因此它们满足这个分支条件。身份拒绝只能证明这次重试没有获准执行，不能证明同编号之前的请求没有提交。

具体触发链：

1. 邮件已持久提交，但发送响应丢失或超时，页面保存原 UUID 与冻结输入。
2. 管理员会话到期，或 SendMail 被撤回；在待确认页面按原内容安全重试，服务端返回401/403。
3. 页面清除原待确认记录并隐藏私有资料。重新登录同一管理员，或恢复权限后重新进入邮件页，原请求无法自动恢复。
4. 操作者重新预览相同邮件时生成新 UUID。服务端防重键是 `(administratorId, requestId)`，新编号会作为新发送创建第二批邮件及附件；原编号的持久防重正确仍无法防止这个页面造成的重复发送。

这违反根合同“重试不得重复到账”，及 Spec004 RQ-007/RQ-009 的未知结果保留原编号和冻结输入承诺。当前邮件测试分别覆盖未知重试和身份拒绝清除界面，却没有验证两者串联后的待确认存储；其通过不反驳该路径。对照 `CeraGrantView.tsx:54`，点券实现已在401/403时优先处理身份拒绝并保留待确认存储。

最小修正：邮件发送的401/403分支保留原管理员的待确认 UUID/冻结输入，同时继续清除可见私有资料、执行登录或失权处理；恢复同一管理员后仍只允许查询原编号或按原内容重试。不能通过保留失权页面资料解决，也不需要增加管理员管理或其它模块。

决定性回归：参数化401/403，先制造实际前端未知发送状态，再令安全重试被身份拒绝；断言页面私有资料被清除而原 sessionStorage 内容逐字保留；同一管理员重新登录/恢复权限后恢复原 pending，不自动发送，后续安全重试提交完全相同 UUID/冻结输入并恢复原收据。另一管理员不能看到该 pending。继续保留明确业务拒绝/回滚释放编辑、查询404保留原编号、旧响应不能恢复失权资料的已有判据。修复后需当前源码独立重审，并重新构建/核对最终包资产身份。

## 完整集成范围的检视结果

| 根条件 | 直接检视的实现及行为证据 | 本轮判断 |
|---|---|---|
| SC-1 独立模块和发布 | GmWebOptions/GmWebModule、HttpApiHostingExtensions、HttpListenerBoundaryMiddleware、Host Program/csproj/appsettings；真实进程 LauncherHttpHostStartupTests 的独立端口及游戏开关矩阵；release.py/package资源与HTTP检查 | GM/Launcher任一启用共用同一Host容器及Kestrel，路由按独立端口隔离；默认关闭；前端随包接线成立 |
| SC-2 管理身份和追踪 | GmAccessBoundary/AuthenticationEndpoints/Bootstrap/LoginLimiter、GmAccessDomainService、SqliteGmAdministrationPersistence/Schema；认证HTTP、审计事务、前端AdminApp及其失效/乱序测试 | 管理员与玩家身份隔离；会话摘要、当前权限、注销、CSRF、实际HTTPS/回环判断、限流和成功/失败审计有实际路径；邮件/三币写入事务内再次鉴权 |
| SC-3 全服和当前会话查询 | GmPlayerDirectoryEndpoints/Commands/DomainService/Persistence、GmPlayerRuntimeQuery、OnlineStateRegistry及领域接缝；PlayerDirectory/Overview页面；生命周期与采样交错HTTP测试 | 全服筛选后分页、无角色账号、严格姓名解码、只读白名单成立；账号包括未选角登录，角色要求精确有效且发布的当前会话；位置与频道来自当前领域且标明采样边界 |
| SC-4 官方邮件闭环 | OfficialMailView及mail/api/useGmRead；邮件HTTP/命令/领域/规则/持久表；既有MailboxDomainService与领取/转移领域；MailboxChangedHandler；真实PVF普通装备、堆叠物、共享材料与金币领取判据 | 草稿/模板版本、511字节可表示文本、明确角色批量、新detached UID、既有邮箱保管/领取、整批事务、持久防重及提交后当前邮箱提醒成立；页面未知结果跨401/403恢复存在F-001 |
| SC-5 账号三币闭环 | CeraGrantView/cera、GmCeraEndpoints/Commands/DomainService/RuleService/Persistence；AccountExecutionBehavior与UnitOfWorkBehavior、SqliteDatabase；AccountWalletChangedHandler；三币/CAS/重叠批次/真实商城竞争/未知提交测试 | 实际账号范围有序独占租约位于共同事务外；无角色账号可用，三币CAS和范围校验、收据及审计同事务；防重优先恢复历史事实，通知读取当前钱包并核验当前会话；401/403保留pending |
| SC-6 集成回归及交接 | 完整产品diff的项目引用/模块注册/通知目录/Schema/架构判据，React构建配置、CI validate.yml、release.py及guard测试，GM三份说明/GM01..21/组件和发布文档；fresh日志与最终zip | 构建/验证/交接接线及证据边界成立；集成验收需先解决F-001，当前不能给完整根批准 |

源码检视覆盖上述当前运行时产品，包括 Data/Abstractions 的输入与投影契约、Application 所有 Administration 服务/命令、Persistence 四个GM适配器、Protocol两类中立通知及注册、GmWeb所有端点/边界，以及React所有功能页面/API/恢复状态。检查了新增关键HTTP/SQLite故障注入与资产回归、前端行为测试，以及原基线到HEAD的既有代码和文档改动。依赖锁文件用于固定构建输入；没有把child旧manifest在后续合法修改文档后的差异当成新缺陷。

## 当前证据和发布包身份

本轮没有运行.NET/npm/package/浏览器或改Git，避免共享输出；下列执行结果来自已读取的主控fresh日志，而非本轮自行运行：`fresh-build.log` 为0警告/0错误；`fresh-full-tests.log` 和 `fresh-test-counts.json` 为8测试项目共5373通过、0失败、2个 deliberate missing-PVF 分支跳过；`fresh-frontend-tests.log` 为54通过；`fresh-frontend-build.log` 为TypeScript/Vite构建；`fresh-release-tests.log` 为11通过。日志在 `artifacts/gm-tests/spec006/`。这些验证证明其覆盖路径，通过数量不代表F-001已覆盖。

独立只读打开当前zip、检查全部CRC及相关字节/元数据：

- 包：`artifacts/release/DNF-JP-86-Server-0.2.0-win-x64.zip`，53,211,031字节。
- SHA256：`2f0de0350a5056b6ad8fd30beac17061f7bb5ade8e618bfed5332260237cfdd6`。
- release.json的commit为本次接受HEAD；3个GM资产SHA符合metadata，并与当前dist逐字节相同。
- 7份随包源码Markdown与当前Document逐字节相同；另有生成README，共8份文档。
- 默认GM/Launcher/Game均关闭；包内未发现DB/PVF/log/Node依赖或.env数据。
- `accepted-package.log` 的原生Windows标准package记录重新npm ci/build、自包含publish、3资产HTTP内容/MIME验证、initialized=false及private401成功。

Spec006的环境配置别名隔离已直接检查当前 `smoke`：按 `__`/冒号及大小写归一清除继承的同键别名，再写独立库、临时端口、关闭Game/Launcher、空PVF和Production；bootstrap前缀也被清除。当前行为测试捕获实际Popen参数。这一已解决问题不作为新的残余风险。

## 仍待验收的边界

当前浏览器报告明确本机Chrome实际覆盖邮件编辑/预览，以及三币单账号、明确批量、正常刷新记录和跨页选择；真实浏览器断网恢复仍只由前端行为测试支持。邮件附件领取、钱包编码、当前会话和故障恢复的服务器证据保持CurrentImplementation。A21官方邮件表现、实际附件领取体验、在线提醒接收和重登三币余额仍按 `Document/gm-acceptance-list.md` 的GM01..21由用户验收，未提升为ClientVerified。

Linux/远程CI未执行，本轮只核对配置与源码；Windows候选已实际执行。现服部署、真实管理员初始化/玩家资产、push/PR/merge/Release均不在根授权内。上述明确待验边界符合根合同，不另作为阻塞。主控负责处置F-001、恢复执行及再次提审；本报告不修改六个sealed结果或宣称根任务完成。
