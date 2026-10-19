using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using HarmonyLib;
using Il2CppI2.Loc;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppNewtonsoft.Json.Linq;
using Il2CppVampireSurvivors.App.Data.Adventures;
using Il2CppVampireSurvivors.App.Scripts.Framework.Adventures;
using Il2CppVampireSurvivors.App.Scripts.UI;
using Il2CppVampireSurvivors.Objects;
using Il2CppVampireSurvivors.App.UI;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.Framework;
using Il2CppVampireSurvivors.Framework.RuntimeContent;
using Il2CppVampireSurvivors.Framework.Workshop;
using Il2CppVampireSurvivors.Graphics;
using Il2CppVampireSurvivors.Objects.Weapons;
using Il2CppVampireSurvivors.UI;
using UnityEngine;
using CharacterController = Il2CppVampireSurvivors.Objects.Characters.CharacterController;

namespace VampireRounds.Vs
{
    /// <summary>
    /// The Vampire Rounds adventure, its stage, Orange and Blue and their texts and pictures, put into VS's own data
    /// (sheets: adventure, stages, characters, texts; hooks h_adventure_*, h_character*, h_weapon_prefab). The stage is the
    /// player's own Mad Forest data, copied in memory under a free adventure stage slot; nothing is written to VS's files.
    /// </summary>
    public static class Content
    {
        public const AdventureType Adventure = AdventureType.ADV_LIB_001;
        public const StageSetType StageSet = StageSetType.STAGE_DATA_ADV_BMN002;
        public const WeaponType GunSlot = WeaponType.___PLCHLD200;
        public const string Texture = "VampireRounds";
        public const float SpritePpu = 100f;

        /// <summary>Runtime CharacterType per characters row id, and back.</summary>
        public static readonly Dictionary<string, CharacterType> Types = new Dictionary<string, CharacterType>();
        public static readonly Dictionary<CharacterType, CharacterRow> Rows = new Dictionary<CharacterType, CharacterRow>();
        private static readonly Dictionary<CharacterType, CharacterController> prefabs = new Dictionary<CharacterType, CharacterController>();
        private static readonly Texture2D[] portraitTex = new Texture2D[ArenaLink.Seats];
        private static readonly DateTime[] portraitStamp = new DateTime[ArenaLink.Seats];
        public static bool AdventureInData { get; private set; }
        private static bool missingLogged, presentLogged;

        /// <summary>VS is in the Vampire Rounds adventure (its menus or a run).</summary>
        public static bool InOurAdventure
        {
            get
            {
                try { return AdventureManager.IsInAdventureMode && AdventureManager._adventureManagerInstance != null && AdventureManager._adventureManagerInstance.CurrentAdventure == Adventure; }
                catch { return false; }
            }
        }

        public static bool IsOurs(CharacterType t) => Rows.ContainsKey(t);
        public static int SeatOf(CharacterType t) => Rows.TryGetValue(t, out var r) ? r.Seat : -1;
        public static bool IsOurs(CharacterController c) => c != null && IsOurs(c.CharacterType);

        // ------------------------------------------------------------------ data (h_adventure_json, h_adventure_roster)

        [HarmonyPatch(typeof(DataManager), nameof(DataManager.LoadBaseJObjects))]
        public static class InjectJsonPatch
        {
            public static void Postfix(DataManager __instance) => InjectJson(__instance);
        }

