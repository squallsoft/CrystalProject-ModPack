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
    Dictionary<string, double> installedSpritePixelGrowth = [];
    SpritePlan? spritePlan;
    CancellationTokenSource? spriteScanCancellation;
    readonly SemaphoreSlim spriteScanGate = new(1, 1);
    bool spriteScanning;
    string spriteScanStatus = "Choose an edited folder to check for updates.";
    TextBlock? spriteScanLabel;
    Button? spriteApplyButton, spriteValidateButton, spriteCancelButton;
    Action? refreshSpriteRows;
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
        spriteValidateButton = validate;
        var apply = Button(spritePlan == null ? "Validate Before Applying" : $"Apply {spritePlan.Changes.Count} Sprite Updates", () => _ = ApplyEnemySprites(), true); apply.IsEnabled = spritePlan?.Changes.Count > 0;
        spriteApplyButton = apply;
        var cancel = Button("Cancel Scan", () => spriteScanCancellation?.Cancel()); spriteCancelButton = cancel;
        spriteScanLabel = Text(spriteScanStatus, 12, "#A6BAC5");
        var scanInfo = new ScrollViewer { Content = spriteScanLabel, MaxHeight = 115, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var restore = Button("Restore Backed-Up Sprites", () => _ = RestoreEnemySprites()); restore.IsEnabled = SpriteService().HasBackup;
        var hd = Button(installed?.HdSprites == true ? "HD Support Installed" : "Install HD Support", () => _ = InstallHdSprites()); hd.IsEnabled = loaded && installed != null && !installed.HdSprites;
        var removeHd = Button("Remove HD Support", () => _ = RemoveHdSprites()); removeHd.IsEnabled = installed?.HdSprites == true;
        var summary = Card(Stack(Text(loaded ? $"{enemySprites.Count} enemy sprites · {installedSpriteChanges.Count} updated" : "Read the enemy sprite library", 22),
            Text("Keep filenames, proportions, and transparency. Aim around 10× original pixel count (about 3.16× dimensions). Limits: 4096 per axis, 4 million pixels/image, 32 MiB/PNG, 256 MiB estimated library texture memory.", 13, "#A6BAC5"),
            Actions(extract, Button("Choose Edited Folder", ChooseEnemySpriteFolder), validate, cancel, apply),
            Actions(Button("Reload Sprites", () => _ = LoadEnemySprites()), Button("Open Editing Folder", () => Open(config.EnemySpritesFolder)), restore, hd, removeHd),
            Text(config.EnemySpritesFolder ?? "No editing folder selected", 12, "#65D6C0"),
            scanInfo));
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
                if (edited) { EnemySprites.SafePath(path!); if (new FileInfo(path!).Length > 32 * 1024 * 1024) throw new IOException("Image exceeds 32 MiB. Export a smaller PNG."); png = File.ReadAllBytes(path!); EnemySprites.ValidatePng(png); }
                using var stream = new MemoryStream(png);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image.Source = bitmap;
                double factor = EnemySprites.Scale(bitmap.PixelWidth, bitmap.PixelHeight, row.Sprite.Width, row.Sprite.Height);
                RenderOptions.SetBitmapScalingMode(image, factor > 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
                double growth = EnemySprites.PixelGrowth(bitmap.PixelWidth, bitmap.PixelHeight, row.Sprite.Width, row.Sprite.Height);
                detail.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} · {growth:0.##}× original pixels · {(long)bitmap.PixelWidth * bitmap.PixelHeight * 4 / 1048576.0:0.##} MiB RGBA\nAbout 10× pixels: {(int)Math.Round(row.Sprite.Width * Math.Sqrt(10))} × {(int)Math.Round(row.Sprite.Height * Math.Sqrt(10))} · {(edited ? "Editing folder" : "Original")}";
                replace.IsEnabled = config.EnemySpritesFolder != null;
            }
            catch (Exception error) { image.Source = null; detail.Text = "Preview unavailable: " + error.Message; replace.IsEnabled = config.EnemySpritesFolder != null; }
        };
        void Filter()
        {
            string? selected = selectedSpriteName;
            list.ItemsSource = (loaded ? enemySprites : []).Where(e => e.Name.Contains(spriteSearch, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name)
                .Select(e => new SpriteRow(e, $"{e.Width} × {e.Height} base · {(spritePlan?.Changes.FirstOrDefault(c => c.Name == e.Name) is SpriteChange change ? $"Pending {EnemySprites.PixelGrowth(change.Width, change.Height, e.Width, e.Height):0.##}× pixels" : installedSpritePixelGrowth.GetValueOrDefault(e.Name, 1) > 1 ? $"{installedSpritePixelGrowth[e.Name]:0.##}× pixels installed" : installedSpriteChanges.Contains(e.Name) ? "Updated" : "Original")}")).ToArray();
            list.SelectedItem = list.Items.Cast<SpriteRow>().FirstOrDefault(r => r.Name == selected);
            if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
        }
        query.TextChanged += (_, _) => { spriteSearch = query.Text; Filter(); };
        left.Children.Add(Stack(Text("Search enemy sprites", 12, "#A6BAC5"), query)); left.Children.Add(list); browser.Children.Add(left); browser.Children.Add(previewCard); refreshSpriteRows = Filter; Filter(); UpdateSpriteScanUi();
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
        installedSpritePixelGrowth = data.Installed.ToDictionary(e => e.Name, e => EnemySprites.PixelGrowth(e.Width, e.Height, original[e.Name].Width, original[e.Name].Height));
        if (config.EnemySpritesFolder != null) _ = ValidateEnemySprites();
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
            MessageBox.Show(this, $"Extracted {enemySprites.Count} sprites to:\n{destination}\n\nKeep filenames and proportions; aim around 10× original pixel count (about 3.16× dimensions). Then Validate Edited Folder.", "Enemy sprites extracted");
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
    async Task ValidateEnemySprites()
    {
        if (busy && spriteScanning) return;
        await CheckEnemySpriteEdits();
    }
    void UpdateSpriteScanUi()
    {
        if (spriteScanLabel != null) { spriteScanLabel.Text = spriteScanStatus; spriteScanLabel.Foreground = Brush(!spriteScanning && (spritePlan == null || spritePlan.Warnings?.Count > 0) ? "#EACB89" : "#A6BAC5"); }
        if (spriteApplyButton != null) { spriteApplyButton.Content = spriteScanning ? "Checking Sprite Updates…" : spritePlan == null ? "Fix Images Before Applying" : $"Apply {spritePlan.Changes.Count} Sprite Updates"; spriteApplyButton.IsEnabled = !spriteScanning && spritePlan?.Changes.Count > 0; }
        if (spriteValidateButton != null) spriteValidateButton.IsEnabled = !spriteScanning && config.EnemySpritesFolder != null;
        if (spriteCancelButton != null) spriteCancelButton.Visibility = spriteScanning ? Visibility.Visible : Visibility.Collapsed;
    }
    async Task<bool> CheckEnemySpriteEdits()
    {
        spriteScanCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); spriteScanCancellation = cancellation;
        string? folder = config.EnemySpritesFolder, game = config.GamePath;
        spritePlan = null; spriteScanning = true; spriteScanStatus = "Checking edited PNGs… You can keep browsing or cancel the scan."; UpdateSpriteScanUi();
        bool acquired = false;
        try
        {
            await spriteScanGate.WaitAsync(cancellation.Token); acquired = true;
            var service = SpriteService();
            IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(p =>
            {
                if (spriteScanCancellation != cancellation || cancellation.IsCancellationRequested) return;
                spriteScanStatus = $"Checking edited PNGs · {p.Done} of {p.Total}. You can keep browsing."; UpdateSpriteScanUi();
            });
            var report = await Task.Run(() => service.ScanFolder(folder ?? throw new IOException("Choose an edited folder first."), (done, total) => progress.Report((done, total)), cancellation.Token));
            if (cancellation.IsCancellationRequested || game != config.GamePath || folder != config.EnemySpritesFolder) return false;
            spritePlan = report.Plan;
            spriteScanStatus = report.Plan == null ? $"Fix {report.Issues.Count} image issue(s) before applying. You can still browse and replace PNGs.\n" + string.Join("\n", report.Issues)
                : $"{report.Plan.Changes.Count} pending updates · {report.Plan.HdSprites} HD sprites · {report.Plan.TextureBytes / 1048576.0:0.0} / 256 MiB estimated library textures ({report.Plan.TextureBytes / (double)Math.Max(1, report.Plan.OriginalTextureBytes):0.##}× original pixels)."
                    + (report.Plan.Warnings?.Count > 0 ? "\n" + string.Join("\n", report.Plan.Warnings) : "");
            return report.Plan != null;
        }
        catch (OperationCanceledException) { if (spriteScanCancellation == cancellation) spriteScanStatus = "Scan cancelled. Validate the folder when you're ready."; return false; }
        catch (Exception error) { if (spriteScanCancellation == cancellation) spriteScanStatus = "Folder check needs attention: " + error.Message; return false; }
        finally
        {
            if (acquired) spriteScanGate.Release();
            if (spriteScanCancellation == cancellation) { spriteScanCancellation = null; spriteScanning = false; UpdateSpriteScanUi(); if (page == "Enemy Sprites" && game == config.GamePath && folder == config.EnemySpritesFolder) refreshSpriteRows?.Invoke(); }
        }
    }
    void ReplaceEnemySprite(SpriteEntry? sprite)
    {
        if (sprite == null || config.EnemySpritesFolder == null) return;
        var dialog = new OpenFileDialog { Title = $"Choose a proportional PNG for {sprite.Name} (base {sprite.Width} × {sprite.Height}; aim around 10× pixels)", Filter = "PNG images|*.png" };
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
        if (!await CheckEnemySpriteEdits()) return;
        var plan = spritePlan!;
        if (plan.Changes.Count == 0) return;
        var service = SpriteService(); var game = RequireEngine();
        await Task.Run(() => service.Apply(plan, game.Idle, catalog => EnableHdRendering(game, catalog)));
        spritePlan = null; var updated = await Task.Run(service.InstalledSprites); var original = enemySprites.ToDictionary(e => e.Name);
        installedSpriteChanges = updated.Where(e => !e.Png.AsSpan().SequenceEqual(original[e.Name].Png)).Select(e => e.Name).ToHashSet();
        installedSpritePixelGrowth = updated.ToDictionary(e => e.Name, e => EnemySprites.PixelGrowth(e.Width, e.Height, original[e.Name].Width, original[e.Name].Height));
        await RefreshGame();
        await CheckEnemySpriteEdits();
        MessageBox.Show(this, $"Updated {plan.Changes.Count} enemy sprites. Launch the game to see the changes.", "Sprite updates applied");
    });
    async Task RestoreEnemySprites() => await Run("Restoring backed-up enemy sprites…", async () =>
    {
        var service = SpriteService(); var game = RequireEngine(); await Task.Run(() => service.Restore(game.Idle));
        spritePlan = null; installedSpriteChanges.Clear(); installedSpritePixelGrowth.Clear();
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
