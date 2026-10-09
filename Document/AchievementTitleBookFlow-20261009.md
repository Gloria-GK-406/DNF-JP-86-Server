# 成就与称号簿协议流程

日期：2026-10-09。基准：服务端 `9da9b83e` 加称号任务分流修订；配套 ClientHack 0200_TitleBookMirror（TBM1 v1）。

## 领域职责

业务事实提交后进入 EventService。成就服务消费事实、持久化进度和完成状态，再发布成就完成事件；称号簿服务读取成就资格，幂等写入称号原件并发布 TitleUnlocked。协议订阅者负责在线投影。查询保持只读，不补结算、不解锁、不发放物品。

PVF `grade=achievement` 的任务须受理、推进、正式提交完成；`grade=title` 才被动累积。登录准备核对尚未激活且已满足的固定条件，跳过已激活条目。任务达到目标与任务完成是两个状态，不能用历史自动计数行代替任务正式完成。

## 原件、装备镜像与外观

称号簿保留原件。佩戴复制到装备位13，以 MirrorSourceUid 精确关联分类和书槽；重复 itemId 不能用模板ID判断是哪一个原件。卸下或替换删除旧镜像；真实背包装备按真实实例移动。佩戴中的书原件禁止取出。AppearanceItemId 表示显示外观，属性仍来自实际装备；Shift外观沿用0239。

## 请求、确认与投影

| 动作 | 请求 | 确认后的状态 |
|---|---|---|
| 称号簿佩戴 | CMD019D，五个i32：space、slot、itemId、category、index；装备目标3/13 | 创建或替换镜像，原件留在书中 |
| 称号簿取消 | CMD019C，同样20B，来源3/13及书定位 | 删除匹配镜像，原件不移动 |
| 实物存入/取出 | CMD019C / CMD019D，Main真实槽 | 单事务移动实物，改变书所有权 |
| 查看别人 | CMD01A8 | 只读返回InfoType1的0166，不解锁 |
| 成就核对 | CMD01A1 | 依据服务器权威事实核对，不采信任意客户端delta |

019C/019D均是CMD，不是NOTI。成功17B：u8=1 + i32 space/slot/category/index；失败10B：u8=0 + u8 error + i32 space/category。错误码当前为0x0A。配套插件仅消费自己发送请求的pending7，避免原生helper把书原件当真实物品交换。不能把字段全部置零：space0会进入Main移动分支。历史space=18+category绕过方案未释放pending，已经淘汰。

提交完成后的投影顺序为：CMD确认 → NOTI0002 subtype1装备及subtype0外观 → 必要时Main完整快照 → 静默NOTI0166称号簿。装备与书刷新从权威状态读取，书列表始终包含原件。装备操作不发0168、不用000E全量书回填；后者会触发原生获得提示。真正首次解锁才发0168，再投影0166。0167负责被动成就进度；受理任务使用任务协议。

## TBM1兼容边界

仅自身InfoType0、owner0的0166追加24B尾部，六个小端i32：magic=0x314D4254、version=1、sourceCategory、sourceIndex、sourceItemId、length=24。无来源为-1/-1/0。每条原生实体仍为26B，原生未知4B不作镜像标记。InfoType1不加扩展。插件确认原生消费游标恰好停在24B尾部后才启用镜像语义，并核对来源原件和实际装备ID。

真实Main称号替换镜像时，0200发送自定义019D：space=TBM1 magic、slot=Main原生槽、itemId=新称号、category/index=旧镜像来源。服务端验证后一次事务替换，拒绝保留旧装备；ACK用原生space3及来源分类，不回显magic。

原生0166不重建打开窗口，0200只重建收到的分类和旧/新绑定涉及的分类，同ID同步保留展示动画。悬浮提示借用对象在装备替换后可能失效，插件先核对当前权威指针，再交原生处理。没有完整崩溃转储，不能将这条静态可达路径称为唯一崩溃原因。

## 证据与验收

- ClientStatic：第二代码区构包、确认消费pending、0166原生物化与动画缓存路径；历史研究见 `tasks/title-book-projection-patch/client-*-research.md` 和 `render-recovery-20261009.md`。
- CurrentImplementation：事务、所有权、任务分流、镜像及回包由服务端回归覆盖；策略测试与22处签名校验覆盖补丁策略和固定版本入口。
- ClientVerified：2026-10-09用户反馈“好了，看起来称号系统可以了”，接受当前部署的称号系统及界面闭环。此验收依赖0200配套补丁，不证明原生未补丁客户端具有相同语义。
- 尚未逐项验证：冷启动/重启电脑、长时轮换、重复模板ID的所有边界、跨会话旧批次。TBM1没有revision/角色ID。稳定性风险保留在 [ClientHack #1](https://github.com/Gloria-GK-406/DNF-JP-86-ClientHack/issues/1)。自动化通过不替代这些实机证据。
