---
kind: rks-spec-result
spec: ./spec.md
state: complete
---

# Result: 库存协议槽位与业务槽位边界

## Delivered

- 新增原子的 `GameSlot`、`BusinessSlot`、`BusinessSlotRange` 及携带源坐标的
  `InventorySlotMappingException`，由一个权威映射实现全部受支持区段的强双向转换。
- Move、Use、Delete、Sell、Repair、Disjoint 六条请求在 decoder 组合协议地址，在创建 ICommand
  前转换为业务地址；命令、领域接口、领域结果不再携带分离的协议 space/index。
- 响应与通知在 Protocol 投影边界把业务槽转换回 `GameSlot`；既有 wire 布局保持兼容，
  repair-all 额外保留原请求的 `space/-1` 哨兵。
- PVF 绝对放置区段在 Infrastructure 内容入口一次性转换为业务范围；到达、补充、商城、
  开包、分解及容器写入不再运行第二套绝对区段算法。
- 保持现有数据库 container/index 格式，通过业务槽绑定适配既有 Main、Equipment、仓库数据；
  非法、保留、虚拟和越界地址在网络边界以专用异常隔离。
- 新增库存坐标边界文档以及映射、架构、行为授权、持久化往返、协议字节与网络隔离回归测试。

## Review and verification

- 独立完整结果复审：`approved`，第一次审查的五项 finding 全部闭合。
- Fresh Release build：0 warnings，0 errors。
- Fresh tests：3519 passed，0 failed，2 conditionally skipped。
- `git diff --check` 及旧路径扫描通过。

## Scope boundaries retained

未解释或改变分解 `contextValue`，未新增库存玩法，未改变已确认协议字段顺序，未迁移数据库；
未合并到 `main`、推送、部署或清理任务工作区。
