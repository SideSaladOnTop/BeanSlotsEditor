using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BeanSlotsEditor;
public partial class MainWindow : Window
{
    public class RustItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Shortname { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string Category { get; set; } = "Uncategorized";
        public string LegendaryId { get; set; } = "";
        public string Skin { get; set; } = "0";
        private ImageSource? icon;
        public ImageSource? Icon { get => icon; set { icon = value; PropertyChanged?.Invoke(this, new(nameof(Icon))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public enum PrizeRarity { Unknown, Common, Uncommon, Rare, VeryRare, Legendary, LegendaryCommon, LegendaryUncommon, LegendaryRare, LegendaryVeryRare }
    public sealed class RarityOption { public PrizeRarity Value { get; init; } public string Label { get; init; } = ""; }
    public static readonly RarityOption[] RarityOptions = Enum.GetValues<PrizeRarity>()
        .Select(r => new RarityOption { Value = r, Label = RarityLabel(r) }).ToArray();
    public class Prize : INotifyPropertyChanged
    {
        public JsonObject ExtraFields { get; set; } = new();
        private PrizeRarity rarity = PrizeRarity.Unknown;
        public PrizeRarity Rarity { get => rarity; set { rarity = value; Changed(nameof(Rarity)); Changed(nameof(RarityDisplay)); } }
        public string RarityDisplay => RarityLabel(rarity);
        private bool jackpot;
        public bool Jackpot { get => jackpot; set { jackpot = value; Changed(nameof(Jackpot)); } }
        public string RarityKey => Shortname.Trim().ToLowerInvariant() + "|" + Skin + "|" + (ExtraFields["LegendaryId"]?.ToString() ?? "") + "|" + Amount;

        private string shortname = "scrap"; private int amount = 1; private double chance = 1; private string skin = "0";
        public string Shortname { get => shortname; set { shortname = value; Changed(nameof(Shortname)); } }
        public int Amount { get => amount; set { amount = value; Changed(nameof(Amount)); } }
        public double Chance { get => chance; set { chance = value; Changed(nameof(Chance)); } }
        public string Skin { get => skin; set { skin = value; Changed(nameof(Skin)); } }
        private string displayName = "";
        public string DisplayName { get => displayName; set { displayName = value; Changed(nameof(DisplayName)); } }
        private ImageSource? icon;
        public ImageSource? Icon { get => icon; set { icon = value; Changed(nameof(Icon)); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
    }
    private readonly ObservableCollection<Prize> prizes = new();
    private readonly ObservableCollection<RustItem> items = new();
    private readonly Dictionary<string, RustItem> catalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RustItem> legendaryCatalog = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string legendaryCacheFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeanSlotsEditor", "legendary-items-import.json");
    private readonly Dictionary<string, string> categoryAssignments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> bundledItemCategories = new(StringComparer.OrdinalIgnoreCase);
    private bool updatingCategoryOptions;
    private readonly List<string> configuredCategories = new();
    private static readonly string bundledCategoriesFile = Path.Combine(AppContext.BaseDirectory, "categories.json");
    private static readonly string bundledItemsFile = Path.Combine(AppContext.BaseDirectory, "admin-item-list-public.json");
    private static readonly string categoriesCacheFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeanSlotsEditor", "categories.json");
    private string selectedCategory = "All Categories";
    private string ResolveCategory(string shortname, string supplied)
    {
        if (categoryAssignments.TryGetValue(shortname, out var category) && !string.IsNullOrWhiteSpace(category)) return category;
        if (!string.IsNullOrWhiteSpace(supplied) && !supplied.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase)) return supplied;
        if (bundledItemCategories.TryGetValue(shortname, out var known) && !string.IsNullOrWhiteSpace(known)) return known;
        return "Uncategorized";
    }
    private void LoadCategoryAssignments()
    {
        // Uploaded RustHelp database supplies the base category for all ~1300 item shortnames.
        // Uploaded categories.json assignments override those values; never discard the
        // bundled database just because an older per-user cache exists.
        try
        {
            if (File.Exists(bundledItemsFile))
            {
                using var db = JsonDocument.Parse(File.ReadAllText(bundledItemsFile));
                if (db.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var entry in db.RootElement.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("shortName", out var sn) || sn.ValueKind != JsonValueKind.String) continue;
                        var shortname = sn.GetString();
                        if (string.IsNullOrWhiteSpace(shortname)) continue;
                        if (entry.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.String)
                            bundledItemCategories[shortname] = category.GetString() ?? "Uncategorized";
                    }
            }
            void Merge(string file)
            {
                if (!File.Exists(file)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("Categories", out var names) && names.ValueKind == JsonValueKind.Array)
                    foreach (var name in names.EnumerateArray())
                        if (name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                            configuredCategories.Add(name.GetString()!);
                if (doc.RootElement.TryGetProperty("Assignments", out var assignments) && assignments.ValueKind == JsonValueKind.Object)
                    foreach (var entry in assignments.EnumerateObject())
                        if (entry.Value.ValueKind == JsonValueKind.String) categoryAssignments[entry.Name] = entry.Value.GetString() ?? "Uncategorized";
            }
            // Merge older user-defined assignments first, then the current uploaded assignments.
            // This keeps the supplied files authoritative and avoids stale local mappings.
            Merge(categoriesCacheFile);
            Merge(bundledCategoriesFile);
        }
        catch (Exception ex) { StatusLabel.Text = "Category mappings could not be loaded: " + ex.Message; }
    }
    private void RefreshCategoryOptions()
    {
        if (ItemCategoryFilter == null) return;
        // Display only categories which actually have catalog items. That also prevents
        // seemingly empty dropdown entries before a LegendaryItems config is imported.
        var counts = items.GroupBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 0)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = counts.Select(g => g.Key).ToList();
        names.Insert(0, "All Categories");
        if (!names.Contains(selectedCategory, StringComparer.OrdinalIgnoreCase)) selectedCategory = "All Categories";
        updatingCategoryOptions = true;
        try
        {
            ItemCategoryFilter.ItemsSource = names;
            ItemCategoryFilter.SelectedItem = names.First(c => c.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase));
        }
        finally { updatingCategoryOptions = false; }
    }
    private void ItemCategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (updatingCategoryOptions) return;
        if (ItemCategoryFilter?.SelectedItem is string category) selectedCategory = category;
        RefreshFilters();
        StatusLabel.Text = selectedCategory == "All Categories" ? $"All Categories: {items.Count} items" :
            $"{selectedCategory}: {items.Count(i => i.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase))} items";
    }
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly string preferencesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeanSlotsEditor", "editor-settings.json");
    private double maxWinningChance = 65;
    private readonly Dictionary<string, List<RarityRecord>> rarityByConfig = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<RarityWeightEntry> rarityWeightEntries = new();
    public class RarityWeightEntry : INotifyPropertyChanged
    {
        public PrizeRarity Rarity { get; set; }
        public string Name => RarityLabel(Rarity);
        private double weight;
        public double Weight { get => weight; set { weight = value; PropertyChanged?.Invoke(this, new(nameof(Weight))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static Dictionary<PrizeRarity, double> DefaultWeights() => new()
    {
        [PrizeRarity.Unknown] = 1, [PrizeRarity.Common] = 12,
        [PrizeRarity.Uncommon] = 6, [PrizeRarity.Rare] = 3,
        [PrizeRarity.VeryRare] = 1, [PrizeRarity.LegendaryCommon] = 0.7,
        [PrizeRarity.LegendaryUncommon] = 0.35,
        [PrizeRarity.LegendaryRare] = 0.15,
        [PrizeRarity.LegendaryVeryRare] = 0.05
    };
    private readonly Dictionary<PrizeRarity, double> rarityWeights = DefaultWeights();
    private sealed class RarityRecord { public string Key { get; set; } = ""; public PrizeRarity Rarity { get; set; } }
    public static string RarityLabel(PrizeRarity rarity) => rarity switch
    {
        PrizeRarity.VeryRare => "Very Rare",
        PrizeRarity.Legendary => "Legendary Common (legacy)",
        PrizeRarity.LegendaryCommon => "Legendary Common",
        PrizeRarity.LegendaryUncommon => "Legendary Uncommon",
        PrizeRarity.LegendaryRare => "Legendary Rare",
        PrizeRarity.LegendaryVeryRare => "Legendary Very Rare",
        _ => rarity.ToString()
    };
    private static PrizeRarity MigrateRarity(PrizeRarity rarity) =>
        rarity == PrizeRarity.Legendary ? PrizeRarity.LegendaryCommon : rarity;
    private static PrizeRarity DefaultRarity(Prize prize) =>
        !string.IsNullOrWhiteSpace(prize.ExtraFields["LegendaryId"]?.ToString()) ? PrizeRarity.LegendaryCommon : PrizeRarity.Unknown;
    private static string ConfigKey(string file) => Path.GetFullPath(file);
    private void LoadPrizeRarities(string file)
    {
        if (!rarityByConfig.TryGetValue(ConfigKey(file), out var saved)) return;
        var used = new HashSet<int>();
        for (int index = 0; index < prizes.Count; index++)
        {
            var prize = prizes[index];
            int matched = index < saved.Count && saved[index].Key == prize.RarityKey ? index : -1;
            if (matched < 0)
                matched = Enumerable.Range(0, saved.Count).FirstOrDefault(i => !used.Contains(i) && saved[i].Key == prize.RarityKey, -1);
            if (matched >= 0) { prize.Rarity = MigrateRarity(saved[matched].Rarity); used.Add(matched); }
        }
    }
    private void SnapshotPrizeRarities(string file)
    {
        rarityByConfig[ConfigKey(file)] = prizes.Select(p => new RarityRecord { Key = p.RarityKey, Rarity = p.Rarity }).ToList();
    }

    private static readonly string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeanSlotsEditor", "RustHelp");
    private static readonly string catalogFile = Path.Combine(cacheDir, "items.json");
    private static readonly string iconManifestFile = Path.Combine(cacheDir, "icon-urls.json");
    private readonly Dictionary<string, string> iconManifest = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim updateGate = new(1, 1);
    private const string rustHelpItemsUrl = "https://rusthelp.com/downloads/admin-item-list-public.json";
    private readonly SemaphoreSlim imageSlots = new(6);
    private readonly HashSet<string> imageRequested = new(StringComparer.OrdinalIgnoreCase);
    private JsonObject root = new();
    private string? path;
    private bool dirty;
    private bool loading;
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private static readonly string[] itemSeed = ("scrap|Scrap;metal.refined|High Quality Metal;metal.fragments|Metal Fragments;wood|Wood;stones|Stones;sulfur|Sulfur;sulfur.ore|Sulfur Ore;metal.ore|Metal Ore;hq.metal.ore|High Quality Metal Ore;charcoal|Charcoal;cloth|Cloth;leather|Leather;fat.animal|Animal Fat;lowgradefuel|Low Grade Fuel;gunpowder|Gun Powder;explosives|Explosives;explosive.timed|Timed Explosive Charge;explosive.satchel|Satchel Charge;grenade.f1|F1 Grenade;grenade.beancan|Beancan Grenade;ammo.rifle|5.56 Rifle Ammo;ammo.rifle.explosive|Explosive Rifle Ammo;ammo.rifle.hv|HV Rifle Ammo;ammo.rifle.incendiary|Incendiary Rifle Ammo;ammo.pistol|Pistol Bullet;ammo.pistol.hv|HV Pistol Ammo;ammo.pistol.fire|Incendiary Pistol Ammo;ammo.shotgun|Handmade Shell;ammo.shotgun.slug|12 Gauge Slug;ammo.shotgun.fire|12 Gauge Incendiary Shell;ammo.shotgun.grenade|40mm Shotgun Round;ammo.rocket.basic|Rocket;ammo.rocket.hv|HV Rocket;ammo.rocket.fire|Incendiary Rocket;rifle.ak|Assault Rifle;rifle.lr300|LR-300 Assault Rifle;rifle.bolt|Bolt Action Rifle;rifle.l96|L96 Rifle;rifle.semiauto|Semi-Automatic Rifle;rifle.m39|M39 Rifle;smg.mp5|MP5A4;smg.2|Custom SMG;smg.thompson|Thompson;pistol.semiauto|Semi-Automatic Pistol;pistol.python|Python Revolver;pistol.revolver|Revolver;pistol.m92|M92 Pistol;pistol.eoka|Eoka Pistol;shotgun.pump|Pump Shotgun;shotgun.double|Double Barrel Shotgun;shotgun.spas12|SPAS-12 Shotgun;shotgun.waterpipe|Waterpipe Shotgun;lmg.m249|M249;minigun|Minigun;rocket.launcher|Rocket Launcher;multiplegrenadelauncher|Multiple Grenade Launcher;flamethrower|Flamethrower;chainsaw|Chainsaw;jackhammer|Jackhammer;metal.facemask|Metal Facemask;metal.plate.torso|Metal Chest Plate;roadsign.jacket|Road Sign Jacket;roadsign.kilt|Road Sign Kilt;coffeecan.helmet|Coffee Can Helmet;hazmatsuit|Hazmat Suit;riflebody|Rifle Body;smgbody|SMG Body;semibody|Semi Automatic Body;gears|Gears;techparts|Tech Trash;rope|Rope;sewingkit|Sewing Kit;sheetmetal|Sheet Metal;metalpipe|Metal Pipe;propanetank|Empty Propane Tank;targeting.computer|Targeting Computer;cctv.camera|CCTV Camera;electric.battery.rechargable.large|Large Rechargeable Battery;autoturret|Auto Turret;guntrap|Shotgun Trap;landmine|Land Mine;surveycharge|Survey Charge;largemedkit|Large Medkit;syringe.medical|Medical Syringe;bandage|Bandage;antiradpills|Anti-Radiation Pills;water|Water;horse.saddle|Horse Saddle;vehicle.1mod.cockpit|Cockpit Vehicle Module;weapon.mod.holosight|Holosight;weapon.mod.lasersight|Weapon Lasersight;weapon.mod.silencer|Silencer;weapon.mod.8x.scope|8x Zoom Scope;weapon.mod.small.scope|16x Zoom Scope;weapon.mod.extendedmags|Extended Magazine;weapon.mod.flashlight|Weapon Flashlight").Split(';');
    public MainWindow()
    {
        InitializeComponent();
        LoadCategoryAssignments();
        LoadSavedLegendaryCatalog();
        foreach (var raw in itemSeed) { var a = raw.Split('|'); if (a.Length == 2) items.Add(new RustItem { Shortname = a[0], Name = a[1], Category = ResolveCategory(a[0], "Uncategorized") }); }
        foreach (var legendary in legendaryCatalog.Values) items.Add(legendary);
        ItemsGrid.ItemsSource = CollectionViewSource.GetDefaultView(items);
        PrizesGrid.ItemsSource = CollectionViewSource.GetDefaultView(prizes);
        RefreshCategoryOptions();
        prizes.CollectionChanged += (_, _) => { if (!loading) dirty = true; RefreshFilters(); UpdateOdds(); };
        RarityWeightsGrid.ItemsSource = rarityWeightEntries;
        RefreshWeightEntries();
        NewDefaults();
        LoadEditorPreferences();
        _ = InitializeRustHelpAsync();
    }
    private static string S(JsonNode? n, string fallback = "") => n?.ToString() ?? fallback;
    private static JsonObject Obj(JsonObject parent, string key) { if (parent[key] is not JsonObject o) { o = new JsonObject(); parent[key] = o; } return o; }
    private void NewDefaults()
    {
        loading = true; root = new JsonObject(); path = null;
        root["StartingSpins"] = 5; root["MaxSpinsPerClick"] = 10; root["AnimationFrames"] = 18; root["AnimationIntervalSeconds"] = 0.14; root["AnimationMode"] = "Simultaneous";
        root["SpinsPerPurchase"] = 5; root["PurchaseCost"] = 100; root["EconomicsPurchaseEnabled"] = true;
        root["RewardAllRaiders"] = true;
        var raid = Obj(root, "RaidSpins"); raid["Easy"] = 1; raid["Medium"] = 2; raid["Hard"] = 3; raid["Expert"] = 5; raid["Nightmare"] = 8;
        var overlay = Obj(root, "OverlayButton"); overlay["Enabled"] = true; overlay["Left"] = .438; overlay["Bottom"] = 0; overlay["Width"] = .042; overlay["Height"] = .02; overlay["Text"] = "SLOTS"; overlay["FontSize"] = 10; overlay["ButtonColor"] = "0.18 0.35 0.50 0.90"; overlay["TextColor"] = "1 1 1 1";
        prizes.Clear();
        foreach (var p in new[] { ("scrap",100,20d), ("metal.refined",50,12d), ("rifle.ak",1,3d), ("explosive.timed",2,2d), ("sulfur",1000,10d) }) AddPrize(new Prize { Shortname = p.Item1, Amount = p.Item2, Chance = p.Item3 });
        LoadFields(); loading = false; dirty = false; PathLabel.Text = "New configuration"; UpdateOdds();
    }
    private void AddPrize(Prize p)
    {
        p.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Prize.Shortname)) AttachPrizeIcon(p);
            if (e.PropertyName == nameof(Prize.Rarity))
            {
                if (!loading && !string.IsNullOrWhiteSpace(path)) SaveEditorPreferences();
            }
            else if (!loading) dirty = true;
            UpdateOdds();
        };
        prizes.Add(p);
        AttachPrizeIcon(p);
    }
    private void LoadFields()
    {
        StartingSpins.Text = S(root["StartingSpins"], "5"); MaxSpins.Text = S(root["MaxSpinsPerClick"], "10"); Frames.Text = S(root["AnimationFrames"], "18"); FrameInterval.Text = S(root["AnimationIntervalSeconds"], "0.14"); AnimationMode.SelectedIndex = S(root["AnimationMode"], "Simultaneous").Equals("Sequential", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var pity = Obj(root,"ServerWidePity");
        PityEnabled.IsChecked = S(pity["Enabled"], "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        PityPerLoss.Text = S(pity["WinChanceIncreasePerLoss"], "0.05");
        PityMaxBonus.Text = S(pity["MaximumAdditionalWinChance"], "5");
        JackpotPerLoss.Text = S(pity["JackpotWeightIncreasePerLoss"], "0.02");
        JackpotMaxMultiplier.Text = S(pity["MaximumJackpotWeightMultiplier"], "2");
        PityResetAnyWin.IsChecked = S(pity["ResetStreakOnAnyWin"], "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        SpinsPerPurchase.Text = S(root["SpinsPerPurchase"], "5"); PurchaseCost.Text = S(root["PurchaseCost"], "100"); EconomicsEnabled.IsChecked = S(root["EconomicsPurchaseEnabled"], "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        var raid = Obj(root, "RaidSpins"); Easy.Text = S(raid["Easy"], "1"); Medium.Text = S(raid["Medium"], "2"); Hard.Text = S(raid["Hard"], "3"); Expert.Text = S(raid["Expert"], "5"); Nightmare.Text = S(raid["Nightmare"], "8"); RewardAllRaiders.IsChecked = S(root["RewardAllRaiders"], "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        var o = Obj(root, "OverlayButton"); OverlayEnabled.IsChecked = S(o["Enabled"], "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        OverlayLeft.Text = S(o["Left"], "0.438"); OverlayBottom.Text = S(o["Bottom"], "0"); OverlayWidth.Text = S(o["Width"], "0.042"); OverlayHeight.Text = S(o["Height"], "0.02"); OverlayText.Text = S(o["Text"], "SLOTS"); OverlayFont.Text = S(o["FontSize"], "10"); OverlayButtonColor.Text = S(o["ButtonColor"], "0.18 0.35 0.50 0.90"); OverlayTextColor.Text = S(o["TextColor"], "1 1 1 1");
    }
    private bool CollectFields()
    {
        PrizesGrid.CommitEdit(DataGridEditingUnit.Cell, true); PrizesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        try
        {
            int Int(TextBox t, int min, int max) { if (!int.TryParse(t.Text, out var v) || v < min || v > max) throw new Exception($"Invalid number in {t.Name}: {t.Text}"); return v; }
            double Double(TextBox t, double min, double max) { if (!double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsNaN(v) || v < min || v > max) throw new Exception($"Invalid number in {t.Name}: {t.Text} (use decimal point)"); return v; }
            root["StartingSpins"] = Int(StartingSpins,0,1000000); root["MaxSpinsPerClick"] = Int(MaxSpins,1,100); root["AnimationFrames"] = Int(Frames,6,50); root["AnimationIntervalSeconds"] = Double(FrameInterval,.06,.8); root["AnimationMode"] = AnimationMode.SelectedIndex == 1 ? "Sequential" : "Simultaneous";
            var pity = Obj(root,"ServerWidePity");
            pity["Enabled"] = PityEnabled.IsChecked == true;
            pity["WinChanceIncreasePerLoss"] = Double(PityPerLoss,0,5);
            pity["MaximumAdditionalWinChance"] = Double(PityMaxBonus,0,50);
            pity["JackpotWeightIncreasePerLoss"] = Double(JackpotPerLoss,0,2);
            pity["MaximumJackpotWeightMultiplier"] = Double(JackpotMaxMultiplier,1,100);
            pity["ResetStreakOnAnyWin"] = PityResetAnyWin.IsChecked == true;
            root["SpinsPerPurchase"] = Int(SpinsPerPurchase,1,1000000); root["PurchaseCost"] = Double(PurchaseCost,0,1000000000); root["EconomicsPurchaseEnabled"] = EconomicsEnabled.IsChecked == true;
            var raid = Obj(root,"RaidSpins"); raid["Easy"] = Int(Easy,0,1000000); raid["Medium"] = Int(Medium,0,1000000); raid["Hard"] = Int(Hard,0,1000000); raid["Expert"] = Int(Expert,0,1000000); raid["Nightmare"] = Int(Nightmare,0,1000000); root["RewardAllRaiders"] = RewardAllRaiders.IsChecked == true;
            var o = Obj(root,"OverlayButton"); o["Enabled"] = OverlayEnabled.IsChecked == true; o["Left"] = Double(OverlayLeft,0,1); o["Bottom"] = Double(OverlayBottom,0,1); o["Width"] = Double(OverlayWidth,.005,1); o["Height"] = Double(OverlayHeight,.005,1); if (double.Parse(OverlayLeft.Text,CultureInfo.InvariantCulture)+double.Parse(OverlayWidth.Text,CultureInfo.InvariantCulture)>1 || double.Parse(OverlayBottom.Text,CultureInfo.InvariantCulture)+double.Parse(OverlayHeight.Text,CultureInfo.InvariantCulture)>1) throw new Exception("Overlay extends beyond screen bounds.");
            o["Text"] = OverlayText.Text; o["FontSize"] = Int(OverlayFont,6,32); o["ButtonColor"] = OverlayButtonColor.Text; o["TextColor"] = OverlayTextColor.Text;
            var list = new JsonArray();
            foreach(var p in prizes)
            {
                if (string.IsNullOrWhiteSpace(p.Shortname) || p.Amount < 1 || p.Chance < 0 || p.Chance > 100 || double.IsNaN(p.Chance) || !ulong.TryParse(p.Skin, out var skin)) throw new Exception("Invalid prize: " + p.Shortname + ". Amount >= 1, chance 0–100, skin unsigned integer.");
                var entry = (JsonObject)p.ExtraFields.DeepClone();
                entry["Shortname"] = p.Shortname.Trim(); entry["Amount"] = p.Amount; entry["Chance"] = p.Chance; entry["Jackpot"] = p.Jackpot; entry["Skin"] = skin; list.Add(entry);
            }
            if (prizes.Sum(p=>p.Chance) > maxWinningChance + 0.000001) throw new Exception($"Total chance exceeds the app-only maximum ({maxWinningChance:0.###}%). Use a suggestion or adjust the probabilities before saving.");
            root["Prizes"] = list;
            return true;
        }
        catch(Exception e) { MessageBox.Show(e.Message,"Validation",MessageBoxButton.OK,MessageBoxImage.Warning); return false; }
    }
    private void ReadConfig(string file)
    {
        var parsed = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new Exception("Expected JSON object at root.");
        loading = true; root = parsed; prizes.Clear();
        if(root["Prizes"] is JsonArray arr) foreach(var obj in arr.OfType<JsonObject>())
        { AddPrize(new Prize { Shortname = S(obj["Shortname"]), Amount = int.TryParse(S(obj["Amount"]), out var amt) ? amt:1, Chance = double.TryParse(S(obj["Chance"]), NumberStyles.Float,CultureInfo.InvariantCulture,out var c)?c:0, Skin=S(obj["Skin"],"0"), Jackpot=S(obj["Jackpot"],"false").Equals("true",StringComparison.OrdinalIgnoreCase), ExtraFields=(JsonObject)obj.DeepClone(), Rarity=!string.IsNullOrWhiteSpace(S(obj["LegendaryId"])) ? PrizeRarity.LegendaryCommon : PrizeRarity.Unknown }); }
        LoadPrizeRarities(file);
        LoadFields(); path=file; PathLabel.Text=Path.GetFileName(file); dirty=false; loading=false; UpdateOdds(); SaveEditorPreferences(); StatusLabel.Text=$"Loaded {prizes.Count} prizes";
    }
    private bool Save(string? destination)
    {
        if(string.IsNullOrEmpty(destination)) return SaveAs();
        if(!CollectFields()) return false;
        try { File.WriteAllText(destination, root.ToJsonString(jsonOptions)); path=destination; dirty=false; PathLabel.Text=Path.GetFileName(destination); StatusLabel.Text=$"Saved {prizes.Count} prizes"; SaveEditorPreferences(); return true; }
        catch(Exception ex) { MessageBox.Show(ex.Message,"Save failed",MessageBoxButton.OK,MessageBoxImage.Error); return false; }
    }
    private bool SaveAs() { var dialog = new SaveFileDialog { Filter="JSON config (*.json)|*.json", FileName="BeanSlots.json" }; return dialog.ShowDialog(this)==true && Save(dialog.FileName); }
    private void Open_Click(object sender, RoutedEventArgs e) { if(!ConfirmLeave())return; var dlg=new OpenFileDialog { Filter="JSON config (*.json)|*.json" }; if(dlg.ShowDialog(this)!=true)return; try { ReadConfig(dlg.FileName); } catch(Exception ex) { MessageBox.Show(ex.Message,"Open failed"); } }
    private void Save_Click(object sender, RoutedEventArgs e) => Save(path);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();
    private void New_Click(object sender, RoutedEventArgs e) { if(ConfirmLeave()) { SaveEditorPreferences(); NewDefaults(); } }
    private bool ConfirmLeave() { if(!dirty && path==null && prizes.Count==0)return true; var answer=MessageBox.Show("Save any unsaved changes before continuing?", "BeanSlots Editor", MessageBoxButton.YesNoCancel,MessageBoxImage.Question); if(answer==MessageBoxResult.Cancel)return false; return answer!=MessageBoxResult.Yes || Save(path); }
    private void Window_Closing(object? sender, CancelEventArgs e) { if(!ConfirmLeave())e.Cancel=true; }
    private void RefreshFilters()
    {
        if(ItemsGrid==null || PrizesGrid==null)return;
        var search=ItemSearch?.Text?.Trim()??""; var ps=PrizeSearch?.Text?.Trim()??"";
        var all=CollectionViewSource.GetDefaultView(items); all.Filter=o => o is RustItem i && (selectedCategory == "All Categories" || i.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase)) && (i.Name.Contains(search,StringComparison.OrdinalIgnoreCase)||i.Shortname.Contains(search,StringComparison.OrdinalIgnoreCase)||i.LegendaryId.Contains(search,StringComparison.OrdinalIgnoreCase)) && (HideSelected?.IsChecked!=true || !prizes.Any(p=>p.Shortname.Equals(i.Shortname, StringComparison.OrdinalIgnoreCase) && S(p.ExtraFields["LegendaryId"]).Equals(i.LegendaryId, StringComparison.OrdinalIgnoreCase))); all.Refresh();
        var selected=CollectionViewSource.GetDefaultView(prizes); selected.Filter=o=>o is Prize p && (p.Shortname.Contains(ps,StringComparison.OrdinalIgnoreCase) || p.DisplayName.Contains(ps,StringComparison.OrdinalIgnoreCase)); selected.Refresh();
    }
    private void ItemSearch_Changed(object sender,RoutedEventArgs e)=>RefreshFilters();
    private void PrizeSearch_Changed(object sender,TextChangedEventArgs e)=>RefreshFilters();
    private void Add_Click(object sender,RoutedEventArgs e)
    {
        var chosen=ItemsGrid.SelectedItems.Cast<RustItem>().ToList();
        foreach (var item in chosen)
        {
            if (prizes.Any(p => p.Shortname.Equals(item.Shortname, StringComparison.OrdinalIgnoreCase) &&
                S(p.ExtraFields["LegendaryId"]).Equals(item.LegendaryId, StringComparison.OrdinalIgnoreCase))) continue;
            var prize = new Prize { Shortname = item.Shortname, Amount = 1, Chance = 0, Skin = item.Skin,
                Rarity = string.IsNullOrEmpty(item.LegendaryId) ? PrizeRarity.Unknown : PrizeRarity.LegendaryCommon };
            if (!string.IsNullOrEmpty(item.LegendaryId))
            {
                prize.ExtraFields["LegendaryId"] = item.LegendaryId;
                prize.ExtraFields["LegendaryName"] = item.Name;
            }
            AddPrize(prize);
        }
        RefreshFilters(); UpdateOdds();
    }
    private void Remove_Click(object sender,RoutedEventArgs e) { foreach(var p in PrizesGrid.SelectedItems.Cast<Prize>().ToList()) prizes.Remove(p); }
    private void Duplicate_Click(object sender,RoutedEventArgs e) { foreach(var p in PrizesGrid.SelectedItems.Cast<Prize>().ToList())AddPrize(new Prize { Shortname=p.Shortname,Amount=p.Amount,Chance=p.Chance,Skin=p.Skin, Jackpot=p.Jackpot, ExtraFields=(JsonObject)p.ExtraFields.DeepClone(),Rarity=p.Rarity }); }
    private void Batch_Click(object sender,RoutedEventArgs e)
    {
        var chosen=PrizesGrid.SelectedItems.Cast<Prize>().ToList(); if(chosen.Count==0) { MessageBox.Show("Select prize rows first.");return; }
        var window = new Window { Title="Batch edit prizes", Width=320,Height=270,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=System.Windows.Media.Brushes.White,ResizeMode=ResizeMode.NoResize };
        var stack = new StackPanel { Margin=new Thickness(15) };
        stack.Children.Add(new TextBlock { Text="Amount (leave blank to keep)" }); var amount=new TextBox();stack.Children.Add(amount);
        stack.Children.Add(new TextBlock { Text="Chance % (leave blank to keep)" }); var chance=new TextBox();stack.Children.Add(chance);
        stack.Children.Add(new TextBlock { Text="Skin ID (leave blank to keep)" }); var skin=new TextBox();stack.Children.Add(skin);
        var apply=new Button{Content="Apply to selected",Margin=new Thickness(0,12,0,0)}; apply.Click+=(_,_)=>{
            int parsedAmount = 0; double parsedChance = 0; ulong parsedSkin = 0;
            bool hasAmount = amount.Text.Length > 0, hasChance = chance.Text.Length > 0, hasSkin = skin.Text.Length > 0;
            if ((hasAmount && (!int.TryParse(amount.Text,out parsedAmount) || parsedAmount<1)) ||
                (hasChance && (!double.TryParse(chance.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out parsedChance) || parsedChance<0 || parsedChance>100)) ||
                (hasSkin && !ulong.TryParse(skin.Text,out parsedSkin))) {MessageBox.Show("Invalid batch values");return;}
            foreach(var prize in chosen) { if(hasAmount)prize.Amount=parsedAmount; if(hasChance)prize.Chance=parsedChance; if(hasSkin)prize.Skin=parsedSkin.ToString(); }
            window.DialogResult=true;
        };stack.Children.Add(apply);window.Content=stack;window.ShowDialog();
    }
    private void PrizesGrid_CellEditEnding(object sender,DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(new Action(UpdateOdds));
    private void UpdateOdds()
    {
        if (OddsLabel == null || WinningChanceText == null || LosingChanceText == null || WinningChanceBar == null || ChanceWarningText == null) return;
        var total = prizes.Sum(p => p.Chance);
        var invalid = prizes.Any(p => double.IsNaN(p.Chance) || double.IsInfinity(p.Chance) || p.Chance < 0 || p.Chance > 100);
        var overLimit = total > maxWinningChance + 0.000001;
        var losing = Math.Max(0, 100 - total);
        WinningChanceText.Text = $"{total:0.###}%";
        LosingChanceText.Text = $"{losing:0.###}%";
        WinningChanceBar.Value = double.IsFinite(total) ? Math.Clamp(total, 0, 100) : 0;
        OddsLabel.Text = $"{prizes.Count} prizes   |   Win: {total:0.###}%   |   Lose: {losing:0.###}%";
        OddsLabel.Foreground = overLimit || invalid ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.LightGreen;
        ChanceWarningText.Visibility = overLimit || invalid ? Visibility.Visible : Visibility.Collapsed;
        ChanceWarningText.Text = invalid ? "Invalid prize chances: enter a value from 0 to 100 for each prize." :
            overLimit ? $"Total is {total - maxWinningChance:0.###}% over your app-only maximum of {maxWinningChance:0.###}%. Choose a suggested adjustment." : "";
        if (MaxWinChanceText != null) MaxWinChanceText.Text = $"Editor maximum: {maxWinningChance:0.###}%";
        if (ProbabilitySuggestionText != null) ProbabilitySuggestionText.Text = GetProbabilitySuggestion(total);
    }
    private sealed class EditorPreferences
    {
        public string? LastConfigPath { get; set; }
        public double MaxWinningChance { get; set; } = 65;
        public Dictionary<string, List<RarityRecord>> Rarities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> RarityWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private void LoadEditorPreferences()
    {
        try
        {
            if (!File.Exists(preferencesFile)) { UpdateOdds(); return; }
            var prefs = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(preferencesFile));
            if (prefs == null) return;
            foreach (var item in prefs.Rarities ?? new Dictionary<string, List<RarityRecord>>())
                rarityByConfig[item.Key] = item.Value ?? new List<RarityRecord>();
            if (prefs.RarityWeights != null)
            {
                foreach (var entry in prefs.RarityWeights)
                {
                    if (!Enum.TryParse<PrizeRarity>(entry.Key, true, out var key)) continue;
                    key = MigrateRarity(key);
                    if (double.IsFinite(entry.Value) && entry.Value >= 0 && entry.Value <= 1000000)
                        rarityWeights[key] = entry.Value;
                }
            }
            RefreshWeightEntries();
            if (double.IsFinite(prefs.MaxWinningChance) && prefs.MaxWinningChance >= 0 && prefs.MaxWinningChance <= 100)
                maxWinningChance = prefs.MaxWinningChance;
            if (MaxWinningChanceInput != null) MaxWinningChanceInput.Text = maxWinningChance.ToString("0.###", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(prefs.LastConfigPath))
            {
                if (File.Exists(prefs.LastConfigPath))
                {
                    try { ReadConfig(prefs.LastConfigPath); }
                    catch (Exception ex) { StatusLabel.Text = $"Could not reopen previous config: {ex.Message}"; }
                }
                else StatusLabel.Text = "Last config not found. Open another config to continue.";
            }
        }
        catch (Exception ex) { StatusLabel.Text = $"Editor preferences unavailable: {ex.Message}"; }
        UpdateOdds();
    }
    private void SaveEditorPreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesFile)!);
            // Do not save odds controls or the opened config into BeanSlots.json.
            var last = path;
            if (string.IsNullOrWhiteSpace(last) && File.Exists(preferencesFile))
                last = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(preferencesFile))?.LastConfigPath;
            if (!string.IsNullOrWhiteSpace(path)) SnapshotPrizeRarities(path);
            var prefs = new EditorPreferences { LastConfigPath = last, MaxWinningChance = maxWinningChance,
                Rarities = new Dictionary<string, List<RarityRecord>>(rarityByConfig, StringComparer.OrdinalIgnoreCase),
                RarityWeights = rarityWeights.ToDictionary(k => k.Key.ToString(), v => v.Value) };
            File.WriteAllText(preferencesFile, JsonSerializer.Serialize(prefs, jsonOptions));
        }
        catch (Exception ex) { StatusLabel.Text = $"Couldn't save editor preferences: {ex.Message}"; }
    }
    private void MaxWinningChance_Changed(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(MaxWinningChanceInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < 0 || value > 100)
        { MessageBox.Show("Maximum winning chance must be between 0 and 100 (use a decimal point).", "App-only setting"); MaxWinningChanceInput.Text = maxWinningChance.ToString("0.###", CultureInfo.InvariantCulture); return; }
        maxWinningChance = value;
        SaveEditorPreferences();
        UpdateOdds();
    }
    private double RarityWeight(PrizeRarity rarity) =>
        rarityWeights.TryGetValue(MigrateRarity(rarity), out var value) ? value : 1d;
    private void RefreshWeightEntries()
    {
        rarityWeightEntries.Clear();
        foreach (var rarity in Enum.GetValues<PrizeRarity>().Where(r => r != PrizeRarity.Legendary))
            rarityWeightEntries.Add(new RarityWeightEntry { Rarity = rarity, Weight = RarityWeight(rarity) });
    }
    private void SaveRarityWeights_Click(object sender, RoutedEventArgs e)
    {
        RarityWeightsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        RarityWeightsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        if (rarityWeightEntries.Any(w => !double.IsFinite(w.Weight) || w.Weight < 0 || w.Weight > 1000000))
        {
            MessageBox.Show("Each weight must be a finite number between 0 and 1,000,000.", "Invalid rarity weight");
            return;
        }
        foreach (var row in rarityWeightEntries) rarityWeights[row.Rarity] = row.Weight;
        SaveEditorPreferences();
        UpdateOdds();
        StatusLabel.Text = "Rarity suggestion weights saved to local app settings.";
    }
    private void ResetRarityWeights_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset all rarity suggestion weights to their defaults?", "Reset weights",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        rarityWeights.Clear();
        foreach (var kv in DefaultWeights()) rarityWeights[kv.Key] = kv.Value;
        RefreshWeightEntries();
        SaveEditorPreferences();
        UpdateOdds();
    }
    private double[] RarityChances()
    {
        var weights = prizes.Select(p => RarityWeight(p.Rarity)).ToArray();
        var sum = weights.Sum();
        if (sum <= 0) return new double[weights.Length];
        var chances = weights.Select(w => Math.Floor(maxWinningChance * w / sum * 1000000) / 1000000).ToArray();
        if (chances.Length > 0) chances[0] += Math.Round(maxWinningChance - chances.Sum(), 6);
        return chances;
    }
    private void ApplyRaritySuggestions_Click(object sender, RoutedEventArgs e)
    {
        if (prizes.Count == 0) return;
        if (rarityWeightEntries.Any(w => w.Weight != RarityWeight(w.Rarity)))
        {
            MessageBox.Show("Save your rarity weights before applying suggestions.", "Unsaved weights");
            return;
        }
        if (rarityWeights.Values.Sum() <= 0 || prizes.All(p => RarityWeight(p.Rarity) <= 0))
        {
            MessageBox.Show("Set at least one positive weight for a rarity used by your prizes.", "No eligible weights");
            return;
        }
        if (MessageBox.Show($"Redistribute all {prizes.Count} prize chances based on their rarities, up to {maxWinningChance:0.###}%? Existing individual percentages will be replaced.",
            "Apply rarity suggestions", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var suggestions = RarityChances();
        for (int i = 0; i < prizes.Count; i++) prizes[i].Chance = suggestions[i];
        UpdateOdds();
    }
    private string GetProbabilitySuggestion(double total)
    {
        if (prizes.Count == 0) return "Add prizes and assign their individual win chances.";
        if (prizes.Any(p => !double.IsFinite(p.Chance) || p.Chance < 0 || p.Chance > 100)) return "Correct invalid individual prize odds first.";
        if (total > maxWinningChance + 0.000001)
            return $"Over maximum by {total - maxWinningChance:0.###} points. Apply rarity-weighted suggestions, or scale existing odds. Edit the rarity weights in the Rarity Weights tab.";
        if (total == 0) return "No prizes can currently win. Apply rarity suggestions to assign weighted odds, or enter percentages manually.";
        var proposal = RarityChances();
        var legCount = prizes.Count(p => RarityLabel(p.Rarity).StartsWith("Legendary", StringComparison.Ordinal));
        var legendaryTotal = prizes.Select((p, i) => (p, i)).Where(x => RarityLabel(x.p.Rarity).StartsWith("Legendary", StringComparison.Ordinal)).Sum(x => proposal[x.i]);
        return $"Within your {maxWinningChance:0.###}% max; {maxWinningChance - total:0.###} points remain. Rarity suggestion: {legCount} Legendary prizes share {legendaryTotal:0.###}% of spins. Click Apply to use weighted odds.";
    }
    private void ScaleProbabilities_Click(object sender, RoutedEventArgs e)
    {
        var total = prizes.Sum(p => p.Chance);
        if (prizes.Count == 0 || total <= 0 || !double.IsFinite(total)) { MessageBox.Show("Add positive prize chances before scaling."); return; }
        if (prizes.Any(p => !double.IsFinite(p.Chance) || p.Chance < 0 || p.Chance > 100)) { MessageBox.Show("Fix invalid chances before scaling."); return; }
        var ratio = maxWinningChance / total;
        foreach (var prize in prizes) prize.Chance = Math.Round(prize.Chance * ratio, 6);
        // Correct rounding drift; never exceed the chosen cap.
        var excess = prizes.Sum(p => p.Chance) - maxWinningChance;
        if (excess > 0 && prizes.Count > 0) prizes[0].Chance = Math.Max(0, prizes[0].Chance - excess);
        UpdateOdds();
    }
    private void DistributeProbabilities_Click(object sender, RoutedEventArgs e)
    {
        if (prizes.Count == 0) return;
        var per = Math.Floor(maxWinningChance / prizes.Count * 1000000) / 1000000;
        foreach (var prize in prizes) prize.Chance = per;
        prizes[0].Chance = Math.Round(prizes[0].Chance + (maxWinningChance - prizes.Sum(p => p.Chance)), 6);
        UpdateOdds();
    }
    private void LoadSavedLegendaryCatalog()
    {
        if (!File.Exists(legendaryCacheFile)) return;
        try { ParseLegendaryDefinitions(File.ReadAllText(legendaryCacheFile)); }
        catch { /* A damaged previous import must not stop the editor from opening. */ }
    }

    private int ParseLegendaryDefinitions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("Weapons", out var weapons) || weapons.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected a LegendaryItems config containing a Weapons object.");
        var parsed = new Dictionary<string, RustItem>(StringComparer.OrdinalIgnoreCase);
        static string FieldOr(JsonElement obj, string name, string fallback = "") =>
            obj.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : fallback;
        foreach (var definition in weapons.EnumerateObject())
        {
            var obj = definition.Value;
            if (obj.ValueKind != JsonValueKind.Object ||
                !bool.TryParse(FieldOr(obj, "Enabled", "true"), out var enabled) || !enabled) continue;
            var shortname = FieldOr(obj, "Base weapon shortname").Trim();
            if (string.IsNullOrEmpty(shortname)) continue;
            var type = FieldOr(obj, "Item type (Weapon, Backpack, Clothing)", "Weapon");
            var category = type.Equals("Backpack", StringComparison.OrdinalIgnoreCase) ? "Legendary Backpacks" :
                type.Equals("Clothing", StringComparison.OrdinalIgnoreCase) ? "Legendary Clothing" : "Legendary Weapons";
            var name = FieldOr(obj, "Display name", definition.Name).Trim();
            var skin = FieldOr(obj, "Skin ID", "0").Trim();
            if (!ulong.TryParse(skin, out _)) skin = "0";
            parsed[definition.Name] = new RustItem { Shortname = shortname,
                LegendaryId = definition.Name, Name = string.IsNullOrWhiteSpace(name) ? definition.Name : name,
                Skin = skin, Category = category };
        }
        legendaryCatalog.Clear();
        foreach (var entry in parsed) legendaryCatalog[entry.Key] = entry.Value;
        return parsed.Count;
    }

    private void ImportLegendary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "LegendaryItems config (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import LegendaryItems configuration" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var count = ParseLegendaryDefinitions(json);
            if (count == 0) throw new InvalidDataException("The config contained no enabled Legendary items with valid base shortnames.");
            Directory.CreateDirectory(Path.GetDirectoryName(legendaryCacheFile)!);
            File.WriteAllText(legendaryCacheFile, json);
            // Remove only previous Legendary catalog entries; preserve vanilla catalog and prizes.
            foreach (var previous in items.Where(i => !string.IsNullOrEmpty(i.LegendaryId)).ToList()) items.Remove(previous);
            foreach (var legendary in legendaryCatalog.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (catalog.TryGetValue(legendary.Shortname, out var normal)) legendary.Icon = normal.Icon;
                items.Add(legendary);
            }
            foreach (var prize in prizes) AttachPrizeIcon(prize);
            selectedCategory = legendaryCatalog.Values.Any(x => x.Category == "Legendary Weapons") ? "Legendary Weapons" :
                legendaryCatalog.Values.First().Category;
            ItemSearch.Text = "";
            RefreshCategoryOptions(); RefreshFilters();
            StatusLabel.Text = $"Imported {count} enabled Legendary items. Browse them using the Legendary category filters.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "LegendaryItems import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Import_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="Rust item list JSON (*.json)|*.json|All files (*.*)|*.*"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var node=JsonNode.Parse(File.ReadAllText(dialog.FileName));
            var found=new Dictionary<string,RustItem>(StringComparer.OrdinalIgnoreCase);
            void Walk(JsonNode? n)
            {
                if(n is JsonArray array)foreach(var child in array)Walk(child);
                if(n is JsonObject obj)
                {
                    string shortname=S(obj["shortname"]??obj["Shortname"]??obj["shortName"]);
                    string name=S(obj["displayName"]??obj["DisplayName"]??obj["name"]??obj["Name"],shortname);
                    if(!string.IsNullOrWhiteSpace(shortname) && !shortname.Contains(' ') && shortname.Length<100)found[shortname]=new RustItem{Shortname=shortname,Name=name,IconUrl=S(obj["iconUrl"]??obj["IconUrl"]??obj["icon_url"]), Category=ResolveCategory(shortname,S(obj["category"]??obj["Category"],"Uncategorized"))};
                    foreach(var property in obj)if(property.Value is JsonArray || property.Value is JsonObject)Walk(property.Value);
                }
            }
            Walk(node);
            if(found.Count==0)throw new Exception("No items found. Expected an array of objects with shortname and displayName/name fields.");
            ApplyCatalog(found.Values.ToList());StatusLabel.Text=$"Imported {items.Count} Rust items";
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Import item list failed");}
    }
    private void LoadIconManifest()
    {
        try
        {
            if (File.Exists(iconManifestFile))
            {
                var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(iconManifestFile));
                if (stored != null) foreach (var entry in stored) iconManifest[entry.Key] = entry.Value;
            }
        }
        catch { /* An invalid manifest should not prevent the editor starting. */ }
    }

