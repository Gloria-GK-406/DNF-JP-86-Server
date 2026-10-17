# 婚姻聚合重构：双端实机回归清单

- 日期：2026-10-17；对象：`task/marriage-domain-aggregate` 的构建（基于 `task/next-round-20261013` @ `5a0cbed0`，尚未合并）。
- 结构与偏差见[设计稿的落地状态](MarriageAggregateDesign-20261017.md)。
- **本清单由用户执行。Agent 没有做过任何实机验证。** 下面的「预期」来自代码与自动化测试；副本迁移的实测结果见 `.rks-agent/tasks/marriage-domain-aggregate/evidence/TASK-005-db-copy-migration.txt`。
- 第 2 节的骨架取自 2026-10-16 婚礼调查报告的「双端实机回归」第 1～9 步（报告没有入库）。各步的操作已经写在本文里，不需要再查原报告。
- 刻意修正 B1-1、B1-2、B1-4 需要「会话账号不拥有该角色」，正常客户端操作造不出来，不列入实机，由 `MarriagePairCorrectionTests` 覆盖。

## 0. 观察手段

**阶段与 03D0 的对应**（设计稿 §3.1；03D0 的 state 由阶段决定）

| 阶段 | stage 值 | 03D0 state | 大司祭菜单（出处 `protocol/game/server-to-client/NOTI-0x03D0.md`） |
|---|---:|---:|---|
| 无关系 / 求婚待答 / 已关闭 | — / 0 / 5 | 0（03D0 正文 15 字节全 0） | 「申请订婚」 |
| 已订婚 / 预约待答 | 1 / 2 | 1 | 「退婚」「举办婚礼」 |
| 教堂筹备 | 3 | 2 | 同上；state 2 时客户端拦截再次预约 |
| 已婚 | 4 | 4 | 「离婚」 |

**03D0 正文**（FrameCapture 里的 S2C NOTI 03D0，15 字节）：`[0..3]` 对方角色 id，`[4]` state，`[6..9]` 结婚时刻（Unix 秒），`[10..13]` 心意点。

**服务端日志关键字**

| 场景 | 关键字 | 成功时 |
|---|---|---|
| 求婚、答复 | `Marriage proposal actor=`、`Marriage response actor=` | `result=None` |
| 预约申请 | `Marriage ceremony request actor=` | `result=None` |
| 预约答复 | `Marriage ceremony response actor=` | `result=None` |
| 进教堂（03F7） | `Marriage church entry actor=` | `status=0` |
| 到点 | `Wedding <ceremony> reached Completed` / `reached Cancelled` | — |
| 心意点 | `Command WeddingLovePointCommand handled by WeddingLovePointCommandHandler` | 只说明执行过；结果看 S2C CMD 03F5 的 status |
| 祝词簿 | `Command WeddingCommentCommand handled by WeddingCommentCommandHandler` | 只说明执行过；结果看 S2C CMD 03F8 / 03F9 / 03FA 的 status |
| **不应出现** | `engaged marriage relations hold a Reserved ceremony without a live wedding`（启动审计）、`Wedding advancement failed for`、`Command … failed in handler` | — |

03F5、03F8、03F9、03FA 回包正文都是 `u8 ok=1` + `i32 status`，status 0 为成功。

**查库（可选，停服后做）**：

```sql
SELECT id, stage, revision, ceremony_id, married_at, love_points FROM marriage_relations;
SELECT relation_id, phase, completed_at, love_points FROM marriage_weddings;
```

## 1. 旧库升级（R1，先做）

- [ ] 升级前，旧二进制在运行时登录已婚角色，记下婚姻信息窗口里的婚龄和心意点。客户端正常退出后停服，备份运行库，可选按上面的 SQL 记下 phase=1 婚礼行的 `completed_at` 和 `love_points`。
- [ ] 部署本分支构建并启动。
  - 日志里没有启动审计那条 Warning。
  - 可选查库：多出 `marriage_relations` 表；已婚那对 stage=4、`ceremony_id` 是那场婚礼、`married_at` 等于 `completed_at`、`love_points` 等于婚礼行的值；被拒或过期的求婚 stage=5。
  - 副本参照：npc-shop 副本迁移后 `married_at`=1792226736402、心意点 0；wedding-force 副本 `married_at`=1792211202138、心意点 5。运行库以停服时的实际值为准。
