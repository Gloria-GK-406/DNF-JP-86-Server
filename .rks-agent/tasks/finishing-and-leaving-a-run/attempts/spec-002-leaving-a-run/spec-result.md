---
kind: rks-spec-result
outcome: completed
---

# spec-002-leaving-a-run 结果

## 交付了什么

* `CMD 0x0048 EPLP_COMMAND`：解码 `(state, option)`，回 `[01 state option]`。
* `state == 1` 时结束本局并推城镇状态（`NOTI 0x0003 / 0x0017 / 0x0018`）。
* `TownReturnProjection`：城镇投影抽成一份，`BACK_2_VILLAGE` 与本成果共用。

## 判据强度

四个变异全部被抓住：`LeaveState` 1→2、应答不回显 state、城镇投影改读频道出生点、
以及第一件遗留的注册键变异。

## 未做

上游在城镇状态之后还推 `NOTI 0x00CA VILLAGE_OBJECT_LIST` 与游戏端 `NOTI 0x0002 USERINFO`，
本仓两者都没有；`BACK_2_VILLAGE` 这条早已在用的路子也不推，实机上回城正常。加它们要各自取证。
