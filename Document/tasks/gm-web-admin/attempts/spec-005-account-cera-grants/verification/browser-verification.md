---
kind: rks-browser-verification
decision: pass
---

# 本机 Chrome 实际验证

2026-10-02，已连接本机 Chrome extension browser 3，独立标签 923090709。仅隔离 Host `127.0.0.1:18084/gm/wallet` 和测试管理员 operator（GrantCera 唯一权限），未使用内置浏览器、真实账号或现服。

账号1无角色，初始三币10/20/30；账号2有两个角色，初始100/200/300。实际页面完成：

1. 登录、账号选择；账号1代币点券+7，原因“本机 Chrome 单账号隔离验收”。预览20→27，确认后显示整批已到账及持久收据 `7ea1071c-014d-42ca-9249-322790107eee`。
2. 明确选择账号1/2，点券每账号+50、总100。预览及最终收据显示账号1点券10→60、账号2点券100→150，其余币不变；收据 `9b427cda-d754-4ebc-821e-0462010c762d`。
3. 页面正常刷新，个人记录仍两笔；点击批量记录余额详情恢复相同编号、提交时间、前后余额，没有自动发放。重新编辑跨页选择账号1及第二页21，已选明确显示“1、21”；此编辑没有提交。

SQLite以只读连接独立核验：账号1 `(60,27,30,honor_exp77)`；账号2 `(150,200,300,honor_exp88)`；恰好2笔成功收据、2条GrantCera成功审计；characters只有账号2的2角色。完整收据只读输出为 `artifacts/gm-tests/spec005/main-chrome-db-proof.json`，HTTP日志为 main-chrome-fixture.log。

截图：[批量预览](chrome-batch-preview.png)、[持久到账结果](chrome-batch-result.png)。截图已向用户直接展示。未制造实际浏览器断网；未知刷新/原UUID恢复/失权乱序行为由13项新增前端测试覆盖，不能把正常刷新描述为真实断网验证。服务器实现证据不是A21客户端到账展示验收。

测试标签正常关闭，stop.txt正常停止夹具；runner35657退出码0，1项测试通过。新鲜隔离目录：`E:\Project\DNF\DNF-JP-86-Server\.worktrees\gm-web-admin\artifacts\gm-cera-browser\spec005\main-chrome-bf614d239c4640168565cbcb6a849fa8`。内置浏览器导致App退出仍是用户观察/假设，本次切换成功不能证明崩溃根因。
