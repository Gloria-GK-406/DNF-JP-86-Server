# 实际后台浏览器验证

目标：Spec004，生产 manifest 963AF5D85502E0647C943437118CAB2DE591992D05808B3D6C429BD16A1B3908，逐文件 SHA 已复验一致。

通过 cua 的真实 IAB 页面访问 http://127.0.0.1:18083/gm/mail，使用合成管理员及 fresh main-ui-befca228d9b442f88bb503920ea9a588/test.db；游戏网络关闭。不是真实玩家发放或 A21 ClientVerified。

- 编辑中文标题、含换行正文、金币1234，保存草稿；刷新恢复一致。
- 选择角色1、2；加载预置测试补偿模板，选择不丢失。
- 为每人设置3037×5、27312×2、金币1000；预览61/511 UTF8字节、2人、总金币2000、总物品数量14；冻结输入，确认整批发送。
- 收据 UUID 5fa679fc-473d-4130-88ea-48f96b7061d3，邮件ID1/2，装备UID1/2及3/4。刷新后查询同一收据，提交及期限不变，无自动再发送。
- 单人角色3纯文本、金币0，预览74字节、0物品；确认后邮件ID3、无装备附件。UUID 8b589b37-97df-4ccd-8411-4bc011de6a27。
- Python SQLite mode=ro核对：3封邮件/3收件行/8附件/2操作收据/2条成功发送审计；装备UID各自独立；ID3确无金币或物品附件。中文原文逐值断言一致。raw证据 artifacts/gm-tests/spec004/browser-database-proof.json。

截图：[批量预览](browser-preview.png)、[批量结果](browser-result.png)、[单人结果](browser-single-result.png)。布局可操作，中文可读。Ant Select的虚拟option没有可点击区域，使用实际显示文本选择；登录后默认操作记录页，使用官方邮件导航进入。这是工具定位/既定导航，不是产品失败。

App再次关闭后测试进程session22588不可获取，fixture原runner无最终退出码，18083已无监听。实际已完成的UI及持久库证据保留；不把该runner当作正常退出证明。最后验证须另起fresh VERIFY_ONLY自检，获取实际退出码。
