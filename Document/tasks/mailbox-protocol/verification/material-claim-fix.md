---
kind: rks-verification
decision: manual_required
---
# 材料领取：虚拟格扫描修复

用户实测装备可以领取、材料不能。运行日志显示附件2与5领取时MailStackTransfers.GrantAsync把Main/1送入ToBusinessSlot，抛InventorySlotMappingException；数据库两附件claimed_flag仍0，装备附件3与金币1/4已为1。问题发生在服务端入包算法，不是客户端领取请求或官方标志。

修复只排除Main 0..2已知虚拟资源格，不吞掉其他非法坐标异常，不占用普通物品分区容量。新增两种真实SQLite回归：含虚拟格的新材料入空格、同材料叠加。修改前两例失败，修改后邮箱29例全过，虚拟格值保持6且领取标记准确提交。日志见test-material-Application.log。

Release发布至artifacts/mailbox-material-20260924/runtime，保留原配置、DB绝对路径与资源。git diff --check通过。当前仍在线（40116进程10010连接），已请求正常退出；尚未重启部署，实机领取待验证。
