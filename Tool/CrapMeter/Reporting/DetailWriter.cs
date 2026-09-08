using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CrapMeter.Analysis;

namespace CrapMeter.Reporting;

/// <summary>
/// 函数明细的机器可读落盘。未舍入的输入与结果全部保留——报告里的每个数字都要能回到这里，
/// 舍入只发生在展示层。
/// </summary>
public static class DetailWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteJson(string path, ImmutableArray<FunctionRecord> records)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("schema", "crapmeter-function-details/2");
        writer.WriteNumber("count", records.Length);
        writer.WriteStartArray("functions");

        foreach (FunctionRecord record in records)
        {
            writer.WriteStartObject();
            writer.WriteString("project", record.Member.Project);
            writer.WriteString("id", record.Member.Id);
            writer.WriteString("mappingRule", "module + source-range; overlapping accessors resolved by IL accessor name, otherwise ambiguous");
            writer.WriteString("complexityEvidence", "raw/source-inventory.json");
            writer.WriteStartArray("coverageReports");
            foreach (string report in record.CoverageReports)
                writer.WriteStringValue(Paths.Normalize(Path.GetRelativePath(Path.GetDirectoryName(path)!, report)));
            writer.WriteEndArray();
            writer.WritePropertyName("coveragePoints");
            JsonSerializer.Serialize(writer, record.CoveragePoints, Options);
            writer.WriteString("path", record.Member.RelativePath);
            writer.WriteString("container", record.Member.Container);
            writer.WriteString("symbol", record.Member.QualifiedSymbol);
            writer.WriteString("signature", record.Member.Signature);
            writer.WriteString("kind", record.Member.Kind.ToString());
            writer.WriteNumber("startLine", record.Member.StartLine);
            writer.WriteNumber("endLine", record.Member.EndLine);
            writer.WriteBoolean("hasBody", record.Member.HasBody);
            writer.WriteNumber("complexity", record.Member.Complexity);
            writer.WriteString("complexitySource", "roslyn-source-syntax");

            if (record.IlComplexity is { } il)
            {
                writer.WriteNumber("ilComplexityCrossCheck", il);
            }

            writer.WriteNumber("sequencePointsTotal", record.SequencePointsTotal);
            writer.WriteNumber("sequencePointsCovered", record.SequencePointsCovered);
            writer.WriteNumber("branchesTotal", record.BranchesTotal);
            writer.WriteNumber("branchesVisited", record.BranchesVisited);
            writer.WriteString("coverageBasis", "sequence-points (line) coverage");

            WriteNullable(writer, "coverage", record.Coverage);
            WriteNullable(writer, "branchCoverage", record.BranchCoverage);

            writer.WriteString("evidenceState", record.State.ToString());
            writer.WriteBoolean("measuredZero", record.IsMeasuredZero);

            if (record.Reason is { } reason)
            {
                writer.WriteString("reason", reason);
            }

            WriteNullable(writer, "crap", record.Crap);
            if (record.Crap is { } crap)
            {
                writer.WriteString("riskBand", RiskBands.Of(crap));
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } actual)
        {
            writer.WriteNumber(name, actual);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    public static void WriteCsv(string path, ImmutableArray<FunctionRecord> records)
    {
        var text = new StringBuilder();
        text.AppendLine(
            "project,path,container,symbol,signature,kind,startLine,endLine,complexity," +
            "ilComplexityCrossCheck,sequencePointsTotal,sequencePointsCovered,branchesTotal," +
            "branchesVisited,coverage,branchCoverage,evidenceState,measuredZero,reason,crap,riskBand");

        foreach (FunctionRecord record in records)
        {
            text.Append(Csv(record.Member.Project)).Append(',');
            text.Append(Csv(record.Member.RelativePath)).Append(',');
            text.Append(Csv(record.Member.Container)).Append(',');
            text.Append(Csv(record.Member.QualifiedSymbol)).Append(',');
            text.Append(Csv(record.Member.Signature)).Append(',');
            text.Append(Csv(record.Member.Kind.ToString())).Append(',');
            text.Append(Number(record.Member.StartLine)).Append(',');
            text.Append(Number(record.Member.EndLine)).Append(',');
            text.Append(Number(record.Member.Complexity)).Append(',');
            text.Append(record.IlComplexity is { } il ? Number(il) : string.Empty).Append(',');
            text.Append(Number(record.SequencePointsTotal)).Append(',');
            text.Append(Number(record.SequencePointsCovered)).Append(',');
            text.Append(Number(record.BranchesTotal)).Append(',');
            text.Append(Number(record.BranchesVisited)).Append(',');
            text.Append(Number(record.Coverage)).Append(',');
            text.Append(Number(record.BranchCoverage)).Append(',');
            text.Append(Csv(record.State.ToString())).Append(',');
            text.Append(record.IsMeasuredZero ? "true" : "false").Append(',');
            text.Append(Csv(record.Reason ?? string.Empty)).Append(',');
            text.Append(Number(record.Crap)).Append(',');
            text.Append(Csv(record.Crap is { } crap ? RiskBands.Of(crap) : string.Empty));
            text.AppendLine();
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(double? value) =>
        value is { } actual ? actual.ToString("R", CultureInfo.InvariantCulture) : string.Empty;

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
