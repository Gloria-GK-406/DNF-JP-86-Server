# 根审查局部收尾修复

root_review/in_progress，原接受baseline a4195c31，根授权及SC-4/重试不得重复既定边界不变。独立完整根审查F-001已确认：原未知邮件请求安全重试401/403错误地清除sessionStorage操作编号。它是可局部修正的产品缺陷，根审查尚未通过且当前Spec边界/路线不失效，保持根rework，不apply terminal review-not-approved或重开任何sealed child。

最小修正由主控执行：仅OfficialMailView.tsx身份/权限失败优先保留冻结请求，并新增OfficialMail.test.tsx行为回归，观察未知→失权重试→隐藏私有界面→恢复身份同UUID安全重试和成功后释放。新产品diff留待原独立reviewer重审、fresh前端/工具/构建/候选验证后，调用workspace controller接受完整根rework；一次本地提交、父a4195c31，原六接受提交和sealed结果原样保留。成功接受后推进workspace accepted baseline，才关闭根review gate并执行accepted-baseline root verification。

不新增业务、不操作真实资产/管理员/现服/远程，不增加独立模块或独立Spec边界；当前rework及正常根/child游标为root声明durable证据。C#未改，完整5373/2skip在同一当前服务端代码通过；新增UI变化须完整前端回归和新GM资源实际publish/HTTP验证，不能只沿用旧54/旧资产。