- [ ] 已婚两个角色分别登录：
  - 03D0 `[4]` = 4，`[6..9]` = `completed_at` ÷ 1000 向下取整，`[10..13]` = 升级前的心意点；
  - 大司祭菜单显示「离婚」；婚姻信息窗口的婚龄、心意点与升级前相同。
  - 如果某条旧已婚行的 `completed_at` 为 0：`married_at` 为空，`[6..9]` 为 0，婚龄会显示成从 1970 年起算的天数。这是旧数据没有完成时刻，不是迁移错误（两份副本都没有这种行）。

**回退须知**：心意点有启动对账——新版本每次启动把关系行心意点设为它与对应已婚婚礼行旧列 `love_points` 的较大值，回退期间旧二进制用券加的点切回后会补上。阶段不对账：退回旧二进制后再推进的婚姻阶段，切回新版本时不会补进已有的关系行，回退窗口里开出的婚礼切回后还会卡住。需要回退时，回退期间不要做婚姻操作；已经做了的，切回前恢复与新版本配套的数据库备份。

## 2. 婚礼主流程

前提：恢复「已订婚、未预约」的基线。开始前两人 03D0 `[4]` = 1。

- [ ] **§6-1 预约并接受**：申请 5 分钟普通婚礼，对方接受；双方自动进教堂，检查婚服、倒计时、请柬余量。
  - 日志：`Marriage ceremony request … result=None`，然后对方的 `Marriage ceremony response … outcome=0 result=None`。
  - 申请人金币减少普通档费用（当前 PVF 为 500000，见 `MarriageWeddingFlow-20261016.md`），对方金币不变。
  - 两人 03D0 `[4]` = 2；查库 stage=3，`ceremony_id` 是这次预约。
  - 另做一次拒绝或超时：两人 03D0 仍为 1，金币不变，查库 stage=1、`ceremony_id` 为空。
- [ ] **§6-2～§6-4 离开、返回、重登**：新郎经大司祭「离开礼堂」，底部出现返回图标；在城里跨区走一次，图标仍在；点图标返回同一间教堂，来回两次；开场前在教堂外重登，图标恢复，余量正确。返回时日志 `Marriage church entry … flag=0 status=0`；全程 03D0 `[4]` 保持 2，开场时刻不变；查库 stage 保持 3、revision 不变。
- [ ] **§6-5 最后 60 秒**：礼堂里点「离开礼堂」会被拦截；派送窗的逐行邀请按钮置灰；用底部按钮、右上 ×、Esc 各关一次再打开，倒计时不变。这一步与本次重构无关。
- [ ] **§6-6 双方在场到点**：开场动画完整播放，期间没有 03CF；结束后客户端自己离场，婚服撤掉，图标消失。
  - 日志：`Wedding <ceremony> reached Completed`。
  - 两人 03D0 `[4]` = 4，`[6..9]` 是到点时刻，`[10..13]` = 0（刚结婚还没有心意点）；大司祭菜单变为「离婚」。
  - 两人邮件各有一份普通婚礼化妆箱；有宾客时，礼金扣手续费后平分，宾客收到回礼。
  - 重登后上面各项不变，不重复发奖、不重复扣款。
  - 查库 stage=4，`married_at` 等于婚礼行 `completed_at`，婚礼行 phase=1。
- [ ] **§6-7 缺席取消**：新郎留在教堂外直到到点；场内一方只被移出一次。
  - 日志：`Wedding <ceremony> reached Cancelled`。
  - 两人 03D0 `[4]` = 1；申请人收到退还婚礼费用的邮件，只有一封；有宾客时，宾客礼金全额退回。
  - 婚前任务保留，可以重新预约（菜单有「举办婚礼」，再次申请得到 `result=None`）。
  - 查库 stage=1，`ceremony_id` 为空。
