---
kind: rks-verification
decision: pass
---

# Spec005 最终新鲜验证

2026-10-02，对独立 change-review.md approved 的当前23文件结果执行，Spec958277A0、manifest0D3EBAF5身份与23/23实际字节一致；expected HEAD仍07e1f607。

| 契约条件 | 当前证据 |
|---|---|
| SC-1 / AC-001..003,008 | 实际Chrome单/批/记录/刷新/跨页与只读SQLite见browser-verification.md；新鲜前端54项通过及TypeScript/Vite build exit0，Host109通过中真实权限/输入/三币HTTP判据全部正常。 |
| SC-2 / AC-002..005 | 新鲜全解决方案8项目5373 passed、0 failed、2 deliberate skips，计数80/2663/109/50/757/86/396/1232；真实SQLite零角色、同账号多角色、并发商城消费、上限、CAS/收据/审计失败整批回滚及重启防重通过。Schema55与新表五列精确断言随Persistence396通过。 |
| SC-3 / AC-005..008 | 持久重试/未知提交故障/当前钱包13B精确会话通知/旧交易及架构回归随完整测试通过；drift R2 approved/aligned、当前完整结果approved、不可变0项挑战已裁决。 |

新鲜构建0警告/0错误；发布exit0，dist的HTML/JS/CSS三文件与新publish wwwroot/gm逐文件字节和SHA一致。新库Release VERIFY_ONLY 1passed/exit0/2.45s；Chrome常驻夹具此前正常stop/runner exit0，18084已释放。

证据位于 artifacts/gm-tests/spec005/fresh-build.log、fresh-full-tests.log、fresh-frontend-tests.log、fresh-frontend-build.log、fresh-publish.log、fresh-fixture-verify.log；浏览器截图/报告在当前verification。只读实际PVF SHA仍3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06；两个skip为档案存在时missing-PVF分支。原54表断言失败已保留并独立复核修正，不掩盖失败。

未知浏览器断网恢复由前端行为测试，实际Chrome只验证正常刷新；A21在线三币展示、提醒接收和重登仍交用户按Document/gm-cera-grants.md验收，不宣称ClientVerified。Vite现有大chunk/AntD提示保持观察。CI与完整发布集成留Spec006。

全部变更已分类为23产品路径或根声明durable任务证据；源main仍5fbe2dad且干净，不切换现服或真实发放。完整暂存使用cr-at-eol兼容历史CRLF，并保留真实空白错误检测。

暂存预检发现两份历史原始日志自带行末空格/末尾空行，未执行commit。保留日志原始字节；对所有产品、测试、Markdown/YAML及其它证据做完整暂存空白检查，仅排除本child verification下原始*.log（它们是不可改写的工具输出，非产品）。检查exit0后才执行唯一commit。
