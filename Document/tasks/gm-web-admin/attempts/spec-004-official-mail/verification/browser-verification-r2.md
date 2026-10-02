# 修复后实际后台浏览器验证

当前冻结manifest owned-files-r2.sha256 SHA9D31E9639EF5E2B161DB24C788AB584A517BA604BD4B31D46B4184BCC5FFF770；仅backend/test改变，UI源文件保持原指纹。旧browser-verification.md与core0数据库保留为修复前证据，不改写历史。

在fresh main-ui-r2-82b1fba2bacc429ebe8c58ff1ad1739d/test.db，真实IAB访问127.0.0.1:18083/gm/mail，合成管理员登录后明确选择角色1/2、每人27312×2、金币0；中文预览67/511字节、总物品4、耐久45/未封装，确认实际发送成功。两封邮件ID1/2，装备UID1/2及3/4，持久化发送结果与页面历史正常。截图browser-r2-result.png。

只读SQLite proof artifacts/gm-tests/spec004/browser-r2-database-proof.json：一个发送操作，两封邮件、四件独立装备，Value分别1167942060/1245084504/1350581995/221843989，全部正且不同、耐久45、封装0，与修复前固定0清晰区分。此UI检查不领取装备，领取及投影由修正后真实HTTP/领域/已注册codec判据负责；不升级为ClientVerified。

服务网络仅GM，原PVF只读、真实玩家/客户端不动。App再次关闭后runner session33951不可获取，日志无最终退出统计，当前18083无监听；不能把此runner称正常退出。主控最终fresh VERIFY_ONLY应另取actualexitcode。stop.txt已写于精确隔离目录，不删除旧库。
