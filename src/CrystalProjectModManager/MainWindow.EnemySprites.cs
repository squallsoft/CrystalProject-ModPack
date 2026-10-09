using CrystalProjectModManager.Sprites;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrystalProjectModManager;

public partial class MainWindow
{
    IReadOnlyList<SpriteEntry> enemySprites = [];
    HashSet<string> installedSpriteChanges = [];
    Dictionary<string, int> installedSpriteScales = [];
    SpritePlan? spritePlan;
    string? spriteScanGame, selectedSpriteName;
    string spriteSearch = "";
    EnemySprites SpriteService() => new(config.GamePath ?? throw new IOException("Choose the game folder in Installation / Game first."), library.Store);
    sealed record SpriteRow(SpriteEntry Sprite, string Detail)
    {
        public string Name => Sprite.Name;
    }
    UIElement EnemySpritesPage()
    {
        if (config.GamePath == null)
            return Card(Stack(Text("Choose your game installation", 22), Text("Enemy sprites are read from your installed game."), Button("Choose Game Folder", () => ShowPage("Installation / Game"), true)));
        bool loaded = enemySprites.Count > 0 && spriteScanGame == config.GamePath;
        var extract = Button("Extract All Sprites", ExtractEnemySprites, true); extract.IsEnabled = loaded;
        var validate = Button("Validate Edited Folder", () => _ = ValidateEnemySprites()); validate.IsEnabled = loaded && config.EnemySpritesFolder != null;
        var apply = Button(spritePlan == null ? "Validate Before Applying" : $"Apply {spritePlan.Changes.Count} Sprite Updates", () => _ = ApplyEnemySprites(), true); apply.IsEnabled = spritePlan?.Changes.Count > 0;
        var restore = Button("Restore Backed-Up Sprites", () => _ = RestoreEnemySprites()); restore.IsEnabled = SpriteService().HasBackup;
        var hd = Button(installed?.HdSprites == true ? "HD Support Installed" : "Install HD Support", () => _ = InstallHdSprites()); hd.IsEnabled = loaded && installed != null && !installed.HdSprites;
        var removeHd = Button("Remove HD Support", () => _ = RemoveHdSprites()); removeHd.IsEnabled = installed?.HdSprites == true;
        var summary = Card(Stack(Text(loaded ? $"{enemySprites.Count} enemy sprites · {installedSpriteChanges.Count} updated" : "Read the enemy sprite library", 22),
            Text("Use original, 2×, 4×, or 10× PNGs. Keep filenames, proportions, and transparency. HD rendering preserves the original in-game size.", 13, "#A6BAC5"),
            Actions(extract, Button("Choose Edited Folder", ChooseEnemySpriteFolder), validate, apply),
            Actions(Button("Reload Sprites", () => _ = LoadEnemySprites()), Button("Open Editing Folder", () => Open(config.EnemySpritesFolder)), restore, hd, removeHd),
            Text(config.EnemySpritesFolder ?? "No editing folder selected", 12, "#65D6C0"),
            Text(spritePlan == null ? "Choose an edited folder to check for updates. HD support installs automatically for HD images." : $"{spritePlan.Changes.Count} pending updates · {spritePlan.HdSprites} HD sprites after apply. Edits are checked when you return to this app.", 12, "#A6BAC5")));
        var browser = new Grid { Height = ActualHeight < 700 ? 330 : double.NaN };
        browser.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); browser.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var left = new Grid { Margin = new(0, 0, 16, 0) }; left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.RowDefinitions.Add(new());
        var query = new TextBox { Text = spriteSearch, Margin = new(0, 0, 0, 10), ToolTip = "Search enemy sprite filenames" };
        System.Windows.Automation.AutomationProperties.SetName(query, "Search enemy sprites");
        var list = new ListBox { ItemTemplate = CueTemplate() }; Grid.SetRow(list, 1);
        var preview = new Grid(); preview.RowDefinitions.Add(new());
        for (int i = 0; i < 3; i++) preview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var image = new Image { Stretch = Stretch.Uniform, Margin = new(0, 0, 0, 12) }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var label = Text("Select a sprite to preview", 14); var detail = Text("", 12, "#A6BAC5");
        var replace = Button("Replace Selected PNG", () => ReplaceEnemySprite((list.SelectedItem as SpriteRow)?.Sprite)); replace.IsEnabled = false;
        Grid.SetRow(label, 1); Grid.SetRow(detail, 2); Grid.SetRow(replace, 3);
        preview.Children.Add(image); preview.Children.Add(label); preview.Children.Add(detail); preview.Children.Add(replace);
        var previewCard = Card(preview); Grid.SetColumn(previewCard, 1); previewCard.Margin = new(0);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not SpriteRow row) { image.Source = null; label.Text = "No matching sprites"; detail.Text = ""; replace.IsEnabled = false; return; }
            selectedSpriteName = row.Sprite.Name; label.Text = row.Sprite.Filename;
            try
            {
                byte[] png = row.Sprite.Png; string? path = config.EnemySpritesFolder == null ? null : Path.Combine(config.EnemySpritesFolder, row.Sprite.Filename);
                bool edited = path != null && File.Exists(path);
                if (edited) { EnemySprites.SafePath(path!); png = File.ReadAllBytes(path!); EnemySprites.ValidatePng(png); }
                using var stream = new MemoryStream(png);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image.Source = bitmap;
                int factor = EnemySprites.Scale(bitmap.PixelWidth, bitmap.PixelHeight, row.Sprite.Width, row.Sprite.Height);
                RenderOptions.SetBitmapScalingMode(image, factor > 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
                detail.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} · {factor}×{(factor > 1 ? " HD" : " original size")} · {(edited ? "Editing folder" : "Original")}";
                replace.IsEnabled = config.EnemySpritesFolder != null;
            }
            catch (Exception error) { image.Source = null; detail.Text = "Preview unavailable: " + error.Message; replace.IsEnabled = config.EnemySpritesFolder != null; }
        };
        void Filter()
        {
            string? selected = selectedSpriteName;
            list.ItemsSource = (loaded ? enemySprites : []).Where(e => e.Name.Contains(spriteSearch, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name)
                .Select(e => new SpriteRow(e, $"{e.Width} × {e.Height} base · {(spritePlan?.Changes.FirstOrDefault(c => c.Name == e.Name) is SpriteChange change ? $"Pending {change.Scale}×" : installedSpriteScales.GetValueOrDefault(e.Name, 1) > 1 ? $"{installedSpriteScales[e.Name]}× HD installed" : installedSpriteChanges.Contains(e.Name) ? "Updated" : "Original")}")).ToArray();
            list.SelectedItem = list.Items.Cast<SpriteRow>().FirstOrDefault(r => r.Name == selected);
            if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
        }
        query.TextChanged += (_, _) => { spriteSearch = query.Text; Filter(); };
        left.Children.Add(Stack(Text("Search enemy sprites", 12, "#A6BAC5"), query)); left.Children.Add(list); browser.Children.Add(left); browser.Children.Add(previewCard); Filter();
        var footer = Text("Close the game before applying or restoring sprites. Backups stay outside Steam.", 12, "#A6BAC5"); footer.Margin = new(0, 14, 0, 0);
        if (ActualHeight < 700) return Stack(summary, browser, footer);
        var pageGrid = new Grid(); pageGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); pageGrid.RowDefinitions.Add(new()); pageGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(browser, 1); Grid.SetRow(footer, 2); pageGrid.Children.Add(summary); pageGrid.Children.Add(browser); pageGrid.Children.Add(footer); return pageGrid;
    }
    async Task LoadEnemySprites() => await Run("Reading enemy sprites…", async () =>
    {
        spriteScanGame = config.GamePath; spritePlan = null; enemySprites = []; installedSpriteChanges.Clear();
        var service = SpriteService();
        var data = await Task.Run(() => (Original: service.Catalog(), Installed: service.InstalledSprites()));
        enemySprites = data.Original;
        var original = data.Original.ToDictionary(e => e.Name);
        installedSpriteChanges = data.Installed.Where(e => !e.Png.AsSpan().SequenceEqual(original[e.Name].Png)).Select(e => e.Name).ToHashSet();
        installedSpriteScales = data.Installed.ToDictionary(e => e.Name, e => EnemySprites.Scale(e.Width, e.Height, original[e.Name].Width, original[e.Name].Height));
        if (config.EnemySpritesFolder != null) await CheckEnemySpriteEdits();
    });
    Action<int, int> SpriteProgress(string operation)
    {
        var progress = new Progress<(int Done, int Total)>(p => { OperationProgress.IsIndeterminate = false; OperationProgress.Value = p.Total == 0 ? 100 : 100.0 * p.Done / p.Total; Status.Text = $"{operation} · {p.Done} of {p.Total} sprites"; });
        return (done, total) => ((IProgress<(int, int)>)progress).Report((done, total));
    }
    void ExtractEnemySprites()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a parent folder for extracted enemy sprites" };
        if (dialog.ShowDialog(this) != true) return;
        string destination = Path.Combine(dialog.FolderName, "CrystalProject-EnemySprites-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        _ = Run("Extracting all enemy sprites…", async () =>
        {
            var service = SpriteService(); var progress = SpriteProgress("Extracting");
            config.EnemySpritesFolder = await Task.Run(() => service.ExtractAll(destination, progress)); spritePlan = null; SaveDraft();
            await CheckEnemySpriteEdits();
            MessageBox.Show(this, $"Extracted {enemySprites.Count} sprites to:\n{destination}\n\nKeep filenames and use original, 2×, 4×, or 10× dimensions. Then Validate Edited Folder.", "Enemy sprites extracted");
        });
    }
    void ChooseEnemySpriteFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose an extracted folder containing sprites.json and edited PNGs" };
        if (dialog.ShowDialog(this) != true) return;
        EnemySprites.SafePath(dialog.FolderName);
        if (!File.Exists(Path.Combine(dialog.FolderName, "sprites.json"))) throw new IOException("Choose a folder exported by Extract All Sprites, containing sprites.json.");
        config.EnemySpritesFolder = dialog.FolderName; spritePlan = null; SaveDraft(); _ = ValidateEnemySprites();
    }
    async Task ValidateEnemySprites() => await Run("Validating edited enemy sprites…", async () =>
    {
        await CheckEnemySpriteEdits();
    });
    async Task CheckEnemySpriteEdits()
    {
        spritePlan = null; var service = SpriteService(); var progress = SpriteProgress("Validating");
        spritePlan = await Task.Run(() => service.ValidateFolder(config.EnemySpritesFolder ?? throw new IOException("Extract sprites or choose an edited folder first."), progress));
    }
    void ReplaceEnemySprite(SpriteEntry? sprite)
    {
        if (sprite == null || config.EnemySpritesFolder == null) return;
        var dialog = new OpenFileDialog { Title = $"Choose a 1×, 2×, 4×, or 10× PNG for {sprite.Name} (base {sprite.Width} × {sprite.Height})", Filter = "PNG images|*.png" };
        if (dialog.ShowDialog(this) != true) return;
        var folder = config.EnemySpritesFolder;
        _ = Run("Preparing replacement sprite…", async () =>
        {
            spritePlan = null;
            await Task.Run(() =>
            {
                EnemySprites.SafePath(dialog.FileName); EnemySprites.SafePath(folder);
                byte[] png = File.ReadAllBytes(dialog.FileName); var size = EnemySprites.ValidatePng(png);
                EnemySprites.Scale(size.Width, size.Height, sprite.Width, sprite.Height);
                string destination = Path.Combine(folder, sprite.Filename); EnemySprites.SafePath(destination);
                File.WriteAllBytes(destination, png);
            });
            await CheckEnemySpriteEdits();
        });
    }
    async Task ApplyEnemySprites() => await Run("Applying enemy sprite updates…", async () =>
    {
        await CheckEnemySpriteEdits();
        var plan = spritePlan!;
        if (plan.Changes.Count == 0) return;
        var service = SpriteService(); var game = RequireEngine();
        await Task.Run(() => service.Apply(plan, game.Idle, catalog => EnableHdRendering(game, catalog)));
        spritePlan = null; var updated = await Task.Run(service.InstalledSprites); var original = enemySprites.ToDictionary(e => e.Name);
        installedSpriteChanges = updated.Where(e => !e.Png.AsSpan().SequenceEqual(original[e.Name].Png)).Select(e => e.Name).ToHashSet();
        installedSpriteScales = updated.ToDictionary(e => e.Name, e => EnemySprites.Scale(e.Width, e.Height, original[e.Name].Width, original[e.Name].Height));
        await RefreshGame();
        await CheckEnemySpriteEdits();
        MessageBox.Show(this, $"Updated {plan.Changes.Count} enemy sprites. Launch the game to see the changes.", "Sprite updates applied");
    });
    async Task RestoreEnemySprites() => await Run("Restoring backed-up enemy sprites…", async () =>
    {
        var service = SpriteService(); var game = RequireEngine(); await Task.Run(() => service.Restore(game.Idle));
        spritePlan = null; installedSpriteChanges.Clear(); installedSpriteScales.Clear();
        if (config.EnemySpritesFolder != null) await CheckEnemySpriteEdits();
        MessageBox.Show(this, "The enemy sprite archive has been restored from its verified backup. Your edited PNGs remain in the editing folder.", "Sprites restored");
    });
    void EnableHdRendering(CrystalProjectModInstaller.Engine game, byte[] catalog)
    {
        var selection = game.Inspect().Mods ?? throw new IOException("This game executable is not supported for HD rendering.");
        string work = Path.Combine(library.Store, "work", "hd-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        string file = Path.Combine(work, "sprite-sizes.txt");
        try { File.WriteAllBytes(file, catalog); game.Apply(selection with { HdSprites = true }, new() { [CrystalProjectModInstaller.Engine.SpriteSizesName] = file }); }
        finally { File.Delete(file); Directory.Delete(work); }
    }
    async Task InstallHdSprites() => await Run("Installing HD enemy sprite rendering…", async () =>
    {
        var service = SpriteService(); var game = RequireEngine(); await Task.Run(() => EnableHdRendering(game, service.OriginalSizeCatalog())); await RefreshGame();
    });
    async Task RemoveHdSprites() => await Run("Removing HD enemy sprite rendering…", async () =>
    {
        var service = SpriteService();
        if (await Task.Run(service.InstalledHdCount) > 0) throw new IOException("Restore Backed-Up Sprites or replace all HD images with original-size PNGs before removing HD support.");
        var game = RequireEngine();
        await Task.Run(() => game.Apply((game.Inspect().Mods ?? throw new IOException("Unsupported game executable.")) with { HdSprites = false })); await RefreshGame();
    });
}
