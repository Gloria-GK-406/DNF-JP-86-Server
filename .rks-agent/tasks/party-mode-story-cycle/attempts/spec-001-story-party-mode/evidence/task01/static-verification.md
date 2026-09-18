# Story 静态验证记录

## 验证方法

探针 `probe_story_party.py` 对当前 B1 EXE/dstr、19 个最小语义窗口、6 条资源文本和固定上游 commit 的
5 个 Git blob 做 fail-closed 校验。任何输入哈希、ImageBase、窗口字节、资源文本或上游 blob 不一致都会
非零退出。

运行命令（从 successor worktree 根目录）：

```powershell
python -X utf8 `
  .rks-agent/tasks/party-mode-story-cycle/attempts/spec-001-story-party-mode/evidence/task01/probe_story_party.py `
  E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe `
  E:/Project/DNF/DNFClient-A21-debug/dstr.dat `
  --upstream E:/Project/DNF/ServerS4A21
```

## 当前结果

- 当前 B1 身份与 ImageBase：PASS。
- dstr 身份与 `FILE/102` 解码：PASS。
- 资源 `48600/48601/48605/48612/48613/49275`：`6/6 PASS`。
- Story 构包、通用提交、S2C 消费、target 查找/比较/进度、委任阻断、自动刷新和进入失败窗口：
  `19/19 PASS`。
- 固定上游 commit 的 parser/handler/Party/PartyManager/NOTI builder：`5/5 PASS`。

窗口输出逐项包含 VA、字节数和该窗口的 SHA-256；完整 stdout 由命令即时生成，不在文档中复制冗长日志。

## 静态边界

探针只证明选定字节没有漂移，并不自动赋予业务语义。业务裁决仍按完整构包/消费调用链记录在
`story-field-map.md`，其中精确资料表身份、合法 target 值域、raw `[3..7]` 的业务含义和真实 wire 默认
保持未决。
