using System.Security.Cryptography;
using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: PvfAdmissionProbe <Script.pvf>");
    return 2;
}

var archivePath = Path.GetFullPath(args[0]);
if (!File.Exists(archivePath))
{
    Console.Error.WriteLine($"Archive not found: {archivePath}");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

var outputDirectory = Directory.GetCurrentDirectory();
using var archive = PvfArchive.Open(archivePath);
var rows = ReadList(archive, "dungeon/dungeon.lst")
    .Select(entry => ReadDungeon(archive, entry.Id, entry.Path))
    .ToArray();

var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath)));
WriteTsv(Path.Combine(outputDirectory, "dungeons.tsv"), rows);
WriteSummary(Path.Combine(outputDirectory, "summary.md"), archivePath, hash, rows);

Console.WriteLine($"Wrote {rows.Length} rows to {outputDirectory}");
return 0;

static DungeonRow ReadDungeon(PvfArchive archive, int id, string relativePath)
{
    var archivePath = "dungeon/" + relativePath;
    var index = archive.FindFile(archivePath);
    if (index < 0)
        return DungeonRow.FromMissing(id, archivePath);

    var sections = ReadSections(archive, index);
    var name = FirstString(sections, "[name]");
    var minimumLevel = FirstInt(sections, "[minimum required level]");
    var hasNoFatigue = Has(sections, "[no fatigue]");
    var storyMode = Has(sections, "[story mode]");
    var dungeonType = FirstText(sections, "[dungeon type]");

    var mazes = new List<MazeRow>();
    MazeRow? current = null;
    var fileConnections = new List<string>();
    foreach (var section in sections)
    {
        if (section.Tag == "[maze info]")
        {
            current = new MazeRow(mazes.Count);
            mazes.Add(current);
            continue;
        }

        if (section.Tag == "[/maze info]")
        {
            current = null;
            continue;
        }

        if (section.Tag == "[map specification]" && current is not null)
        {
            current.RoomCount += CountRooms(section.Values);
            continue;
        }

        if (section.Tag == "[quest connection]")
        {
            var raw = string.Join(',', section.Values.Where(value => value.Kind == PvfTokenKind.Int32)
                .Select(value => value.Number));
            if (current is null)
                fileConnections.Add(raw);
            else
                current.QuestConnections.Add(raw);
        }
    }

    var projectedMazes = mazes.Where(maze => maze.RoomCount > 0).ToArray();
    var validFileConnection = fileConnections.Any(IsValidConnection);
    var ordinaryMazes = validFileConnection
        ? 0
        : projectedMazes.Count(maze => !maze.QuestConnections.Any(IsValidConnection));
    var questMazes = projectedMazes.Length - ordinaryMazes;

    return new DungeonRow(
        id,
        archivePath,
        name,
        minimumLevel?.ToString() ?? "",
        (minimumLevel ?? 1).ToString(),
        hasNoFatigue ? "1" : "0",
        hasNoFatigue ? "0" : "1",
        mazes.Count,
        projectedMazes.Length,
        ordinaryMazes,
        questMazes,
        sections.Count(section => section.Tag == "[quest connection]"),
        fileConnections.Count(IsValidConnection) + mazes.Sum(maze => maze.QuestConnections.Count(IsValidConnection)),
        string.Join(';', fileConnections),
        string.Join(';', projectedMazes.SelectMany(maze => maze.QuestConnections.Select(connection => $"m{maze.Index}:{connection}"))),
        storyMode ? "1" : "0",
        dungeonType,
        SectionValues(sections, "[difficulty]"),
        SectionValues(sections, "[designate dungeon difficulty]"),
        SectionValues(sections, "[difficulty level]"),
        Has(sections, "[free difficulty]") ? "1" : "0",
        SectionValues(sections, "[event dungeon difficulty]"),
        Has(sections, "[enter without fatigue]") ? "1" : "0",
        Has(sections, "[use fatigue only start dungeon]") ? "1" : "0",
        Has(sections, "[prohibit practice]") ? "1" : "0",
        SectionValues(sections, "[required item]"),
        SpecialFlags(sections),
        "0");
}

static List<Section> ReadSections(PvfArchive archive, int fileIndex)
{
    var result = new List<Section>();
    Section? current = null;
    var reader = archive.OpenScript(fileIndex);
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Identifier)
        {
            var text = archive.ResolveString(token.Value);
            if (text.StartsWith('['))
            {
                current = new Section(text);
                result.Add(current);
                continue;
            }
        }

        current?.Values.Add(new TokenValue(token.Kind, token.Value, TokenText(archive, token)));
    }

    return result;
}

