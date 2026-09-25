using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
using var archive = PvfArchive.Open(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")!);
for (var i=0;i<archive.FileCount;i++)
{
    var entry=archive.GetEntry(i);
    var path=archive.ResolveString(entry.PathOffset)+"/"+archive.ResolveString(entry.NameOffset);
    if (!entry.IsScript || !(path.EndsWith(".dgn")||path.EndsWith(".map"))) continue;
    var reader=archive.OpenScript(i); bool active=false;
    while(reader.Read(out var t))
    {
        if(t.Kind==PvfTokenKind.Identifier)
        {
            var tag=archive.ResolveString(t.Value);
            active=tag is "[special passive object]" or "[special passive object item]" or "[object drop item]" or "[passive object]";
            if(active) Console.WriteLine("\n"+path+" "+tag);
        }
        else if(active) Console.Write((t.IsString?archive.ResolveString(t.Value):t.Value.ToString())+" ");
    }
}
