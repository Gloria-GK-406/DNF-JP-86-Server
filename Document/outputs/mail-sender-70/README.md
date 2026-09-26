# 70端邮件发件人调查证据

2026-09-26归档。五份原始资料共196140字节；保留用于系统奖励发件人、每日物品投送与特殊邮件来源的后续复核。

## 来源与边界

样本为1995chen/dnf仓库的 other/等级补丁/70/df_game_r，32位有符号ELF。本次只读复核SHA-256：CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7，与[70端邮件系统调查](../mail-system-investigation/70-server.md)一致。反汇编地址均为该ELF的VA。

这些是70端静态参考证据，不是A21实帧，不证明A21采用同一规则；不凭函数名称推断完整业务语义。本次仅归档，没有重新运行服务器或完成新的逆向定案。

A21 senderId=0/2000000000的系统图形行为，应查[NOTI0061](../../../protocol/game/server-to-client/NOTI-0x0061.md)。当前任务助手人偶采用系统senderId=0及登录补缺，是项目已接受的设计，不宣称由本目录证明官方唯一实现。

## 文件

| 文件 | 内容 |
|---|---|
| core.asm.txt | ReqDBSendNewSystemMail、_makeSendMailData及活动奖励、每日奖励、广告奖励、拍卖邮件相关函数 |
| target.asm.txt | 商城邮件、CUser::AddDailyItem、GM邮件及公会仓库相关函数 |
| callers.txt | 两个核心邮件函数的直接调用点和所属符号 |
| premium-symbols.txt | 增值服务相关符号检索结果，作为定位线索 |
| find-sender-calls.py | 只读直接调用点扫描；候选经所属符号入口反汇编校验指令边界 |

脚本依赖同级上层已跟踪的mail-system-investigation/mail_elf.py，以及Python capstone、pyelftools。样本路径在mail_elf.py内；跨机器使用前核对路径及哈希。脚本只枚举直接call，不覆盖虚表、间接调用或运行期动态行为。

## 原始文件校验值

- callers.txt: SHA-256 e4b7127339fcd93788f33fdd8367ad1386eff5127555c2633706f20eb90f722e
- core.asm.txt: SHA-256 f3ef3a9c121499c9c0e82e8d45315a507f9163a6acb09b7cdfb957c8006dd5b0
- find-sender-calls.py: SHA-256 85a799cf16d81295ba09bc369c1ee054df23873fa0890af2514a14dfe95a3497
- premium-symbols.txt: SHA-256 375bfcabe3a94b2acf723c62241b5137322fc0aed9b8e22c1db4bb16193e44e9
- target.asm.txt: SHA-256 50d56dc3261af55c077b49119008444474ef51dfbb4bd7b2feafe4be4f7cabc2

## 归档格式规范化

上述校验值保留原始采集文件身份。提交版本仅统一LF并移除行尾空白，逐行非空白文本未变；以下为归档版本校验值。

- callers.txt: SHA-256 25de08d46c870b11b7c46be3cecb43912d6a8af4fd074a0d480ff344b3749edc
- core.asm.txt: SHA-256 adf38aaa09aa1b928ecbc9dbb708e406b7c3463083e4d5e6710c2fa8fec1d76a
- find-sender-calls.py: SHA-256 cee17be768ea3cd0e2c48df6d95c1865362e72453446ee2956db69f81323330e
- premium-symbols.txt: SHA-256 8bce710eadb8facc71471aaa69280a3a9e92c567c1f8484dbc498718e659a93b
- target.asm.txt: SHA-256 6f0b6f33ed05402764d1696bcf1658d64dd9796122523ff3c3f6aac5a946e971
