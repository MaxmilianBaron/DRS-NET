using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DungeonRunners.Engine;
using DungeonRunners.Utilities;

namespace DungeonRunners.Data
{
    public class GCObject
    {
        public static readonly byte DFC_VERSION = 0x2D;

        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public string DFCClass { get; set; } = "";
        public string GCClass { get; set; } = "";

        private static readonly HashSet<string> _itemsPalNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1haxepal", "1hmacepal", "1hstaffpal", "1hswordpal",
            "fighterbodypal", "fighterbootspal", "fighterglovespal", "fighterhelmpal",
            "fightershieldpal", "fightershoulderspal",
            "itempackpal", "itempackvisuals",
            "magebodypal", "magebootspal", "mageglovespal", "magehelmpal",
            "mageshieldpal", "mageshoulderspal",
            "rangerbodypal", "rangerbootspal", "rangerglovespal", "rangerhelmpal",
            "rangershoulderspal",
            "shieldvisuals", "voucherpal",
        };

        public string GetPacketGCClass() => GetPacketGCClassFor(GCClass);

        public static string GetPacketGCClassFor(string gcClass)
        {
            if (string.IsNullOrEmpty(gcClass)) return gcClass ?? string.Empty;
            string lower = gcClass.ToLowerInvariant();
            if (lower == "potionpal.healthpotion_itempack")
                return "items.consumables.consumable_majorhealthpotion";
            if (lower == "potionpal.manapotion_itempack")
                return "items.consumables.consumable_majormanapotion";
            if (lower.StartsWith("items.pal."))
                return lower;
            int dot = lower.IndexOf('.');
            if (dot > 0)
            {
                string ns = lower.Substring(0, dot);
                if (_itemsPalNamespaces.Contains(ns))
                    return "items.pal." + lower;
            }
            return lower;
        }
        public List<GCObjectProperty> Properties { get; set; } = new List<GCObjectProperty>();
        public List<GCObject> Children { get; set; } = new List<GCObject>();
        public byte[] ExtraData { get; set; } = Array.Empty<byte>();
        public uint? TargetSlot { get; set; } = null;
        public string PresetScaleMod { get; set; } = null;
        public int StoredRarity { get; set; } = -1;
        public int StoredLevel { get; set; } = -1;

        public static int DetectRarityFromGCClass(string gcClass)
        {
            if (string.IsNullOrEmpty(gcClass)) return 0;
            string lower = gcClass.ToLowerInvariant();
            if (lower.Contains("mythicpal")) return 5;
            if (lower.Contains("mythic")) return 5;
            if (lower.Contains("unique")) return 4;
            if (lower.Contains("rare")) return 3;
            if (lower.Contains("magical") || lower.Contains("magic")) return 2;
            if (lower.Contains("superior")) return 1;
            return 0;
        }

