---
kind: rks-spec-result
spec: ./spec.md
state: complete
---

# Result: Cell-owned dungeon runtime

## Delivered

- Dungeon Run 按坐标拥有已创建 Cell；初始进入和首次换房都先构造完整 seed，再受版本保护地
  发布，重访直接复用原 Cell。
- Actor、`IsMonster`、pending loot、统一 GroundItems、清理事实和 Cell revision 均归 Cell；
  删除 Run 级死亡、怪物掉落和已拾取平行真源。
- Cell Factory 用未发布的 `DungeonCellCreationContext` 一次性取得房间内容、物品分类、怪物
  语义和确定性 loot；死亡运行期不再查 PVF/content。
- 死亡把 pending loot 原子转入 Cell；拾取以 Run/Room/Cell revision 预占，持久化成功后移除，
  失败释放，领取期间禁止跨 Cell 移动。
- 提供来源中立的 Cell 地面物添加入口；声明物、怪物掉落以及后续玩家/机关来源共用编号与
  change 路径。
- 保持现有 A21 进入、死亡、拾取和结算协议投影；更新领域文档与测试夹具。

## Review and verification

- Independent complete-result review round 3: `approved`。
- Fresh solution tests: 3345 passed, 0 failed, 2 conditionally skipped。
- Fresh Release build: 0 warnings, 0 errors。
- Structural and whitespace checks: passed。

## Scope boundaries retained

未重做 Boss 翻牌/回城等待、任务/经验/概率规则，未更换 SQLite，未修改客户端，未合并到
`main`，未部署，也未进行实机验收。

