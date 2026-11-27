using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using PvfPackageContentRepair;

if (args is not [var source, var upstream, var output])
{
    Console.Error.WriteLine("Usage: PvfGuildDungeonPrerequisitePatch <source.pvf> <upstream.pvf> <new.pvf>");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
const string StylePath = "etc/guild/guilddungeon/0_2way6dungeon.etc";
const string Beginning = "[required dungeon conquest]";
const string Ending = "[/required dungeon conquest]";
using var reference = PvfArchive.Open(upstream);
using var patch = new Engine(source);
var referenceTokens = Engine.Read(reference, StylePath);
var sourceTokens = patch.Read(StylePath);
var unchanged = new List<Sym>();
var prerequisites = 0;
var readingPrerequisite = false;
var blocks = 0;

foreach (var token in referenceTokens)
{
    if (token.IsString && token.Text == Beginning && !readingPrerequisite)
    {
        readingPrerequisite = true;
        prerequisites = 0;
    }
    else if (token.IsString && token.Text == Ending && readingPrerequisite && prerequisites > 0)
    {
        readingPrerequisite = false;
        blocks++;
    }
    else if (!readingPrerequisite)
    {
        unchanged.Add(token);
    }
    else if (token.Kind == PvfTokenKind.Int32 && token.Value > 0)
    {
        prerequisites++;
    }
    else
    {
        throw new InvalidDataException("Unexpected token inside the upstream conquest prerequisite.");
    }
}

if (readingPrerequisite || blocks != 5 || unchanged.Count != sourceTokens.Count
    || !unchanged.Zip(sourceTokens).All(pair => pair.First.SameAs(pair.Second)))
    throw new InvalidDataException("Expected exactly five omitted conquest prerequisites and identical remaining tokens.");

patch.Replace(StylePath, referenceTokens, "Restore five conquest prerequisites from upstream A21");
patch.Write(output);
foreach (var line in patch.Log)
    Console.WriteLine(line);
return 0;
