---
kind: rks-spec-result
spec: ./spec.md
contract: ./TaskContract.md
change_review: ./reviews/change-review.md
verification: ./verification/completion-evidence.md
outcome: completed
---

# spec-002 结果：item 载体收敛与无用构造退役

## 交付了什么

四个为「副本运行时的临时背包」准备、却从未被任何生产代码调用过的构造，不在生产里了：

| 构造 | 处置 |
|---|---|
| `IItemContainerFactory`（Abstractions） | 删除 |
| `ItemContainerFactory`（Application） | 删除 |
| `TransientItemContainer`（Application） | 删除 |
| `MemoryItemStore`（Application，239 行） | 移出生产，成为测试替身 `VolatileItemStore` |

以及容器里那两条没有人解析的注册。

`MemoryItemStore` 没有被删掉而是移走，因为服务器真正有两条关于「非持久存储」的规则——
一个变更可以在用例外发生、任意多个非持久参与者可与唯一那个持久的并存——
而服务器实际拥有的每一个存储都是持久的。没有它，这两条规则一条也测不了。
搬家时逻辑一字未改，这一点由评审逐字比对确认。

## 过程中查出的一处真实问题

第一轮评审拦下了一件我做错的事：被删的两个测试文件里，有四条断言证的不是即将退役的东西，
而是**仍然存在的** `PersistentItemContainer` 边界（用例外写抛出、用例外读放行、用例内接受且可见）
与 `ContainerSlotItem` 的结构守卫。它们全仓无替代——删掉之后，
把 `PersistentItemContainer` 那句拒绝也删了，不会有任何用例转红。

已搬进新的 `Application.Tests/Inventory/PersistentContainerBoundaryTests.cs`，
并由第二轮评审确认逐字等价且确实能红。

## 一处未照契约字面执行

契约 SC-E 写的减量是 23（假设三个测试文件全删）。实际是 5：删两个（8 例）、
保留一个（15 例，测的是仍然存在的替身）、搬回四条被误删的保护、删掉一条已成同义反复的用例。
评审判定保留合理并授权把 SC-E 的数改成 5。

## 证据

- 变更评审：`reviews/change-review.md`（两轮，approved）
- 完成验证：`verification/completion-evidence.md`（新鲜构建 0 警告，全量 1032 / 2 / 0）

## 留给后面的一件事

替身退役出生产之后，服务器里没有任何非持久的 `IItemStore`，
于是 `AmbientStoreAccess` 与容器按 `IsDurable` 的分叉在生产上都只走一边。
这批分支是否也该退役，与「要不要有临时背包」是同一个产品问题，不在本 Spec 边界内。