        public static void InjectJson(DataManager dm)
        {
            try
            {
                var adv = Sheets.Adventures[0];
                string advKey = Adventure.ToString(), setKey = StageSet.ToString();
                if (dm._allAdventuresJson[advKey] != null && !AdventureInData) { Mod.Warn("content: VS already uses " + advKey + "; the Vampire Rounds adventure stays out"); return; }
                // The stages: the player's own stage data, stored again under a free adventure stage slot.
                var setNames = new List<string>();
                foreach (var row in Sheets.Stages.OrderBy(r => r.Order))
                {
                    var src = dm._allStagesJson[row.VsStage];
                    if (src == null) { Mod.Warn("content: VS has no stage " + row.VsStage); return; }
                    var arr = JsonNode.Parse(src.ToString()).AsArray();
                    var first = arr[0].AsObject();
                    first["stageKey"] = row.Slot;
                    first["tilesetStageType"] = row.VsStage;
                    first["hidden"] = false;
                    first["unlocked"] = row.UnlockedBy == "start";
                    first["order"] = row.Order;
                    first["stageNumber"] = "Chapter " + row.Order;
                    first["frameNameUnlock"] = "icon_chapter" + Math.Min(row.Order, 6);
                    var mods = first["mods"] as JsonObject;
                    if (mods != null) mods["TimeLimit"] = row.Minutes * 60;
                    dm._allAdventureStagesJson[row.Slot] = JArray.Parse(arr.ToJsonString());
                    setNames.Add(row.Slot);
                }
                dm._allStageSetJson[setKey] = JArray.Parse(new JsonArray(setNames.Select(n => (JsonNode)JsonValue.Create(n)).ToArray()).ToJsonString());
                // The ROUNDS gun's VS weapon slot: the Whip's data under a placeholder type (taken away before a run starts).
                var whip = JsonNode.Parse(dm._allWeaponDataJson["WHIP"].ToString()).AsArray();
                var gun = new JsonArray();
                var lvl1 = whip[0].AsObject();
                lvl1["bulletType"] = GunSlot.ToString();
                lvl1["name"] = "ROUNDS Gun";
                lvl1["description"] = "ROUNDS's own gun.";
                lvl1["texture"] = Texture;
                lvl1["frameName"] = "gun_icon.png";
                lvl1.Remove("evoInto");
                lvl1.Remove("evoSynergy");
                lvl1["rarity"] = 0;
                gun.Add(JsonNode.Parse(lvl1.ToJsonString()));
                dm._allWeaponDataJson[GunSlot.ToString()] = JArray.Parse(gun.ToJsonString());
                // The adventure. Its roster names a stand-in until Orange and Blue have runtime types (FixAdventure).
                string first0 = Sheets.Stages.OrderBy(r => r.Order).First().Slot;
                var stage1 = Sheets.Stages.OrderBy(r => r.Order).First();
                var json = new JsonObject
                {
                    ["progressKey"] = adv.ProgressKey,
                    ["index"] = -1,
                    ["coreAdventureData"] = new JsonObject
                    {
                        ["name"] = adv.Name,
                        ["startingCoins"] = adv.StartingCoins,
                        ["completionCoinReward"] = adv.CompletionCoins,
                        ["startingCharacter"] = "ANTONIO",
                        ["startingStage"] = first0,
                        ["subtitleImage"] = "vampirerounds_title",
                    },
                    ["CHARACTER_DATA"] = new JsonArray(JsonValue.Create("ANTONIO")),
                    ["STAGE_DATA"] = setKey,
                    ["WEAPON_DATA"] = new JsonArray(JsonValue.Create(GunSlot.ToString())),
                    ["PROGRESS_DATA"] = new JsonArray(new JsonObject
                    {
                        ["description"] = stage1.Goal,
                        ["goldPrize"] = 1000,
                        ["adventureUnlockData"] = new JsonObject
                        {
                            ["Type"] = "TEST001_ACH001",
                            ["iconTextureName"] = "UI",
                            ["iconSpriteName"] = "icon_chapter2",
                            ["requiredMinute"] = 10,
                            ["requiredStage"] = first0,
                        },
                    }),
                };
                dm._allAdventuresJson[advKey] = JObject.Parse(json.ToJsonString());
                AdventureInData = true;
                Mod.Log("content: Vampire Rounds adventure, its stage set and the ROUNDS gun slot are in VS's data");
                TestMode.Mark("adventure-listed");
            }
            catch (Exception e) { Mod.Warn("content: adventure data: " + e); }
        }

        [HarmonyPatch(typeof(DataManager), nameof(DataManager.LoadDataFromJson))]
        public static class FixAdventurePatch
        {
            public static void Postfix(DataManager __instance) => FixAdventure(__instance);
        }

        /// <summary>The roster: Orange and Blue (runtime CharacterTypes VS gave them at start), Orange first.</summary>
        public static void FixAdventure(DataManager dm)
        {
            try
            {
                if (!AdventureInData || dm.AllAdventures == null) return;
                AdventureData data = null;
                if (!dm.AllAdventures.TryGetValue(Adventure, out data) || data == null)
                {
                    if (!missingLogged) { missingLogged = true; Mod.Warn("content: VS did not take the Vampire Rounds adventure into its adventures (" + dm.AllAdventures.Count + " adventures, stage sets " + (dm.AllStageSetData != null ? dm.AllStageSetData.Count : -1) + ")"); }
                    return;
                }
                if (!presentLogged) { presentLogged = true; Mod.Log("content: VS has the Vampire Rounds adventure (" + dm.AllAdventures.Count + " adventures)"); }
                if (Types.Count == 0) return;
                var list = new Il2CppSystem.Collections.Generic.List<CharacterType>();
                foreach (string id in Sheets.Adventures[0].Characters) if (Types.TryGetValue(id, out var t)) list.Add(t);
                if (list.Count == 0) return;
                data.CharacterTypes = list;
                if (data.CoreAdventureData != null && Types.TryGetValue(Sheets.Adventures[0].StartingCharacter, out var start)) data.CoreAdventureData.StartingCharacter = start;
            }
            catch (Exception e) { Mod.Once("content: roster", e); }
        }