    private async Task InitializeRustHelpAsync()
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            LoadIconManifest();
            if (File.Exists(catalogFile))
            {
                ApplyCatalog(ParseCatalog(await File.ReadAllTextAsync(catalogFile)));
                StatusLabel.Text = $"Loaded {items.Count} RustHelp items from %LOCALAPPDATA% cache.";
            }
            else
            {
                if (File.Exists(bundledItemsFile))
                {
                    ApplyCatalog(ParseCatalog(await File.ReadAllTextAsync(bundledItemsFile)));
                    StatusLabel.Text = $"Loaded {items.Count} bundled Rust items. Use Check new items & images to update.";
                }
                else await CheckForUpdatesAsync(false);
            }
        }
        catch (Exception ex) { StatusLabel.Text = $"Catalog unavailable: {ex.Message}. Using offline items."; }
    }

    private async void UpdateRustHelp_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private static string IconPath(string shortname) => Path.Combine(cacheDir, "icons", Uri.EscapeDataString(shortname) + ".png");

    private async Task CheckForUpdatesAsync(bool showErrors)
    {
        if (!await updateGate.WaitAsync(0)) return;
        UpdateRustHelpButton.IsEnabled = false;
        try
        {
            StatusLabel.Text = "Checking RustHelp for new items and changed images...";
            var json = await http.GetStringAsync(rustHelpItemsUrl);
            var incoming = ParseCatalog(json);
            if (incoming.Count < 100) throw new Exception("RustHelp returned an unexpectedly small item catalog.");
            int newItems = incoming.Count(i => !catalog.ContainsKey(i.Shortname));
            int changedItems = incoming.Count(i => catalog.TryGetValue(i.Shortname, out var old) &&
                (old.Name != i.Name || old.IconUrl != i.IconUrl));
            int missingImages = incoming.Count(i => IsValidIconUrl(i.IconUrl) && !File.Exists(IconPath(i.Shortname)));
            int changedImages = incoming.Count(i => IsValidIconUrl(i.IconUrl) && File.Exists(IconPath(i.Shortname)) &&
                iconManifest.TryGetValue(i.Shortname, out var previousUrl) && previousUrl != i.IconUrl);
            int removedItems = catalog.Keys.Count(key => !incoming.Any(i => i.Shortname.Equals(key, StringComparison.OrdinalIgnoreCase)));
            await File.WriteAllTextAsync(catalogFile, json);
            ApplyCatalog(incoming);
            StatusLabel.Text = $"RustHelp check: {newItems} new items, {changedItems} changed, {removedItems} removed; {missingImages} missing images, {changedImages} changed URLs. Downloading images...";
            await LoadIconsAsync(incoming.ToList());
            await File.WriteAllTextAsync(iconManifestFile, JsonSerializer.Serialize(iconManifest, jsonOptions));
            StatusLabel.Text = $"Update complete: {newItems} new, {changedItems} changed, {removedItems} removed; {missingImages} missing and {changedImages} changed image candidates checked. Cache: %LOCALAPPDATA%\\BeanSlotsEditor\\RustHelp";
            if (showErrors) MessageBox.Show($"Item catalog: {incoming.Count} items\nNew items: {newItems}\nChanged items: {changedItems}\nRemoved items: {removedItems}\nMissing images checked: {missingImages}\nChanged image URLs checked: {changedImages}\n\nCache: {cacheDir}", "RustHelp update complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"RustHelp check failed; existing local cache retained: {ex.Message}";
            if (showErrors) MessageBox.Show(ex.Message, "RustHelp update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { UpdateRustHelpButton.IsEnabled = true; updateGate.Release(); }
    }

    private static bool IsValidIconUrl(string input) =>
        Uri.TryCreate(input, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps &&
        (url.Host.Equals("cdn.rusthelp.com", StringComparison.OrdinalIgnoreCase) || url.Host.Equals("rusthelp.com", StringComparison.OrdinalIgnoreCase));

    private List<RustItem> ParseCatalog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new Exception("Expected RustHelp item array.");
        static string Field(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var found = new Dictionary<string, RustItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var shortname = Field(entry, "shortName");
            if (string.IsNullOrWhiteSpace(shortname)) continue;
            var display = Field(entry, "displayName");
            found[shortname] = new RustItem { Shortname = shortname, Name = string.IsNullOrWhiteSpace(display) ? shortname : display, IconUrl = Field(entry, "iconUrl"), Category = ResolveCategory(shortname, Field(entry, "category")) };
        }
        return found.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ApplyCatalog(IReadOnlyCollection<RustItem> incoming)
    {
        catalog.Clear(); items.Clear(); imageRequested.Clear();
        foreach (var item in incoming.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            item.Category = ResolveCategory(item.Shortname, item.Category);
            catalog[item.Shortname] = item;
            items.Add(item);
        }
        // Legendary items are separate products even when they share a vanilla shortname.
        // Reapply them after every RustHelp refresh so imported definitions persist.
        foreach (var legendary in legendaryCatalog.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (catalog.TryGetValue(legendary.Shortname, out var vanilla)) legendary.Icon = vanilla.Icon;
            items.Add(legendary);
        }
        foreach (var prize in prizes) AttachPrizeIcon(prize);
        RefreshCategoryOptions();
        RefreshFilters();
        _ = LoadIconsAsync(incoming.ToList());
    }

    // LegendaryId is the stable identifier supplied by the LegendaryItems integration.
    // Keep it authoritative for prize labels: RustHelp's catalog only describes the
    // underlying base item and must never replace a legendary prize's name.
    private static string LegendaryPrizeName(Prize prize)
    {
        var legendaryId = S(prize.ExtraFields["LegendaryId"]).Trim();
        if (string.IsNullOrWhiteSpace(legendaryId)) return "";

        // An explicitly supplied custom display name takes precedence when available.
        var customName = S(prize.ExtraFields["LegendaryName"]).Trim();
        if (!string.IsNullOrWhiteSpace(customName)) return customName;

        // Configs usually only contain LegendaryId. Turn e.g. hellfire_ak into
        // Hellfire AK and capacitor_pack into Capacitor Pack for the editor UI.
        var words = legendaryId.Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(word => word.Equals("ak", StringComparison.OrdinalIgnoreCase)
            ? "AK" : word.Equals("smg", StringComparison.OrdinalIgnoreCase)
            ? "SMG" : word.Equals("c4", StringComparison.OrdinalIgnoreCase)
            ? "C4" : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private void AttachPrizeIcon(Prize prize)
    {
        var legendaryName = LegendaryPrizeName(prize);
        var id = S(prize.ExtraFields["LegendaryId"]);
        if (!string.IsNullOrWhiteSpace(id) && legendaryCatalog.TryGetValue(id, out var legendary))
        {
            prize.DisplayName = legendary.Name;
            prize.Icon = catalog.TryGetValue(prize.Shortname, out var baseItem) ? baseItem.Icon : null;
        }
        else if (catalog.TryGetValue(prize.Shortname, out var item))
        {
            prize.DisplayName = !string.IsNullOrWhiteSpace(legendaryName) ? legendaryName : item.Name;
            prize.Icon = item.Icon;
        }
        else
        {
            prize.DisplayName = !string.IsNullOrWhiteSpace(legendaryName) ? legendaryName : prize.Shortname;
            prize.Icon = null;
        }
    }

    private async Task LoadIconsAsync(List<RustItem> entries)
    {
        // Cache all known icons without blocking the WPF UI thread or overwhelming RustHelp's CDN.
        var tasks = entries.Where(i => !string.IsNullOrWhiteSpace(i.IconUrl))
            .Select(LoadIconAsync).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task LoadIconAsync(RustItem item)
    {
        if (!IsValidIconUrl(item.IconUrl)) return;
        if (!imageRequested.Add(item.Shortname)) return;
        await imageSlots.WaitAsync();
        try
        {
            var destination = IconPath(item.Shortname);
            var mustDownload = !File.Exists(destination) ||
                (iconManifest.TryGetValue(item.Shortname, out var lastUrl) && lastUrl != item.IconUrl);
            if (mustDownload)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var bytes = await http.GetByteArrayAsync(item.IconUrl);
                if (bytes.Length == 0 || bytes.Length > 2_000_000) throw new IOException("Icon file was empty or too large.");
                // Validate before replacing a working cached icon.
                await Task.Run(() =>
                {
                    using var memory = new MemoryStream(bytes);
                    var decoder = BitmapDecoder.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) throw new IOException("Invalid icon file.");
                });
                var temporary = destination + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, destination, true);
            }
            var image = await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(destination, UriKind.Absolute);
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.DecodePixelWidth = 64;
                bmp.EndInit(); bmp.Freeze();
                return bmp;
            });
            iconManifest[item.Shortname] = item.IconUrl;
            await Dispatcher.InvokeAsync(() =>
            {
                if (!catalog.TryGetValue(item.Shortname, out var current) || !ReferenceEquals(current, item)) return;
                item.Icon = image;
                foreach (var legendary in legendaryCatalog.Values.Where(l => l.Shortname.Equals(item.Shortname, StringComparison.OrdinalIgnoreCase))) legendary.Icon = image;
                foreach (var prize in prizes.Where(p => p.Shortname.Equals(item.Shortname, StringComparison.OrdinalIgnoreCase))) prize.Icon = image;
            });
        }
        catch { /* A failed download never overwrites a working cache; retry on next check. */ }
        finally { imageSlots.Release(); }
    }

}
