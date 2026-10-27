using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Rental;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfLuckyStarRentalContentSource(PvfArchiveProvider provider) : ILuckyStarRentalContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lock gate=new();
    private LuckyStarRentalCatalog? catalog;
    private IReadOnlyDictionary<int,string>? stackables;
    private IReadOnlyDictionary<int,string>? equipment;
    private static readonly IReadOnlyDictionary<string,byte> Jobs=new Dictionary<string,byte>
    {
        ["[swordman]"]=0,["[fighter]"]=1,["[gunner]"]=2,["[mage]"]=3,["[priest]"]=4,
        ["[at gunner]"]=5,["[thief]"]=6,["[at fighter]"]=7,["[at mage]"]=8,["[at swordman]"]=9,
        ["[creator mage]"]=10,["[demonic swordman]"]=11,["[knight]"]=12,["[demonic lancer]"]=13
    };
    public string Name => "lucky-star-rental";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Load();
        lock (gate)
        {
            var archive = provider.Archive;
            StackablesIndex(archive);
            EquipmentIndex(archive);
            return ValueTask.FromResult(stackables.Count + equipment.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(stackables))]
    private void StackablesIndex(Dfo.Pvf.PvfArchive archive)
    {
        stackables??=PvfListFile.Paths(archive, "stackable/stackable.lst", prefix: "stackable/");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(equipment))]
    private void EquipmentIndex(Dfo.Pvf.PvfArchive archive)
    {
        equipment??=PvfListFile.Paths(archive, "equipment/equipment.lst", prefix: "equipment/");
    }

    public LuckyStarRentalCatalog Load()
    {
        lock(gate) return catalog??=Read();
    }
    private LuckyStarRentalCatalog Read()
    {
        var archive=provider.Archive;
        var sections=PvfNpcShopDefinitionSource.Sections(archive,"etc/chnrentsystem/rentsysteminfo.etc");
        int[] Numbers(string tag)
        {
            var tokens=sections.Single(x=>x.Name==tag).Values;
            if(tokens.Any(x=>x.Kind!=PvfTokenKind.Int32)) throw new InvalidDataException("Invalid rental integers: "+tag);
            return tokens.Select(x=>x.Value).ToArray();
        }
        var limit=Numbers("[limit point]");
        var charging=sections.Single(x=>x.Name=="[point charge]").Values;
        if(limit.Length!=1 || limit[0]<=0 || charging.Count!=4 || !charging[0].IsString || !charging[2].IsString
            || archive.ResolveString(charging[0].Value)!="pdungeon" || archive.ResolveString(charging[2].Value)!="gold"
            || charging[1].Kind!=PvfTokenKind.Int32 || charging[3].Kind!=PvfTokenKind.Int32
            || charging[1].Value<0 || charging[3].Value<=0) throw new InvalidDataException("Invalid lucky star charging rules.");
        var bands=Numbers("[section]");
        if(bands.Length==0 || bands.Length%7!=0) throw new InvalidDataException("Invalid rental level bands.");
        var levels=Enumerable.Range(0,bands.Length/7).ToDictionary(i=>bands[i*7],i=>bands[i*7+1]);
        var offers=new List<RentalOffer>();
        byte? job=null; int band=-1;
        foreach(var section in sections)
        {
            if(section.Name=="[group]")
            {
                if(section.Values.Count!=2 || !section.Values[0].IsString || section.Values[1].Kind!=PvfTokenKind.Int32
                    || !Jobs.TryGetValue(archive.ResolveString(section.Values[0].Value),out var resolved)
                    || !levels.ContainsKey(section.Values[1].Value)) throw new InvalidDataException("Invalid rental group.");
                job=resolved;band=section.Values[1].Value;
            }
            else if(section.Name=="[package selection]")
            {
                if(job is null || section.Values.Count==0 || section.Values.Count%2!=0
                    || section.Values.Any(x=>x.Kind!=PvfTokenKind.Int32 || x.Value<=0)) throw new InvalidDataException("Invalid rental package.");
                var family=ArchiveJobFamilyTokens.Resolve(Jobs.Single(x=>x.Value==job).Key);
                for(var i=0;i<section.Values.Count;i+=2)
                    offers.Add(new(section.Values[i].Value,family,levels[band],section.Values[i+1].Value){CharacterJob=job});
            }
            else if(section.Name=="[/group]") {job=null;band=-1;}
        }
        if(offers.Count==0 || offers.GroupBy(x=>(x.CharacterJob,x.ItemId)).Any(x=>x.Count()!=1)) throw new InvalidDataException("Duplicate or empty rental offers.");
        return new(limit[0],charging[3].Value,charging[1].Value,Numbers("[except dungeon]").ToHashSet(),offers.AsReadOnly());
    }
    public ValueTask<int?> RentPointsAsync(int itemId,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock(gate)
        {
            var archive=provider.Archive;
            StackablesIndex(archive);
            if(!stackables.TryGetValue(itemId,out var path)) return ValueTask.FromResult<int?>(null);
            var matches=PvfNpcShopDefinitionSource.Sections(archive,path).Where(x=>x.Name=="[action type]").ToArray();
            if(matches.Length==0) return ValueTask.FromResult<int?>(null);
            if(matches.Length!=1 || matches[0].Values.Count==0 || !matches[0].Values[0].IsString)
                return ValueTask.FromResult<int?>(null);
            if(archive.ResolveString(matches[0].Values[0].Value)!="[rent point]") return ValueTask.FromResult<int?>(null);
            if(matches[0].Values.Count!=2 || matches[0].Values[1].Kind!=PvfTokenKind.Int32 || matches[0].Values[1].Value<=0)
                throw new InvalidDataException("Invalid lucky star consumable points.");
            return ValueTask.FromResult<int?>(matches[0].Values[1].Value);
        }
    }
    public ValueTask<int?> QualityTierAsync(int itemId,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock(gate)
        {
            var archive=provider.Archive;
            EquipmentIndex(archive);
            if(!equipment.TryGetValue(itemId,out var path)) return ValueTask.FromResult<int?>(null);
            var matches=PvfNpcShopDefinitionSource.Sections(archive,path).Where(x=>x.Name=="[equipment grade]").ToArray();
            if(matches.Length==0) return ValueTask.FromResult<int?>(null);
            if(matches.Length!=1 || matches[0].Values.Count!=1 || !matches[0].Values[0].IsString
                || archive.ResolveString(matches[0].Values[0].Value)!="superlative")
                throw new InvalidDataException("Unsupported rental equipment quality declaration.");
            return ValueTask.FromResult<int?>(4);
        }
    }
    public ValueTask<IReadOnlySet<byte>?> UsableJobsAsync(int itemId,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock(gate)
        {
            var archive=provider.Archive;
            EquipmentIndex(archive);
            if(!equipment.TryGetValue(itemId,out var path)) return ValueTask.FromResult<IReadOnlySet<byte>?>(null);
            var matches=PvfNpcShopDefinitionSource.Sections(archive,path).Where(x=>x.Name=="[usable job]").ToArray();
            if(matches.Length!=1 || matches[0].Values.Count==0 || matches[0].Values.Any(x=>!x.IsString))
                throw new InvalidDataException("Invalid rental equipment jobs.");
            var jobs=new HashSet<byte>();
            foreach(var token in matches[0].Values)
            {
                var text=archive.ResolveString(token.Value);
                if(text=="[all]") foreach(var value in Jobs.Values) jobs.Add(value);
                else if(Jobs.TryGetValue(text,out var value)) jobs.Add(value);
                else throw new InvalidDataException("Unknown rental equipment job: "+text);
            }
            return ValueTask.FromResult<IReadOnlySet<byte>?>(jobs);
        }
    }
}
