# Spec-002 实现安全挑战

目标：`feature/gm-web-admin` 当前未提交管理员访问实现，基线 `0630348d2bace7a35887b43b8aaa0c145b0b66a2`，2026-10-02 捕获的 tracked diff 与新增 Administration、GmWeb、HTTP/持久化/UI 测试文件。依据为根与 child `TaskContract.md` revision 1、approved `spec.md` 和 `reviews/spec-review.md` Round 2。攻击范围为认证隔离、默认鉴权、CSRF、连接传输、bootstrap、限流、原子审计以及页面会话失效；不把后续尚未实施的玩家、邮件、点券模块计为当前遗漏。

本记录由独立挑战生成上下文产出，不是质量审查结论。直接检查了产品源文件、完整注册/Schema/架构测试 diff、新增真实 HTTP/限流/事务测试和前端交互测试；生产报告仅用于找到文件。另读取主控的 `verification/browser-verification.md`，其中已观察正常登录/退出、开发代理、真实审计分页和重启恢复。未运行共享 .NET 构建、未操作现服或真实数据库。以下明确区分源代码观察与未探测的时序假设；没有用缺少形式证明推导认证绕过。

## CH-1: 在途身份查询可能在退出后恢复旧的页面身份

- Risk: low-risk
- Failure and impact: 每分钟身份查询已在服务器按旧会话完成鉴权，但响应因网络延迟仍在途中；管理员随后退出成功，页面清空身份并显示登录表单；更早的查询响应最后到达，可能把旧身份重新写回 UI。管理员会暂时看到已退出身份和权限菜单，或者一次旧的 401 响应会把之后的新登录清掉。这影响 RQ-009 的会话状态反馈。服务器每次仍查询持久化会话，旧 Cookie 已被撤销，后续私有请求会再次被拒绝，因此当前路径不能恢复服务器授权或发放资产，修复成本低，非当前安全阻塞。
- Evidence: **已确认源代码观察**：`Web/gm-admin/src/AdminApp.tsx:32-36` 的 interval 查询直接 `.then(setIdentity)`，cleanup 仅清除定时器，不撤销/忽略已发出的请求；`:42-44` 退出成功直接 `setIdentity(null)`，没有请求世代检查。现有交互测试覆盖正常退出和定时查询返回 401，但未覆盖响应乱序。**尚未实际探测的假设**：用 deferred Promise 挂起一次 `auth/session`，完成 `auth/logout`，再解析前者为旧 Identity，检查是否再次出现“退出登录”和旧菜单；另在旧查询挂起时完成新登录后再返回旧 401。
- Minimum correction: 若 reviewer 确认上述时序，身份变更后忽略属于旧登录状态的在途回复，并添加一个可控响应顺序的 UI 回归。无须更改服务器会话、权限或当前合同。

## CH-2: 会话到期不会回收持久化会话记录

- Risk: low-risk
- Failure and impact: 管理员长期通过关闭页面而非退出结束工作，或者反复登录；每次成功创建一条会话，8 小时到期只拒绝使用，旧记录保留。重复使用会使 `gm_sessions` 持续增长。当前账号数有限，记录较小且有主键索引；过期状态仍被服务器拒绝，没有造成旧会话恢复或权限泄露。RQ-007 的限流状态容量在内存容器内已经独立限制，这个清理问题不应混同为已违反限流存储边界，可作为运营维护项裁决。
- Evidence: **已确认源代码观察**：`Application/Administration/GmAccessDomainService.cs:33-35` 每次成功登录创建新会话，`:39-43` 到期返回 null；`Persistence/Administration/SqliteGmAdministrationPersistence.cs:22-29` 只有插入、按摘要读取和显式退出删除；当前完整新增/修改实现中没有到期清理入口。**尚未实际探测的长期影响**：连续成功登录并推进时钟后，`SELECT COUNT(*) FROM gm_sessions` 仍包含全部过期记录；没有测量实际容量或性能影响，不声称发生现有拒绝服务。
- Minimum correction: 当前不需要阻塞验收。reviewer 可记录一个明确的过期会话清理/保留策略后续项；若选择本阶段处理，清理应沿用当前事务，不影响未到期会话和审计历史。

挑战数量少于通常范围，是因为已有实现与真实 HTTP 用例对当前核心失败边界提供了具体保护；没有为了凑数量把未实现的后续模块、显式记录的临时 CSRF 密钥行为、正常 Cookie 凭据交付或常规部署选择改写为漏洞。最终 reviewer 仍需独立核对完整结果和证据，并对 CH-1、CH-2 逐项裁决；本记录不批准实现或宣布任务完成。
