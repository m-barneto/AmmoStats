import { DependencyContainer } from "tsyringe";

import { jsonc } from "jsonc";
import path from "path";
import { QuestRewardType } from "@spt/models/enums/QuestRewardType";
import { IPostDBLoadMod } from "@spt/models/external/IPostDBLoadMod";
import { ILogger } from "@spt/models/spt/utils/ILogger";
import { DatabaseServer } from "@spt/servers/DatabaseServer";
import { VFS } from "@spt/utils/VFS";
import { ITemplateItem, ItemType, SlotFilter, StackSlot } from "@spt/models/eft/common/tables/ITemplateItem";
import { LocaleService } from "@spt/services/LocaleService";
import { IHandbookBase } from "@spt/models/eft/common/tables/IHandbookBase";


class AmmoStats implements IPostDBLoadMod {
    private modConfig;
    private itemDatabase: Record<string, ITemplateItem>;
    private handbookDatabase: IHandbookBase;
    private localeDatabase: Record<string, string>;
    private logger: ILogger;

    public postDBLoad(container: DependencyContainer): void {
        const vfs = container.resolve<VFS>("VFS");
        this.modConfig = jsonc.parse(vfs.readFile(path.resolve(__dirname, "../config/config.jsonc")));

        this.logger = container.resolve<ILogger>("WinstonLogger");
        const databaseServer = container.resolve<DatabaseServer>("DatabaseServer");
        const localeService = container.resolve<LocaleService>("LocaleService");

        this.itemDatabase = databaseServer.getTables().templates.items;
        this.handbookDatabase = databaseServer.getTables().templates.handbook;
        this.localeDatabase = localeService.getLocaleDb();

        for (const itemId in this.itemDatabase) {
            const item = this.itemDatabase[itemId];
            if (item._type != ItemType.ITEM) continue;
            const handbookItem = this.handbookDatabase.Items.find((i) => i.Id === item._id);
            if (handbookItem == undefined) continue;

            if (item._parent == "5485a8684bdc2da71d8b4567") {
                if (!("ammoType" in item._props) || item._props.ammoType != "bullet" && item._props.ammoType != "buckshot" && item._props.ammoType != "grenade") {
                    continue;
                }
                this.addInfoToName(item, item);
            } else if (item._parent == "543be5cb4bdc2deb348b4568") {
                // Get the bullet for our ammo pack
                if (!("StackSlots" in item._props)) {
                    continue;
                }

                const stackSlots = item._props.StackSlots as StackSlot[];
                if (stackSlots.length != 1) {
                    continue;
                }

                // Get first slot
                if (stackSlots[0]._parent != item._id) {
                    this.logger.error(`Problem handling ammo box with ID ${item._id}`);
                    continue;
                }

                if (!("filters" in stackSlots[0]._props)) {
                    this.logger.error(`Problem with filters in ammo box with ID ${item._id}`);
                    continue;
                }

                const filters = stackSlots[0]._props.filters;
                if (filters.length != 1) {
                    this.logger.error(`Problem with filters length in ammo box with ID ${item._id}`);
                }

                const filter = filters[0].Filter[0];
                const bulletId = filter;

                this.addInfoToName(item, this.itemDatabase[bulletId]);
            }
        }
    }

    private addInfoToName(item: ITemplateItem, bullet: ITemplateItem) {
        if (bullet._name.toLowerCase().indexOf("shrapnel") != -1 || bullet._name.toLowerCase().indexOf("patron_rsp") != -1 || bullet._name.toLowerCase().indexOf("patron_26x75") != -1) {
            return;
        }
        
        const itemName = item._id + " Name";

        const hasDamageProp = "Damage" in bullet._props;
        const hasPenProp = "PenetrationPower" in bullet._props;
        if (!hasPenProp || !hasDamageProp) return;

        let damageMult = 1;
        if (bullet._props.ammoType == "buckshot") {
            damageMult = bullet._props.buckshotBullets;
        }

        const damage = String(bullet._props.Damage * damageMult).padStart(this.modConfig.PaddingLength, "0");
        const pen = String(bullet._props.PenetrationPower).padStart(this.modConfig.PaddingLength, "0");

        let bulletInfo;
        if (this.modConfig.InfoInParenthesis) {
            bulletInfo = "(";
        }
        if (this.modConfig.ShowPenBeforeDmg) {
            bulletInfo += `${pen}/${damage}`;
        } else {
            bulletInfo += `${damage}/${pen}`;
        }
        if (this.modConfig.InfoInParenthesis) {
            bulletInfo += ")";
        }

        const itemLocaleName = this.localeDatabase[itemName];
        if (this.modConfig.InfoBeforeName) {
            this.localeDatabase[itemName] = bulletInfo + " " + itemLocaleName;
        } else {
            this.localeDatabase[itemName] = itemLocaleName + " " + bulletInfo;
        }
    }
}

module.exports = { mod: new AmmoStats() };