        // ------------------------------------------------------------------ characters (h_characters, h_character_prefab)

        [HarmonyPatch(typeof(WorkshopCharacterBootstrapper), nameof(WorkshopCharacterBootstrapper.LoadCustomCharacters))]
        public static class RegisterCharactersPatch
        {
            public static void Postfix(WorkshopCharacterBootstrapper __instance) => RegisterCharacters(__instance);
        }

        public static void RegisterCharacters(WorkshopCharacterBootstrapper boot)
        {
            try
            {
                var dm = boot._dataManager;
                RegisterSprites();
                foreach (var row in Sheets.Characters)
                {
                    ulong hash = RuntimeCharacters.HashFolderName(row.Folder);
                    var ids = new Il2CppSystem.Collections.Generic.List<ulong>();
                    ids.Add(hash);
                    RuntimeCharacters.Custom.Allocate(ids.Cast<Il2CppSystem.Collections.Generic.IEnumerable<ulong>>());
                    CharacterType type;
                    if (!RuntimeCharacters.Custom.TryGetCharacterType(hash, out type)) { Mod.Warn("content: no custom character slot for " + row.Id); continue; }
                    var levels = JArray.Parse(CharacterJson(row));
                    WorkshopCharacterDataNormalizer.InjectTextureNames(levels, TextureOf(row), TextureOf(row));
                    dm.RegisterWorkshopCharacter(type, levels);
                    RuntimeCharacters.Custom.SetController(type, "CharacterController");
                    RuntimeCharacters.Custom.MarkLoaded(hash);
                    Types[row.Id] = type;
                    Rows[type] = row;
                    Mod.Log("content: " + row.VsName + " is VS character " + (int)type);
                }
                Localize();
                FixAdventure(dm);
            }
            catch (Exception e) { Mod.Warn("content: characters: " + e); }
        }

        private static string CharacterJson(CharacterRow row)
        {
            var lvl = new JsonObject
            {
                ["level"] = 1,
                ["charName"] = row.VsName,
                ["surname"] = row.Surname,
                ["description"] = row.Description,
                ["startingWeapon"] = GunSlot.ToString(),
                ["spriteName"] = row.Id + "_01.png",
                ["walkingFrames"] = row.WalkFrames,
                ["walkFrameRate"] = 8,
                ["bloodColor"] = "0x" + row.Color,
                ["isBought"] = true,
                ["hidden"] = true, // only in the Vampire Rounds adventure (its roster shows them), never in VS's own story
                ["price"] = 0,
                ["autoSetBodyOffset"] = true,
                ["maxHp"] = row.MaxHp,
                ["armor"] = 0, ["regen"] = 0, ["moveSpeed"] = row.MoveSpeed, ["power"] = 1, ["area"] = 1, ["speed"] = 1, ["duration"] = 1,
                ["amount"] = 0, ["cooldown"] = 1, ["luck"] = 1, ["growth"] = row.Growth, ["greed"] = 1, ["curse"] = 1, ["magnet"] = 0,
                ["revivals"] = 0, ["rerolls"] = 0, ["skips"] = 0, ["banish"] = 0,
            };
            return new JsonArray(lvl).ToJsonString();
        }

        [HarmonyPatch(typeof(CharacterFactory), nameof(CharacterFactory.GetCharacterPrefab))]
        public static class CharacterPrefabPatch
        {
            public static bool Prefix(CharacterFactory __instance, CharacterType characterType, ref CharacterController __result) => CharacterPrefab(__instance, characterType, ref __result);
        }

