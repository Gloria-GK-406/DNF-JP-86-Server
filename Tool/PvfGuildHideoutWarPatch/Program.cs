using System.Text;
using Dfo.Pvf.Tokens;
using PvfPackageContentRepair;

if (args is not [var source, var output])
{
    Console.Error.WriteLine("Usage: PvfGuildHideoutWarPatch <source.pvf> <new.pvf>");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
const string Hall = "map/guildagit/guildagit_s.map";
const string Battle = "map/guildagit/guildagit_h0.map";
using var patch = new Engine(source);
var hall = patch.Read(Hall);
var battle = patch.Read(Battle);
var (first, last) = Transit(hall);
var (returnFirst, returnLast) = Transit(battle);
if (Rows(hall, first, last).Any(row => row[4].Value == 8 && row[5].Value == 2)
    || !Rows(battle, returnFirst, returnLast).Any(row => row[4].Value == 8 && row[5].Value == 0))
    throw new InvalidDataException("Expected the hall exit to be absent and the battle area's return route to remain.");

int[] portal = [1136, 131, 140, 60, 8, 2];
hall.InsertRange(last, portal.Select(Sym.Int));
patch.Replace(Hall, hall, "Restore hall-to-war passage at the unused upper arch");
patch.Write(output);
foreach (var line in patch.Log) Console.WriteLine(line);
return 0;

static (int First, int Last) Transit(List<Sym> tokens)
{
    var first = tokens.FindIndex(token => token.IsString && token.Text == "[town movable area]") + 1;
    var last = tokens.FindIndex(first, token => token.IsString && token.Text == "[/town movable area]");
    if (first <= 0 || last <= first || (last - first) % 6 != 0
        || tokens.GetRange(first, last - first).Any(token => token.Kind != PvfTokenKind.Int32))
        throw new InvalidDataException("Invalid guild hall town traversal rows.");
    return (first, last);
}

static IEnumerable<Sym[]> Rows(List<Sym> tokens, int first, int last) =>
    tokens.GetRange(first, last - first).Chunk(6);
