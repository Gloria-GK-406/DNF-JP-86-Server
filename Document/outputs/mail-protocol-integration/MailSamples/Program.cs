using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
 ["Database:Path"]=args[0], ["Pvf:ArchivePath"]="E:/Project/DNF/DNFClient-A21-protocol/Script.pvf"
}).Build();
var services = new ServiceCollection().AddLogging();
new InfrastructureModule().ConfigureServices(services,config);
new PersistenceModule().ConfigureServices(services,config);
new ApplicationModule().ConfigureServices(services,config);
await using var provider = services.BuildServiceProvider();
var mail = provider.GetRequiredService<IMailboxDomainService>();
if ((await mail.InboxAsync(new(1))).Any(x=>x.Title.StartsWith("MailTest-20260924"))) throw new Exception("Samples already exist; refusing duplicates.");
var transfer=provider.GetRequiredService<IEquipmentTransferDomainService>();
var rules=provider.GetRequiredService<IMailItemRuleService>();
EquipmentTransferItem? chosen=null;
foreach(var uid in new[]{674,676,277,232}) {
 var item=await transfer.ReadOwnedAsync(new(2),new(uid),default);
 if(item is not null && (await rules.CanSendAsync(item.Core,false,default)).Allowed) { chosen=item;break; }
}
if(chosen is null) throw new Exception("No sendable test equipment found.");
Console.WriteLine($"Equipment UID={chosen.Identity.Value} item={chosen.Core.ItemId} sourceSlot={chosen.SlotIndex}");
MailBuilder Create(string n,string body)=>new MailBuilder(MailSource.FromCharacter(new(2)),new(1)).WithText("MailTest-20260924-"+n,body).ExpiresAt(DateTimeOffset.UtcNow.AddDays(30));
var samples=new[]{Create("01-text","1123寄给000的纯文字测试。"),Create("02-gold","金币测试：1000金币。").AddGold(1000),Create("03-material","材料测试：1个材料。").AddStackable(3272,1),Create("04-equipment","装备测试：背包中的可交易装备。").AddEquipment(chosen.Identity),Create("05-mixed","混合测试：100金币和1个材料。").AddGold(100).AddStackable(3303,1)};
foreach(var sample in samples) { var receipt=await mail.SendAsync(sample.Build());Console.WriteLine($"Sent mail={receipt.MailId}, changes={System.Text.Json.JsonSerializer.Serialize(receipt.Changes)}"); }
foreach(var view in await mail.InboxAsync(new(1))) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{view.Id,view.SenderCharacterId,view.SenderName,view.Title,Attachments=view.Attachments.Select(a=>new{a.Id,Type=a.Asset.GetType().Name})}));
