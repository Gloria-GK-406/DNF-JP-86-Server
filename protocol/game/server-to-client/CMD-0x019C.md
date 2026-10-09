# CMD 0x019C 称号簿操作确认

本包确认取消佩戴/实物存入，不是NOTI。

成功17B：u8=1，i32space，i32slot，i32category，i32index。失败10B：u8=0，u8error，i32space，i32category。当前错误码0x0A。镜像操作使用真实space3/slot13及精确书来源；space0表示Main，不能用全零确认代替。配套0200只消费本请求的pending7，避免原生helper执行书实体交换。

确认后由EventService投影0002装备及外观、必要的Main快照、静默0166原件列表。书原件始终保留，装备镜像取消或替换不触发0168获得提示。历史space=18+category绕过未清pending，隐藏原件和000E全量回填方案均已淘汰。

完整字段、TBM1扩展与证据见[协议流程](../../../Document/AchievementTitleBookFlow-20261009.md)。ClientStatic / CurrentImplementation；2026-10-09用户接受配套0200的玩家闭环（ClientVerified）。未知原生尾部、官方帧组合和长时稳定性不扩大验收。
