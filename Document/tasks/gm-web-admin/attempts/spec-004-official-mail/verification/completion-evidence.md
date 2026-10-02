---
kind: rks-verification
decision: pass
---

# 官方邮件最终新鲜验证

2026-10-02，主控对最终 R2 approved 的29文件冻结结果验证；manifest 9D31E9639EF5E2B161DB24C788AB584A517BA604BD4B31D46B4184BCC5FFF770 与29/29实际字节一致，Spec 4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F，HEAD仍573be46b。

| 条件 | 当前证明 |
|---|---|
| SC-1；AC-001..004/009 | 前端41 passed、TypeScript/Vite build exit0；真实HTTP权限、草稿模板、预览文本及目标边界全部随当前Host91/91通过。实际页面单人/批量与已保存截图见browser-verification-r2.md及首次browser-verification.md；本机Chrome另完成登录、编辑、72字节单人零附件预览，browser-chrome.md/png。 |
| SC-2；AC-005/006 | 当前Host集成测试真实PVF装备/堆叠物/金币、原邮箱领取/事务故障回滚通过。四装备随机Value正且互异，经现有Mailbox命令、已注册编码器到正常背包，领取前后原UUID重试均保留同一UID/core且一次成功审计。F001因果及两个测试oracle修正由change-review-r2.md独立批准。 |
| SC-3；AC-007/008 | 持久化并发、重启、防重、通知失败、当前数量投影、架构/注册和原邮箱回归随完整解决方案通过。drift-review-r2 approved/aligned，change-review-r2 approved/F001 closed；不可变挑战集0项。 |

全部 .NET 输出串行：fresh-build.log 0警告/0错误；fresh-full-tests.log 八项目5355 passed / 0 failed / 2 deliberate skips（档案存在时两个missing-PVF分支），explicit PVF_ARCHIVE_PATH 指向只读真实档案，SHA未变。前端日志 fresh-frontend-tests/build.log；构建仍有既有chunk大小与AntD deprecation观察，不影响功能判据。

fresh-publish.log exit0，Web/gm-admin/dist 3个HTML/JS/CSS与新发布wwwroot/gm逐文件SHA一致。Chrome夹具正常关闭exit0/1passed；另新库VERIFY_ONLY current测试fresh-fixture-verify.log exit0/1passed/2.8s，明确正常停止、18083监听释放。此前随App crash丢失的runner没有退出码，历史记录保留，未冒称正常停止。

日志均在工作区 artifacts/gm-tests/spec004（ignored），截图/审查和摘要在此任务目录。git diff --check exit0；源main仍5fbe2dad且干净。仅隔离测试数据，不切换现服、改客户端/PVF或真实发放。验证证明服务器及后台页面实现；A21实际邮件表现仍由Document/gm-official-mail.md的MAIL验收，未升级ClientVerified。

完整暂存60路径后，默认diff检查识别未提交历史证据中的CRLF为行末空白；无提交发生。保持已批准和历史字节不变，显式cr-at-eol兼容仓库CRLF，同时保留blank-at-eol/blank-at-eof/space-before-tab检测；完整暂存检查exit0。
