# 本机 Chrome 验证

2026-10-02，使用已连接的本机 Chrome extension（browser 3），独立新建测试标签页。隔离 Host 127.0.0.1:18083，仅测试账号及新 SQLite，游戏监听关闭。

实际 UI 登录成功，进入官方邮件；输入标题“本机 Chrome 验收”和正文“确认本机浏览器可登录、编辑和预览。”、选择角色1，打开“发送前核对”。预览显示72/511字节、收件人1、每人金币0、总物品数量0，与输入一致。没有确认发送。截图 browser-chrome.png。

关闭 agent 测试标签页后向该夹具目录写 stop.txt；runner session54896 实际exit0，chrome-fixture.log 显示正常停止、1 passed，18083不再监听。日志位于 artifacts/gm-tests/spec004。此结果证明本机 Chrome 自动化路径可用，不能由此确认 Codex App 崩溃原因。