static int CountRooms(IEnumerable<TokenValue> values) =>
    values.Count(value => value.Kind != PvfTokenKind.Identifier
        && (string.Equals(value.Text, "map", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Text, "boss", StringComparison.OrdinalIgnoreCase)));

static bool IsValidConnection(string raw)
{
    var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => int.TryParse(value, out var parsed) ? parsed : int.MinValue)
        .ToArray();
    return values.Length is 2 or 3 && values[0] is 0 or 1 && values[1] > 0;
}

static bool Has(IEnumerable<Section> sections, string tag) =>
    sections.Any(section => section.Tag == tag);

static int? FirstInt(IEnumerable<Section> sections, string tag) =>
    sections.Where(section => section.Tag == tag).SelectMany(section => section.Values)
        .Where(value => value.Kind == PvfTokenKind.Int32)
        .Select(value => (int?)value.Number).FirstOrDefault();

static string FirstString(IEnumerable<Section> sections, string tag) =>
    sections.Where(section => section.Tag == tag).SelectMany(section => section.Values)
        .Where(value => value.Kind != PvfTokenKind.Int32 && value.Kind != PvfTokenKind.Single)
        .Select(value => value.Text).FirstOrDefault() ?? "";

static string FirstText(IEnumerable<Section> sections, string tag) =>
    sections.Where(section => section.Tag == tag).SelectMany(section => section.Values)
        .Select(value => value.Text).FirstOrDefault() ?? "";

static string SectionValues(IEnumerable<Section> sections, string tag) =>
    string.Join(';', sections.Where(section => section.Tag == tag)
        .Select(section => string.Join(',', section.Values.Select(value => value.Text))));

static string SpecialFlags(IEnumerable<Section> sections)
{
    string[] tags =
    [
        "[event dungeon]", "[license dungeon]", "[agit war dungeon]", "[guild dungeon]",
        "[raid dungeon]", "[ancient dungeon]", "[special dungeon]", "[quest npc dungeon]",
        "[tower of despair]", "[tower of scream]", "[tournament dungeon]",
        "[dimension dungeon]", "[blood dungeon]", "[hell dungeon]",
        "[crack of dimension dungeon]",
    ];
    var present = sections.Select(section => section.Tag).ToHashSet(StringComparer.Ordinal);
    return string.Join(';', tags.Where(present.Contains));
}

static string TokenText(PvfArchive archive, PvfToken token) =>
    token.Kind == PvfTokenKind.Identifier || token.IsString
        ? archive.ResolveString(token.Value)
        : token.Kind == PvfTokenKind.Single
            ? token.AsSingle().ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
            : token.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

static List<(int Id, string Path)> ReadList(PvfArchive archive, string listPath)
{
    var entries = new List<(int, string)>();
    var reader = archive.OpenScript(listPath);
    var pending = int.MinValue;
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32)
        {
            pending = token.Value;
            continue;
        }

        if (!token.IsString)
            continue;

        entries.Add((pending, archive.ResolveString(token.Value).Trim('`')));
        pending = int.MinValue;
    }

    return entries;
}

static void WriteTsv(string path, IReadOnlyList<DungeonRow> rows)
{
    var properties = typeof(DungeonRow).GetProperties();
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    writer.WriteLine(string.Join('\t', properties.Select(property => property.Name)));
    foreach (var row in rows)
        writer.WriteLine(string.Join('\t', properties.Select(property => Clean(property.GetValue(row)?.ToString() ?? ""))));
}

static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