        public static bool CharacterPrefab(CharacterFactory factory, CharacterType type, ref CharacterController result)
        {
            if (!Rows.TryGetValue(type, out var row)) return true;
            try
            {
                if (!prefabs.TryGetValue(type, out var built) || built == null)
                {
                    var basePrefab = factory.GetCharacterPrefab(Enum.Parse<CharacterType>(row.BasePrefab));
                    built = new WorkshopCharacterPrefabBuilder(basePrefab.gameObject, factory).BuildAndRegister(type, "CharacterController");
                    prefabs[type] = built;
                    Mod.Log("content: built " + row.VsName + "'s prefab: " + (built != null ? built.name : "none"));
                }
                result = built;
                return built == null;
            }
            catch (Exception e) { Mod.Once("content: prefab", e); return true; }
        }

        // ------------------------------------------------------------------ the gun's VS slot (h_weapon_prefab)

        [HarmonyPatch(typeof(WeaponFactory), nameof(WeaponFactory.GetWeaponPrefab))]
        public static class WeaponPrefabPatch
        {
            public static bool Prefix(WeaponFactory __instance, WeaponType weaponType, ref WeaponType forcedWeaponType, ref Weapon __result) => WeaponPrefab(__instance, weaponType, ref forcedWeaponType, ref __result);
        }

        public static bool WeaponPrefab(WeaponFactory factory, WeaponType type, ref WeaponType forced, ref Weapon result)
        {
            if (type != GunSlot) return true;
            WeaponType ignored;
            result = factory.GetWeaponPrefab(WeaponType.WHIP, out ignored);
            forced = GunSlot;
            return false;
        }

        // ------------------------------------------------------------------ menus (h_adventure_background, _button, _card)

        [HarmonyPatch(typeof(MainMenuBackgroundFactory), nameof(MainMenuBackgroundFactory.GetBackgroundForAdventureType))]
        public static class BackgroundPatch
        {
            public static void Postfix(MainMenuBackgroundFactory __instance, AdventureType adventureType, ref GameObject __result) => Background(__instance, adventureType, ref __result);
        }

        private static bool inBackground;

        /// <summary>VS has no backdrop for a new adventure (it gives an empty white one): Vampire Rounds uses A Garlic
        /// Paradise's, the base-game adventure that opens in a forest like the Mad Forest.</summary>
        public static void Background(MainMenuBackgroundFactory f, AdventureType type, ref GameObject result)
        {
            if (type != Adventure || inBackground) return;
            try { inBackground = true; result = f.GetBackgroundForAdventureType(AdventureType.ADV_POES_001); }
            catch (Exception e) { Mod.Once("content: background", e); }
            finally { inBackground = false; }
        }

        [HarmonyPatch(typeof(MainMenuBackgroundManager), nameof(MainMenuBackgroundManager.SetBackgroundForAdventure))]
        public static class MenuBackgroundPatch
        {
            public static bool Prefix(MainMenuBackgroundManager __instance, AdventureType adventureType) => MenuBackground(__instance, adventureType);
        }

        /// <summary>The menus behind the adventure (its main menu, character and chapter select) show VS's own main-menu art:
        /// another adventure's animated backdrop only fills in its pictures for that adventure (it stays white here).</summary>
        public static bool MenuBackground(MainMenuBackgroundManager m, AdventureType type)
        {
            if (type != Adventure) return true;
            try { m.ResetBackgroundToMainGame(); ClearBorrowedBackdrop(); }
            catch (Exception e) { Mod.Once("content: menu backdrop", e); }
            return false;
        }

        private static float nextBackdropCheck;

        /// <summary>Menus of the Vampire Rounds adventure: the backdrop the adventure list moved behind them (another
        /// adventure's, white here) is put away, so VS's own main-menu art shows.</summary>
        public static void ClearBorrowedBackdrop()
        {
            var holder = GameObject.Find("CustomBackgroundHolder");
            if (holder == null) return;
            for (int i = 0; i < holder.transform.childCount; i++)
            {
                var c = holder.transform.GetChild(i).gameObject;
                if (c.activeSelf) c.SetActive(false);
            }
        }

        /// <summary>Every second while VS is in the adventure's menus.</summary>
        public static void MenuTick()
        {
            if (Time.unscaledTime < nextBackdropCheck) return;
            nextBackdropCheck = Time.unscaledTime + 0.5f;
            if (InOurAdventure && !Session.InRun) ClearBorrowedBackdrop();
        }

        [HarmonyPatch(typeof(MainMenuPage), nameof(MainMenuPage.HasAdventuresUnlocked))]
        public static class AdventuresButtonPatch
        {
            public static void Postfix(ref bool __result) => AdventuresButton(ref __result);
        }

