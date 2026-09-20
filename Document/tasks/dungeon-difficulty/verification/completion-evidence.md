---
kind: rks-verification
decision: pass
---

# Verification: 地下城难度

本契约范围内实现、独立审查、自动回归及用户辅助实机验收通过。独立审查见 ../reviews/change-review.md。

| 条件 | 当前证据 |
|---|---|
| SC-1 | Protocol987通过：普通/冒险样本、高位地图编号、非法枚举与4/9..14字节截断拒绝；全解决方案编译通过。 |
| SC-2/SC-3 | Persistence361通过，其中新增6项覆盖缺省、隔离、单调幂等、重新构建服务/数据库读取、回滚与旧记录兼容。 |
| SC-4 | Application使用真实PVF1639通过；新增单人超限无Run及冒险布局/首房/换房/重访一致性；原组队回归保持。 |
| SC-5 | Protocol987包含0005的u32+u8及选图前最新权限帧顺序；普通/冒险成功入场、换房与重登已完成，见 live 证据。 |
| SC-6 | 未改布局可达性、通关提升、组队新政策及疲劳；边界经代码审查核对。 |

命令：设置 PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-debug/Script.pvf 后，dotnet test Server/tests/Dfo.Server.Application.Tests、Dfo.Server.Protocol.Tests、Dfo.Server.Persistence.Tests。相关输出分别保存在任务目录 application-pvf-tests.log、protocol-tests.log、persistence-tests.log。另 CardRewardReconnectIntegrationTests 真实Socket1项通过。最新Release发布成功。

## Client verification

- [普通入场](../live/normal-entry.md)：0010、001C和服务端Run均Normal，用户确认入场。
- [冒险入场](../live/adventure-entry.md)：0010、001C和服务端Run均Adventure，用户确认入场。
- [冒险换房](../live/adventure-move.md)：002D→001D→0025→001E及用户观察确认换房；实例难度不变由自动回归支撑，换房包不单独携带难度。
- [重登权限](../live/relogin-permissions.md)：重新选角和选图的0005均为150/state1，用户确认普通、冒险仍可选。

## Limits

仅完成本契约，不宣称整个 #102 完成。通关解锁触发、组队新准入政策、疲劳未实现。实机使用原数据库副本，原数据库未改动；2..4档显示名称仍未实测。角色重登已人工验证，服务重启持久化依赖真实SQLite重载自动验证。
