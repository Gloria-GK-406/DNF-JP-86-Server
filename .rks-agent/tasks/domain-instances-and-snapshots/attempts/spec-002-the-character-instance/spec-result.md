---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: The character instance

## Delivered

- `ICharacter` 与 `PlayingCharacter`：人物成为可操作的实例，由 `ICharacterDomainService.Get` 按需交出。
  它只持身份与依赖，每次回答都读当下。
- 「这个角色能不能用这件物品」经由它表达；`UseStackableCommandHandler` 不再持有可用性服务。
- 原先既解析身份又作判决的 `ItemUsabilityDomainService` 已删除，拆成两半：实例供身份，
  `ItemUseJudgementService` 作判决且**找不到任何角色**。全仓十个规则/判定服务的签名中，
  此后没有一个带身份。
- 判定的先后次序随比较一并迁入判决，并首次获得六对全覆盖的守卫。
- 设计文档的第一处未决项已裁定；R-4 与 R-6 各修订一处。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-B1 | `CharacterRepositoryProductsTests` 三条（用工厂构造，问哪个拿哪个）；未被选中时的答案由既有用例守住 |
| SC-B2 | `UseStackableCommandHandler` 的依赖清单与调用点 |
| SC-B3 | 十个规则/判定服务接口逐个清点，签名无身份；`int recordedAccountId` 的区分已写入 R-6 |
| SC-B4 | 七种结果逐条可构造；`Every_pair_of_reasons_is_reported_in_the_settled_order` 覆盖六对且各自实测会红 |
| SC-B5 | 构建零错误零警告；全量 1172 / 2 跳过 / 0 失败 |
| SC-B6 | `Docs/Refactor/DomainInstanceDesign.md` 的「Settled by the character-side implementation」一节 |
| SC-B7 | 八次可证伪演示，见 `verification/completion-evidence.md` |

## Repository Result

- Changed paths：24 个，全部属于本尝试。
- Baseline：`7478e2e`，尝试期间未变。
- Parent rollback required：no。

## Successor Guidance

- **R-4 与契约 SC-B1 都被改写过一次，理由记在契约的 revision 2 修订说明里**：背包要注册是因为一个角色
  可持有同类型多个并在其间切换，人物没有这回事。改写前的理由（分配开销）与设计文档自己的规模前提冲突。
  下一个成果若要给某个实例加生命周期，判据是「它有没有一个需要被记住的绑定」，不是「背包是这么做的」。
- **把一条闸门搬进被守卫的一侧，可能让守卫恒不可能变红。** 本轮实际发生过：冷却闸门移入判决后，
  一条用 `RequiringLevel(50)`（其 `CooldownMilliseconds` 为 0）构造的次序守卫从此不可能红，
  而它的注释仍宣称覆盖了那一对。搬动闸门之后，要重跑一次针对被搬闸门的可证伪演示。
- **一条 `[Theory]` 的行数不等于它覆盖的对数。** 本轮六行只覆盖了五对：每一行「等级不足」的同时都
  「不在冷却」，于是等级对冷却这一对写在摘要里、没写进任何一行。
- `ItemUseEffectDomainService.ApplyAsync` 仍同时收 `CharacterId` 与 `IItemContainer`，
  是设计文档剩下的两处未决项之一。
- `ApplicationModule` 里构造 `PlayingCharacter` 的工厂 lambda 仍无测试跑过。

## Artifacts

- `spec.md`、`plan.md`
- `reviews/change-review.md`（两轮，第一轮 `changes_required`，第二轮 `passed`）
- `verification/completion-evidence.md`
- `Docs/Refactor/DomainInstanceDesign.md`（R-4、R-6 的修订与落地记录）
