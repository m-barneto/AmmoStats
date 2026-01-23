using System.Reflection;
using System.Text;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace AmmoStats;

public record ModMetadata : AbstractModMetadata {
    public override string ModGuid { get; init; } = "com.mattdokn.ammostats";
    public override string Name { get; init; } = "AmmoStats";
    public override string Author { get; init; } = "Mattdokn";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.3.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/m-barneto/AmmoStats";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}


[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 90000)]
public class AmmoStats(
    DatabaseServer databaseServer,
    DatabaseService databaseService,
    LocaleService localeService,
    ModHelper modHelper,
    ConfigServer configServer,
    ISptLogger<AmmoStats> logger)
    : IOnLoad {

    Dictionary<MongoId, TemplateItem>? itemDatabase;
    HandbookBase? handbookDatabase;
    Dictionary<string, string>? locales;
    Dictionary<string, string>? bulletNames;
    ModConfig? config;

    public Task OnLoad() {
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        config = modHelper.GetJsonDataFromFile<ModConfig>(pathToMod, "config.json");
        if (config == null) {
            logger.Error("Unable to locate mod config file!");
            return Task.CompletedTask;
        }

        itemDatabase = databaseServer.GetTables().Templates.Items;
        handbookDatabase = databaseServer.GetTables().Templates.Handbook;
        locales = localeService.GetLocaleDb();
        bulletNames = new();

        // Loop through all items
        foreach (var (itemId, item) in itemDatabase) {
            if (item.Type != "Item") continue;

            HandbookItem? handbookEntry = handbookDatabase.Items.Find(item => item.Id == itemId);
            if (handbookEntry == null) continue;

            TemplateItemProperties? props = item.Properties;
            if (props == null) continue;

            // If base Ammo
            if (item.Parent == "5485a8684bdc2da71d8b4567") {
                if (props.AmmoType != "bullet" && props.AmmoType != "buckshot" && props.AmmoType != "grenade") continue;
                AddInfoToBullet(item);
            }
            // If ammo box
            else if (item.Parent == "543be5cb4bdc2deb348b4568") {
                // Get ammo out of the box
                if (props.StackSlots == null || props.StackSlots.Count() != 1) continue;

                // Make sure the slotted item has this box as it's parent
                StackSlot slot = props.StackSlots.First();
                if (slot.Parent == null || slot.Parent != item.Id || slot.Properties == null) continue;

                // Navigate to the bullet item id
                MongoId bullet = slot.Properties!.Filters!.First().Filter!.First();

                AddInfoToBullet(item, itemDatabase[bullet]);
            }

        }

        // Load our changes into the english locales
        if (databaseService.GetLocales().Global.TryGetValue(configServer.GetConfig<LocaleConfig>().GameLocale, out var lazyloadedValue)) {
            lazyloadedValue.AddTransformer(lazyloadedLocaleData => {
                bulletNames.ToList().ForEach(x => lazyloadedLocaleData![x.Key] = x.Value);

                return lazyloadedLocaleData;
            });
        }

        logger.Success($"Modified {bulletNames.Count} ammo and ammobox names.");

        return Task.CompletedTask;
    }

    void AddInfoToBullet(TemplateItem item, TemplateItem? bullet = null) {
        if (bullet == null) bullet = item;
        if (bullet.Name == null) return;
        string name = bullet.Name.ToLower();
        if (name.Contains("patron_rsp") || name.Contains("patron_26x75")) return;


        string localeIndex = item.Id + " Name";
        double damageMultiplier = 1.0;

        if (bullet.Properties!.AmmoType == "buckshot") {
            damageMultiplier = bullet.Properties!.BuckshotBullets!.Value;
        }

        string pen = bullet.Properties!.PenetrationPower!.Value
            .ToString()
            .PadLeft(config!.PaddingLength, '0');

        string damage = (bullet.Properties!.Damage!.Value * damageMultiplier)
            .ToString()
            .PadLeft(config!.PaddingLength, '0');

        StringBuilder bulletInfo = new StringBuilder();
        
        if (config!.InfoInParenthesis) {
            bulletInfo.Append("(");
        }
        if (config.ShowPenBeforeDmg) {
            // Show pen before dmg
            bulletInfo.Append($"{pen}/{damage}");

        } else {
            bulletInfo.Append($"{damage}/{pen}");
        }
        if (config!.InfoInParenthesis) {
            bulletInfo.Append(")");
        }

        ModifyLocale(localeIndex, bulletInfo.ToString());
    }

    void ModifyLocale(string localeIndex, string bulletInfo, bool infoBeforeName = true) {
        bulletNames!.Add(localeIndex, infoBeforeName ? $"{bulletInfo} {locales![localeIndex]}" : $"{locales![localeIndex]} {bulletInfo}");
    }
}

public record ModConfig {
    public required bool ShowPenBeforeDmg { get; set; } = true;
    public required bool InfoBeforeName { get; set; } = true;
    public required bool InfoInParenthesis { get; set; } = true;
    public required int PaddingLength { get; set; } = 2;

}