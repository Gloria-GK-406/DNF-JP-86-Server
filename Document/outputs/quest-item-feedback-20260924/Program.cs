using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
using var a = PvfArchive.Open(@"E:/Project/DNF/DNFClient-A21-protocol/Script.pvf");
var ids = new HashSet<int> { 10149153, 3266 };
var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var i=0; i<a.FileCount; i++) {
 var e=a.GetEntry(i); var path=a.ResolveString(e.PathOffset)+"/"+a.ResolveString(e.NameOffset);
 if(!path.EndsWith("stackable.lst")) continue;
 var r=a.OpenScript(i); int id=0;
 while(r.Read(out var t)) { if(t.IsString) { if(ids.Contains(id)) { var f=a.ResolveString(t.Value);paths.Add("stackable/"+f);Console.WriteLine($"ID {id}: {f}"); }} else id=t.Value; }
}
for (var i=0; i<a.FileCount; i++) {
 var e=a.GetEntry(i);var path=a.ResolveString(e.PathOffset)+"/"+a.ResolveString(e.NameOffset);
 if(!paths.Contains(path))continue;
 Console.WriteLine(path);var r=a.OpenScript(i);
 while(r.Read(out var t))Console.WriteLine(t.IsString?a.ResolveString(t.Value):t.Value.ToString());
}
