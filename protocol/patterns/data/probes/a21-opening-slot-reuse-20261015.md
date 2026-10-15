# A21：开包同格替换边界

日期：2026-10-15；服务端调查基线：`99eb3c1c5d8276848243f301dcd629113caf6475`。

证据：`DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`，SHA-256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。以下地址为 VA，image base=`00400000`。

## ClientStatic / Confirmed

```text
0117E90F call 016EE6C0       ; 查当前格
0117E918 je   0117EEFD      ; 空格创建分支
0117E91E cmp  [ebp-216],-1  ; 传入 itemId
0117E925 jne  0117E93B
0117E931 call 016F1300      ; 删除旧格
0117E951 mov  edx,[eax+5C]
0117E957 call edx           ; 当前物品模板匹配检查
0117E95F je   0117EAF0
0117EAF2 call [eax]         ; 当前对象 class getter
0117EAF4 cmp  eax,2
0117EAF7 jne  0117F062      ; 不同模板且 class!=2：跳过更新
0117EF24 call 01A85220      ; 空格创建新物品
0117EF6C call 016F2FB0      ; 安装到格子
```

篮子 vtable=`032FE0D4`，vtable+5C=`00EE9110`：`00EE9137` 比较对象+20 的物品 ID 与传入 ID，不匹配返回 false；vtable+0=`02846D50` 读取对象+4 的 class。

普通 00A0 handler=`0113A830`。当前第二附加整数为0：`0113AA13 cmp eax,1`、`0113AB9E cmp eax,2` 均不匹配，跳至 `0113AC8F` 调用 `016F3360`。后者只将 manager+70 的待处理数组尾指针减4，不删除背包源对象。魔盒 ACK 的原生扣减不能直接推广到普通礼包。

## 当前观测与历史边界

本轮只读采样客户端 PID=41404，第7格对象=`36159C00`，ID=10007393、class=1、vtable 如上；代码锚点 `02730A50` 为 `558bec6aff68704de90264a100000000`。数据库角色8/Main第7格为 ID=10007344、count=1。

服务端日志 21:21:54.218 第7格开包成功，发送 CMD00A0/36B 和一个 NOTI000E/119B，无独立清格；21:22:14.236 同格拒绝 `Item 10007344 does not belong to Package`。历史奖励 ID、开包前数量及原始成功 body 未保存，因此这一操作耗尽并复用源格为 High 归因，不能称 WireCaptured。

## CurrentImplementation

生产命令/真实临时 SQLite/生产编码器加上述有限接收模型复现：最后一份礼包和配方多消耗格被奖励复用时，旧客户端物品保留。修复后 receipt 保留 Replaced，ACK → 清复用格 → 最终更新 → 其他清格，使模型收敛；已有堆叠、空格与魔盒 ACK 差异有回归覆盖。修复后的原生实机效果仍待用户验收。