        public static void AdventuresButton(ref bool result)
        {
            if (AdventureInData) result = true;
        }

        [HarmonyPatch(typeof(AdventureItemUI), nameof(AdventureItemUI.SetData))]
        public static class AdventureCardPatch
        {
            public static void Postfix(AdventureItemUI __instance, AdventureType type) => AdventureCard(__instance, type);
        }

        public static void AdventureCard(AdventureItemUI ui, AdventureType type)
        {
            if (type != Adventure) return;
            try
            {
                ui._isUnlockedViaAtlas = true;
                ui._ownsRequiredDlc = true;
                if (ui._LockedGroup != null) ui._LockedGroup.SetActive(false);
                if (ui._RequiresDlcPurchaseGroup != null) ui._RequiresDlcPurchaseGroup.SetActive(false);
                if (ui._AvailableGroup != null) ui._AvailableGroup.SetActive(true);
                if (ui._Title != null) ui._Title.text = Sheets.Adventures[0].Name;
            }
            catch (Exception e) { Mod.Once("content: card", e); }
        }

        // ------------------------------------------------------------------ both heroes from the start (h_both_heroes)

        [HarmonyPatch(typeof(AdventureManager), nameof(AdventureManager.InitAdventure))]
        public static class BothHeroesPatch
        {
            public static void Postfix(AdventureManager __instance, AdventureType adventureType) => BothHeroes(__instance, adventureType);
        }

        /// <summary>Orange and Blue are both playable from the start of the adventure (a second player takes the other).</summary>
        public static void BothHeroes(AdventureManager am, AdventureType type)
        {
            if (type != Adventure) return;
            try
            {
                var pod = am.CurrentAdventureSaveData;
                if (pod == null) return;
                foreach (var t in Types.Values)
                {
                    if (pod.UnlockedCharacters != null && !pod.UnlockedCharacters.Contains(t)) pod.UnlockedCharacters.Add(t);
                    if (pod.BoughtCharacters != null && !pod.BoughtCharacters.Contains(t)) pod.BoughtCharacters.Add(t);
                }
            }
            catch (Exception e) { Mod.Once("content: heroes", e); }
        }

        // ------------------------------------------------------------------ no VS weapon shop in the adventure (h_no_merchant)

        [HarmonyPatch(typeof(Stage), nameof(Stage.SpawnAdventureMerchants))]
        public static class NoMerchantPatch
        {
            public static bool Prefix() => NoMerchant();
        }

        [HarmonyPatch(typeof(Stage), nameof(Stage.SpawnMerchant))]
        public static class NoStageMerchantPatch
        {
            public static bool Prefix() => NoMerchant();
        }

        [HarmonyPatch(typeof(Stage), nameof(Stage.SpawnCustomMerchants))]
        public static class NoCustomMerchantPatch
        {
            public static bool Prefix() => NoMerchant();
        }

        [HarmonyPatch(typeof(TilingTileset), nameof(TilingTileset.SpawnAdventureMerchant))]
        public static class NoMerchantTilePatch
        {
            public static bool Prefix() => NoMerchant();
        }

        /// <summary>VS's adventure merchants sell VS weapons; Orange and Blue fight with ROUNDS's gun, so none appear here.</summary>
        public static bool NoMerchant() => !InOurAdventure;

        // ------------------------------------------------------------------ texts

        public static void Localize()
        {
            try
            {
                LanguageSourceData src = null;
                foreach (var s in LocalizationManager.Sources)
                    if (s.GetTermData("weaponLang/{WHIP}name", false) != null) { src = s; break; }
                if (src == null) { Mod.Warn("content: no VS text source; names stay as keys"); return; }
                int n = 0;
                foreach (var t in Sheets.Texts)
                {
                    string term = t[0];
                    int i = term.IndexOf("{type:", StringComparison.Ordinal);
                    if (i >= 0)
                    {
                        int j = term.IndexOf('}', i);
                        if (!Types.TryGetValue(term.Substring(i + 6, j - i - 6), out var ct)) continue;
                        term = term.Substring(0, i) + "{" + ((int)ct) + "}" + term.Substring(j + 1);
                    }
                    var td = src.GetTermData(term, false) ?? src.AddTerm(term);
                    for (int k = 0; k < src.mLanguages.Count; k++) td.SetTranslation(k, t[1], null);
                    n++;
                }
                src.UpdateDictionary(true);
                Mod.Log("content: " + n + " texts added");
            }
            catch (Exception e) { Mod.Warn("content: texts: " + e); }
        }

