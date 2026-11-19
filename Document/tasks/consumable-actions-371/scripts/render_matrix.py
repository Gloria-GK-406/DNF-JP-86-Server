"""Render the reviewed per-action judgments against the actual PVF scope."""
from pathlib import Path
import json
import sys

sys.stdout.reconfigure(encoding="utf-8")
TASK = Path(__file__).resolve().parents[1]
scope = json.loads((TASK / "evidence/scope.json").read_text(encoding="utf-8"))
# Batch is a proposed dependency boundary, not implementation or acceptance status.
# B1=existing owners, B2=domain extensions, B3=gameplay foundations, R=route/semantics research.
DECISIONS = {
    "[any quest clear]": ("B1", "任务/任务奖励", "按列出的任务 ID、职业和等级清理；逐券确定奖励和额外材料", "请求路径、指定任务链、奖励口径"),
    "[first awaken clear]": ("B2", "任务/角色职业/技能", "完成一觉链并应用应有的职业与技能变化；不只置完成标志", "一觉链、职业限制、材料、转职及技能投影"),
    "[second awaken clear]": ("B2", "任务/角色职业/技能", "完成二觉链；校验已有一觉、职业和等级", "二觉参数变体、角色副作用和请求"),
    "[achievement quest clear]": ("B2", "成就/称号簿/任务", "按文案处理普通成就任务和称号簿，不清成普通 Epic", "与称号簿映射、完成范围及奖励"),
    "[quest clear]": ("B1", "任务", "取消单 ID 限制前逐族区分外传、主线等；保留现有券已确认策略", "12 张新增券的类型、等级和奖励"),
    "[quest clear coupon]": ("R", "任务/经验", "选一个已接主线任务；代表券文案为只给经验，需界面确认", "选择界面最终 CMD/任务 ID、经验倍率及取消"),
    "[all quest clear]": ("R", "任务", "先恢复占位文案与实际作用范围；不能按名称清全任务", "真实文案、客户端分支与完成范围"),
    "[assist quest clear]": ("R", "任务", "先确定辅助任务集合及奖励，再复用任务 owner", "占位文本、集合来源与奖励"),
    "[change creature]": ("B2", "宠物/物品身份", "按源宠物到目标模板的 PVF 映射变化；明确 UID/名字/经验保留规则", "候选动作64、目标容器槽位及保留字段"),
    "[change creature random]": ("B2", "宠物/随机奖励", "解析候选及权重，事务内选择并改变宠物；不采用均匀概率", "随机数据格式、目标选择、属性保留与结果通知"),
    "[creature growth]": ("B2", "宠物", "区分升级与进化参数，复用宠物实例及经验表", "候选106、四种参数组合、上限及材料"),
    "[creature use item]": ("B2", "宠物", "区分百分比经验与指定等级；复用 AddExp，不把三种参数都当固定经验", "三个参数组合的意义、目标宠物和上限"),
    "[creature transport stone]": ("R", "宠物/账号物品容器", "先核对是否走跨界专用协议；确认绑定、目标账号仓容量及 UID", "专用 CMD、两种参数、与 #372 的归属"),
    "[up layer despair dungeon]": ("B2", "绝望之塔", "先区分邀请入场与直接推进层数，按实际行为扩展塔 owner", "邀请函 UI/请求、是否消耗次数与奖励"),
    "[clear layer despair dungeon]": ("B2", "绝望之塔/发奖", "直接通关由塔 owner 完成进度、次数、奖励和任务副作用", "奖励集合、每日次数、100层及循环边界"),
    "[elevator tower of despair]": ("B2", "绝望之塔", "按 PVF 目标层移动进度，明确是否发沿途奖励", "九种目标参数、是否含目标层与奖励"),
    "[init dimension dungeon count]": ("B2", "异界/次数", "按 PVF 指定副本集合重置，不能把旧副本号无条件映射当前融合异界", "四种参数族、副本对应、重置口径"),
    "[add dimension dungeon enter count]": ("B1", "异界/次数", "增加额外次数，与入场扣次和显示使用同一 owner", "额外次数存储、跨日策略、材料"),
    "[add account enter limit dungeonincount]": ("B3", "账号次数/对应地下城", "建立真实账号级次数；代表券目标6004不能套用角色级异界次数", "账号归属、目标副本能力、跨角色共享与重置"),
    "[auto dungeon clear]": ("B3", "地下城结算/奖励", "按券声明的副本表和次数自动结算，必须完整覆盖奖励及任务副作用", "扫荡玩法、消耗/收益表和最终界面请求"),
    "[init anton raid try]": ("B3", "安徒恩攻坚", "仅在攻坚次数 owner 建立且 A21 路径证实后接入", "攻坚玩法、次数状态及投影"),
    "[init anton raid buy item]": ("B3", "安徒恩攻坚/商店", "重置指定购买额度，不能重置入场次数冒充生效", "购买额度 owner、商品范围与 A21 UI"),
    "[dungeon reward reset]": ("B3", "地下城奖励资格", "按奖励种类重置领取资格，不能只修改入场次数", "参数28/0定义、奖励资格 owner"),
    "[guild dungeon time ticket]": ("B3", "公会地下城", "按声明延长指定公会副本时限；不能作为个人次数券", "公会副本 owner、作用域、计时和广播"),
    "[clear layer scream dungeon]": ("B3", "对应幻影塔玩法", "先辨明 PVF 指的是哪座塔，不能仅凭译名复用已有幻影/死亡塔", "副本身份、层数、奖励及已有塔的差异"),
    "[reset all skill cooltime]": ("B2", "技能运行态/客户端战斗", "确认哪些冷却由客户端计算，再决定确认和服务端运行态变更", "技能冷却清除路径；现有物品冷却不能替代技能冷却"),
    "[reset second grow skill cooltime]": ("B2", "技能运行态/客户端战斗", "仅重置指定觉醒技能冷却，保留其他技能及物品冷却", "候选81、适用技能集合、城镇/副本及限次"),
    "[equipment item period change]": ("B2", "装备身份/期限", "按模板映射变更装备和期限；逐族确定强化、锻造、附魔等保留字段", "候选132、目标槽参数、16种映射与保留矩阵"),
    "[change growth equipment]": ("B2", "装备身份/期限", "区分成长装备转换与期限修改；不能直接调用杀怪成长升级", "候选88、两个参数族、永久标志和模板变化"),
    "[equipment purify]": ("R", "装备异界处理", "同标签含净化、清除和黄金形状；先核对0x00CC/0x00CD，沿原owner补缺", "当前净化/清除表覆盖9个，黄金1个、表外2个需补证；最终CMD和失败提示"),
    "[extend item period]": ("B2", "物品期限", "解析可延长的物品类别、单位、上限及现有到期时间", "三种参数、目标、叠加/永久/过期边界"),
    "[change phoenix period]": ("B2", "装备身份/期限", "按不死鸟系列定义处理永久化或兑换，保留合法属性", "0/0含义、对应装备及到期状态"),
    "[init roption unique change count]": ("B2", "装备魔法封印", "重置目标装备的指定变更次数，不清已有词条或增幅", "计数字段和生命周期是否已有、目标请求"),
    "[free repair equipment]": ("B1", "装备修理", "复用耐久 owner，先确认免费修理作用对象和是否仅打开界面", "最终 CMD、范围、无损装备时扣券口径"),
    "[repair equipment]": ("B1", "装备修理", "复用修理规划和耐久写入，按券选择单件或全部", "目标选择、修理量、金币与耗材关系"),
    "[item change]": ("R", "物品转换", "占位物品先取实际文案/脚本表；不能凭标签构造兑换", "输入输出表、目标及数量"),
    "[level up decision change]": ("B3", "预约角色活动", "归预约等级活动 owner，不能直接修改角色等级", "预约活动事实、可改范围与奖励资格"),
    "[open aura skin slot]": ("B1", "光环幻化栏", "复用现有开栏事实与效果规划，扩展四券识别；两入口不重复扣", "候选123、0x0357与0204两路径及已开栏提示"),
    "[rent point]": ("B1", "账号幸运星", "复用账号余额与 GrantFromItem；002C/0204共用一次结算", "候选105、上限、四种增量及双入口"),
    "[add auctionpoint]": ("B3", "拍卖点数", "先确定点数用途、owner及消费方，不能只加一个无人使用的数值", "八种增量、余额/时长单位和实际消费"),
    "[add mileage]": ("B3", "决斗进度/对应点数", "代表物品增加决斗经验；按14种参数和模板分族，禁止统一作商城积分", "参数到收益映射、PvP分支协作与持久化"),
    "[give goldcoin]": ("B3", "勇士徽章拼图活动", "代表物品用于活动拼图；不能按名称发金币", "活动入口、拼图状态和奖励表"),
    "[charm charging]": ("B2", "符咒装备/能量", "按目标符咒补能并保留身份，确认100/20等参数量纲", "能量 owner、三种参数、目标与上限"),
    "[decrease pcroom honey time]": ("B3", "网吧活动计时", "在对应活动 owner 缩短所需时间，核对秒/分钟和领取资格", "计时与领奖玩法、六种参数及重登"),
    "[chatemoticon]": ("B3", "账号表情授权", "按列表授权表情；不能只扣道具不改变可用列表", "账号/角色作用域、期限、重复授权与 UI"),
    "[open guide book]": ("R", "客户端指南 UI", "代表物品3330右键查看深渊入场材料；仅打开指南不得扣邀请函", "是否本地完成、是否有最终CMD、额外材料仅在何时使用"),
    "[story book]": ("B2", "任务接受/故事簿", "按书到任务序列建立阅读/接受流程，禁止当批量完成券", "候选135、45个序列、重复阅读/取消与消费时点"),
    "[ridable item]": ("B3", "外观/骑乘", "辨明变身胶囊或真正骑乘；别直接当遗忘之地 action99", "外观对象、动作状态、地图限制、旁观者同步与取消"),
    "[change avatar]": ("B2", "装扮物品身份", "按映射转换指定装扮，明确期限、徽章及染色保留", "六种参数、选择协议、目标与属性保留"),
    "[compound item]": ("R", "物品组合/表现", "代表物品玫瑰花瓣只给输出线索；先区分表现动作与合成兑换", "21种参数、材料和产物数量及实际协议"),
    "[increase guild exp fund]": ("B3", "公会经验/资金", "按子类型增加真实公会经验或资金，校验成员资格", "公会 owner、参数模式与成员/全会收益"),
    "[guild hongbao]": ("B3", "公会红包/经济", "实现出资、红包创建、领取资格及一次领取完整链路", "四种红包类型、货币、多人事务及领取持久化"),
    "[guild mark effect]": ("B3", "公会标记外观", "按期限设置标记效果并同步成员可见状态", "标记 owner、期限和其他玩家可见性"),
    "[add guildmileage]": ("B3", "公会积分", "确定角色贡献还是公会余额并在真实消费链路生效", "积分归属、上限和消费方"),
    "[teleport to partner]": ("B2", "婚姻/城镇放置", "复用关系与 TownTransit，校验伴侣在线、可到达且在允许区域", "传送请求、跨区/婚房/副本限制和双人通知"),
    "[deploy in isometric cell map]": ("B3", "对应等距地图活动", "需格子占用、建筑对象、升级/时限和活动结算 owner", "144种参数、地图/对象存在性及完整玩法"),
    "[guess number item]": ("B3", "猜数字活动", "按活动配置处理机会或奖励，需活动状态和玩法闭环", "候选150、11种参数与活动配置"),
    "[bingo]": ("B3", "BINGO活动", "按活动棋盘与奖励结算；70枚举不能直接用于A21", "A21棋盘入口、五种参数和发奖"),
    "[bingo stamp]": ("B3", "BINGO活动", "与同一棋盘 owner 增加印章或落印", "印章用途、目标、次数与棋盘刷新"),
    "[use bingo mal item]": ("B3", "数字解密活动", "按棋盘活动配置使用，不能假定与旧BINGO同一规则", "活动种类、参数10及奖励表"),
    "[real estate item]": ("B3", "地产活动", "需要地产/建筑状态和目标行为，不套婚房装饰", "六种类型、地图与活动状态"),
    "[event action]": ("B3", "对应活动", "按事件编号分派至真实活动 owner；禁止通用无效果成功", "五种事件参数、所属活动与奖励"),
    "[counting coin]": ("B3", "计数币活动", "按对应币的活动统计与兑换链路处理", "三个物品参数的用途与统计 owner"),
    "[yet game item]": ("B3", "对应掷柶/活动", "先取客户端入口与本地文案，再定义玩法", "真实活动身份、状态及请求"),
    "[territory combat item]": ("B3", "领地战活动", "需要领地战参与资格、物资和收益模型", "三种参数、参与者与奖励作用域"),
    "[snorkeling chance item]": ("B3", "潜水活动", "增加潜水机会并在实际小游戏消费", "候选154、活动配置和机会计数"),
    "[snorkeling upgrade item]": ("B3", "潜水活动", "升级潜水设备，必须影响后续玩法", "候选155、升级表、上限和设备状态"),
    "[lucky balloon item]": ("B3", "飞镖/气球活动", "按活动机会与奖励表执行", "活动入口、-1/1参数与奖励"),
    "[seria room deco]": ("B3", "赛丽亚房间活动装饰", "区分临时房间装饰与婚房；同步期限和重登", "房间装饰 owner、期限与资源"),
    "[tournament ticket]": ("R", "对应比赛玩法", "先确认是否本仓比武大会；不可仅因 tournament 同名就加次数", "两种参数、真实比赛身份及使用路径"),
    "[Red envelope item]": ("B3", "节日红包活动", "按节日红包规则打开/领取；与公会红包分开", "文案、奖励表与活动时间"),
    "[steampunk]": ("B3", "蒸汽朋克活动", "取证活动入口、状态和奖品，再实现闭环", "玩法身份、参与条件和结算"),
    "[celebrate big transition]": ("B3", "毁灭纪变身活动", "按候选对象表处理变身，核实随机及结束条件", "对象列表量纲、外观广播与恢复"),
    "[Change RedMonkey Item]": ("B3", "红猴召唤/变身活动", "先区分宠物/APC/变身，核实实际参与者", "召唤种类、持续时间和客户端行为"),
    "[event extexsion day item]": ("B3", "活动延期", "仅延长所属活动的有效期，不套任意装备期限", "活动身份、延期单位和领奖资格"),
    "[npc growth]": ("B3", "NPC养成活动", "建立对应NPC成长与领取奖励的闭环", "参数0、NPC身份和养成表"),
    "[create passive object]": ("B2", "副本/场景对象", "核对是否客户端生成，再决定服务端校验、消耗及对象投影", "两个对象参数、场景限制和他人可见性"),
    "[expert job store doll]": ("B3", "专家职业商店", "开店助手需真实商店、交易和关闭生命周期", "店铺 owner、职业限制、交易和掉线收尾"),
    "[random recovery fatigue]": ("B1", "疲劳/随机表", "复用疲劳与使用限制，按已证的随机表结算", "数据表语法、<106限制、恢复上限和用后投影"),
}

