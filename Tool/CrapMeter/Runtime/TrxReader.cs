using System.Globalization;
using System.Xml.Linq;

namespace CrapMeter.Runtime;

public static class TrxReader
{
    public static List<string> ExpectedProjects(string solutionPath)
    {
        string directory = Path.GetDirectoryName(solutionPath)!;
        List<string> expected = [];
        foreach (XElement project in XDocument.Load(solutionPath).Descendants("Project"))
        {
            string path = Paths.Normalize(project.Attribute("Path")?.Value ?? "");
            if (!path.StartsWith("tests/", StringComparison.Ordinal)) continue;
            XDocument definition = XDocument.Load(Path.Combine(directory, path));
            string name = definition.Descendants("AssemblyName").LastOrDefault()?.Value
                ?? Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name) || name.Contains('$') || expected.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("测试程序集身份无法唯一确定：" + path);
            expected.Add(name);
        }
        if (expected.Count == 0) throw new InvalidDataException("slnx 没有预期测试项目。");
        return expected;
    }

    public static void ValidateExpected(IReadOnlyList<Manifest.TestOutcome> outcomes, IReadOnlyList<string> expected)
    {
        if (expected.Count == 0 || outcomes.Count != expected.Count
            || outcomes.Any(o => o.Total <= 0)
            || !outcomes.Select(o => o.TestProject).Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(expected.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("TRX 测试项目身份/数量与 slnx 不符，或测试结果为空。");
    }

    public static List<Manifest.TestOutcome> ReadAll(string resultsDirectory)
    {
        List<Manifest.TestOutcome> outcomes = [];
        if (!Directory.Exists(resultsDirectory)) return outcomes;
        foreach (string path in Directory.EnumerateFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            XDocument document = XDocument.Load(path);
            XElement root = document.Root ?? throw new InvalidDataException("TRX 没有根节点：" + path);
            XNamespace ns = root.Name.Namespace;
            XElement counters = root.Element(ns + "ResultSummary")?.Element(ns + "Counters")
                ?? throw new InvalidDataException("TRX 没有计数：" + path);
            XElement[] results = root.Element(ns + "Results")?.Elements(ns + "UnitTestResult").ToArray() ?? [];
            XElement[] definitions = root.Element(ns + "TestDefinitions")?.Elements(ns + "UnitTest").ToArray() ?? [];
            string[] assemblies = definitions.Select(d => d.Element(ns + "TestMethod")?.Attribute("codeBase")?.Value
                    ?? d.Attribute("storage")?.Value ?? "")
                .Select(p => Path.GetFileNameWithoutExtension(Paths.Normalize(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            int total = Attribute(counters, "total");
            int passed = Attribute(counters, "passed");
            int failed = Attribute(counters, "failed");
            int executed = Attribute(counters, "executed");
            int skipped = results.Count(r => (string?)r.Attribute("outcome") == "NotExecuted");
            var ids = definitions.Select(d => (string?)d.Attribute("id")).Where(id => id is not null).ToHashSet();
            if (total <= 0 || results.Length != total || assemblies.Length != 1 || string.IsNullOrWhiteSpace(assemblies[0])
                || results.Any(r => r.Attribute("testId") is not { } id || !ids.Contains(id.Value))
                || passed != results.Count(r => (string?)r.Attribute("outcome") == "Passed")
                || failed != results.Count(r => (string?)r.Attribute("outcome") == "Failed")
                || executed != total - skipped || passed + failed + skipped != total)
                throw new InvalidDataException("TRX 身份、非空结果与计数无法对账：" + path);

            outcomes.Add(new Manifest.TestOutcome(assemblies[0], total, passed, failed, skipped,
                Paths.Relative(resultsDirectory, path)));
        }
        return outcomes;
    }

    private static int Attribute(XElement element, string name) =>
        element.Attribute(name) is { } attribute
        && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value : throw new InvalidDataException("TRX 计数无效：" + name);
}
