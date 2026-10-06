using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfWeeklyAttendanceDefinitionSource(PvfArchiveProvider provider) : IWeeklyAttendanceDefinitionSource
{
    public const string Path = "event/chn_event/chn_totalattendance.evt";
    public ValueTask<WeeklyAttendanceDefinition> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var archive = provider.Archive;
        var sections = PvfDocument.Open(archive, Path).Sections;
        int[] Read(string name) => sections.Where(s => s.Name == name)
            .SelectMany(s => PvfField.Int32List(name).Decode(archive, s.Values, Path)).ToArray();
        var duration = Read("[event duration]"); var condition = Read("[attendance condition]");
        AttendanceReward[] Rewards(string name)
        {
            var v = Read(name);
            if (v.Length == 0 || v.Length % 3 != 0) throw new InvalidDataException($"{Path}: invalid {name}.");
            return Enumerable.Range(0, v.Length / 3).Select(i => new AttendanceReward(v[i*3],v[i*3+1],v[i*3+2])).ToArray();
        }
        if (duration.Length != 1 || condition.Length != 2) throw new InvalidDataException($"{Path}: invalid duration/condition.");
        return ValueTask.FromResult(new WeeklyAttendanceDefinition(Path, condition[0], condition[1], duration[0],
            Rewards("[attendance week]"), Rewards("[total attendance week]")));
    }
}