expected = {r["ActionType"] for r in scope["Representatives"]}
assert set(DECISIONS) == expected, (expected-set(DECISIONS), set(DECISIONS)-expected)
rows = []
for index, item in enumerate(scope["Representatives"], 1):
    batch, owner, behavior, gate = DECISIONS[item["ActionType"]]
    representative = item["Representative"]
    rows.append({"Case": f"A{index:03}", "Group": item["Group"], "ActionType": item["ActionType"],
        "Count": item["Count"], "RepresentativeId": representative["Id"], "RepresentativeName": " ".join(representative["Name"]),
        "PvfPath": representative["Path"], "RepresentativeAction": representative["Action"],
        "OperandVariants": len(item["OperandVariants"]), "Batch": batch, "Owner": owner,
        "ProposedBehavior": behavior, "EvidenceGate": gate, "AcceptanceStatus": "未执行",
        "RouteStatus": "未新增A21实帧；需逐族核对", "UnsupportedA21": False})
(TASK/"evidence/action-decisions.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2)+"\n", encoding="utf-8", newline="\n")

lines = ["# #371 逐动作实现判断", "", "范围来自 issue 快照与现行 dev PVF，79 行、819 个模板；每行对应一个验收身份。数量是模板数，不是可直接使用的功能数。", "",
    "本表是实现建议和预期，不是实机结果。所有行均为**未执行验收**，没有任何一行已被判定为 A21 不支持。", "",
    "B1：优先复用现有领域，但仍须补齐本行取证；B2：扩展领域能力；B3：依赖完整玩法基础；R：先纠正协议路径或语义，再决定归属。", "",
    "公共验收：按真实协议操作、检查明确反馈、只发生预期效果、消费型动作成功扣除道具及已确认属于该操作的材料。失败保持资产，只读UI动作不扣物品。详见 [验收计划](acceptance-plan.md)。", ""]
for group in scope["Groups"]:
    lines += [f"## {group['Group']}（{len(group['Tags'])} 种，{group['Items']} 个模板）", "",
        "| 验收ID / action type | 模板数 / 参数组合 | PVF代表物品 | 批次 / owner | 建议效果与实现判断 | 放行前必须确认 |",
        "|---|---:|---|---|---|---|"]
    for row in rows:
        if row["Group"] != group["Group"]:
            continue
        def escape(text): return str(text).replace("|", "\\|").replace("\n", " ")
        name = row["RepresentativeName"]
        if not name or name.startswith("name_"):
            name = "本地文案缺失/占位，不能作语义证据"
        lines.append(f"| {row['Case']} `{row['ActionType']}` | {row['Count']} / {row['OperandVariants']} | {row['RepresentativeId']} {escape(name)} | {row['Batch']} / {row['Owner']} | {escape(row['ProposedBehavior'])} | {escape(row['EvidenceGate'])} |")
    lines += [""]
lines += ["## 复核入口", "", "- 每个代表物品的脚本路径、原始动作参数、文案和限制：[scope.json](evidence/scope.json)。",
    "- 所有带动作标签物品的重新扫描：[pvf-actions.json](evidence/pvf-actions.json)。",
    "- 本仓/上游/第三方逐标签定位和70字符串存在性：[reference-matches.json](evidence/reference-matches.json)。字符串搜索未命中不能证明没有能力。",
    "- 机器可核对的本表字段：[action-decisions.json](evidence/action-decisions.json)。", ""]
(TASK/"action-matrix.md").write_text("\n".join(lines), encoding="utf-8", newline="\n")
counts = {}
for batch in ["B1", "B2", "B3", "R"]:
    subset = [row for row in rows if row["Batch"] == batch]
    counts[batch] = {"ActionTypes": len(subset), "Templates": sum(row["Count"] for row in subset)}
print(json.dumps(counts, ensure_ascii=False))
