# PVF 镜像候选核对

2026-10-10，使用当前只读 Script.pvf。

Mirror pairs=393; unique quest IDs=393; clear-map quests in all n_quest=126; intersection=[]

探针命令：`PvfProbe dump etc/crackofdimensionlist.etc 10000`、`PvfProbe where n_quest/quest.lst "[type]" "[clear map]" 10000`。原始输出见相邻 txt。候选列表与全资源 clear-map 任务 ID 取交集；当前没有镜像 clear-map 候选。

全部镜像候选的领域目标解析另由 `Every_declared_A21_mirror_pair_has_a_supported_run_objective` 在启用真实 PVF 时验证。ClearMap 分支是兼容支持，仅按 MAP 语义；上游另有历史 DGN 兜底，本次未保留该混用。
