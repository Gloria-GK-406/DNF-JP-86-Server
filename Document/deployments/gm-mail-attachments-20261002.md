# GM 邮件附件流程更新部署

2026-10-02，按用户「继续改造流程，完成后部署」授权完成本机更新。当前入口：[官方邮件](http://127.0.0.1:18081/gm/mail)。支持物品检索、分类与装备筛选、附件编辑，以及封装/强化/增幅配置；具体规则见 [官方邮件说明](../gm-official-mail.md)。

## 运行与恢复

- 分支：`feature/gm-web-admin`。部署源提交及文件 SHA-256 记录于下面部署目录的 `deployment.json`。
- 部署目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/gm-mail-attachments-20261002-170520`；程序位于 `runtime/`，启动时 PID `2100`，隐藏窗口运行。
- 管理员及密码沿用 [首次部署记录](gm-web-20261002.md)，没有重置凭据。GM 仍只绑定 `127.0.0.1:18081`。
- Launcher `127.0.0.1:18080`、Channel `7001`、Game `10010/10011`、UDP `12311/12312` 保留；配置与 PVF 路径沿用原部署。
- 数据库仍为 `E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`；一致备份为部署目录的 `before-deploy.db`。
- 旧程序保留于 `E:/Project/DNF/DNF-JP-86-Server/artifacts/gm-web-20261002-155143/runtime`。如需回退，先核实当前进程路径并停止，再启动旧目录 `Dfo.Server.Host.exe`；已有新配置草稿/收据应先备份，旧版页面不支持编辑新装备选项。不要直接覆盖当前数据库。

## 验证与边界

Release 解决方案构建和 Windows x64 自包含发布通过。邮件 HTTP/SQLite 测试 42 项、鉴权/限流 15 项、装备成长/架构/邮箱集成相关测试 65 项通过，结果位于任务工作区 `artifacts/gm-mail-config-tests/` 的 TRX；前端全部 62 项测试及生产构建通过。前端仍有既有大 chunk 提示。

应用内浏览器使用真实 PVF 验证了名称搜索“无色小晶块”、精确查找手镯 `100310498`、解除封装/增幅 +10/力量配置、混合附件列表及服务端预览。仅编辑和预览，未保存演示草稿或实际发送。保留编辑页面供查看；截图为部署目录的 `gm-mail-picker.png`、`gm-mail-preview.png` 和 `gm-mail-editor.png`。本次浏览器证据来自 Codex 应用内浏览器，不是 Chrome 或 A21。

部署 HTTP 的 HTML/JS/CSS 与磁盘资源逐字节相同，管理员状态 initialized，监听归属新程序。部署前后 48 张业务表行数及内容相同，stderr 为空；证据为 `verification.json`、`business-check.json`。隔离测试已覆盖同 ID 不同装备配置、独立 UID、实际邮箱领取与重启持久化、旧请求哈希/旧收据兼容，以及非法组合拒绝。

真实 A21 的封装显示、增幅属性效果、领取和重登体验仍待人工验收，未标记 ClientVerified。继续按 [MAIL-10 至 MAIL-13](../gm-official-mail.md#待真实客户端验收) 和 [总验收表](../gm-acceptance-list.md) 检查。