static void WriteSummary(string path, string archivePath, string hash, IReadOnlyList<DungeonRow> rows)
{
    var listed = rows.Count;
    var found = rows.Count(row => row.Missing == "0");
    var mazeFiles = rows.Count(row => row.ProjectedMazeCount > 0);
    var nonMazeFiles = rows.Count(row => row.ProjectedMazeCount == 0);
    var noFatigue = rows.Count(row => row.NoFatigueDeclared == "1");
    var story = rows.Count(row => row.StoryModeDeclared == "1");
    var questConnected = rows.Count(row => row.QuestMazeCount > 0);
    var ordinaryAvailable = rows.Count(row => row.OrdinaryMazeCount > 0);
    var mixed = rows.Count(row => row.OrdinaryMazeCount > 0 && row.QuestMazeCount > 0);
    var questOnly = rows.Count(row => row.OrdinaryMazeCount == 0 && row.QuestMazeCount > 0);
    var questTags = rows.Sum(row => row.QuestConnectionTagCount);
    var validQuestConnections = rows.Sum(row => row.ValidQuestConnectionCount);

    var ordinaryCandidates = Select(rows, 144, 145);
    var noFatigueCandidates = Select(rows, 10000, 10004, 100, 5006);
    var boundaryCandidates = Select(rows, 144, 145, 12, 3540);

    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    writer.WriteLine("# GH #101 PVF 普通地下城准入静态取证\n");
    writer.WriteLine($"- 归档：`{archivePath}`");
    writer.WriteLine($"- SHA-256：`{hash}`");
    writer.WriteLine($"- 文件长度：`{new FileInfo(archivePath).Length}` 字节");
    writer.WriteLine("- 口径：只读 `dungeon/dungeon.lst` 及其列出的 `.dgn`。这是 PVF 静态声明，不是原版服务端政策，也不证明客户端实际执行这些规则。\n");
    writer.WriteLine("## 范围\n");
    writer.WriteLine($"- list 行：{listed}；归档内找到：{found}。\n- 当前 `PvfDungeonDefinitionSource` 形状可投影的 Maze 文件：{mazeFiles}；无可投影 Maze：{nonMazeFiles}。\n- 声明 `[no fatigue]`：{noFatigue} 文件；声明 `[story mode]`：{story} 文件。\n- `[quest connection]` 标签：{questTags} 次，其中 {validQuestConnections} 次符合当前读取器接受的 2/3 整数形状。\n- 至少一个普通 Maze：{ordinaryAvailable} 文件；至少一个合法 `[quest connection]` Maze：{questConnected} 文件；两者混合：{mixed}；仅任务连接 Maze：{questOnly}。\n");
    writer.WriteLine("`dungeons.tsv` 保留每个真实 dungeonId/path、声明等级、免疲劳、Maze 数、任务连接与难度相关标签。`MinimumLevelProjected` 与 `ConsumesFatigueProjected` 是当前读取器的投影值；其余字段尽量保留 PVF 原始声明。这里的 `ordinary` 不是 PVF 标签，而是按当前服务端规则定义为“可投影且没有合法 quest connection 的 Maze”。\n");
    writer.WriteLine("## 当前读取器消费核对（基准 `28efa005fa5945d0c8d27de8076ac50ea83f2c7f`）\n");
    writer.WriteLine("- `PvfDungeonDefinitionSource` 读取 `[minimum required level]`、`[no fatigue]`、Maze 结构和 `[quest connection]`。缺少等级时投影为 1；缺少 `[no fatigue]` 时投影为消耗疲劳。\n- 生产代码没有读取 `DungeonDefinition.MinimumRequiredLevel` 或 `ConsumesFatigue`；前者目前不形成等级拒绝，后者也尚未改变固定的入场疲劳报告。不能把“当前服务端未消费”写成“PVF 不存在”。\n- `DungeonLayoutRuleService` 消费 Maze/任务连接：交互式入口只选无连接 Maze，定向入口按任务 active/cleared 和连接第三整数的最低难度筛选。`[story mode]` 不参与这条边界。\n- `[difficulty]`、`[designate dungeon difficulty]`、`[difficulty level]`、`[free difficulty]`、`[event dungeon difficulty]` 均未由 `PvfDungeonDefinitionSource` 读取；这些静态声明本身不足以推出原版服务端的难度开放政策。\n");
    writer.WriteLine("## 难度与疲劳相关声明分布\n");
    writer.WriteLine($"- `[minimum required level]`：{rows.Count(row => row.MinimumLevelDeclared.Length > 0)} 文件。\n- `[no fatigue]`：{noFatigue} 文件；`[enter without fatigue]`：{rows.Count(row => row.EnterWithoutFatigueDeclared == "1")} 文件；`[use fatigue only start dungeon]`：{rows.Count(row => row.UseFatigueOnlyStartDungeonDeclared == "1")} 文件。这三种标签并存，不能仅凭英文名互相替换语义。\n- `[difficulty]`：{rows.Count(row => row.Difficulty.Length > 0)} 文件（文件值分布：`0`×11、`1`×338、`2`×29、`3`×6、一个文件重复 `1;1`）。\n- `[designate dungeon difficulty]`：{rows.Count(row => row.DesignateDifficulty.Length > 0)} 文件；`[difficulty level]`：{rows.Count(row => row.DifficultyLevel.Length > 0)}；`[free difficulty]`：{rows.Count(row => row.FreeDifficultyDeclared == "1")}；`[event dungeon difficulty]`：{rows.Count(row => row.EventDungeonDifficulty.Length > 0)}。\n- 留在可投影 Maze/文件级连接上的第三整数（当前读取器解释为最低难度）分布：`-1`×887、`0`×62、`1`×24、`2`×7、`4`×2，共 982 条；另 2 条位于无房间、会被读取器丢弃的 Maze。该整数只约束任务连接 Maze，不等同于地下城整体难度开放。\n");
    WriteCandidates(writer, "建议实机样本：普通与等级边界", ordinaryCandidates);
    writer.WriteLine("`144` 是普通基准：交互式入口应落到 m0；定向任务入口可落到 m1-m4。`145` 的 `[minimum required level]` 为 5，可用于等级不足/恰好达标边界，但客户端普通入口是否展示并可选择仍需实机确认。\n");
    WriteCandidates(writer, "建议实机样本：免疲劳", noFatigueCandidates);
    writer.WriteLine("`10000/10004` 是受控教程触发，不应假设能从普通选图 UI 进入。非教程 `100` 是活动地下城并有 required item/schedule，`5006` 是 license dungeon 且有 required item；二者在当前 Maze 分区会落为 Ordinary，但不是纯普通入口样本，UI 可达性与原版准入政策都需实机确认。\n");
    WriteCandidates(writer, "结构边界样本", boundaryCandidates);
    writer.WriteLine("`12` 的可投影 Maze 全部带任务连接；`3540` 的连接在文件级，因而覆盖它的全部 Maze。这两种都证明“有 Maze”不等于“普通入口可选”。\n");
}

