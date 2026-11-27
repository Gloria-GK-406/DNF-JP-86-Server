using System.Text;
using Dfo.Pvf.Tokens;
using PvfPackageContentRepair;

if (args is not [var source, var output])
{
    Console.Error.WriteLine("Usage: PvfGuildMedalShopPatch <source.pvf> <new.pvf>");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
const string npcPath = "npc/guild_reghin.npc";
const string stockTag = "[guild secret shop list]";
const string stockEndTag = "[/guild secret shop list]";
using var patch = new Engine(source);
var npc = patch.Read(npcPath).ToList();
if (npc.Any(token => token.IsString && token.Text is stockTag or stockEndTag))
    throw new InvalidDataException("The Reghin NPC already declares a rotating shop list.");

var role = npc.FindIndex(token => token.IsString && token.Text == "[/role]");
if (role < 2 || !npc[role - 2].IsString || npc[role - 2].Text != "[item shop]"
    || npc[role - 1].Kind != PvfTokenKind.Int32 || npc[role - 1].Value != 150)
    throw new InvalidDataException("Expected the Reghin NPC to have its original shop 150 role.");

for (var shop = 150; shop <= 159; shop++)
{
    var catalog = patch.Read($"itemshop/guild_reghin{shop - 149}.shp");
    if (catalog.Count < 2 || !catalog[0].IsString || catalog[0].Text != "[NPC]"
        || catalog[1].Kind != PvfTokenKind.Int32 || catalog[1].Value != 383)
        throw new InvalidDataException($"Stock {shop} is not declared for the Reghin NPC.");
}

// A21's integer reader consumes the first non-integer token, which terminates the list.
npc.InsertRange(role + 1, new[] { patch.Tag(stockTag) }
    .Concat(Enumerable.Range(150, 10).Select(Sym.Int))
    .Append(patch.Tag(stockEndTag)));
patch.Replace(npcPath, npc, "Enable A21 native guild secret shop lists for Reghin");
patch.Write(output);
foreach (var line in patch.Log)
    Console.WriteLine(line);
return 0;
