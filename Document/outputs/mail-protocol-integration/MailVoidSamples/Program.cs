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
const string prefix="MailVoid-20260924-";
if((await mail.InboxAsync(new(1))).Any(x=>x.Title.StartsWith(prefix)))throw new Exception("Samples already exist; no changes made.");
var encoder=typeof(PersistenceModule).Assembly.GetType("Dfo.Server.Persistence.Inventory.InventoryItemCore")!.GetMethod("Encode")!;
await using var db=new SqliteConnection("Data Source="+args[0]);await db.OpenAsync();
async Task<ItemIdentity> Mint(int id,ushort durability,byte seal){
 var core=StackableItemCore.Create(1,id,0) with {Durability=durability,SealFlag=seal};
 var bytes=(byte[])encoder.Invoke(null,new object[]{core})!;
 await using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO items(item_core) VALUES($core) RETURNING item_uid";cmd.Parameters.AddWithValue("$core",bytes);
 return new ItemIdentity(Convert.ToInt32(await cmd.ExecuteScalarAsync()));
}
MailBuilder Create(string name,string content)=>new MailBuilder(MailSource.FromSystem(),new(1)).WithText(prefix+name,content).ExpiresAt(DateTimeOffset.UtcNow.AddDays(30));
var samples=new[]{
 Create("01-normal","普通装备：皮护肩 ×1").AddEquipment(await Mint(14412,25,0)),
 Create("02-magic","魔法封印模板：密制圆月弯刀 ×1（封装初始状态）").AddEquipment(await Mint(101010339,45,1)),
 Create("03-epic","不可交易史诗：赫格尼之剑 ×1").AddEquipment(await Mint(101020254,45,0)),
 Create("04-material","单附件材料：3272 ×20").AddStackable(3272,20),
 Create("05-materials","多附件材料：3272 ×30；3303 ×40").AddStackable(3272,30).AddStackable(3303,40),
 Create("06-mixed","多附件：普通装备、魔法封印装备、不可交易史诗、两种材料").AddEquipment(await Mint(14412,25,0)).AddEquipment(await Mint(101010339,45,1)).AddEquipment(await Mint(101020254,45,0)).AddStackable(3272,50).AddStackable(3303,60)
};
foreach(var sample in samples){var receipt=await mail.SendAsync(sample.Build());Console.WriteLine($"Sent mail={receipt.MailId}");}
foreach(var view in (await mail.InboxAsync(new(1))).Where(x=>x.Title.StartsWith(prefix)))Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{view.Id,view.SenderCharacterId,view.SenderName,view.Title,Attachments=view.Attachments.Select(a=>new{a.Id,Asset=a.Asset.ToString()})}));
