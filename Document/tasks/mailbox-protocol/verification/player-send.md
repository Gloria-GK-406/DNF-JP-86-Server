---
kind: rks-verification
decision: manual_required
---
# 玩家发送部署验证

2026-09-24，005E/013B发送增量。独立审查reviews/send-review.md approved；SourceSlot精确材料选择修复已复核。

Application 1992/1992、Protocol 1110/1110通过；Release publish通过。最初Application全测缺少PVF_ARCHIVE_PATH，36失败/133跳过；设置实际A21 PVF后全量通过，无跳过。初次新增装备测试错用fixture消耗品2000，改用fixture装备3000；一次测试编辑编码导致“小明”样本乱码，恢复UTF-8原文后通过。未改变生产编码以绕过测试。

部署 artifacts/mailbox-send-20260924/runtime，PID31340。停止旧PID8900前无游戏连接，SQLite backup API备份before-deploy.db、quick_check=ok，保留旧绝对数据库路径与配置。新进程监听7001/10010/10011/18080，stderr空。既往Unhandled发送仅采样，不重放、不补扣。

## Unverified Conditions

A21实机重新发送单装备及金币+多附件中文邮件；发送端扣除、接收方列表与领取、在线0063提示。纯正文无附件哨兵和非ASCII收件人名字尚无实帧。此为可测试部署，不标整个邮箱系统ClientVerified。
