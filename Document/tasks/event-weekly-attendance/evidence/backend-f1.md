---
kind: rks-verification
decision: pass
---

# F-1：006C 中文编码与保存边界

基线 `af0715492d9b9850756afdd908f8a93614795ea6`，在原未提交后端结果上修正。依据独立审查F-1及新鲜 [calendar-text-encoding.txt](calendar-text-encoding.txt)，只调整006C日历文本：严格CP936/GBK编码，dstring长度为编码字节数；规则服务在配置保存前拒绝无法表示的字符。未修改其他协议文本编码、Web、契约/YAML，未提交或操作服务。

## 保护的行为

中文必须按真实消费者的默认代码页显示，避免UTF8写入/UTF8读回的自洽假阳性。新向量独立指定“中文”的GBK字节 `D6 D0 CE C4`，按CP936消费者解码；既有完整日历字段测试也改为CP936消费者。HTTP测试分别把emoji放入五个展示字段，观察400、整个配置投影（含revision）不变、成功审计仍只有初次配置一条。这些断言观察输出与持久行为，不限定内部调用结构。

## 实际 RED/GREEN

所有命令从工作区根执行；`DOTNET_PROCESSOR_COUNT=2`，经授权require_escalated串行执行单节点构建测试。

```powershell
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore -m:1 -nr:false --filter Calendar_chinese_notice
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-restore -m:1 -nr:false --filter Unrepresentable_calendar_text
```

以上均在生产修正前退出1：[protocol-red.log](backend-f1-protocol-red.log) Expected `[214,208,206,196]` / Actual UTF8 `[228,184,173,230,150,...]`；[http-red.log](backend-f1-http-red.log) Expected BadRequest / Actual OK。完整原始输出保存，未事后制造RED。

```powershell
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore -m:1 -nr:false --filter 'WeeklyAttendance|DungeonClear|SelectCharacter'
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-restore -m:1 -nr:false --filter 'GmEventsHttpTests|GmDailyRefillHttpTests|GmInitializationHttpTests'
```

修正后协议176/176通过、零跳过、退出0：[protocol-green.log](backend-f1-protocol-green.log)。HTTP首轮19通过/1失败仅为测试DTO内数组引用相等，输出显示400且字段与版本均相同；修正为序列化整个投影的值比较，保留 [http-green-first.log](backend-f1-http-green-first.log)，再运行同一范围。最终HTTP20/20通过、零跳过、退出0：[http-green.log](backend-f1-http-green.log)。`git diff --check`通过。

## 范围

默认936来自ClientStatic / High，尚无运行时CodePage捕获；不推定其他文本协议。协议006C说明补充event9条件前置尾分支未覆盖，当前encoder为2208首例投影。真实DNF GUI、日历点击与奖励窗口仍待人工验收；本记录的pass仅针对F-1自动验证范围。最终包含前端dist的Release构建由根控制器串行执行。