        // ------------------------------------------------------------------ pictures

        /// <summary>Orange's and Blue's sprites (ROUNDS's portrait when the engine has drawn one, else a stand-in blob in the
        /// team colour), the gun icon and the adventure title, under VS's sprite texture "VampireRounds".</summary>
        /// <summary>Each hero has its own VS sprite sheet: VS's character select looks up "charsel" in the character's sheet.</summary>
        public static string TextureOf(CharacterRow row) => Texture + "_" + row.Id;

        private static Il2CppSystem.Collections.Generic.Dictionary<SpriteManager.StringHashCaseI, Sprite> Bag(string texture)
        {
            var refs = SpriteManager.SpriteTextureReference;
            var key = new SpriteManager.StringHashCaseI(texture);
            Il2CppSystem.Collections.Generic.Dictionary<SpriteManager.StringHashCaseI, Sprite> bag;
            if (!refs.TryGetValue(key, out bag) || bag == null) { bag = new Il2CppSystem.Collections.Generic.Dictionary<SpriteManager.StringHashCaseI, Sprite>(); refs[key] = bag; }
            return bag;
        }

        private static void RegisterSprites()
        {
            var bag = Bag(Texture);
            foreach (var row in Sheets.Characters)
            {
                var tex = PortraitTexture(row);
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.35f), SpritePpu);
                sprite.name = row.Id + "_01.png";
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                var own = Bag(TextureOf(row));
                foreach (var name in new[] { row.Id + "_01.png", row.Id + "_01", "charsel.png", "charsel" }) Add(own, name, sprite);
                Add(bag, row.Id + "_01.png", sprite);
                Add(bag, row.Id + "_01", sprite);
            }
            var art = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "VampireRounds", "art");
            AddRaw(bag, Path.Combine(art, "title.rgba"), "vampirerounds_title", 100f);
            AddRaw(bag, Path.Combine(art, "gun_icon.rgba"), "gun_icon", 100f);
        }

        private static void Add(Il2CppSystem.Collections.Generic.Dictionary<SpriteManager.StringHashCaseI, Sprite> bag, string name, Sprite s)
        {
            bag[new SpriteManager.StringHashCaseI(name)] = s;
        }

        private static void AddRaw(Il2CppSystem.Collections.Generic.Dictionary<SpriteManager.StringHashCaseI, Sprite> bag, string file, string name, float ppu)
        {
            var tex = RawImage.Load(file);
            if (tex == null) { Mod.Warn("content: missing art " + file); return; }
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            s.name = name;
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Add(bag, name, s);
            Add(bag, name + ".png", s);
            // VS also looks sprites up by name alone (adventure titles, icons): SpriteManager.Sprites.
            SpriteManager.Sprites[new SpriteManager.StringHashCaseI(name)] = s;
            SpriteManager.Sprites[new SpriteManager.StringHashCaseI(name + ".png")] = s;
        }

        private static Texture2D PortraitTexture(CharacterRow row)
        {
            var tex = new Texture2D(RawImage.PortraitSize, RawImage.PortraitSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            portraitTex[row.Seat] = tex;
            if (!RawImage.LoadInto(tex, RawImage.PortraitFile(row.Seat))) RawImage.StandIn(tex, row.Color);
            else portraitStamp[row.Seat] = File.GetLastWriteTimeUtc(RawImage.PortraitFile(row.Seat));
            return tex;
        }

        public static bool IsOurTexture(Texture t)
        {
            foreach (var p in portraitTex) if (p != null && p.Pointer == t.Pointer) return true;
            return false;
        }

        private static float nextPortraitCheck;

        /// <summary>When the engine draws a newer portrait (the first run on this PC), it replaces the stand-in in place.</summary>
        public static void RefreshPortraits()
        {
            if (Time.unscaledTime < nextPortraitCheck) return;
            nextPortraitCheck = Time.unscaledTime + 3f;
            for (int i = 0; i < portraitTex.Length; i++)
            {
                var tex = portraitTex[i];
                if (tex == null) continue;
                string f = RawImage.PortraitFile(i);
                if (!File.Exists(f)) continue;
                var stamp = File.GetLastWriteTimeUtc(f);
                if (stamp <= portraitStamp[i]) continue;
                if (RawImage.LoadInto(tex, f)) { portraitStamp[i] = stamp; Mod.Log("content: ROUNDS portrait for seat " + i + " loaded"); }
            }
        }
    }
}