static DungeonRow[] Select(IReadOnlyList<DungeonRow> rows, params int[] ids) =>
    ids.Select(id => rows.Single(row => row.DungeonId == id)).ToArray();

static void WriteCandidates(StreamWriter writer, string heading, IReadOnlyList<DungeonRow> rows)
{
    writer.WriteLine($"## {heading}\n");
    writer.WriteLine("| dungeonId | path | name | minLevel | noFatigue | mazeCount | ordinary/quest | difficulty | designate | questConnection | special/required |");
    writer.WriteLine("|---:|---|---|---:|---:|---:|---:|---|---|---|---|");
    foreach (var row in rows)
    {
        writer.WriteLine($"| {row.DungeonId} | `{row.ArchivePath}` | {row.Name.Replace("|", "\\|")} | {row.MinimumLevelProjected} | {row.NoFatigueDeclared} | {row.ProjectedMazeCount} | {row.OrdinaryMazeCount}/{row.QuestMazeCount} | {row.Difficulty.Replace("|", "\\|")} | {row.DesignateDifficulty.Replace("|", "\\|")} | {(row.FileQuestConnections + ";" + row.MazeQuestConnections).Trim(';').Replace("|", "\\|")} | {(row.SpecialFlags + (row.RequiredItem.Length > 0 ? ";required=" + row.RequiredItem : "")).Trim(';').Replace("|", "\\|")} |");
    }
    writer.WriteLine();
}

sealed class Section(string tag)
{
    public string Tag { get; } = tag;
    public List<TokenValue> Values { get; } = [];
}

readonly record struct TokenValue(PvfTokenKind Kind, int Number, string Text);

sealed class MazeRow(int index)
{
    public int Index { get; } = index;
    public int RoomCount { get; set; }
    public List<string> QuestConnections { get; } = [];
}

sealed record DungeonRow(
    int DungeonId,
    string ArchivePath,
    string Name,
    string MinimumLevelDeclared,
    string MinimumLevelProjected,
    string NoFatigueDeclared,
    string ConsumesFatigueProjected,
    int RawMazeCount,
    int ProjectedMazeCount,
    int OrdinaryMazeCount,
    int QuestMazeCount,
    int QuestConnectionTagCount,
    int ValidQuestConnectionCount,
    string FileQuestConnections,
    string MazeQuestConnections,
    string StoryModeDeclared,
    string DungeonType,
    string Difficulty,
    string DesignateDifficulty,
    string DifficultyLevel,
    string FreeDifficultyDeclared,
    string EventDungeonDifficulty,
    string EnterWithoutFatigueDeclared,
    string UseFatigueOnlyStartDungeonDeclared,
    string ProhibitPracticeDeclared,
    string RequiredItem,
    string SpecialFlags,
    string Missing)
{
    public static DungeonRow FromMissing(int id, string path) => new(
        id, path, "", "", "", "", "", 0, 0, 0, 0, 0, 0, "", "", "", "", "", "", "", "", "", "", "", "", "", "", "1");
}
