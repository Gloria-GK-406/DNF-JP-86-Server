using System.Security.Cryptography;
using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

// 婚房 / 情侣树 / 成长胶囊 / 情侣仓库 只读 PVF 调查扫描器。
// 用法（PVF 路径为第一个参数）：
//   PvfHousingScanner <pvf> lst <listPath>                       -> id \t path （TSV）
//   PvfHousingScanner <pvf> items <listPath> <id|lo-hi>...        -> 按 .lst 解析 id 后完整 dump
//   PvfHousingScanner <pvf> dump <path>...                        -> 完整 dump
//   PvfHousingScanner <pvf> scan                                  -> 全表数值 + 关键词扫描（TSV）
//   PvfHousingScanner <pvf> hash <path>...                        -> 规范化 dump 的 SHA-256
//   PvfHousingScanner <pvf> files <substring>...                  -> 文件表中路径包含子串的行

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PvfHousingScanner <Script.pvf> <lst|items|dump|scan|hash|files> ...");
    return 2;
}

using var archive = PvfArchive.Open(args[0]);
var command = args[1];
var rest = args.Skip(2).ToArray();

switch (command)
{
    case "lst":
        foreach (var (id, path) in ReadList(archive, rest[0]))
            Console.WriteLine($"{id}\t{path}");
        return 0;

    case "items":
    {
        var list = ReadList(archive, rest[0]).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().Path);
        var folder = FolderOf(rest[0]);
        foreach (var spec in rest.Skip(1))
        {
            foreach (var id in ExpandIds(spec))
            {
                if (!list.TryGetValue(id, out var rel))
                {
                    Console.WriteLine($"######## id {id}: not listed in {rest[0]}");
                    continue;
                }

                Console.WriteLine($"######## id {id} -> {folder}{rel}");
                Console.WriteLine(DumpText(archive, folder + rel));
            }
        }

        return 0;
    }

    case "dump":
        foreach (var path in rest)
        {
            Console.WriteLine($"######## {path}");
            Console.WriteLine(DumpText(archive, path));
        }

        return 0;

    case "hash":
        foreach (var path in rest)
        {
            var text = DumpText(archive, path);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            Console.WriteLine($"{path}\t{hash}\t{text.Length}");
        }

        return 0;

    case "files":
        for (var i = 0; i < archive.FileCount; i++)
        {
            var p = PathOf(archive, i);
            if (rest.Any(n => p.Contains(n, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"{archive.GetEntry(i).DataSize}\t{p}");
        }

        return 0;

    case "text":
        // UTF-16 文本文件（.xui/.lua 等）原文输出。
        foreach (var path in rest)
        {
            var idx = archive.FindFile(path);
            Console.WriteLine($"######## {path}");
            Console.WriteLine(idx < 0 ? "!! not found" : archive.ReadUnicodeText(idx));
        }

        return 0;

    case "tagwhere":
        // 列出使用指定 tag（精确匹配标识符）的所有脚本文件及上下文。
        for (var fi = 0; fi < archive.FileCount; fi++)
        {
            var en = archive.GetEntry(fi);
            if (!en.IsScript)
                continue;
            var p = PathOf(archive, fi);
            if (p.EndsWith(".ani") || p.EndsWith(".act") || p.EndsWith(".als"))
                continue;
            var toks = new List<(PvfToken Token, string Text)>();
            try
            {
                var rd = archive.OpenScript(fi);
                while (rd.Read(out var t))
                    toks.Add((t, TokenText(archive, t)));
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < toks.Count; i++)
                if ((toks[i].Token.Kind == PvfTokenKind.Identifier || toks[i].Token.IsString) && rest.Contains(toks[i].Text))
                    Console.WriteLine($"{p}\t{toks[i].Text}\t{Context(toks, i)}");
        }

        return 0;

    case "numwhere":
    {
        // 全表搜索 Int32 token 精确等于给定值的脚本（跳过 .ani/.act/.als）。
        var wanted = rest.Select(int.Parse).ToHashSet();
        for (var fi = 0; fi < archive.FileCount; fi++)
        {
            var en = archive.GetEntry(fi);
            if (!en.IsScript)
                continue;
            var p = PathOf(archive, fi);
            if (p.EndsWith(".ani") || p.EndsWith(".act") || p.EndsWith(".als"))
                continue;
            var toks = new List<(PvfToken Token, string Text)>();
            try
            {
                var rd = archive.OpenScript(fi);
                while (rd.Read(out var t))
                    toks.Add((t, TokenText(archive, t)));
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < toks.Count; i++)
                if (toks[i].Token.Kind == PvfTokenKind.Int32 && wanted.Contains(toks[i].Token.Value))
                    Console.WriteLine($"{p}\t{toks[i].Token.Value}\t{Context(toks, i)}");
        }

        return 0;
    }

    case "scan":
        Scan(archive);
        return 0;

    default:
        Console.Error.WriteLine($"unknown command {command}");
        return 2;
}

static void Scan(PvfArchive archive)
{
    // 婚房/树/胶囊/仓库相关的已知 ID 与 tag 关键词。
    var numericTargets = new HashSet<int>
    {
        101, 20011, 20012, 10002765, 10002766, 10002767, 10002768, 10002905, 10002910, 10002903,
        10002758, 10002756, 10002869, 10002887, 10002911, 2673000, 2673001, 2673002, 2673003,
        2673004, 2673005, 2673006, 99999000, 1078, 1192,
    };
    var textNeedles = new[]
    {
        "couple", "coupleroom", "decorat", "grow tree", "growtree", "couple tree", "watering", "harvest",
        "seed", "capsule", "growth capsule", "couple storage", "couple inven", "wedding ring", "ring level",
        "婚房", "情侣", "夫妻", "花圃", "浇水", "种子", "收获", "胶囊", "仓库", "装饰", "家具", "壁纸", "地板",
        "心意", "爱心", "戒指", "枫树", "宝石树", "成长",
    };

    Console.WriteLine("section\tpath\tposition\tkind\tmatch\tcontext");
    var hits = 0;
    for (var fileIndex = 0; fileIndex < archive.FileCount; fileIndex++)
    {
        var entry = archive.GetEntry(fileIndex);
        var path = PathOf(archive, fileIndex);

        // 动画/行为/图像引用脚本数量巨大且与规则无关；仅对规则性扩展名做全文扫描。
        if (path.EndsWith(".ani", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".act", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".als", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".atk", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ptl", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
            continue;

        if (entry.IsUnicodeText)
        {
            string text;
            try { text = archive.ReadUnicodeText(fileIndex); }
            catch { continue; }
            foreach (var needle in textNeedles)
            {
                var start = 0;
                var perNeedle = 0;
                while ((start = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase)) >= 0 && perNeedle < 50)
                {
                    var cs = Math.Max(0, start - 60);
                    var cl = Math.Min(text.Length - cs, needle.Length + 120);
                    Console.WriteLine($"unicode\t{Esc(path)}\t{start}\ttext\t{Esc(needle)}\t{Esc(text.Substring(cs, cl))}");
                    hits++;
                    perNeedle++;
                    start += Math.Max(1, needle.Length);
                }
            }

            continue;
        }

        if (!entry.IsScript)
            continue;

        var tokens = new List<(PvfToken Token, string Text)>();
        try
        {
            var reader = archive.OpenScript(fileIndex);
            while (reader.Read(out var token))
                tokens.Add((token, TokenText(archive, token)));
        }
        catch
        {
            continue;
        }

        // 列表文件里的数字 ID 极多，命中 101 这类小数字噪声过大：小数字只在非 .lst 的规则文件里记。
        var isList = path.EndsWith(".lst", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < tokens.Count; i++)
        {
            var current = tokens[i];
            if (current.Token.Kind == PvfTokenKind.Int32 && numericTargets.Contains(current.Token.Value))
            {
                var small = current.Token.Value < 100000;
                if (!small || (!isList && (path.StartsWith("etc/") || path.StartsWith("decoration/") || path.Contains(".npc") || path.Contains(".twn"))))
                {
                    Console.WriteLine($"number\t{Esc(path)}\t{i + 1}\t{current.Token.Kind}\t{current.Token.Value}\t{Context(tokens, i)}");
                    hits++;
                }
            }

            if (!(current.Token.Kind == PvfTokenKind.Identifier || current.Token.IsString))
                continue;
            foreach (var needle in textNeedles)
            {
                if (!current.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    continue;
                Console.WriteLine($"token-text\t{Esc(path)}\t{i + 1}\t{current.Token.Kind}\t{Esc(needle)}\t{Context(tokens, i)}");
                hits++;
                break;
            }
        }
    }

    Console.Error.WriteLine($"hits={hits}; files={archive.FileCount}");
}

static IEnumerable<int> ExpandIds(string spec)
{
    var dash = spec.IndexOf('-');
    if (dash <= 0)
    {
        yield return int.Parse(spec);
        yield break;
    }

    var lo = int.Parse(spec[..dash]);
    var hi = int.Parse(spec[(dash + 1)..]);
    for (var i = lo; i <= hi; i++)
        yield return i;
}

static string PathOf(PvfArchive archive, int index)
{
    var entry = archive.GetEntry(index);
    var folder = archive.ResolveString(entry.PathOffset);
    var name = archive.ResolveString(entry.NameOffset);
    return folder.Length == 0 ? name : folder + "/" + name;
}

static string FolderOf(string listPath) => listPath[..(listPath.LastIndexOf('/') + 1)];

static string TokenText(PvfArchive archive, PvfToken token) =>
    token.Kind == PvfTokenKind.Identifier || token.IsString
        ? archive.ResolveString(token.Value)
        : token.Kind == PvfTokenKind.Single
            ? BitConverter.Int32BitsToSingle(token.Value).ToString("0.######")
            : token.Value.ToString();

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

static string DumpText(PvfArchive archive, string path)
{
    var index = archive.FindFile(path);
    if (index < 0)
        return $"!! not found: {path}";

    var sb = new StringBuilder();
    sb.Append("=== ").Append(path).AppendLine(" ===");
    var reader = archive.OpenScript(index);
    var line = new StringBuilder();
    while (reader.Read(out var token))
    {
        var text = TokenText(archive, token);
        if (token.IsString && token.Kind != PvfTokenKind.Identifier)
            text = "`" + text + "`";
        var isTag = token.Kind == PvfTokenKind.Identifier && text.StartsWith('[');
        if (isTag && line.Length > 0)
        {
            sb.AppendLine(line.ToString().TrimEnd());
            line.Clear();
        }

        line.Append(text).Append(' ');
        if (isTag)
        {
            sb.AppendLine(line.ToString().TrimEnd());
            line.Clear();
        }
    }

    if (line.Length > 0)
        sb.AppendLine(line.ToString().TrimEnd());
    return sb.ToString();
}

static string Context(List<(PvfToken Token, string Text)> tokens, int index)
{
    var start = Math.Max(0, index - 6);
    var end = Math.Min(tokens.Count - 1, index + 6);
    return Esc(string.Join(" ", tokens.GetRange(start, end - start + 1).Select(v => v.Text)));
}

static string Esc(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
