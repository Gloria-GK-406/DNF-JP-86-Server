# 探针产物：`etc/serverparameter.etc` 的 `[escalade tutorial reward]`

归档一次性探查的结果，使引用它的主张可以解析。见 [evidence.md](../../../evidence.md#一次性探查--直接写进文档)。

- 归档时间：2026-08-18
- 归档源：`DNFClient-debug/Script.pvf`
- 读法：`Dfo.Pvf` 打开归档 → `FindFile("etc/serverparameter.etc")`（**注意在 `etc/` 下，
  根目录没有同名文件**）→ `OpenScript` 逐 token 读，取 `[escalade tutorial reward]`
  与其闭合标签之间的内容

```text
[escalade tutorial reward] 38 8474 1
[/escalade tutorial reward]
```

## 读法：三元组 `(教程flag, 物品id, 数量)`

上游 `GameWorld/TutorialRewardProvider.cs:61-70` 按 3 个一组解析：
`flagIndex` / `itemId` / `count`，同一 flag 可以有多条（本档只有一条）。

**全档只有 flag 38 有奖励**，物品 `8474` 一件。

## 与实机的关系

2026-08-18 走完教程副本一圈，客户端发出的
[`0x008F CHANGE_TUTORIAL_FLAG`](../../../game/client-to-server/0x008F.md) 携带的 flag 依次是
**30**（进城突发里）、**1**、**2**、**3**（进副本后），**未出现 38**。

所以"NPC 给小药瓶"这一步除了服务端要回 `0x008F` 的奖励 ACK 之外，
还缺**触发 flag 38 的那一步**——按位置推测是与教程 NPC 对话，协议尚未定位。
这一条是推测，不是观察。

引用它的主张：教程奖励发放的物品来自归档而非服务端自定义。
