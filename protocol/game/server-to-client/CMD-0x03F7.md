# CMD `0x03F7` `WEDDING_ENTER_CEREMONY`

更新2026-10-16。ClientStatic / Confirmed：A21 handler RVA008924B0先检查结果信封，再消费i32 status；正文共5B：`u8 ok=1 + i32 LE status`。请求的14B opaque前缀不进入回包。

成功向量（编码测试，非实帧）：`01 00000000`；当前通用拒绝：`01 F6FFFFFF`（status=-10）。不能只回1B成功或无结果信封的4B状态。

客户端跳表RVA00892568：-10为dstr8D45“当前无法移动”，-9为11CD5消耗品栏空两格，-7为11CD6超过入场时间，-6为11CD4宾客礼金；-8分支直接退出。当前普通新人入口仅使用0、-10与-7，不开放宾客礼金/奖励结算。

CurrentImplementation：事务提交后投影场地迁移与名册，再发送本ACK；失败不移动。原生成功窗口、帧顺序与双端进入效果仍待实机。见[请求](../client-to-server/0x03F7.md)、[准备通知](NOTI-0x03D4.md)、[指令和错误码补证](../../../Document/MarriageChurchEntryEvidence-20261016.md)及[入场实现与测试流程](../../../Document/MarriageChurchEntry-20261016.md)。

客户端EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；镜像SHA256与地址约定见补证。