        public int GetEffectiveRarity()
        {
            if (StoredRarity >= 0) return StoredRarity;
            return DetectRarityFromGCClass(GCClass);
        }
        private string GetModifierGCClass()
        {
            if (!string.IsNullOrEmpty(PresetScaleMod))
            {
                Debug.LogError($"[MODIFIER] Item '{GCClass}' -> Using PresetScaleMod '{PresetScaleMod}'");
                return PresetScaleMod;
            }

            string armorType = "Scale";
            string gcLower = GCClass.ToLower();

            if (gcLower.Contains("plate"))
                armorType = "Plate";
            else if (gcLower.Contains("crystal"))
                armorType = "Crystal";
            else if (gcLower.Contains("chain"))
                armorType = "Chain";
            else if (gcLower.Contains("leather"))
                armorType = "Leather";
            else if (gcLower.Contains("cloth"))
                armorType = "Cloth";
            else if (gcLower.Contains("scale"))
                armorType = "Scale";

            int tierSuffix = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
            var itemRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(tierSuffix);
            if (itemRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
            {
                int effective = GetEffectiveRarity();
                if (effective > 0 && effective < 5)
                    itemRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
            }
            string scaleMod = DungeonRunners.Gameplay.RPGSettings.GetDeterministicScaleMod(GCClass, itemRarity);

            Debug.LogError($"[MODIFIER] Item '{GCClass}' -> ArmorType '{armorType}' -> tier={tierSuffix} rarity={itemRarity} -> ScaleMod '{scaleMod}'");
            return scaleMod;
        }

        public void AddProperty(GCObjectProperty property)
        {
            Properties.Add(property);
        }

        public void AddChild(GCObject child)
        {
            Children.Add(child);
        }

        public void WriteFullGCObject(LEWriter writer)
        {
            Debug.Log($"[GC-OBJECT] writeDfc id={Id} dfcClass='{DFCClass}' gcClass='{GCClass}' props={Properties.Count} children={Children.Count}");

            writer.WriteByte(DFC_VERSION);

            uint dfcHash = HashDjb2(DFCClass);
            writer.WriteUInt32(dfcHash);
            Debug.Log($"[GC-OBJECT] dfcClass='{DFCClass}' hash=0x{dfcHash:X8}");

            writer.WriteUInt32(Id);

            writer.WriteCString(Name);

            writer.WriteUInt32((uint)Children.Count);

            foreach (var child in Children)
            {
                child.WriteFullGCObject(writer);
            }

            string gcForHash = GetPacketGCClassFor(GCClass);
            uint gcHash = HashDjb2(gcForHash);
            writer.WriteUInt32(gcHash);
            Debug.Log($"[GC-OBJECT] gcClass='{GCClass}' hash=0x{gcHash:X8}");
            if (DFCClass == "Avatar")
            {
                Debug.LogError($"[CHARLIST-AVATAR] Avatar GCClass='{GCClass}' -> hash 0x{gcHash:X8}");
            }
            foreach (var prop in Properties)
            {
                prop.WriteDFC(writer);
            }

            writer.WriteUInt32(0);

            if (ExtraData.Length > 0)
            {
                writer.WriteBytes(ExtraData);
                Debug.Log($"[GC-OBJECT] extraDataBytes={ExtraData.Length}");
            }
        }



        public void WriteInitForDroppedItem(LEWriter writer, int playerLevel)
        {
            Debug.LogError($"[DROP-WRITEINIT] start");
            Debug.LogError($"[DROP-WRITEINIT] GCClass: '{GCClass}'");
            Debug.LogError($"[DROP-WRITEINIT] DFCClass: '{DFCClass}'");

            string gcCheck = GCClass.ToLower();
            if (gcCheck.Contains("ring") || gcCheck.Contains("amulet"))
            {
                DFCClass = "Item";
            }

            writer.WriteByte(0xFF);
            writer.WriteCString(GetPacketGCClass());
            Debug.LogError($"[DROP-WRITEINIT] write=0xFF,GCClass");

            string gcLowerCheck = GCClass.ToLower();

            bool isConsumableDrop = DFCClass == "ActiveItem"
                || gcLowerCheck.Contains("potion")
                || gcLowerCheck.Contains("scroll")
                || gcLowerCheck.Contains("skillbook")
                || gcLowerCheck.Contains("voucher")
                || gcLowerCheck.Contains("consumable")
                || gcLowerCheck.Contains("townportal")
                || gcLowerCheck.Contains("questitem")
                || gcLowerCheck.Contains("itempal");
            if (isConsumableDrop)
            {
                int simpleLevel = StoredLevel >= 0 ? StoredLevel : 1;
                writer.WriteUInt32(0);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);
                writer.WriteByte((byte)simpleLevel);
                writer.WriteByte(0x00);
                bool hasTransientMod = gcLowerCheck.Contains("dragonjuice")
                    || gcLowerCheck.Contains("intbuff");
                if (hasTransientMod)
                {
                    writer.WriteByte(0x00);
                }
                writer.WriteByte(0x00);
                Debug.LogError($"[DROP-WRITEINIT] format=consumable level={simpleLevel} transientMod={hasTransientMod}");
                Debug.LogError($"[DROP-WRITEINIT] end");
                return;
            }

            if (DFCClass == "Item" || gcLowerCheck.Contains("ring") || gcLowerCheck.Contains("amulet"))
            {
                bool isAmulet = gcLowerCheck.Contains("amulet");
                uint ringSlot = TargetSlot ?? GetEquipmentSlotFromGCClass();

                bool isMythicJewelry = gcLowerCheck.Contains("mythic");
                int jewelryLevel = StoredLevel >= 0 ? StoredLevel : (isMythicJewelry ? (playerLevel + 3) : GetItemRequiredLevel());

                writer.WriteUInt32(ringSlot);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);
                writer.WriteByte((byte)jewelryLevel);

                if (isMythicJewelry)
                {
                    int jewelryPlaceholderCount;
                    List<uint> jewelryResolvedHashes = new List<uint>();
                    string jewelryKey = GCClass.ToLowerInvariant();
                    if (jewelryKey.StartsWith("items.pal.")) jewelryKey = jewelryKey.Substring("items.pal.".Length);
                    if (DungeonRunners.Gameplay.MerchantRuntime.TryGetAuthoredMerchantModSlots(jewelryKey, out int merchantSlots))
                    {
                        jewelryPlaceholderCount = System.Math.Max(0, merchantSlots - 1);
                    }
                    else
                    {
                        var jewelryMods = isAmulet
                            ? DatabaseLoader.GetAmuletModifiers(GCClass)
                            : DatabaseLoader.GetRingModifiers(GCClass);
                        jewelryPlaceholderCount = jewelryMods.Count;
                    }
                    var jewelryWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                    foreach (var (slot, modRef) in jewelryWireMods.OrderBy(w => w.Slot))
                    {
                        uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                        if (h != 0) jewelryResolvedHashes.Add(h);
                    }

                    Debug.LogError($"[DROP-{(isAmulet ? "AMULET" : "RING")}-MYTHIC] GCClass: {GCClass}, Slot: {ringSlot}, Level: {jewelryLevel}, Placeholders: {jewelryPlaceholderCount}, WireMods: {jewelryResolvedHashes.Count}");

                    writer.WriteByte(0x00);
                    for (int m = 0; m < jewelryPlaceholderCount; m++) writer.WriteByte(0x00);
                    writer.WriteByte((byte)jewelryResolvedHashes.Count);
                    foreach (uint h in jewelryResolvedHashes)
                    {
                        writer.WriteByte(0x04);
                        writer.WriteUInt32(h);
                        writer.WriteByte(0x00);
                    }
                }
                else
                {
                    int dropTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                    var dropRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(dropTier);
                    if (dropRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        int effective = GetEffectiveRarity();
                        if (effective > 0 && effective < 5)
                            dropRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                    }
                    Debug.LogError($"[DROP-{(isAmulet ? "AMULET" : "RING")}-NONMYTHIC] GCClass: {GCClass}, Slot: {ringSlot}, Level: {jewelryLevel}, Rarity: {dropRarity}");
                    writer.WriteByte(0x00);

                    var dropJewelryMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                    if (dropJewelryMods.Count == 0)
                        dropJewelryMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, dropRarity.ToString());

                    if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && dropJewelryMods.Count > 0)
                    {
                        var hashes = new List<uint>();
                        foreach (var (slot, modRef) in dropJewelryMods.OrderBy(w => w.Slot))
                        {
                            uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                            if (h != 0) hashes.Add(h);
                        }
                        writer.WriteByte((byte)hashes.Count);
                        foreach (uint h in hashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(h);
                            writer.WriteByte(0x00);
                        }
                        Debug.LogError($"[ITEM-WIRE-MODS] context=drop kind={(isAmulet ? "amulet" : "ring")} item={GCClass} rarity={dropRarity} mods={hashes.Count}");
                    }
                    else if (dropRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        writer.WriteByte(0x00);
                    }
                    else
                    {
                        writer.WriteByte(0x01);
                        writer.WriteByte(0xFF);
                        writer.WriteCString(GetModifierGCClass());
                        writer.WriteByte(0x03);
                        writer.WriteByte(0x15);
                        writer.WriteUInt32(0x11111111);
                    }
                }

                return;
            }

            ItemData itemData = DatabaseLoader.FindItem(GCClass);
            string gcLower = GCClass.ToLower();

            bool isMythic = gcLower.Contains("mythic")
                || DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(
                    gcLower.StartsWith("items.pal.") ? gcLower.Substring(10) : gcLower);
            bool isPartialbuilt = gcLower.Contains("partialbuilt");
            bool isPrebuilt = gcLower.Contains("prebuilt");
            bool isGeneratedboss = gcLower.Contains("generatedboss");
            bool hasMythicSeasonal = gcLower.Contains("mythicseasonal");
            bool hasMythicInName = gcLower.Contains("mythic") && !hasMythicSeasonal;
            bool hasSeasonal = gcLower.Contains("seasonal");
            bool hasWishingwell = gcLower.Contains("wishingwell");
            bool hasUnique = gcLower.Contains("unique");
            bool hasJustSeasonal = hasSeasonal && !gcLower.Contains("mythic") && !hasUnique;
            bool hasUniqueSeasonal = hasUnique && hasSeasonal;

            Debug.LogError($"[DROP-WRITEINIT] ItemData lookup: {(itemData != null ? "FOUND" : "NULL")}, isMythic={isMythic}, isPartialbuilt={isPartialbuilt}, isPrebuilt={isPrebuilt}, isGeneratedboss={isGeneratedboss}, hasMythicSeasonal={hasMythicSeasonal}, hasSeasonal={hasSeasonal}, hasWishingwell={hasWishingwell}, hasUniqueSeasonal={hasUniqueSeasonal}");

            uint equipSlot = GetPropertyUInt32("Slot");
            if (equipSlot == 0)
            {
                equipSlot = GetEquipmentSlotFromGCClass();
            }
            Debug.LogError($"[DROP-WRITEINIT] equipSlot={equipSlot}");

            writer.WriteUInt32(equipSlot);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x01);

            int level = StoredLevel >= 0 ? StoredLevel : (isMythic ? (playerLevel + 3) : GetItemRequiredLevel());
            writer.WriteByte((byte)level);

            bool writeFlag = false;

            if (isMythic && !isPartialbuilt)
            {
                writeFlag = true;
            }
            else if (isPartialbuilt && hasMythicSeasonal)
            {
                writeFlag = true;
            }
            else if (isPartialbuilt && hasUniqueSeasonal && !gcLower.Contains("fighter"))
            {
                writeFlag = true;
            }
            else if (isPrebuilt && hasMythicInName)
            {
                writeFlag = true;
            }
            else if (isPrebuilt && hasMythicSeasonal)
            {
                writeFlag = true;
            }
            else if (isPrebuilt && hasWishingwell && !gcLower.Contains("fighter") && !gcLower.Contains("helm"))
            {
                writeFlag = true;
            }

            if (!writeFlag && isMythic && GCClass.IndexOf("MythicPAL", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                writeFlag = true;
            }

            if (writeFlag)
            {
                if ((gcLower.Contains("scale") && !gcLower.Contains("helm") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")) ||
                    (gcLower.Contains("splint") && !gcLower.Contains("shoulder") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")))
                {
                    Debug.LogError($"[DROP-WRITEINIT] SKIPPED flag for scale/splint");
                    writeFlag = false;
                }
            }

            bool isWeapon = (DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon");
            if (writeFlag && (!isWeapon || isMythic))
            {
                writer.WriteByte(0x00);
                Debug.LogError($"[EQUIP-WRITEINIT] Wrote FLAG byte (armor only)");
            }

            Debug.LogError($"[DROP-WRITEINIT] format=item equipSlot={equipSlot} level={level}");

            int modCount;

            int dropGcLookup = DungeonRunners.Gameplay.MerchantRuntime.GetOP5ModCount(GCClass);
            if (dropGcLookup >= 0)
            {
                modCount = dropGcLookup;
                if (!writeFlag) modCount++;
                Debug.LogError($"[DROP-WRITEINIT] GCClass={GCClass} modCount={modCount}");
            }
            else
            {
                modCount = itemData?.modCount ?? 3;
                if (gcLower.Contains("chain") && !gcLower.Contains("shield"))
                    modCount = 1;
                Debug.LogError($"[DROP-WRITEINIT] source=db modCount={modCount}");
            }

            for (int modSlot = 0; modSlot < modCount; modSlot++)
            {
                writer.WriteByte(0x00);
            }

            if (dropGcLookup >= 0)
            {
                var dropWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                var dropResolved = new List<uint>(dropWireMods.Count);
                foreach (var (slot, modRef) in dropWireMods.OrderBy(w => w.Slot))
                {
                    uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                    if (h != 0) dropResolved.Add(h);
                }
                writer.WriteByte((byte)dropResolved.Count);
                foreach (uint h in dropResolved)
                {
                    writer.WriteByte(0x04);
                    writer.WriteUInt32(h);
                    writer.WriteByte(0x00);
                }
                Debug.LogError($"[DROP-WRITEINIT] gcLookupMods={dropResolved.Count} previousEmptyMods=1");
            }
            else
            {
                int dropTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                var dropRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(dropTier);
                if (dropRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                {
                    int effective = GetEffectiveRarity();
                    if (effective > 0 && effective < 5)
                        dropRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                }

                var dropArmorMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                if (dropArmorMods.Count == 0)
                    dropArmorMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, dropRarity.ToString());

                if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && dropArmorMods.Count > 0)
                {
                    var hashes = new List<uint>();
                    var skipped = new List<string>();
                    var emitted = new List<string>();
                    foreach (var (slot, modRef) in dropArmorMods.OrderBy(w => w.Slot))
                    {
                        uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                        if (h != 0) { hashes.Add(h); emitted.Add($"{modRef}#0x{h:X8}"); }
                        else skipped.Add(modRef);
                    }
                    writer.WriteByte((byte)hashes.Count);
                    foreach (uint h in hashes)
                    {
                        writer.WriteByte(0x04);
                        writer.WriteUInt32(h);
                        writer.WriteByte(0x00);
                    }
                    Debug.LogError($"[ITEM-WIRE-MODS] context=drop kind=armor item={GCClass} rarity={dropRarity} mods={hashes.Count}/{dropArmorMods.Count} phase1ModCount={modCount} emitted=[{string.Join(" | ", emitted)}] skipped=[{string.Join(",", skipped)}]");
                }
                else if (dropRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                {
                    writer.WriteByte(0x00);
                }
                else
                {
                    writer.WriteByte(0x01);
                    writer.WriteByte(0xFF);
                    writer.WriteCString(GetModifierGCClass());
                    writer.WriteByte(0x03);
                    writer.WriteByte(0x15);
                    writer.WriteUInt32(0x11111111);
                }
            }

            Debug.LogError($"[DROP-WRITEINIT] end");
        }




        public void WriteInit(LEWriter writer, int playerLevel)
        {
            Debug.LogError($"[GCOBJECT-WRITEINIT] DFCClass={DFCClass} GCClass={GCClass}");
            Debug.LogError($"[WRITEINIT-START] {DFCClass} - {GCClass}");

            string gcCheck = GCClass.ToLower();
            if (gcCheck.Contains("ring") || gcCheck.Contains("amulet"))
            {
                DFCClass = "Item";
            }

            if (DFCClass == "Armor" || DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon" || DFCClass == "Item")
            {
                int startPos = writer.Position;
                Debug.LogError($"[EQUIPMENT-INIT] mode=fullEquipment GCClass={GCClass}");

                writer.WriteByte(0xFF);
                writer.WriteCString(GetPacketGCClass());
                Debug.LogError($"[EQUIPMENT-INIT] Wrote type tag + GCClass: {GCClass}");

                uint equipSlot = GetPropertyUInt32("Slot");
                if (equipSlot == 0)
                {
                    equipSlot = GetEquipmentSlotFromGCClass();
                }
                writer.WriteUInt32(equipSlot);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);

                string gcLower = GCClass.ToLower();
                if (DFCClass == "Item" || gcLower.Contains("ring") || gcLower.Contains("amulet"))
                {
                    bool isAmulet = gcLower.Contains("amulet");

                    if (isAmulet)
                    {
                        bool isMythicAmulet = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicAmulet ? (playerLevel + 3) : GetItemRequiredLevel());

                        int amuletModCount;
                        List<string> amuletMods;

                        if (isMythicAmulet)
                        {
                            amuletMods = DatabaseLoader.GetAmuletModifiers(GCClass);
                            amuletModCount = amuletMods.Count;
                        }
                        else
                        {
                            amuletModCount = 1;
                            amuletMods = new List<string>();
                        }

                        if (!isMythicAmulet)
                        {
                            int eqTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var eqRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(eqTier);
                            if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    eqRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            var pbAmuletWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (pbAmuletWire.Count == 0)
                                pbAmuletWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, eqRarity.ToString());
                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && pbAmuletWire.Count > 0)
                            {
                                foreach (var (_, modRef) in pbAmuletWire.OrderBy(w => w.Slot))
                                    amuletMods.Add(modRef);
                            }
                        }

                        Debug.LogError($"[INIT-AMULET] GCClass: {GCClass}, Level: {itemLevel}, ModCount: {amuletModCount}, Mythic: {isMythicAmulet}, PathBMods: {amuletMods.Count}");

                        writer.WriteByte((byte)itemLevel);

                        if (amuletModCount <= 0 && amuletMods.Count == 0)
                        {
                            return;
                        }

                        if (isMythicAmulet)
                        {
                            writer.WriteByte(0x00);
                        }

                        for (int m = 0; m < amuletModCount; m++)
                        {
                            writer.WriteByte(0x00);
                        }

                        var resolvedAmuletHashes = new List<uint>(amuletMods.Count);
                        foreach (var mod in amuletMods)
                        {
                            uint amuletHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                            if (amuletHash != 0) resolvedAmuletHashes.Add(amuletHash);
                        }
                        writer.WriteByte((byte)resolvedAmuletHashes.Count);
                        foreach (uint amuletHash in resolvedAmuletHashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(amuletHash);
                            writer.WriteByte(0x00);
                        }
                        if (!isMythicAmulet && resolvedAmuletHashes.Count > 0)
                            Debug.LogError($"[ITEM-WIRE-MODS] context=init kind=amulet item={GCClass} mods={resolvedAmuletHashes.Count}");

                        return;
                    }
                    else
                    {
                        bool isMythicRing = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicRing ? (playerLevel + 3) : GetItemRequiredLevel());

                        int ringModCount;
                        List<string> ringMods;

                        if (isMythicRing)
                        {
                            ringMods = DatabaseLoader.GetRingModifiers(GCClass);
                            ringModCount = ringMods.Count;
                        }
                        else
                        {
                            ringModCount = 1;
                            ringMods = new List<string>();
                        }

                        if (!isMythicRing)
                        {
                            int eqTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var eqRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(eqTier);
                            if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    eqRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            var pbRingWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (pbRingWire.Count == 0)
                                pbRingWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, eqRarity.ToString());
                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && pbRingWire.Count > 0)
                            {
                                foreach (var (_, modRef) in pbRingWire.OrderBy(w => w.Slot))
                                    ringMods.Add(modRef);
                            }
                        }

                        Debug.LogError($"[INIT-RING] GCClass: {GCClass}, Level: {itemLevel}, ModCount: {ringModCount}, Mythic: {isMythicRing}, PathBMods: {ringMods.Count}");

                        writer.WriteByte((byte)itemLevel);

                        if (ringModCount <= 0 && ringMods.Count == 0)
                        {
                            return;
                        }

                        if (isMythicRing)
                        {
                            writer.WriteByte(0x00);
                        }

                        for (int m = 0; m < ringModCount; m++)
                        {
                            writer.WriteByte(0x00);
                        }

                        var resolvedRingHashes = new List<uint>(ringMods.Count);
                        foreach (var mod in ringMods)
                        {
                            uint ringHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                            if (ringHash != 0) resolvedRingHashes.Add(ringHash);
                        }
                        writer.WriteByte((byte)resolvedRingHashes.Count);
                        foreach (uint ringHash in resolvedRingHashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(ringHash);
                            writer.WriteByte(0x00);
                        }
                        if (!isMythicRing && resolvedRingHashes.Count > 0)
                            Debug.LogError($"[ITEM-WIRE-MODS] context=init kind=ring item={GCClass} mods={resolvedRingHashes.Count}");

                        return;
                    }
                }

                ItemData itemData = DatabaseLoader.FindItem(GCClass);

                bool isMythic = gcLower.Contains("mythic")
                || DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(
                    gcLower.StartsWith("items.pal.") ? gcLower.Substring(10) : gcLower);
                bool isPartialbuilt = gcLower.Contains("partialbuilt");
                bool isPrebuilt = gcLower.Contains("prebuilt");
                bool isGeneratedboss = gcLower.Contains("generatedboss");
                bool hasMythicSeasonal = gcLower.Contains("mythicseasonal");
                bool hasMythicInName = gcLower.Contains("mythic") && !hasMythicSeasonal;
                bool hasSeasonal = gcLower.Contains("seasonal");
                bool hasWishingwell = gcLower.Contains("wishingwell");
                bool hasUnique = gcLower.Contains("unique");
                bool hasJustSeasonal = hasSeasonal && !gcLower.Contains("mythic") && !hasUnique;
                bool hasUniqueSeasonal = hasUnique && hasSeasonal;

                int level = StoredLevel >= 0 ? StoredLevel : (isMythic ? (playerLevel + 3) : GetItemRequiredLevel());
                writer.WriteByte((byte)level);
                Debug.LogError($"[EQUIPMENT-INIT] Wrote level: {level} (0x{level:X2})");

                bool writeFlag = false;

                if (isMythic && !isPartialbuilt)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasUniqueSeasonal && !gcLower.Contains("fighter"))
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicInName)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasWishingwell && !gcLower.Contains("fighter") && !gcLower.Contains("helm"))
                {
                    writeFlag = true;
                }

                if (!writeFlag && isMythic && GCClass.IndexOf("MythicPAL", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    writeFlag = true;
                }

                if (writeFlag)
                {
                    if ((gcLower.Contains("scale") && !gcLower.Contains("helm") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")) ||
                        (gcLower.Contains("splint") && !gcLower.Contains("shoulder") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")))
                    {
                        Debug.LogError($"[EQUIPMENT-INIT] SKIPPED flag for scale/splint");
                        writeFlag = false;
                    }
                }

                bool isWeapon = (DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon");
                if (writeFlag && (!isWeapon || isMythic))
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[EQUIP-WRITEINIT] Wrote FLAG byte (armor only)");
                }

                int itemModCount;

                int writeInitGcLookup = DungeonRunners.Gameplay.MerchantRuntime.GetOP5ModCount(GCClass);
                if (writeInitGcLookup >= 0)
                {
                    itemModCount = writeInitGcLookup;
                    if (!writeFlag) itemModCount++;
                    Debug.LogError($"[EQUIPMENT-INIT] GCClass={GCClass} itemModCount={itemModCount}");
                }
                else
                {
                    itemModCount = itemData?.modCount ?? 3;
                    if (gcLower.Contains("chain") && !gcLower.Contains("shield"))
                        itemModCount = 1;
                    Debug.LogError($"[EQUIPMENT-INIT] source=db modCount={itemModCount}");
                }

                Debug.LogError($"[EQUIPMENT-INIT] GCClass={GCClass} isMythic={isMythic} isPartialbuilt={isPartialbuilt} isGeneratedboss={isGeneratedboss} hasMythicSeasonal={hasMythicSeasonal} hasSeasonal={hasSeasonal} hasUniqueSeasonal={hasUniqueSeasonal} finalModCount={itemModCount}");

                for (int modSlot = 0; modSlot < itemModCount; modSlot++)
                {
                    writer.WriteByte(0x00);
                }
                Debug.LogError($"[EQUIPMENT-INIT] Wrote {itemModCount} mod slot bytes");

                if (writeInitGcLookup >= 0)
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[EQUIPMENT-INIT] GC lookup item - no ScaleMod");
                }
                else
                {
                    int writeTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                    var writeRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(writeTier);
                    if (writeRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        int effective = GetEffectiveRarity();
                        if (effective > 0 && effective < 5)
                            writeRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                    }

                    var initArmorMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                    if (initArmorMods.Count == 0)
                        initArmorMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, writeRarity.ToString());

                    if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && initArmorMods.Count > 0)
                    {
                        var hashes = new List<uint>();
                        var emitted = new List<string>();
                        var skipped = new List<string>();
                        foreach (var (slot, modRef) in initArmorMods.OrderBy(w => w.Slot))
                        {
                            uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                            if (h != 0) { hashes.Add(h); emitted.Add($"{modRef}#0x{h:X8}"); }
                            else skipped.Add(modRef);
                        }
                        writer.WriteByte((byte)hashes.Count);
                        foreach (uint h in hashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(h);
                            writer.WriteByte(0x00);
                        }
                        Debug.LogError($"[ITEM-WIRE-MODS] context=init kind=armor item={GCClass} rarity={writeRarity} storedRarity={StoredRarity} effective={GetEffectiveRarity()} mods={hashes.Count}/{initArmorMods.Count} phase1ModCount={itemModCount} emitted=[{string.Join(" | ", emitted)}] skipped=[{string.Join(",", skipped)}]");
                    }
                    else if (writeRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        writer.WriteByte(0x00);
                        Debug.LogError($"[EQUIPMENT-INIT] Normal item - no ScaleMod");
                    }
                    else
                    {
                        writer.WriteByte(0x01);
                        writer.WriteByte(0xFF);
                        writer.WriteCString(GetModifierGCClass());
                        Debug.LogError($"[EQUIPMENT-INIT] Writing modifier from GetModifierGCClass() rarity={writeRarity}");
                        writer.WriteByte(0x03);
                        writer.WriteByte(0x15);
                        writer.WriteUInt32(0x11111111);
                    }
                }

                Debug.LogError($"[EQUIPMENT-INIT] Wrote ItemModifier");

                if (DFCClass == "MeleeWeapon" ||
                    GCClass.ToLower().Contains("sword") ||
                    GCClass.ToLower().Contains("axe") ||
                    GCClass.ToLower().Contains("mace") ||
                    GCClass.ToLower().Contains("staff") ||
                    GCClass.ToLower().Contains("pick") ||
                    GCClass.ToLower().Contains("club") ||
                    GCClass.ToLower().Contains("katana") ||
                    GCClass.ToLower().Contains("polearm"))
                {
                    writer.WriteUInt16(0x01);
                    writer.WriteByte(0x02);
                    writer.WriteUInt16(0x00);
                    Debug.LogError($"[EQUIPMENT-INIT] Wrote MeleeWeapon extra bytes");
                }
                else if (DFCClass == "RangedWeapon" ||
                    GCClass.ToLower().Contains("crossbow") ||
                    GCClass.ToLower().Contains("bow") ||
                    GCClass.ToLower().Contains("gun") ||
                    GCClass.ToLower().Contains("cannon"))
                {
                    writer.WriteUInt16(0x0000);
                    writer.WriteUInt16(0x0000);
                    Debug.LogError($"[EQUIPMENT-INIT] Wrote RangedWeapon extra bytes");
                }

                int endPos = writer.Position;
                int eqLen = endPos - startPos;
                var eqBuf = writer.ToArray();
                var eqHex = new System.Text.StringBuilder(eqLen * 3);
                for (int hi = 0; hi < eqLen; hi++)
                {
                    if (hi > 0) eqHex.Append(' ');
                    eqHex.Append(eqBuf[startPos + hi].ToString("X2"));
                }
                Debug.LogError($"[EQUIPMENT-INIT] mode=fullEquipment GCClass={GCClass} bytes={eqLen} hex={eqHex}");
            }
            else if (DFCClass == "ActiveSkill" || DFCClass == "PassiveSkill")
            {
                writer.WriteByte(0x00);
                Debug.LogError($"[SKILL-INIT] GCClass={GCClass}");
            }
            else
            {
                Debug.LogError($"[OTHER-INIT] DFCClass={DFCClass}");
            }
            Debug.LogError($"[WRITEINIT-END] {DFCClass} - {GCClass}");
        }


        public void WriteInitWithoutWeaponBytes(LEWriter writer, int playerLevel)
        {
            Debug.LogError($"[GCOBJECT-WRITEINIT-NO-WEAPON] mode=itemNoWeapon GCClass={GCClass}");

            string gcCheck = GCClass.ToLower();
            if (gcCheck.Contains("ring") || gcCheck.Contains("amulet"))
            {
                DFCClass = "Item";
            }

            if (DFCClass == "Armor" || DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon" || DFCClass == "Item")
            {
                writer.WriteByte(0xFF);
                writer.WriteCString(GetPacketGCClass());

                uint equipSlot = GetPropertyUInt32("Slot");
                if (equipSlot == 0)
                {
                    equipSlot = GetEquipmentSlotFromGCClass();
                }
                writer.WriteUInt32(equipSlot);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);

                string gcLower = GCClass.ToLower();
                if (DFCClass == "Item" || gcLower.Contains("ring") || gcLower.Contains("amulet"))
                {
                    bool isAmulet = gcLower.Contains("amulet");

                    if (isAmulet)
                    {
                        bool isMythicAmulet = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicAmulet ? (playerLevel + 3) : GetItemRequiredLevel());

                        int amuletModCount;
                        List<string> amuletMods;

                        if (isMythicAmulet)
                        {
                            amuletMods = DatabaseLoader.GetAmuletModifiers(GCClass);
                            amuletModCount = amuletMods.Count;
                        }
                        else
                        {
                            amuletModCount = 1;
                            amuletMods = new List<string>();
                        }

                        if (!isMythicAmulet)
                        {
                            int eqTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var eqRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(eqTier);
                            if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    eqRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            var pbAmuletWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (pbAmuletWire.Count == 0)
                                pbAmuletWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, eqRarity.ToString());
                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && pbAmuletWire.Count > 0)
                            {
                                foreach (var (_, modRef) in pbAmuletWire.OrderBy(w => w.Slot))
                                    amuletMods.Add(modRef);
                            }
                        }

                        Debug.LogError($"[NOWEAPON-AMULET] GCClass: {GCClass}, Level: {itemLevel}, ModCount: {amuletModCount}, Mythic: {isMythicAmulet}, PathBMods: {amuletMods.Count}");

                        writer.WriteByte((byte)itemLevel);

                        if (amuletModCount <= 0 && amuletMods.Count == 0)
                        {
                            return;
                        }

                        if (isMythicAmulet)
                        {
                            writer.WriteByte(0x00);
                        }

                        for (int m = 0; m < amuletModCount; m++)
                        {
                            writer.WriteByte(0x00);
                        }

                        var resolvedAmuletHashes = new List<uint>(amuletMods.Count);
                        foreach (var mod in amuletMods)
                        {
                            uint amuletHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                            if (amuletHash != 0) resolvedAmuletHashes.Add(amuletHash);
                        }
                        writer.WriteByte((byte)resolvedAmuletHashes.Count);
                        foreach (uint amuletHash in resolvedAmuletHashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(amuletHash);
                            writer.WriteByte(0x00);
                        }
                        if (!isMythicAmulet && resolvedAmuletHashes.Count > 0)
                            Debug.LogError($"[ITEM-WIRE-MODS] context=noWeapon kind=amulet item={GCClass} mods={resolvedAmuletHashes.Count}");

                        return;
                    }
                    else
                    {
                        bool isMythicRing = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicRing ? (playerLevel + 3) : GetItemRequiredLevel());

                        int ringModCount;
                        List<string> ringMods;

                        if (isMythicRing)
                        {
                            ringMods = DatabaseLoader.GetRingModifiers(GCClass);
                            ringModCount = ringMods.Count;
                        }
                        else
                        {
                            ringModCount = 1;
                            ringMods = new List<string>();
                        }

                        if (!isMythicRing)
                        {
                            int eqTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var eqRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(eqTier);
                            if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    eqRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            var pbRingWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (pbRingWire.Count == 0)
                                pbRingWire = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, eqRarity.ToString());
                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && pbRingWire.Count > 0)
                            {
                                foreach (var (_, modRef) in pbRingWire.OrderBy(w => w.Slot))
                                    ringMods.Add(modRef);
                            }
                        }

                        Debug.LogError($"[NOWEAPON-RING] GCClass: {GCClass}, Level: {itemLevel}, ModCount: {ringModCount}, Mythic: {isMythicRing}, PathBMods: {ringMods.Count}");

                        writer.WriteByte((byte)itemLevel);

                        if (ringModCount <= 0 && ringMods.Count == 0)
                        {
                            return;
                        }

                        if (isMythicRing)
                        {
                            writer.WriteByte(0x00);
                        }

                        for (int m = 0; m < ringModCount; m++)
                        {
                            writer.WriteByte(0x00);
                        }

                        var resolvedRingHashes = new List<uint>(ringMods.Count);
                        foreach (var mod in ringMods)
                        {
                            uint ringHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                            if (ringHash != 0) resolvedRingHashes.Add(ringHash);
                        }
                        writer.WriteByte((byte)resolvedRingHashes.Count);
                        foreach (uint ringHash in resolvedRingHashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(ringHash);
                            writer.WriteByte(0x00);
                        }
                        if (!isMythicRing && resolvedRingHashes.Count > 0)
                            Debug.LogError($"[ITEM-WIRE-MODS] context=noWeapon kind=ring item={GCClass} mods={resolvedRingHashes.Count}");

                        return;
                    }
                }

                ItemData itemData = DatabaseLoader.FindItem(GCClass);

            bool isMythic = gcLower.Contains("mythic")
                || DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(
                    gcLower.StartsWith("items.pal.") ? gcLower.Substring(10) : gcLower);
                bool isPartialbuilt = gcLower.Contains("partialbuilt");
                bool isPrebuilt = gcLower.Contains("prebuilt");
                bool isGeneratedboss = gcLower.Contains("generatedboss");
                bool hasMythicSeasonal = gcLower.Contains("mythicseasonal");
                bool hasMythicInName = gcLower.Contains("mythic") && !hasMythicSeasonal;
                bool hasSeasonal = gcLower.Contains("seasonal");
                bool hasWishingwell = gcLower.Contains("wishingwell");
                bool hasUnique = gcLower.Contains("unique");
                bool hasJustSeasonal = hasSeasonal && !gcLower.Contains("mythic") && !hasUnique;
                bool hasUniqueSeasonal = hasUnique && hasSeasonal;

                int level = StoredLevel >= 0 ? StoredLevel : (isMythic ? (playerLevel + 3) : GetItemRequiredLevel());
                writer.WriteByte((byte)level);

                bool writeFlag = false;

                if (isMythic && !isPartialbuilt)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasUniqueSeasonal && !gcLower.Contains("fighter"))
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicInName)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasWishingwell && !gcLower.Contains("fighter") && !gcLower.Contains("helm"))
                {
                    writeFlag = true;
                }

                if (!writeFlag && isMythic && GCClass.IndexOf("MythicPAL", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    writeFlag = true;
                }

                if (writeFlag)
                {
                    if ((gcLower.Contains("scale") && !gcLower.Contains("helm") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")) ||
                        (gcLower.Contains("splint") && !gcLower.Contains("shoulder") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")))
                    {
                        Debug.LogError($"[WRITEINIT-NO-WEAPON] SKIPPED flag for scale/splint");
                        writeFlag = false;
                    }
                }

                bool isWeapon = (DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon");
                if (writeFlag && (!isWeapon || isMythic))
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[EQUIP-WRITEINIT] Wrote FLAG byte (armor only)");
                }

                int itemModCount;

                int noWepGcLookup = DungeonRunners.Gameplay.MerchantRuntime.GetOP5ModCount(GCClass);
                if (noWepGcLookup >= 0)
                {
                    itemModCount = noWepGcLookup;
                    if (!writeFlag) itemModCount++;
                    Debug.LogError($"[WRITEINIT-NO-WEAPON] GCClass={GCClass} itemModCount={itemModCount}");
                }
                else
                {
                    itemModCount = itemData?.modCount ?? 3;
                    if (gcLower.Contains("chain") && !gcLower.Contains("shield"))
                        itemModCount = 1;
                    Debug.LogError($"[WRITEINIT-NO-WEAPON] source=db modCount={itemModCount}");
                }

                Debug.LogError($"[WRITEINIT-NO-WEAPON] GCClass={GCClass} isMythic={isMythic} isPartialbuilt={isPartialbuilt} isGeneratedboss={isGeneratedboss} hasMythicSeasonal={hasMythicSeasonal} hasSeasonal={hasSeasonal} hasUniqueSeasonal={hasUniqueSeasonal} finalModCount={itemModCount}");

                for (int modSlot = 0; modSlot < itemModCount; modSlot++)
                {
                    writer.WriteByte(0x00);
                }

                if (noWepGcLookup >= 0)
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[WRITEINIT-NO-WEAPON] GC lookup item - no ScaleMod");
                }
                else
                {
                    int noWepTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                    var noWepRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(noWepTier);
                    if (noWepRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        int effective = GetEffectiveRarity();
                        if (effective > 0 && effective < 5)
                            noWepRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                    }

                    var noWepWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                    if (noWepWireMods.Count == 0)
                        noWepWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, noWepRarity.ToString());

                    if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && noWepWireMods.Count > 0)
                    {
                        var hashes = new List<uint>();
                        foreach (var (slot, modRef) in noWepWireMods.OrderBy(w => w.Slot))
                        {
                            uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                            if (h != 0) hashes.Add(h);
                        }
                        writer.WriteByte((byte)hashes.Count);
                        foreach (uint h in hashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(h);
                            writer.WriteByte(0x00);
                        }
                        Debug.LogError($"[ITEM-WIRE-MODS] context=noWeapon kind=armor item={GCClass} rarity={noWepRarity} mods={hashes.Count} phase1ModCount={itemModCount}");
                    }
                    else if (noWepRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        writer.WriteByte(0x00);
                    }
                    else
                    {
                        writer.WriteByte(0x01);
                        writer.WriteByte(0xFF);
                        writer.WriteCString(GetModifierGCClass());
                        writer.WriteByte(0x03);
                        writer.WriteByte(0x15);
                        writer.WriteUInt32(0x11111111);
                    }
                }
                Debug.LogError($"[EQUIPMENT-INIT-NO-WEAPON] mode=itemNoWeapon GCClass={GCClass}");
            }
            else
            {
                WriteInit(writer, playerLevel);
            }
        }



        public void WriteInitForInventory(LEWriter writer, byte posX, byte posY, uint inventorySlot, int playerLevel)
        {
            Debug.LogError($"[GC-DETAIL] GCClass='{GCClass}'");
            Debug.LogError($"[INVENTORY-WRITEINIT] Writing item at position ({posX}, {posY}), slot={inventorySlot}: {GCClass}");

            string gcCheck = GCClass.ToLower();
            if (gcCheck.Contains("ring") || gcCheck.Contains("amulet"))
            {
                DFCClass = "Item";
            }

            if (DFCClass == "Armor" || DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon" || DFCClass == "Item")
            {
                writer.WriteByte(0xFF);
                writer.WriteCString(GetPacketGCClass());
                writer.WriteUInt32(inventorySlot);
                writer.WriteByte(posX);
                writer.WriteByte(posY);
                writer.WriteByte(0x01);

                string gcLower = GCClass.ToLower();
                if (DFCClass == "Item" || gcLower.Contains("ring") || gcLower.Contains("amulet"))

                {
                    bool isAmulet = gcLower.Contains("amulet");

                    if (isAmulet)
                    {
                        bool isMythicAmulet = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicAmulet ? playerLevel : GetItemRequiredLevel());

                        writer.WriteByte((byte)itemLevel);

                        if (isMythicAmulet)
                        {
                            var amuletMods = DatabaseLoader.GetAmuletModifiers(GCClass);
                            int amuletModCount = amuletMods.Count;
                            Debug.LogError($"[INV-AMULET-MYTHIC] GCClass: {GCClass}, Level: {itemLevel}, Placeholders: {amuletModCount}");

                            writer.WriteByte(0x00);
                            for (int m = 0; m < amuletModCount; m++) writer.WriteByte(0x00);

                            var resolvedAmuletHashes = new List<uint>(amuletMods.Count);
                            foreach (var mod in amuletMods)
                            {
                                uint amuletHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                                if (amuletHash != 0) resolvedAmuletHashes.Add(amuletHash);
                            }
                            writer.WriteByte((byte)resolvedAmuletHashes.Count);
                            foreach (uint amuletHash in resolvedAmuletHashes)
                            {
                                writer.WriteByte(0x04);
                                writer.WriteUInt32(amuletHash);
                                writer.WriteByte(0x00);
                            }
                        }
                        else
                        {
                            int invTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var invRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(invTier);
                            if (invRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    invRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            Debug.LogError($"[INV-AMULET-NONMYTHIC] GCClass: {GCClass}, Level: {itemLevel}, Rarity: {invRarity}");
                            writer.WriteByte(0x00);

                            var amuletWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (amuletWireMods.Count == 0)
                                amuletWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, invRarity.ToString());

                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && amuletWireMods.Count > 0)
                            {
                                var hashes = new List<uint>();
                                foreach (var (slot, modRef) in amuletWireMods.OrderBy(w => w.Slot))
                                {
                                    uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                                    if (h != 0) hashes.Add(h);
                                }
                                writer.WriteByte((byte)hashes.Count);
                                foreach (uint h in hashes)
                                {
                                    writer.WriteByte(0x04);
                                    writer.WriteUInt32(h);
                                    writer.WriteByte(0x00);
                                }
                                Debug.LogError($"[ITEM-WIRE-MODS] context=inventory kind=amulet item={GCClass} rarity={invRarity} mods={hashes.Count}");
                            }
                            else if (invRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                writer.WriteByte(0x00);
                            }
                            else
                            {
                                writer.WriteByte(0x01);
                                writer.WriteByte(0xFF);
                                writer.WriteCString(GetModifierGCClass());
                                writer.WriteByte(0x03);
                                writer.WriteByte(0x15);
                                writer.WriteUInt32(0x11111111);
                            }
                        }
                        return;
                    }
                    else
                    {
                        bool isMythicRing = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicRing ? playerLevel : GetItemRequiredLevel());

                        writer.WriteByte((byte)itemLevel);

                        if (isMythicRing)
                        {
                            var ringMods = DatabaseLoader.GetRingModifiers(GCClass);
                            int ringModCount = ringMods.Count;
                            Debug.LogError($"[INV-RING-MYTHIC] GCClass: {GCClass}, Level: {itemLevel}, Placeholders: {ringModCount}");

                            writer.WriteByte(0x00);
                            for (int m = 0; m < ringModCount; m++) writer.WriteByte(0x00);

                            var resolvedRingHashes = new List<uint>(ringMods.Count);
                            foreach (var mod in ringMods)
                            {
                                uint ringHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                                if (ringHash != 0) resolvedRingHashes.Add(ringHash);
                            }
                            writer.WriteByte((byte)resolvedRingHashes.Count);
                            foreach (uint ringHash in resolvedRingHashes)
                            {
                                writer.WriteByte(0x04);
                                writer.WriteUInt32(ringHash);
                                writer.WriteByte(0x00);
                            }
                        }
                        else
                        {
                            int invTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var invRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(invTier);
                            if (invRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    invRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            Debug.LogError($"[INV-RING-NONMYTHIC] GCClass: {GCClass}, Level: {itemLevel}, Rarity: {invRarity}");
                            writer.WriteByte(0x00);

                            var ringWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (ringWireMods.Count == 0)
                                ringWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, invRarity.ToString());

                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && ringWireMods.Count > 0)
                            {
                                var hashes = new List<uint>();
                                foreach (var (slot, modRef) in ringWireMods.OrderBy(w => w.Slot))
                                {
                                    uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                                    if (h != 0) hashes.Add(h);
                                }
                                writer.WriteByte((byte)hashes.Count);
                                foreach (uint h in hashes)
                                {
                                    writer.WriteByte(0x04);
                                    writer.WriteUInt32(h);
                                    writer.WriteByte(0x00);
                                }
                                Debug.LogError($"[ITEM-WIRE-MODS] context=inventory kind=ring item={GCClass} rarity={invRarity} mods={hashes.Count}");
                            }
                            else if (invRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                writer.WriteByte(0x00);
                            }
                            else
                            {
                                writer.WriteByte(0x01);
                                writer.WriteByte(0xFF);
                                writer.WriteCString(GetModifierGCClass());
                                writer.WriteByte(0x03);
                                writer.WriteByte(0x15);
                                writer.WriteUInt32(0x11111111);
                            }
                        }
                        return;
                    }
                }

                ItemData itemData = DatabaseLoader.FindItem(GCClass);

            bool isMythic = gcLower.Contains("mythic")
                || DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(
                    gcLower.StartsWith("items.pal.") ? gcLower.Substring(10) : gcLower);
                bool isPartialbuilt = gcLower.Contains("partialbuilt");
                bool isPrebuilt = gcLower.Contains("prebuilt");
                bool isGeneratedboss = gcLower.Contains("generatedboss");
                bool hasMythicSeasonal = gcLower.Contains("mythicseasonal");
                bool hasMythicInName = gcLower.Contains("mythic") && !hasMythicSeasonal;
                bool hasSeasonal = gcLower.Contains("seasonal");
                bool hasWishingwell = gcLower.Contains("wishingwell");
                bool hasUnique = gcLower.Contains("unique");
                bool hasJustSeasonal = hasSeasonal && !gcLower.Contains("mythic") && !hasUnique;
                bool hasUniqueSeasonal = hasUnique && hasSeasonal;

                int level = StoredLevel >= 0 ? StoredLevel : playerLevel;
                writer.WriteByte((byte)level);

                bool writeFlag = false;

                if (isMythic && !isPartialbuilt)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasUniqueSeasonal && !gcLower.Contains("fighter"))
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicInName)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasWishingwell && !gcLower.Contains("fighter") && !gcLower.Contains("helm"))
                {
                    writeFlag = true;
                }

                if (!writeFlag && isMythic && GCClass.IndexOf("MythicPAL", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    writeFlag = true;
                }

                if (writeFlag)
                {
                    if ((gcLower.Contains("scale") && !gcLower.Contains("helm") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")) ||
                        (gcLower.Contains("splint") && !gcLower.Contains("shoulder") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")))
                    {
                        Debug.LogError($"[INVENTORY-WRITEINIT] SKIPPED flag for scale/splint");
                        writeFlag = false;
                    }
                }

                bool isWeapon = (DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon");
                if (writeFlag && (!isWeapon || isMythic))
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[EQUIP-WRITEINIT] Wrote FLAG byte (armor only)");
                }

                int itemModCount;

                int invGcLookup = DungeonRunners.Gameplay.MerchantRuntime.GetOP5ModCount(GCClass);
                if (invGcLookup >= 0)
                {
                    itemModCount = invGcLookup;
                    if (!writeFlag) itemModCount++;
                    Debug.LogError($"[INVENTORY-WRITEINIT] GCClass={GCClass} itemModCount={itemModCount}");
                }
                else
                {
                    itemModCount = itemData?.modCount ?? 3;
                    if (gcLower.Contains("chain") && !gcLower.Contains("shield"))
                        itemModCount = 1;
                    Debug.LogError($"[INVENTORY-ITEM] source=db modCount={itemModCount}");
                }

                Debug.LogError($"[INVENTORY-WRITEINIT] finalModCount={itemModCount}");

                for (int modSlot = 0; modSlot < itemModCount; modSlot++)
                {
                    writer.WriteByte(0x00);
                }

                if (invGcLookup >= 0)
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[INVENTORY-WRITEINIT] GC lookup item - no ScaleMod (mods baked in GC class)");
                }
                else
                {
                    int invTier2 = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                    var invRarity2 = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(invTier2);
                    if (invRarity2 == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        int effective = GetEffectiveRarity();
                        if (effective > 0 && effective < 5)
                            invRarity2 = (DungeonRunners.Gameplay.ItemRarity)effective;
                    }

                    var armorWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                    if (armorWireMods.Count == 0)
                        armorWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, invRarity2.ToString());

                    if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && armorWireMods.Count > 0)
                    {
                        var hashes = new List<uint>();
                        var skipped = new List<string>();
                        var emitted = new List<string>();
                        foreach (var (slot, modRef) in armorWireMods.OrderBy(w => w.Slot))
                        {
                            uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                            if (h != 0) { hashes.Add(h); emitted.Add($"{modRef}#0x{h:X8}"); }
                            else skipped.Add(modRef);
                        }
                        writer.WriteByte((byte)hashes.Count);
                        foreach (uint h in hashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(h);
                            writer.WriteByte(0x00);
                        }
                        Debug.LogError($"[ITEM-WIRE-MODS] context=inventory kind=armor item={GCClass} rarity={invRarity2} mods={hashes.Count}/{armorWireMods.Count} phase1ModCount={itemModCount} emitted=[{string.Join(" | ", emitted)}] skipped=[{string.Join(",", skipped)}]");
                    }
                    else if (invRarity2 == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        writer.WriteByte(0x00);
                    }
                    else
                    {
                        writer.WriteByte(0x01);
                        writer.WriteByte(0xFF);
                        writer.WriteCString(GetModifierGCClass());
                        writer.WriteByte(0x03);
                        writer.WriteByte(0x15);
                        writer.WriteUInt32(0x11111111);
                    }
                }


                Debug.LogError($"[INVENTORY-WRITEINIT] GCClass={GCClass}");
            }
            else
            {
                WriteInit(writer, playerLevel);
            }
        }


        public void WriteInitForEquip(LEWriter writer, int playerLevel)
        {
            Debug.LogError($"[EQUIP-WRITEINIT] Writing equipped item: {GCClass}");

            string gcCheck = GCClass.ToLower();
            if (gcCheck.Contains("ring") || gcCheck.Contains("amulet"))
            {
                DFCClass = "Item";
            }

            if (DFCClass == "Armor" || DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon" || DFCClass == "Item")
            {
                writer.WriteByte(0xFF);
                writer.WriteCString(GetPacketGCClass());

                uint equipSlot = TargetSlot ?? GetPropertyUInt32("Slot");
                if (equipSlot == 0)
                {
                    equipSlot = GetEquipmentSlotFromGCClass();
                }
                writer.WriteUInt32(equipSlot);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);

                string gcLower = GCClass.ToLower();
                if (DFCClass == "Item" || gcLower.Contains("ring") || gcLower.Contains("amulet"))
                {
                    bool isAmulet = gcLower.Contains("amulet");

                    if (isAmulet)
                    {
                        bool isMythicAmulet = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicAmulet ? (playerLevel + 3) : GetItemRequiredLevel());
                        writer.WriteByte((byte)itemLevel);

                        if (isMythicAmulet)
                        {
                            var amuletMods = DatabaseLoader.GetAmuletModifiers(GCClass);
                            int amuletModCount = amuletMods.Count;
                            Debug.LogError($"[EQUIP-AMULET-MYTHIC] GCClass: {GCClass}, Slot: {equipSlot}, Level: {itemLevel}, Placeholders: {amuletModCount}");
                            writer.WriteByte(0x00);
                            for (int m = 0; m < amuletModCount; m++) writer.WriteByte(0x00);
                            var resolvedAmuletHashes = new List<uint>(amuletMods.Count);
                            foreach (var mod in amuletMods)
                            {
                                uint amuletHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                                if (amuletHash != 0) resolvedAmuletHashes.Add(amuletHash);
                            }
                            writer.WriteByte((byte)resolvedAmuletHashes.Count);
                            foreach (uint amuletHash in resolvedAmuletHashes)
                            {
                                writer.WriteByte(0x04);
                                writer.WriteUInt32(amuletHash);
                                writer.WriteByte(0x00);
                            }
                        }
                        else
                        {
                            int eqTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var eqRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(eqTier);
                            if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    eqRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            Debug.LogError($"[EQUIP-AMULET-NONMYTHIC] GCClass: {GCClass}, Slot: {equipSlot}, Level: {itemLevel}, Rarity: {eqRarity}");
                            writer.WriteByte(0x00);

                            var eqAmuletMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (eqAmuletMods.Count == 0)
                                eqAmuletMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, eqRarity.ToString());

                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && eqAmuletMods.Count > 0)
                            {
                                var hashes = new List<uint>();
                                foreach (var (slot, modRef) in eqAmuletMods.OrderBy(w => w.Slot))
                                {
                                    uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                                    if (h != 0) hashes.Add(h);
                                }
                                writer.WriteByte((byte)hashes.Count);
                                foreach (uint h in hashes)
                                {
                                    writer.WriteByte(0x04);
                                    writer.WriteUInt32(h);
                                    writer.WriteByte(0x00);
                                }
                                Debug.LogError($"[ITEM-WIRE-MODS] context=equip kind=amulet item={GCClass} rarity={eqRarity} mods={hashes.Count}");
                            }
                            else if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                writer.WriteByte(0x00);
                            }
                            else
                            {
                                writer.WriteByte(0x01);
                                writer.WriteByte(0xFF);
                                writer.WriteCString(GetModifierGCClass());
                                writer.WriteByte(0x03);
                                writer.WriteByte(0x15);
                                writer.WriteUInt32(0x11111111);
                            }
                        }
                        return;
                    }
                    else
                    {
                        bool isMythicRing = gcLower.Contains("mythic");
                        int itemLevel = StoredLevel >= 0 ? StoredLevel : (isMythicRing ? (playerLevel + 3) : GetItemRequiredLevel());
                        writer.WriteByte((byte)itemLevel);

                        if (isMythicRing)
                        {
                            var ringMods = DatabaseLoader.GetRingModifiers(GCClass);
                            int ringModCount = ringMods.Count;
                            Debug.LogError($"[EQUIP-RING-MYTHIC] GCClass: {GCClass}, Slot: {equipSlot}, Level: {itemLevel}, Placeholders: {ringModCount}");
                            writer.WriteByte(0x00);
                            for (int m = 0; m < ringModCount; m++) writer.WriteByte(0x00);
                            var resolvedRingHashes = new List<uint>(ringMods.Count);
                            foreach (var mod in ringMods)
                            {
                                uint ringHash = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(mod);
                                if (ringHash != 0) resolvedRingHashes.Add(ringHash);
                            }
                            writer.WriteByte((byte)resolvedRingHashes.Count);
                            foreach (uint ringHash in resolvedRingHashes)
                            {
                                writer.WriteByte(0x04);
                                writer.WriteUInt32(ringHash);
                                writer.WriteByte(0x00);
                            }
                        }
                        else
                        {
                            int eqTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                            var eqRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(eqTier);
                            if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                int effective = GetEffectiveRarity();
                                if (effective > 0 && effective < 5)
                                    eqRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                            }
                            Debug.LogError($"[EQUIP-RING-NONMYTHIC] GCClass: {GCClass}, Slot: {equipSlot}, Level: {itemLevel}, Rarity: {eqRarity}");
                            writer.WriteByte(0x00);

                            var eqRingMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                            if (eqRingMods.Count == 0)
                                eqRingMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, eqRarity.ToString());

                            if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && eqRingMods.Count > 0)
                            {
                                var hashes = new List<uint>();
                                foreach (var (slot, modRef) in eqRingMods.OrderBy(w => w.Slot))
                                {
                                    uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                                    if (h != 0) hashes.Add(h);
                                }
                                writer.WriteByte((byte)hashes.Count);
                                foreach (uint h in hashes)
                                {
                                    writer.WriteByte(0x04);
                                    writer.WriteUInt32(h);
                                    writer.WriteByte(0x00);
                                }
                                Debug.LogError($"[ITEM-WIRE-MODS] context=equip kind=ring item={GCClass} rarity={eqRarity} mods={hashes.Count}");
                            }
                            else if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                            {
                                writer.WriteByte(0x00);
                            }
                            else
                            {
                                writer.WriteByte(0x01);
                                writer.WriteByte(0xFF);
                                writer.WriteCString(GetModifierGCClass());
                                writer.WriteByte(0x03);
                                writer.WriteByte(0x15);
                                writer.WriteUInt32(0x11111111);
                            }
                        }
                        return;
                    }
                }

                ItemData itemData = DatabaseLoader.FindItem(GCClass);

            bool isMythic = gcLower.Contains("mythic")
                || DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(
                    gcLower.StartsWith("items.pal.") ? gcLower.Substring(10) : gcLower);
                bool isPartialbuilt = gcLower.Contains("partialbuilt");
                bool isPrebuilt = gcLower.Contains("prebuilt");
                bool isGeneratedboss = gcLower.Contains("generatedboss");
                bool hasMythicSeasonal = gcLower.Contains("mythicseasonal");
                bool hasMythicInName = gcLower.Contains("mythic") && !hasMythicSeasonal;
                bool hasSeasonal = gcLower.Contains("seasonal");
                bool hasWishingwell = gcLower.Contains("wishingwell");
                bool hasUnique = gcLower.Contains("unique");
                bool hasJustSeasonal = hasSeasonal && !gcLower.Contains("mythic") && !hasUnique;
                bool hasUniqueSeasonal = hasUnique && hasSeasonal;

                int level = StoredLevel >= 0 ? StoredLevel : (isMythic ? (playerLevel + 3) : GetItemRequiredLevel());
                writer.WriteByte((byte)level);

                bool writeFlag = false;

                if (isMythic && !isPartialbuilt)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPartialbuilt && hasUniqueSeasonal && !gcLower.Contains("fighter"))
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicInName)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasMythicSeasonal)
                {
                    writeFlag = true;
                }
                else if (isPrebuilt && hasWishingwell && !gcLower.Contains("fighter") && !gcLower.Contains("helm"))
                {
                    writeFlag = true;
                }

                if (!writeFlag && isMythic && GCClass.IndexOf("MythicPAL", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    writeFlag = true;
                }

                if (writeFlag)
                {
                    if ((gcLower.Contains("scale") && !gcLower.Contains("helm") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")) ||
                        (gcLower.Contains("splint") && !gcLower.Contains("shoulder") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")))
                    {
                        Debug.LogError($"[EQUIP-WRITEINIT] SKIPPED flag for scale/splint");
                        writeFlag = false;
                    }
                }

                bool isWeapon = (DFCClass == "MeleeWeapon" || DFCClass == "RangedWeapon");
                if (writeFlag && (!isWeapon || isMythic))
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[EQUIP-WRITEINIT] Wrote FLAG byte (armor only)");
                }

                int itemModCount;

                int equipGcLookup = DungeonRunners.Gameplay.MerchantRuntime.GetOP5ModCount(GCClass);
                if (equipGcLookup >= 0)
                {
                    itemModCount = equipGcLookup;
                    if (!writeFlag) itemModCount++;
                    Debug.LogError($"[EQUIP-WRITEINIT] GCClass={GCClass} itemModCount={itemModCount}");
                }
                else
                {
                    itemModCount = itemData?.modCount ?? 3;
                    if (gcLower.Contains("chain") && !gcLower.Contains("shield"))
                        itemModCount = 1;
                    Debug.LogError($"[EQUIPMENT-WRITE] source=db modCount={itemModCount}");
                }

                Debug.LogError($"[EQUIP-WRITEINIT] finalModCount={itemModCount}");

                for (int modSlot = 0; modSlot < itemModCount; modSlot++)
                {
                    writer.WriteByte(0x00);
                }

                if (equipGcLookup >= 0)
                {
                    writer.WriteByte(0x00);
                    Debug.LogError($"[EQUIP-WRITEINIT] GC lookup item - no ScaleMod");
                }
                else
                {
                    int eqTier = DungeonRunners.Gameplay.RPGSettings.GetTierFromGcType(GCClass);
                    var eqRarity = DungeonRunners.Gameplay.RPGSettings.GetRarityFromTier(eqTier);
                    if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        int effective = GetEffectiveRarity();
                        if (effective > 0 && effective < 5)
                            eqRarity = (DungeonRunners.Gameplay.ItemRarity)effective;
                    }

                    var eqArmorMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(GCClass);
                    if (eqArmorMods.Count == 0)
                        eqArmorMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(GCClass, eqRarity.ToString());

                    if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && eqArmorMods.Count > 0)
                    {
                        var hashes = new List<uint>();
                        foreach (var (slot, modRef) in eqArmorMods.OrderBy(w => w.Slot))
                        {
                            uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                            if (h != 0) hashes.Add(h);
                        }
                        writer.WriteByte((byte)hashes.Count);
                        foreach (uint h in hashes)
                        {
                            writer.WriteByte(0x04);
                            writer.WriteUInt32(h);
                            writer.WriteByte(0x00);
                        }
                        Debug.LogError($"[ITEM-WIRE-MODS] context=equip kind=armor item={GCClass} rarity={eqRarity} mods={hashes.Count} phase1ModCount={itemModCount}");
                    }
                    else if (eqRarity == DungeonRunners.Gameplay.ItemRarity.Normal)
                    {
                        writer.WriteByte(0x00);
                    }
                    else
                    {
                        writer.WriteByte(0x01);
                        writer.WriteByte(0xFF);
                        writer.WriteCString(GetModifierGCClass());
                        writer.WriteByte(0x03);
                        writer.WriteByte(0x15);
                        writer.WriteUInt32(0x11111111);
                    }
                }

                if (DFCClass == "MeleeWeapon" ||
                    GCClass.ToLower().Contains("sword") ||
                    GCClass.ToLower().Contains("axe") ||
                    GCClass.ToLower().Contains("mace") ||
                    GCClass.ToLower().Contains("staff") ||
                    GCClass.ToLower().Contains("pick") ||
                    GCClass.ToLower().Contains("club") ||
                    GCClass.ToLower().Contains("katana") ||
                    GCClass.ToLower().Contains("polearm"))
                {
                    writer.WriteUInt16(0x01);
                    writer.WriteByte(0x02);
                    writer.WriteUInt16(0x00);
                    Debug.LogError($"[EQUIP-WRITEINIT] Wrote MeleeWeapon extra bytes");
                }
                else if (DFCClass == "RangedWeapon" ||
                    GCClass.ToLower().Contains("crossbow") ||
                    GCClass.ToLower().Contains("bow") ||
                    GCClass.ToLower().Contains("gun") ||
                    GCClass.ToLower().Contains("cannon"))
                {
                    writer.WriteUInt16(0x0000);
                    writer.WriteUInt16(0x0000);
                    Debug.LogError($"[EQUIP-WRITEINIT] Wrote RangedWeapon extra bytes");
                }

                Debug.LogError($"[EQUIP-WRITEINIT] GCClass={GCClass}");
            }
            else
            {
                WriteInit(writer, playerLevel);
            }
        }

        public void WriteInitForDrop(LEWriter writer)
        {
            writer.WriteByte(0xFF);
            writer.WriteCString(GetPacketGCClass());
        }

        public void WriteData(LEWriter writer)
        {
            if (DFCClass == "ActiveSkill")
            {
                uint skillSlot = GetPropertyUInt32("SkillSlot");
                if (skillSlot == 0)
                {
                    skillSlot = 100;
                    Debug.LogError($"[SKILL-DATA] missing=SkillSlot default=100");
                }

                writer.WriteUInt32(skillSlot);
                writer.WriteByte(0x01);
                Debug.LogError($"[SKILL-DATA] slot={skillSlot} level=1");
            }
            else
            {
                Debug.LogError($"[WRITE-DATA] nonSkill DFCClass={DFCClass}");
            }
        }

        private uint GetPropertyUInt32(string propertyName)
        {
            var prop = Properties?.FirstOrDefault(p => p.Name == propertyName);
            if (prop is UInt32Property uintProp)
            {
                return uintProp.Value;
            }
            return 0;
        }

        public uint GetEquipmentSlotFromGCClass()
        {
            if (TargetSlot.HasValue)
                return TargetSlot.Value;

            if (TryResolveEquipmentSlotType(GCClass, out uint slotType))
                return slotType;

            string lower = GCClass.ToLower();
            if (lower.Contains("helm")) return 5;
            if (lower.Contains("shoulder") || lower.Contains("pauldron")) return 8;
            if (lower.Contains("armor") || lower.Contains("body") || lower.Contains("chest")) return 6;
            if (lower.Contains("gloves")) return 2;
            if (lower.Contains("boots")) return 7;
            if (lower.Contains("shield")) return 11;
            if (lower.Contains("ring")) return 3;
            if (lower.Contains("amulet")) return 1;
            if (lower.Contains("weapon") || lower.Contains("sword") || lower.Contains("axe") ||
         lower.Contains("mace") || lower.Contains("staff") || lower.Contains("pick") || lower.Contains("club") || lower.Contains("katana") || lower.Contains("polearm")) return 10;
            return 10;
        }

        public static bool TryResolveEquipmentSlotType(string gcClass, out uint slotType)
        {
            slotType = 0;
            GCNode desc = ResolveItemDescription(gcClass);
            if (desc == null) return false;
            int value = desc.GetInt("SlotType", 0);
            if (value <= 0) return false;
            slotType = (uint)value;
            return true;
        }

        public static bool TryResolveWeaponClass(string gcClass, out string weaponClass)
        {
            weaponClass = string.Empty;
            GCNode desc = ResolveItemDescription(gcClass);
            if (desc == null) return false;
            string value = desc.GetString("WeaponClass", string.Empty);
            if (string.IsNullOrWhiteSpace(value)) return false;
            weaponClass = value.Trim();
            return true;
        }

        public static GCNode ResolveItemDescription(string gcClass)
        {
            if (string.IsNullOrWhiteSpace(gcClass)) return null;
            if (GCDatabase.Instance == null || !GCDatabase.Instance.IsLoaded) return null;
            GCNode node = GCDatabase.Instance.ResolveWithInheritance(gcClass);
            if (node == null)
                node = GCDatabase.Instance.ResolveWithInheritance(GetPacketGCClassFor(gcClass));
            if (node == null) return null;
            return node.GetChild("Description") ?? node;
        }

        public int GetItemRequiredLevel()
        {
            return ItemData.GetRequiredLevelFromGCClass(GCClass);
        }

        public static uint HashDjb2(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 5381;
            }

            uint hash = 5381;
            string lower = s.ToLowerInvariant();

            foreach (char c in lower)
            {
                hash = ((hash << 5) + hash) + (uint)c;
            }

            return hash & 0xFFFFFFFF;
        }

    }

    public abstract class GCObjectProperty
    {
        public string Name { get; set; } = "";
        public abstract void Serialize(System.IO.BinaryWriter writer);
        public abstract void WriteDFC(LEWriter writer);
    }

    public class StringProperty : GCObjectProperty
    {
        public string Value { get; set; } = "";

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            foreach (var b in Encoding.UTF8.GetBytes(Name))
                writer.Write(b);
            writer.Write((byte)0);
            writer.Write((byte)1);
            foreach (var b in Encoding.UTF8.GetBytes(Value))
                writer.Write(b);
            writer.Write((byte)0);
        }

        public override void WriteDFC(LEWriter writer)
        {
            uint nameHash = GCObject.HashDjb2(Name);
            writer.WriteUInt32(nameHash);
            writer.WriteCString(Value);
        }
    }

    public class UInt32Property : GCObjectProperty
    {
        public uint Value { get; set; }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            foreach (var b in Encoding.UTF8.GetBytes(Name))
                writer.Write(b);
            writer.Write((byte)0);
            writer.Write((byte)2);
            writer.Write(Value);
        }

        public override void WriteDFC(LEWriter writer)
        {
            uint nameHash = GCObject.HashDjb2(Name);
            writer.WriteUInt32(nameHash);
            writer.WriteUInt32(Value);
        }
    }
}
