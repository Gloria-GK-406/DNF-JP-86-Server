using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Database:Path"]=args[0], ["Pvf:ArchivePath"]="E:/Project/DNF/DNFClient-A21-protocol/Script.pvf" }).Build();
var services=new ServiceCollection().AddLogging();
new InfrastructureModule().ConfigureServices(services,config);new PersistenceModule().ConfigureServices(services,config);new ApplicationModule().ConfigureServices(services,config);
await using var provider=services.BuildServiceProvider();var mail=provider.GetRequiredService<IMailboxDomainService>();

const string title="系统奖励测试-20260924";
if ((await mail.InboxAsync(new(1))).Any(x=>x.Title==title)) throw new Exception("System sample already exists.");
var sample=MailBuilder.FromSystem(new(1))
    .WithText(title,"这是一封系统奖励邮件，用于核对玩家邮件标志。附件：1000金币和3个材料。")
    .ExpiresAt(DateTimeOffset.UtcNow.AddDays(30)).AddGold(1000).AddStackable(3272,3).Build();
var receipt=await mail.SendAsync(sample);
var view=(await mail.InboxAsync(new(1))).Single(x=>x.Id==receipt.MailId);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{view.Id,view.SenderCharacterId,view.SenderName,view.Title,Count=view.Attachments.Length,Debits=receipt.Changes.Length}));
