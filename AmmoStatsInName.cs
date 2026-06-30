using System.Reflection;
using System.Text;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace AmmoStatsInName;

public record ModMetadata : AbstractModMetadata {
    public override string ModGuid { get; init; } = "com.mattdokn.ammostats";
    public override string Name { get; init; } = "AmmoStatsInName";
    public override string Author { get; init; } = "Mattdokn";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.4.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/m-barneto/AmmoStats";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 10)]
public class AmmoStatsInName(
    DatabaseServer databaseServer,
    DatabaseService databaseService,
    LocaleService localeService,
    ModHelper modHelper,
    ItemHelper itemHelper,
    ISptLogger<AmmoStatsInName> logger)
    : IOnLoad
{

    Dictionary<MongoId, TemplateItem>? itemDatabase;
    HandbookBase? handbookDatabase;
    int bulletCount = 0;
    ModConfig? config;

    private Dictionary<string, Dictionary<string, string>> NewLocale { get; set; } = [];
    private Dictionary<string, Dictionary<string, string>> OriginalLocale { get; set; } = [];

    public Task OnLoad()
    {
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        config = modHelper.GetJsonDataFromFile<ModConfig>(pathToMod, "config.json");
        if (config == null)
        {
            logger.Error($"[{GetType().Namespace}] Unable to locate mod config file! Using built-in defaults");
            config = new ModConfig();
        }

        itemDatabase = databaseServer.GetTables().Templates.Items;
        handbookDatabase = databaseServer.GetTables().Templates.Handbook;

        var Locale = databaseService.GetLocales();
        foreach (var lang in Locale.Global.Keys)
        {
            OriginalLocale.Add(lang, localeService.GetLocaleDb(lang));
            NewLocale.Add(lang, []);
        }

        // Loop through all base ammo
        var allBullets = itemHelper.GetItemTplsOfBaseType("5485a8684bdc2da71d8b4567");
        foreach (var itemId in allBullets)
        {

            var item = itemDatabase[itemId];

            if (item.Type != "Item") continue;

            HandbookItem? handbookEntry = handbookDatabase.Items.Find(item => item.Id == itemId);
            if (handbookEntry == null) continue;

            TemplateItemProperties? props = item.Properties;
            if (props == null) continue;

            if (props.AmmoType != "bullet" && props.AmmoType != "buckshot" && props.AmmoType != "grenade") continue;
            AddInfoToBullet(item);
        }
        // Loop through all ammo boxes
        var allAmmoBoxes = itemHelper.GetItemTplsOfBaseType("5485a8684bdc2da71d8b4567");
        foreach (var itemId in allAmmoBoxes)
        {
            var item = itemDatabase[itemId];

            if (item.Type != "Item") continue;

            HandbookItem? handbookEntry = handbookDatabase.Items.Find(item => item.Id == itemId);
            if (handbookEntry == null) continue;

            TemplateItemProperties? props = item.Properties;
            if (props == null) continue;

            // Get ammo out of the box
            if (props.StackSlots == null || props.StackSlots.Count() != 1) continue;

            // Make sure the slotted item has this box as it's parent
            StackSlot slot = props.StackSlots.First();
            if (slot.Parent == null || slot.Parent != item.Id || slot.Properties == null) continue;

            // Navigate to the bullet item id
            MongoId bullet = slot.Properties!.Filters!.First().Filter!.First();

            AddInfoToBullet(item, itemDatabase[bullet]);
        }

        foreach (var langId in Locale.Global.Keys)
        {
            if (Locale is not null && Locale.Global.TryGetValue(langId, out var lazyloadedValue))
            {
                NewLocale.TryGetValue(langId, out var newLocaleToAdd);
                if (newLocaleToAdd is null)
                    NewLocale.TryGetValue("en", out newLocaleToAdd);

                if (newLocaleToAdd is null) continue;

                lazyloadedValue.AddTransformer(lazyloadedLocaleData =>
                {
                    if (lazyloadedLocaleData is null) return lazyloadedLocaleData;
                    foreach (var (key, value) in newLocaleToAdd)
                    {
                        lazyloadedLocaleData[key] = value;
                    }
                    return lazyloadedLocaleData;
                });
            }
        }

        logger.Success($"[{GetType().Namespace}] Modified {bulletCount} ammo and ammobox names");

        return Task.CompletedTask;
    }

    void AddInfoToBullet(TemplateItem item, TemplateItem? bullet = null)
    {
        if (bullet == null) bullet = item;
        if (bullet.Name == null) return;
        string name = bullet.Name.ToLower();
        if (name.Contains("patron_rsp") || name.Contains("patron_26x75")) return;

        TemplateItemProperties? props = item.Properties;
        if (props == null) return;

        string localeIndex = item.Id + " Name";

        string damage;
        if (props.AmmoType == "buckshot")
        {
            damage = $"{props.Damage!.Value}x{props.ProjectileCount!.Value}".PadLeft(config!.PaddingLength, '0');
        }
        else
        {
            damage = $"{props.Damage!.Value}".PadLeft(config!.PaddingLength, '0');
        }
        string pen = props.PenetrationPower!.Value
            .ToString()
            .PadLeft(config!.PaddingLength, '0');

        StringBuilder bulletInfo = new();

        if (config!.InfoInParenthesis)
        {
            bulletInfo.Append('(');
        }
        if (config.ShowPenBeforeDmg)
        {
            // Show pen before dmg
            bulletInfo.Append($"{pen}/{damage}");
        }
        else
        {
            bulletInfo.Append($"{damage}/{pen}");
        }
        if (bullet.Properties!.AmmoType == "grenade")
        {
            bulletInfo.Append($"/{props.FuzeArmTimeSec}s");
        }
        if (config!.InfoInParenthesis)
        {
            bulletInfo.Append(')');
        }
        bulletCount++;
        foreach (var (lang, newLang) in NewLocale)
        {
            var bulletName = TryGetLocaleText(lang, localeIndex);
            if (bulletName is null) continue;
            newLang.Add(localeIndex, config.InfoBeforeName ? $"{bulletInfo} {bulletName}" : $"{bulletName} {bulletInfo}");
        }
    }

    private string? TryGetLocaleText(string lang, string key)
    {
        var sources = new[]
        {
            OriginalLocale.GetValueOrDefault(lang),
            OriginalLocale.GetValueOrDefault("en")
        };

        foreach (var source in sources)
        {
            if (source != null && source.TryGetValue(key, out var text) && text.Length > 0)
                return text;
        }
        return null;
    }
}

public record ModConfig
{
    public bool ShowPenBeforeDmg { get; set; } = true;
    public bool InfoBeforeName { get; set; } = true;
    public bool InfoInParenthesis { get; set; } = true;
    public int PaddingLength { get; set; } = 2;
}