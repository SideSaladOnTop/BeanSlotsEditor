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
        private ImageSource? icon;
        public ImageSource? Icon { get => icon; set { icon = value; PropertyChanged?.Invoke(this, new(nameof(Icon))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public class Prize : INotifyPropertyChanged
    {
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
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
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
        foreach (var raw in itemSeed) { var a = raw.Split('|'); if (a.Length == 2) items.Add(new RustItem { Shortname = a[0], Name = a[1] }); }
        ItemsGrid.ItemsSource = CollectionViewSource.GetDefaultView(items);
        PrizesGrid.ItemsSource = CollectionViewSource.GetDefaultView(prizes);
        prizes.CollectionChanged += (_, _) => { if (!loading) dirty = true; RefreshFilters(); UpdateOdds(); };
        NewDefaults();
        _ = InitializeRustHelpAsync();
    }
    private static string S(JsonNode? n, string fallback = "") => n?.ToString() ?? fallback;
    private static JsonObject Obj(JsonObject parent, string key) { if (parent[key] is not JsonObject o) { o = new JsonObject(); parent[key] = o; } return o; }
    private void NewDefaults()
    {
        loading = true; root = new JsonObject(); path = null;
        root["StartingSpins"] = 5; root["MaxSpinsPerClick"] = 10; root["AnimationFrames"] = 18; root["AnimationIntervalSeconds"] = 0.14;
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
            if (!loading) dirty = true;
            UpdateOdds();
        };
        prizes.Add(p);
        AttachPrizeIcon(p);
    }
    private void LoadFields()
    {
        StartingSpins.Text = S(root["StartingSpins"], "5"); MaxSpins.Text = S(root["MaxSpinsPerClick"], "10"); Frames.Text = S(root["AnimationFrames"], "18"); FrameInterval.Text = S(root["AnimationIntervalSeconds"], "0.14");
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
            root["StartingSpins"] = Int(StartingSpins,0,1000000); root["MaxSpinsPerClick"] = Int(MaxSpins,1,100); root["AnimationFrames"] = Int(Frames,6,50); root["AnimationIntervalSeconds"] = Double(FrameInterval,.06,.8);
            root["SpinsPerPurchase"] = Int(SpinsPerPurchase,1,1000000); root["PurchaseCost"] = Double(PurchaseCost,0,1000000000); root["EconomicsPurchaseEnabled"] = EconomicsEnabled.IsChecked == true;
            var raid = Obj(root,"RaidSpins"); raid["Easy"] = Int(Easy,0,1000000); raid["Medium"] = Int(Medium,0,1000000); raid["Hard"] = Int(Hard,0,1000000); raid["Expert"] = Int(Expert,0,1000000); raid["Nightmare"] = Int(Nightmare,0,1000000); root["RewardAllRaiders"] = RewardAllRaiders.IsChecked == true;
            var o = Obj(root,"OverlayButton"); o["Enabled"] = OverlayEnabled.IsChecked == true; o["Left"] = Double(OverlayLeft,0,1); o["Bottom"] = Double(OverlayBottom,0,1); o["Width"] = Double(OverlayWidth,.005,1); o["Height"] = Double(OverlayHeight,.005,1); if (double.Parse(OverlayLeft.Text,CultureInfo.InvariantCulture)+double.Parse(OverlayWidth.Text,CultureInfo.InvariantCulture)>1 || double.Parse(OverlayBottom.Text,CultureInfo.InvariantCulture)+double.Parse(OverlayHeight.Text,CultureInfo.InvariantCulture)>1) throw new Exception("Overlay extends beyond screen bounds.");
            o["Text"] = OverlayText.Text; o["FontSize"] = Int(OverlayFont,6,32); o["ButtonColor"] = OverlayButtonColor.Text; o["TextColor"] = OverlayTextColor.Text;
            var list = new JsonArray();
            foreach(var p in prizes)
            {
                if (string.IsNullOrWhiteSpace(p.Shortname) || p.Amount < 1 || p.Chance < 0 || p.Chance > 100 || double.IsNaN(p.Chance) || !ulong.TryParse(p.Skin, out var skin)) throw new Exception("Invalid prize: " + p.Shortname + ". Amount >= 1, chance 0–100, skin unsigned integer.");
                var entry = new JsonObject { ["Shortname"] = p.Shortname.Trim(), ["Amount"] = p.Amount, ["Chance"] = p.Chance, ["Skin"] = skin }; list.Add(entry);
            }
            if (prizes.Sum(p=>p.Chance) > 100.000001) throw new Exception("Prize probabilities total over 100%. Reduce some chances to save.");
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
        { AddPrize(new Prize { Shortname = S(obj["Shortname"]), Amount = int.TryParse(S(obj["Amount"]), out var amt) ? amt:1, Chance = double.TryParse(S(obj["Chance"]), NumberStyles.Float,CultureInfo.InvariantCulture,out var c)?c:0, Skin=S(obj["Skin"],"0") }); }
        LoadFields(); path=file; PathLabel.Text=Path.GetFileName(file); dirty=false; loading=false; UpdateOdds(); StatusLabel.Text=$"Loaded {prizes.Count} prizes";
    }
    private bool Save(string? destination)
    {
        if(string.IsNullOrEmpty(destination)) return SaveAs();
        if(!CollectFields()) return false;
        try { File.WriteAllText(destination, root.ToJsonString(jsonOptions)); path=destination; dirty=false; PathLabel.Text=Path.GetFileName(destination); StatusLabel.Text=$"Saved {prizes.Count} prizes"; return true; }
        catch(Exception ex) { MessageBox.Show(ex.Message,"Save failed",MessageBoxButton.OK,MessageBoxImage.Error); return false; }
    }
    private bool SaveAs() { var dialog = new SaveFileDialog { Filter="JSON config (*.json)|*.json", FileName="BeanSlots.json" }; return dialog.ShowDialog(this)==true && Save(dialog.FileName); }
    private void Open_Click(object sender, RoutedEventArgs e) { if(!ConfirmLeave())return; var dlg=new OpenFileDialog { Filter="JSON config (*.json)|*.json" }; if(dlg.ShowDialog(this)!=true)return; try { ReadConfig(dlg.FileName); } catch(Exception ex) { MessageBox.Show(ex.Message,"Open failed"); } }
    private void Save_Click(object sender, RoutedEventArgs e) => Save(path);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();
    private void New_Click(object sender, RoutedEventArgs e) { if(ConfirmLeave()) NewDefaults(); }
    private bool ConfirmLeave() { if(!dirty && path==null && prizes.Count==0)return true; var answer=MessageBox.Show("Save any unsaved changes before continuing?", "BeanSlots Editor", MessageBoxButton.YesNoCancel,MessageBoxImage.Question); if(answer==MessageBoxResult.Cancel)return false; return answer!=MessageBoxResult.Yes || Save(path); }
    private void Window_Closing(object? sender, CancelEventArgs e) { if(!ConfirmLeave())e.Cancel=true; }
    private void RefreshFilters()
    {
        if(ItemsGrid==null || PrizesGrid==null)return;
        var search=ItemSearch?.Text?.Trim()??""; var ps=PrizeSearch?.Text?.Trim()??"";
        var all=CollectionViewSource.GetDefaultView(items); all.Filter=o => o is RustItem i && (i.Name.Contains(search,StringComparison.OrdinalIgnoreCase)||i.Shortname.Contains(search,StringComparison.OrdinalIgnoreCase)) && (HideSelected?.IsChecked!=true || !prizes.Any(p=>p.Shortname==i.Shortname)); all.Refresh();
        var selected=CollectionViewSource.GetDefaultView(prizes); selected.Filter=o=>o is Prize p && (p.Shortname.Contains(ps,StringComparison.OrdinalIgnoreCase) || p.DisplayName.Contains(ps,StringComparison.OrdinalIgnoreCase)); selected.Refresh();
    }
    private void ItemSearch_Changed(object sender,RoutedEventArgs e)=>RefreshFilters();
    private void PrizeSearch_Changed(object sender,TextChangedEventArgs e)=>RefreshFilters();
    private void Add_Click(object sender,RoutedEventArgs e)
    {
        var chosen=ItemsGrid.SelectedItems.Cast<RustItem>().ToList();
        foreach(var item in chosen)if(!prizes.Any(p=>p.Shortname==item.Shortname)) AddPrize(new Prize { Shortname=item.Shortname,Amount=1,Chance=0,Skin="0" });
        RefreshFilters(); UpdateOdds();
    }
    private void Remove_Click(object sender,RoutedEventArgs e) { foreach(var p in PrizesGrid.SelectedItems.Cast<Prize>().ToList()) prizes.Remove(p); }
    private void Duplicate_Click(object sender,RoutedEventArgs e) { foreach(var p in PrizesGrid.SelectedItems.Cast<Prize>().ToList())AddPrize(new Prize { Shortname=p.Shortname,Amount=p.Amount,Chance=p.Chance,Skin=p.Skin }); }
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
        var overLimit = total > 100.000001;
        var losing = Math.Max(0, 100 - total);
        WinningChanceText.Text = $"{total:0.###}%";
        LosingChanceText.Text = $"{losing:0.###}%";
        WinningChanceBar.Value = double.IsFinite(total) ? Math.Clamp(total, 0, 100) : 0;
        OddsLabel.Text = $"{prizes.Count} prizes   |   Win: {total:0.###}%   |   Lose: {losing:0.###}%";
        OddsLabel.Foreground = overLimit || invalid ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.LightGreen;
        ChanceWarningText.Visibility = overLimit || invalid ? Visibility.Visible : Visibility.Collapsed;
        ChanceWarningText.Text = invalid ? "Invalid prize chances: enter a value from 0 to 100 for each prize." :
            overLimit ? $"Total is {total - 100:0.###}% over 100%. Reduce prize chances before saving." : "";
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
                    if(!string.IsNullOrWhiteSpace(shortname) && !shortname.Contains(' ') && shortname.Length<100)found[shortname]=new RustItem{Shortname=shortname,Name=name,IconUrl=S(obj["iconUrl"]??obj["IconUrl"]??obj["icon_url"])};
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
                await CheckForUpdatesAsync(false);
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

    private static List<RustItem> ParseCatalog(string json)
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
            found[shortname] = new RustItem { Shortname = shortname, Name = string.IsNullOrWhiteSpace(display) ? shortname : display, IconUrl = Field(entry, "iconUrl") };
        }
        return found.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ApplyCatalog(IReadOnlyCollection<RustItem> incoming)
    {
        catalog.Clear(); items.Clear(); imageRequested.Clear();
        foreach (var item in incoming.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            catalog[item.Shortname] = item;
            items.Add(item);
        }
        foreach (var prize in prizes) AttachPrizeIcon(prize);
        RefreshFilters();
        _ = LoadIconsAsync(incoming.ToList());
    }

    private void AttachPrizeIcon(Prize prize)
    {
        if (catalog.TryGetValue(prize.Shortname, out var item))
        {
            prize.DisplayName = item.Name;
            prize.Icon = item.Icon;
        }
        else
        {
            prize.DisplayName = prize.Shortname;
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
                foreach (var prize in prizes.Where(p => p.Shortname.Equals(item.Shortname, StringComparison.OrdinalIgnoreCase))) prize.Icon = image;
            });
        }
        catch { /* A failed download never overwrites a working cache; retry on next check. */ }
        finally { imageSlots.Release(); }
    }

}
