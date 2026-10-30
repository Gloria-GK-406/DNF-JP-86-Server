# 地下城日志诊断

普通入场摘要保留 Information；逐只死亡、普通切房、正常规则拒绝使用 Debug。Warning 表示异常状态，例如房间已推进但没有有效收件人；Error 表示提交后的通知投递失败，原异常继续上抛。正常关停取消记录 Debug。Trace 才整理剩余阻塞怪物序号。

正式环境保持默认 Information。需要定位地下城问题时，仅对下面的类别临时开启 Debug，勿把全局 Default 改成 Debug：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Dfo.Server.Protocol.Game.DungeonEntry": "Debug",
      "Dfo.Server.Protocol.Game.MonsterDeath": "Debug",
      "Dfo.Server.Application.World.DungeonVisitDomainService": "Debug",
      "Dfo.Server.Application.World.DungeonRunStore": "Debug",
      "Dfo.Server.Application.World.MonsterDeathDomainService": "Debug"
    }
  }
}
```

诊断结束后移除这些类别覆盖。默认过滤下不计算新增的怪物清单、阻塞怪物计数，也不构造高频成功日志参数。

按连接、RunId、房间版本关联切房链路：

- `Move map received`：解码后的请求进入 handler，含序号、入站 ordinal、目标格与路径位置。
- `Move map state`：服务端当前房间、可离开状态、清场状态、存活阻塞者与机关状态。
- `refused` / `ignored` / `routed`：校验拒绝、静默应答分支或分流至专用塔服务。聚合拒绝细分死亡事务占用、拾取占用、队长归属、版本变化、机关与地图连接等原因。
- `Dungeon move preparing`：目标地图已选择，正在创建房间。随后缺少结果时结合异常日志定位。
- `stage=Committed`：访问服务已返回房间推进结果。
- `Move map delivered`：发送调用完成，含投递目标数与房间描述规模；不表示客户端已处理，也不保证每个尽力投递成员成功。
- `Monster death prepared` 与 `Monster death delivered` 分别标识事务准备和提交后的投递完成；清场字段区分普通房间与 Boss。

没有 `received` 只能说明请求没有进入该 handler。仍须结合网络层解码/分发错误与客户端抓包判断，不能直接认定客户端未发送。日志不修改规则、重试、应答或事务顺序。