- [ ] **§6-8 多场隔离**：两对新人在不同的教堂区域同时筹备。两对新人各自的 03D0、阶段、到点结果互不影响；查库两条关系各自推进，`ceremony_id` 各指各的婚礼。
- [ ] **§6-9 商店**：在教堂里向大司祭购买两种礼炮和真爱考验箱，金币分别减少 1000、1000、10000；重登后金币和物品保持不变。这一步与本次重构无关。

## 3. 新增项

### R2 心意点：使用一次

前提：一对已婚角色同时在线，背包里有心意通。

- [ ] 使用一枚：
  - 03F5 status 0；
  - 背包里这枚券数量减 1；
  - 两人都收到 NOTI 03D3，点数相同，为原点数加每枚点数（取 PVF，当前为 1，见 `MarriageWeddingSixIssues-20261017.md`）；
  - 日志有 `Command WeddingLovePointCommand handled by WeddingLovePointCommandHandler`。
- [ ] 两人重登：03D0 `[10..13]` 和婚姻信息窗口显示新点数。
- [ ] 可选查库：`marriage_relations.love_points` 与 phase=1 婚礼行的 `love_points` 相同（过渡期双写）。
- [ ] 可选：未婚角色使用，03F5 status -3，券不扣。

### R3 心意点：到上限

上限取 PVF（当前为 999，见 `MarriageWeddingSixIssues-20261017.md`）。正常使用很难攒到上限，所以先改库：

- [ ] 停服，把这对关系的 `marriage_relations.love_points` 设为 999 后启动。**只改旧列 `marriage_weddings.love_points` 已经不起作用**，点数以关系行为准。
- [ ] 使用一枚：03F5 status -2，客户端弹出心意点上限提示（dstr72913，见 `protocol/game/server-to-client/CMD-0x03F5.md`）；券数量不变；没有 03D3；重登后 03D0 `[10..13]` 仍为 999。
- [ ] 可选：改成 998 再试。第一次成功并到 999，第二次得 -2。

### R4 祝词簿

前提：一对新人处于教堂筹备（03D0 `[4]` = 2），另有第三个账号的角色作为宾客，在同一频道。祝福列表在教堂里点 NPC 20010「幸福的见证」打开；写和删的入口以客户端实际界面为准。

- [ ] 宾客收到请柬、付了礼金、已进教堂（日志 `Marriage church entry … flag=1 status=0`）后写一条 20 字节以内的可打印文本：03F8 status 0。
- [ ] 新人打开祝福列表：03FA status 0，列表里有这条，作者是宾客的名字。
- [ ] 新人删除这条：03F9 status 0；再读，列表里没有它。
- [ ] 收到请柬但**没有进教堂**的宾客写一条：03F8 status -1，新人读到的列表没有新增。
- [ ] 没有收到请柬的角色读、写都得 -1。
- [ ] 新人自己写：03F8 status -1（现状就是新人只能读和删）。
- [ ] 可选查库：`marriage_guest_comments` 的行数与上面的增删一致。

### R5 重启后阶段不变

每一项都是：先确认 03D0，客户端正常退出，停服再启动，重登后再看。

- [ ] 已订婚：重启前后 03D0 `[4]` 都是 1，查库 stage=1。
- [ ] 教堂筹备，且在开场时刻之前重启：前后都是 2；开场时刻不变；可以照 §6-3 返回教堂。查库 stage=3、`ceremony_id` 不变。如果停服跨过了开场时刻，启动后到点推进会按缺席处理，与重构前相同。
- [ ] 已婚：前后都是 4，`[6..9]`、`[10..13]` 不变；婚姻信息窗口的婚龄、心意点不变。
- [ ] 预约待答时重启：重登后会话已经不是申请时那一个，预约按到期处理。重登看到 03D0 `[4]` = 1，可以重新申请（日志 `Marriage ceremony request … result=None`）。
- [ ] 每次启动都没有启动审计那条 Warning。
