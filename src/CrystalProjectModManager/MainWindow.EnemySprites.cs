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
        var apply = Button($"Apply {spritePlan?.Changes.Count ?? 0} Sprite Updates", () => _ = ApplyEnemySprites(), true); apply.IsEnabled = spritePlan?.Changes.Count > 0;
        var restore = Button("Restore Backed-Up Sprites", () => _ = RestoreEnemySprites()); restore.IsEnabled = SpriteService().HasBackup;
        var summary = Card(Stack(Text(loaded ? $"{enemySprites.Count} enemy sprites · {installedSpriteChanges.Count} updated" : "Read the enemy sprite library", 22),
            Text("Export PNGs, edit them in your image editor, then validate and apply the changed files. Keep the filenames, canvas sizes, and transparent backgrounds.", 13, "#A6BAC5"),
            Actions(extract, Button("Choose Edited Folder", ChooseEnemySpriteFolder), validate, apply),
            Actions(Button("Reload Sprites", () => _ = LoadEnemySprites()), Button("Open Editing Folder", () => Open(config.EnemySpritesFolder)), restore),
            Text(config.EnemySpritesFolder ?? "No editing folder selected", 12, "#65D6C0"),
            Text(spritePlan == null ? "Validate to review how many files will change. Sprite updates use their own Apply button." : $"Validated: {spritePlan.Changes.Count} changed PNGs. Apply uses this validated snapshot; validate again after further edits.", 12, "#A6BAC5")));
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
                detail.Text = $"{row.Sprite.Width} × {row.Sprite.Height} · {(edited ? "Editing folder preview" : "Original sprite")}";
                replace.IsEnabled = config.EnemySpritesFolder != null;
            }
            catch (Exception error) { image.Source = null; detail.Text = "Preview unavailable: " + error.Message; replace.IsEnabled = config.EnemySpritesFolder != null; }
        };
        void Filter()
        {
            string? selected = selectedSpriteName;
            list.ItemsSource = (loaded ? enemySprites : []).Where(e => e.Name.Contains(spriteSearch, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name)
                .Select(e => new SpriteRow(e, $"{e.Width} × {e.Height} · {(spritePlan?.Changes.Any(c => c.Name == e.Name) == true ? "Pending update" : installedSpriteChanges.Contains(e.Name) ? "Updated" : "Original")}")).ToArray();
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
            MessageBox.Show(this, $"Extracted {enemySprites.Count} sprites to:\n{destination}\n\nEdit the PNGs, keep their filenames and dimensions, then Validate Edited Folder.", "Enemy sprites extracted");
        });
    }
    void ChooseEnemySpriteFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose an extracted folder containing sprites.json and edited PNGs" };
        if (dialog.ShowDialog(this) != true) return;
        EnemySprites.SafePath(dialog.FolderName);
        if (!File.Exists(Path.Combine(dialog.FolderName, "sprites.json"))) throw new IOException("Choose a folder exported by Extract All Sprites, containing sprites.json.");
        config.EnemySpritesFolder = dialog.FolderName; spritePlan = null; SaveDraft(); ShowPage("Enemy Sprites");
    }
    async Task ValidateEnemySprites() => await Run("Validating edited enemy sprites…", async () =>
    {
        spritePlan = null; var service = SpriteService(); var progress = SpriteProgress("Validating");
        spritePlan = await Task.Run(() => service.ValidateFolder(config.EnemySpritesFolder ?? throw new IOException("Extract sprites or choose an edited folder first."), progress));
    });
    void ReplaceEnemySprite(SpriteEntry? sprite)
    {
        if (sprite == null || config.EnemySpritesFolder == null) return;
        var dialog = new OpenFileDialog { Title = $"Choose a {sprite.Width} × {sprite.Height} PNG for {sprite.Name}", Filter = "PNG images|*.png" };
        if (dialog.ShowDialog(this) != true) return;
        var folder = config.EnemySpritesFolder;
        _ = Run("Preparing replacement sprite…", async () =>
        {
            spritePlan = null;
            await Task.Run(() =>
            {
                EnemySprites.SafePath(dialog.FileName); EnemySprites.SafePath(folder);
                byte[] png = File.ReadAllBytes(dialog.FileName); var size = EnemySprites.ValidatePng(png);
                if (size != (sprite.Width, sprite.Height)) throw new IOException($"Use a {sprite.Width} × {sprite.Height} PNG. The selected image is {size.Width} × {size.Height}.");
                string destination = Path.Combine(folder, sprite.Filename); EnemySprites.SafePath(destination);
                File.WriteAllBytes(destination, png);
            });
        });
    }
    async Task ApplyEnemySprites() => await Run("Applying enemy sprite updates…", async () =>
    {
        var plan = spritePlan ?? throw new IOException("Validate the edited folder before applying.");
        var service = SpriteService(); var game = RequireEngine();
        await Task.Run(() => service.Apply(plan, game.Idle));
        spritePlan = null; var updated = await Task.Run(service.InstalledSprites); var original = enemySprites.ToDictionary(e => e.Name);
        installedSpriteChanges = updated.Where(e => !e.Png.AsSpan().SequenceEqual(original[e.Name].Png)).Select(e => e.Name).ToHashSet();
        MessageBox.Show(this, $"Updated {plan.Changes.Count} enemy sprites. Launch the game to see the changes.", "Sprite updates applied");
    });
    async Task RestoreEnemySprites() => await Run("Restoring backed-up enemy sprites…", async () =>
    {
        var service = SpriteService(); var game = RequireEngine(); await Task.Run(() => service.Restore(game.Idle));
        spritePlan = null; installedSpriteChanges.Clear();
        MessageBox.Show(this, "The enemy sprite archive has been restored from its verified backup. Your edited PNGs remain in the editing folder.", "Sprites restored");
    });
}
