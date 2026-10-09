using CrystalProjectModManager.Core;
using CrystalProjectModInstaller;
using Microsoft.Win32;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CrystalProjectModManager;
public partial class MainWindow : Window
{
    readonly MusicLibrary library;
    Configuration config = new();
    readonly PreviewPlayer player = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    Engine? engine;
    Selection? installed;
    string gameStatus = "Game not detected";
    string page = "Home";
    MusicCue? currentCue;
    ListBox? cueList, trackList;
    TextBox? search;
    ComboBox? category, replacementFilter, cueSort;
    TextBlock? cueCount;
    string musicSearch = "", musicCategory = "All categories", musicFilter = "All songs", musicSort = "Name A–Z";
    bool busy, changingSeek, configLoaded;
    TrackRow[] previewPool = [];
    int previewIndex;
    bool previewOriginal;
    readonly string? renderDirectory;
    static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        int render = Array.IndexOf(args, "--render"); renderDirectory = render >= 0 && args.Length > render + 1 ? Path.GetFullPath(args[render + 1]) : null;
        if (renderDirectory != null) { ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual; Left = -10000; }
        library = new(renderDirectory == null ? null : Path.Combine(renderDirectory, "qa-data"));
        foreach (string label in new[] { "Home", "Music", "Enemy Sprites", "Home Points", "Installation / Game", "Backups & Repair", "Settings", "About" })
        {
            var button = Button(label, () => ShowPage(label)); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new(12, 9, 12, 9); button.Margin = new(0, 0, 10, 6); button.FontSize = 13; button.Tag = label; Navigation.Children.Add(button);
        }
        PlayPause.Click += (_, _) => PreviewAction(() => { if (player.State == PlaybackState.Playing) player.Pause(); else if (player.FilePath != null) player.Play(); else PlaySelected(); });
        Stop.Click += (_, _) => PreviewAction(player.Stop);
        Previous.Click += (_, _) => MoveTrack(-1); Next.Click += (_, _) => MoveTrack(1);
        Seek.ValueChanged += (_, _) => { if (!changingSeek && (Seek.IsMouseCaptureWithin || Seek.IsKeyboardFocusWithin)) PreviewAction(() => player.Seek(TimeSpan.FromSeconds(Seek.Value))); };
        Volume.ValueChanged += (_, _) => { player.Volume = (float)Volume.Value; config.PreviewVolume = Volume.Value; };
        player.Error += error => Dispatcher.InvokeAsync(() => Error(error, "This track could not be played."));
        timer.Tick += (_, _) =>
        {
            changingSeek = true; Seek.Maximum = Math.Max(1, player.Duration.TotalSeconds); if (!Seek.IsMouseCaptureWithin) Seek.Value = player.Position.TotalSeconds; changingSeek = false;
            if (player.FilePath != null) PlaybackTime.Text = $"{player.Position:mm\\:ss} / {player.Duration:mm\\:ss}";
            bool playing = player.State == PlaybackState.Playing;
            PlayPause.Content = playing ? "\uE769" : "\uE768";
            PlayPause.ToolTip = playing ? "Pause" : "Play";
            System.Windows.Automation.AutomationProperties.SetName(PlayPause, playing ? "Pause" : "Play");
            bool available = player.FilePath != null || trackList?.SelectedItem != null; PlayPause.IsEnabled = available; Stop.IsEnabled = player.FilePath != null; Seek.IsEnabled = player.FilePath != null;
            Previous.IsEnabled = Next.IsEnabled = !previewOriginal && (trackList?.Items.Count > 1 || previewPool.Length > 1);
        };
        timer.Start();
        Closing += (_, e) => { if (busy) { e.Cancel = true; Status.Text = "Wait for the current operation to finish before closing."; } };
        Closed += (_, _) => { timer.Stop(); player.Dispose(); if (configLoaded) { try { library.Save(config); } catch { } } };
        Loaded += async (_, _) =>
        {
            try { config = library.Load(); configLoaded = true; }
            catch (Exception error) { Error(error, "Your saved configuration could not be opened. It has been preserved."); ShowPage("Settings"); IsEnabled = false; return; }
            Volume.Value = config.PreviewVolume;
            config.GamePath ??= SteamInstallations.Detect().FirstOrDefault();
            await RefreshGame();
            if (!File.Exists(library.ConfigPath) && installed != null) { config.MusicEnabled = installed.Music; config.UnlimitedHomePoints = installed.Home; }
            ShowPage("Home");
            if (renderDirectory != null) await RenderPages();
        };
    }
    async Task RefreshGame()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.GamePath)) { engine = null; installed = null; gameStatus = "Choose your Crystal Project folder to get started."; return; }
            engine = new(config.GamePath, library.Store, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrystalProjectModInstaller", "backups", Patches.Original, "Crystal Project.exe")); var state = await Task.Run(engine.Inspect); gameStatus = state.Status; installed = state.Mods;
        }
        catch (Exception e) { installed = null; gameStatus = e.Message; }
    }
    Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text }; if (primary) b.Style = (Style)FindResource("Primary"); b.Click += (_, _) => { if (!busy) { try { action(); } catch (Exception e) { Error(e); } } }; return b;
    }
    TextBlock Text(string text, double size = 14, string? color = null) => new() { Text = text, FontSize = size, Foreground = Brush(color ?? "#ECF3F4"), Margin = new(0, 0, 0, 12) };
    Border Card(UIElement content) => new() { Child = content, Background = Brush("#192834"), CornerRadius = new(10), Padding = new(22), Margin = new(0, 0, 0, 18) };
    StackPanel Stack(params UIElement[] children) { var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    WrapPanel Actions(params UIElement[] children) { var panel = new WrapPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    void ShowPage(string name)
    {
        if (busy) return; page = name; PageTitle.Text = name;
        PreviewBar.Visibility = name == "Enemy Sprites" && player.FilePath == null ? Visibility.Collapsed : Visibility.Visible;
        cueList = trackList = null;
        foreach (Button b in Navigation.Children) b.Background = Brush((string)b.Tag == name ? "#28514F" : "#142430");
        PageSubtitle.Text = name switch { "Music" => "Make the soundtrack your own. Single tracks or random pools, for every cue.", "Home" => "Your game, your soundtrack, your destinations.", "Home Points" => "More places to return to, with the game's Enhanced Home Point option.", "Backups & Repair" => "Verified originals and recovery, kept outside your Steam installation.", _ => "Crystal Project Mod Manager · Development preview" };
        UIElement content = name switch { "Home" => HomePage(), "Music" => MusicPage(), "Enemy Sprites" => EnemySpritesPage(), "Home Points" => HomePointPage(), "Installation / Game" => GamePage(), "Backups & Repair" => BackupPage(), "Settings" => SettingsPage(), _ => AboutPage() };
        PageContent.Content = name == "Music" || name == "Enemy Sprites" && ActualHeight >= 700 ? content : new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Status.Text = "Changes are saved as a draft. Apply Changes updates the game.";
        if (name == "Enemy Sprites")
        {
            PageSubtitle.Text = "Extract, preview, and replace your entire enemy sprite library.";
            Status.Text = "Enemy sprites use Apply Sprite Updates on this page; Home Apply Changes handles music and Home Points.";
            if (config.GamePath != null && spriteScanGame != config.GamePath) _ = LoadEnemySprites();
        }
    }
    UIElement HomePage()
    {
        string last = engine?.Record()?.Timestamp.ToLocalTime().ToString("g") ?? "No configuration applied by this manager";
        var apply = Button("Apply Changes", () => _ = Apply(), true); apply.IsEnabled = installed != null;
        var panel = Stack(Card(Stack(Text("CRYSTAL PROJECT", 12, "#65D6C0"), Text(gameStatus, 24), Text(config.GamePath ?? "Installation not selected", 13, "#A6BAC5"), Actions(apply, Button("Launch Game", Launch), Button("Open Game Folder", () => Open(config.GamePath))))),
            Card(Stack(Text("Your configuration", 20), Text($"Music: {(config.MusicEnabled ? "enabled" : "off")} · {config.MusicPools.Count(p => p.Value.Enabled && p.Value.Tracks.Count > 0)} replaced cues\nUnlimited Home Points: {(config.UnlimitedHomePoints ? "enabled" : "off")}\nLast applied: {last}"), Text("Backup: " + BackupStatus(), 13, "#A6BAC5"), Actions(Button("Edit Music", () => ShowPage("Music")), Button("Repair", () => _ = Repair()), Button("Restore Vanilla", () => _ = Restore()), Button("Open Logs", () => Open(Path.Combine(library.Store, "logs")))))));
        if (installed?.Music == true && config.MusicPools.Count == 0 && config.GamePath != null && Directory.Exists(Path.Combine(config.GamePath, "ogg")))
            panel.Children.Insert(1, Card(Stack(Text("Bring your existing music along", 20), Text("Import the previous Battle, Boss, and Victory folders before applying. Empty manager pools use original game music.", 13, "#EACB89"), Button("Import Existing Music", () => _ = ImportLegacy(), true))));
        return panel;
    }
    string BackupStatus()
    {
        try { return engine?.FindOriginal() == null ? "Not available — Steam verification may be needed" : "Verified pristine executable available"; }
        catch { return "Backup failed verification — no changes allowed"; }
    }
    sealed record CueRow(MusicCue Cue, string Summary)
    {
        public string Name => Cue.DisplayName;
        public string Detail => Cue.Category + " · " + Summary;
    }
    sealed record TrackRow(string Id, string Name, string Detail);
    UIElement MusicPage()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(1.1, GridUnitType.Star) });
        var left = new Grid { Margin = new(0, 0, 18, 0) }; left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.RowDefinitions.Add(new());
        search = new() { Text = musicSearch, ToolTip = "Search cue names, areas, original titles, or identifiers", Margin = new(0, 0, 0, 10) }; search.TextChanged += (_, _) => { musicSearch = search.Text; FilterCues(); };
        category = new() { ItemsSource = new[] { "All categories", "Areas", "Battle", "Boss", "Victory", "Events", "Other", "Ambience" }, SelectedItem = musicCategory, MinWidth = 130, ToolTip = "Filter by category" }; category.SelectionChanged += (_, _) => { musicCategory = category.SelectedItem.ToString()!; FilterCues(); };
        replacementFilter = new() { ItemsSource = new[] { "All songs", "Replaced", "Not replaced" }, SelectedItem = musicFilter, MinWidth = 135, ToolTip = "Filter by replacement status" }; replacementFilter.SelectionChanged += (_, _) => { musicFilter = replacementFilter.SelectedItem.ToString()!; FilterCues(); };
        cueSort = new() { ItemsSource = new[] { "Name A–Z", "Replaced first", "Not replaced first" }, SelectedItem = musicSort, MinWidth = 150, ToolTip = "Sort songs" }; cueSort.SelectionChanged += (_, _) => { musicSort = cueSort.SelectedItem.ToString()!; FilterCues(); };
        cueCount = Text("", 12, "#A6BAC5");
        var filterRow = new Grid(); filterRow.ColumnDefinitions.Add(new()); filterRow.ColumnDefinitions.Add(new());
        category.MinWidth = replacementFilter.MinWidth = 0;
        replacementFilter.Margin = new(0, 0, 0, 8); Grid.SetColumn(replacementFilter, 1);
        filterRow.Children.Add(category); filterRow.Children.Add(replacementFilter);
        var filters = Stack(filterRow, Actions(cueSort), cueCount); Grid.SetRow(filters, 1);
        cueList = new() { ItemTemplate = CueTemplate() }; cueList.SelectionChanged += (_, _) => { currentCue = (cueList.SelectedItem as CueRow)?.Cue; UpdatePoolEditor(grid); }; Grid.SetRow(cueList, 2);
        System.Windows.Automation.AutomationProperties.SetName(search, "Search soundtrack");
        var searchPanel = Stack(Text("Search soundtrack", 12, "#A6BAC5"), search);
        left.Children.Add(searchPanel); left.Children.Add(filters); left.Children.Add(cueList); grid.Children.Add(left);
        FilterCues();
        if (cueList.Items.Count > 0) cueList.SelectedItem = cueList.Items.Cast<CueRow>().FirstOrDefault(x => x.Cue.Id == currentCue?.Id) ?? cueList.Items[0];
        if (cueList.SelectedItem != null) cueList.ScrollIntoView(cueList.SelectedItem);
        if (currentCue == null) UpdatePoolEditor(grid);
        return grid;
    }
    DataTemplate CueTemplate()
    {
        var template = new DataTemplate(); var stack = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name")); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); stack.AppendChild(title);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Detail")); detail.SetValue(TextBlock.FontSizeProperty, 12.0); detail.SetValue(TextBlock.ForegroundProperty, Brush("#A6BAC5")); detail.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0)); stack.AppendChild(detail); template.VisualTree = stack; return template;
    }
    void FilterCues()
    {
        if (cueList == null) return;
        var selected = currentCue?.Id;
        bool Replaced(MusicCue c) => config.MusicEnabled && Pool(c).Enabled && Pool(c).Tracks.Count > 0;
        var cues = MusicLibrary.Cues.Where(c => (musicCategory == "All categories" || c.Category == musicCategory) &&
            (musicFilter == "All songs" || Replaced(c) == (musicFilter == "Replaced")) &&
            (c.DisplayName + " " + c.GameCue + " " + c.OriginalTitle + " " + c.Context).Contains(musicSearch, StringComparison.OrdinalIgnoreCase));
        var sorted = musicSort == "Replaced first" ? cues.OrderByDescending(Replaced).ThenBy(c => c.DisplayName) :
            musicSort == "Not replaced first" ? cues.OrderBy(Replaced).ThenBy(c => c.DisplayName) : cues.OrderBy(c => c.DisplayName);
        cueList.ItemsSource = sorted.Select(c => new CueRow(c, Replaced(c) ? $"Replaced · {Pool(c).Tracks.Count} track{(Pool(c).Tracks.Count == 1 ? "" : "s")}" :
            Pool(c).Tracks.Count > 0 ? "Not replaced · pool disabled" : "Not replaced · original music")).ToArray();
        if (cueCount != null) cueCount.Text = $"{cueList.Items.Count} of {MusicLibrary.Cues.Count} cues · {MusicLibrary.Cues.Count(Replaced)} replaced";
        cueList.SelectedItem = cueList.Items.Cast<CueRow>().FirstOrDefault(r => r.Cue.Id == selected);
        if (cueList.SelectedItem == null && cueList.Items.Count > 0) cueList.SelectedIndex = 0;
    }
    Pool Pool(MusicCue cue) => config.MusicPools.TryGetValue(cue.Id, out var p) ? p : new();
    Pool EditablePool() { if (currentCue == null) throw new IOException("Choose a music cue first."); if (!config.MusicPools.ContainsKey(currentCue.Id)) config.MusicPools[currentCue.Id] = new(); return config.MusicPools[currentCue.Id]; }
    void UpdatePoolEditor(Grid grid)
    {
        if (grid.Children.Count > 1) grid.Children.RemoveAt(1);
        var editor = new Grid(); editor.RowDefinitions.Add(new() { Height = GridLength.Auto }); editor.RowDefinitions.Add(new()); editor.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (currentCue == null) { trackList = null; var empty = Card(Text("No cue selected. Adjust your search or category filter to find music.")); Grid.SetColumn(empty, 1); grid.Children.Add(empty); return; }
        var cue = currentCue; var pool = Pool(cue);
        var enabled = new CheckBox { Content = "Use this replacement pool", IsChecked = pool.Enabled }; enabled.Click += (_, _) => { EditablePool().Enabled = enabled.IsChecked == true; SaveDraft(); FilterCues(); };
        var head = Stack(Text(cue.DisplayName, 22), Text(cue.Category + " · Original: " + cue.OriginalTitle, 12, "#65D6C0"), Text(cue.Context, 12, "#A6BAC5"), enabled);
        UIElement header = ActualHeight < 700 ? head : new ScrollViewer { Content = head, MaxHeight = 175, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; editor.Children.Add(header);
        trackList = new() { ItemTemplate = CueTemplate(), AllowDrop = true };
        if (ActualHeight < 700) { trackList.MinHeight = 120; trackList.MaxHeight = 200; }
        trackList.ItemsSource = pool.Tracks.Select(id => { var t = config.Library[id]; return new TrackRow(id, t.Filename, File.Exists(library.TrackPath(id)) ? TimeSpan.FromSeconds(t.Duration).ToString(@"mm\:ss") : "Missing file — use Locate File"); }).ToArray();
        if (trackList.Items.Count > 0) trackList.SelectedIndex = 0;
        trackList.MouseDoubleClick += (_, _) => PreviewAction(PlaySelected);
        trackList.Drop += (_, e) => { if (!busy && e.Data.GetData(DataFormats.FileDrop) is string[] files) _ = AddTracks(files); };
        Grid.SetRow(trackList, 1); editor.Children.Add(trackList);
        var footer = Stack(Text(pool.Tracks.Count == 0 ? "Original music plays when this pool is empty." : "One track always plays. Multiple tracks randomize without immediate repeats.", 12, "#A6BAC5"),
            Actions(Button("Add Music", ChooseMusic, true), Button("Add Folder", ChooseFolder)),
            Actions(Button("Remove", RemoveTrack), Button("Locate File", LocateTrack), Button("Reset to Original", () => { EditablePool().Tracks.Clear(); SaveDraft(); ShowPage("Music"); })),
            Actions(Button("Preview Original", () => PreviewAction(PlayOriginal))));
        Grid.SetRow(footer, 2); editor.Children.Add(footer);
        UIElement editorContent = ActualHeight < 700 ? new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : editor;
        var card = new Border { Child = editorContent, Background = Brush("#192834"), CornerRadius = new(10), Padding = new(20) }; Grid.SetColumn(card, 1); grid.Children.Add(card);
    }
    void ChooseMusic() { var d = new OpenFileDialog { Filter = "Ogg Vorbis music|*.ogg", Multiselect = true }; if (d.ShowDialog(this) == true) _ = AddTracks(d.FileNames); }
    void ChooseFolder() { var d = new OpenFolderDialog { Title = "Choose a music folder" }; if (d.ShowDialog(this) == true) _ = AddTracks(Directory.GetFiles(d.FolderName, "*.ogg", SearchOption.TopDirectoryOnly)); }
    async Task AddTracks(IEnumerable<string> paths)
    {
        var cue = currentCue; if (cue == null || busy) return;
        await Run("Importing and checking music…", async () =>
        {
            var imported = await ImportTracks(paths.ToArray());
            if (!config.MusicPools.TryGetValue(cue.Id, out var pool)) config.MusicPools[cue.Id] = pool = new();
            foreach (var track in imported) { config.Library[track.Id] = track; if (!pool.Tracks.Contains(track.Id)) pool.Tracks.Add(track.Id); }
            SaveDraft();
        });
    }
    async Task<LibraryTrack[]> ImportTracks(string[] paths, int offset = 0, int? total = null)
    {
        int count = total ?? paths.Length;
        OperationProgress.IsIndeterminate = false;
        var progress = new Progress<(int Index, string Name, double Fraction)>(p =>
        {
            OperationProgress.Value = count == 0 ? 100 : 100.0 * (p.Index + p.Fraction) / count;
            string stage = p.Fraction < 0.05 ? "Copying" : p.Fraction < 0.95 ? "Validating audio" : p.Fraction < 1 ? "Checking file" : "Imported";
            Status.Text = $"{stage} · {p.Index + 1} of {count} · {p.Name} · {OperationProgress.Value:0}% overall";
        });
        return await Task.Run(() => paths.Select((path, i) => library.Import(path, fraction =>
            ((IProgress<(int, string, double)>)progress).Report((offset + i, Path.GetFileName(path), fraction)))).ToArray());
    }
    void RemoveTrack() { if (trackList?.SelectedItem is TrackRow row) { EditablePool().Tracks.Remove(row.Id); SaveDraft(); ShowPage("Music"); } }
    async void LocateTrack()
    {
        if (trackList?.SelectedItem is not TrackRow row) return;
        var d = new OpenFileDialog { Filter = "Ogg Vorbis music|*.ogg", Title = "Locate " + row.Name };
        if (d.ShowDialog(this) == true) await Run("Checking located music…", async () => await Task.Run(() => library.Locate(config.Library[row.Id], d.FileName)));
    }
    void PlaySelected()
    {
        if (trackList?.SelectedItem is not TrackRow row) return;
        previewPool = trackList.Items.Cast<TrackRow>().ToArray(); previewIndex = trackList.SelectedIndex;
        PlayRow(row);
    }
    void PlayRow(TrackRow row)
    {
        string path = library.TrackPath(row.Id); if (!File.Exists(path)) throw new IOException("This music file is missing. Use Locate File or remove it from the pool.");
        player.Open(path); previewOriginal = false; player.Play(); NowPlaying.Text = row.Name;
    }
    void PlayOriginal()
    {
        var cue = currentCue; if (cue == null) return;
        if (string.IsNullOrWhiteSpace(config.GamePath)) throw new IOException("Select your game folder in Installation / Game to preview original music.");
        var stream = OriginalMusic.Open(config.GamePath, cue);
        player.OpenOriginal(stream, cue.Id); previewOriginal = true; player.Play();
        NowPlaying.Text = "Original · " + cue.OriginalTitle;
    }
    void MoveTrack(int direction)
    {
        if (previewOriginal) return;
        if (trackList != null && trackList.Items.Count > 0) { trackList.SelectedIndex = (Math.Max(0, trackList.SelectedIndex) + direction + trackList.Items.Count) % trackList.Items.Count; PreviewAction(PlaySelected); }
        else if (previewPool.Length > 0) { previewIndex = (previewIndex + direction + previewPool.Length) % previewPool.Length; PreviewAction(() => PlayRow(previewPool[previewIndex])); }
    }
    void PreviewAction(Action action) { try { action(); } catch (Exception e) { Error(e, "Preview stopped: " + e.Message); } }
    UIElement HomePointPage()
    {
        var enabled = new CheckBox { Content = "Unlimited Home Points", IsChecked = config.UnlimitedHomePoints }; enabled.Click += (_, _) => { config.UnlimitedHomePoints = enabled.IsChecked == true; SaveDraft(); };
        return Card(Stack(Text("Every destination, within reach", 24), enabled, Text("Allows Enhanced Home Point to store more than the normal three destinations. Enable Enhanced Home Point in the game's assist options, then add a new slot whenever you need one."), Text("Extra destinations stay in your saves. Keep this mod enabled when loading saves with more than three points; the original game cannot safely load expanded saves.", 14, "#EACB89"), Button("Apply Changes", () => _ = Apply(), true)));
    }
    UIElement GamePage() => Card(Stack(Text("Crystal Project installation", 22), Text(config.GamePath ?? "No game folder selected", 14, "#A6BAC5"), Text(gameStatus), Actions(Button("Choose Game Folder", () => { var d = new OpenFolderDialog { Title = "Select the folder containing Crystal Project.exe" }; if (d.ShowDialog(this) == true) _ = Run("Checking installation…", async () => { config.GamePath = d.FolderName; SaveDraft(); await RefreshGame(); }); }), Button("Detect Steam Installation", () => _ = Run("Finding Steam libraries…", async () => { config.GamePath = SteamInstallations.Detect().FirstOrDefault(); SaveDraft(); await RefreshGame(); })), Button("Open Game Folder", () => Open(config.GamePath))), Text("Supported game: Steam Windows 1.6.9.0. Unknown updates are refused before deployment.")));
    UIElement BackupPage() => Stack(Card(Stack(Text("Your original game is protected", 22), Text(BackupStatus()), Text("Every patch starts from a verified pristine executable. Restore leaves saves and imported music untouched."), Actions(Button("Repair Installation", () => _ = Repair(), true), Button("Restore Vanilla", () => _ = Restore()), Button("Open Backups", () => Open(Path.Combine(library.Store, "backups")))))), 
        Card(Stack(Text("Legacy cleanup", 20), Text("Archive and remove only recognized files from previous installers. Changed files, unknown files, and your music remain in place."), Button("Clean Legacy Mod Files", () => _ = Run("Archiving known legacy files…", async () => { var e = RequireEngine(); string archive = await Task.Run(e.CleanLegacy); MessageBox.Show(this, "Known legacy files archived to:\n" + archive, "Cleanup complete"); })))), Text("If the verified original is unavailable: Steam → Properties → Installed Files → Verify integrity of game files.", 13, "#A6BAC5"));
    UIElement SettingsPage()
    {
        var music = new CheckBox { Content = "Enable replacement music", IsChecked = config.MusicEnabled }; music.Click += (_, _) => { config.MusicEnabled = music.IsChecked == true; SaveDraft(); };
        return Stack(Card(Stack(Text("Music & library", 22), music, Text("Imports use managed copies of Ogg Vorbis music. Your source files are never edited. Preview volume affects the manager only."), Actions(Button("Open Data Folder", () => Open(library.Store)), Button("Import Legacy Pools", () => _ = ImportLegacy())))),
            Card(Stack(Text("Configuration sharing", 20), Text("Export cue assignments, filenames, and preferences without copying music. After importing on another computer, use Locate File to reconnect tracks."), Actions(Button("Export Configuration", () => { var d = new SaveFileDialog { Filter = "Configuration|*.json", FileName = "crystal-project-config.json" }; if (d.ShowDialog(this) == true) MusicLibrary.Export(config, d.FileName); }), Button("Import Configuration", () => { var d = new OpenFileDialog { Filter = "Configuration|*.json" }; if (d.ShowDialog(this) == true) { var imported = MusicLibrary.Parse(File.ReadAllText(d.FileName)); imported.GamePath = config.GamePath; config = imported; Volume.Value = config.PreviewVolume; SaveDraft(); ShowPage("Settings"); } })))));
    }
    async Task ImportLegacy()
    {
        await Run("Importing legacy battle, boss, and victory pools…", async () =>
        {
            var e = RequireEngine();
            var groups = await Task.Run(() => new[] { ("Battle", new[] { 14,16,19,29,22,23,24 }), ("Boss", new[] { 15,17,20,30,21,25 }), ("Victory", new[] { 9,13 }) }.Select(pair =>
            {
                string folder = Path.Combine(e.Game, "ogg", pair.Item1);
                return (Values: pair.Item2, Paths: Directory.Exists(folder) ? Directory.GetFiles(folder, "*.ogg") : Array.Empty<string>());
            }).ToArray());
            int total = groups.Sum(g => g.Paths.Length), offset = 0;
            foreach (var group in groups)
            {
                var tracks = await ImportTracks(group.Paths, offset, total); offset += group.Paths.Length;
                foreach (var track in tracks) config.Library[track.Id] = track;
                foreach (int value in group.Values)
                {
                    string id = MusicLibrary.Cues.Single(c => c.GameValue == value).Id;
                    if (!config.MusicPools.TryGetValue(id, out var pool)) config.MusicPools[id] = pool = new();
                    foreach (var track in tracks) if (!pool.Tracks.Contains(track.Id)) pool.Tracks.Add(track.Id);
                }
            }
            SaveDraft();
        });
    }
    UIElement AboutPage()
    {
        var details = new Expander { Header = "Advanced diagnostics", Content = Text($"Supported original: {Patches.Original}\nInstalled hash: {(engine != null && File.Exists(engine.Exe) ? Patches.Hash(engine.Exe) : "unavailable")}\nData: {library.Store}\nLog: {engine?.LogPath}", 12, "#A6BAC5"), Margin = new(0, 14, 0, 0) };
        return Card(Stack(Text("Crystal Project Mod Manager", 26), Text("0.1.0-rc1 · Development preview", 14, "#65D6C0"), Text("Supported game: 1.6.9.0\nMusic runtime: generalized queued cue pools\nHome Points runtime: preserved three-slot initialization\n71 music cues · 7 ambience cues"), Text("This preview requires native gameplay acceptance before a public release. No game executable, soundtrack, or save is distributed."), Actions(Button("Open Logs", () => Open(Path.Combine(library.Store, "logs"))), Button("Open Data Folder", () => Open(library.Store)), Button("View README", () => Open(Path.Combine(AppContext.BaseDirectory, "README.md")))), details));
    }
    Engine RequireEngine() => engine ?? throw new IOException("Select your Crystal Project installation first.");
    bool ConfirmHomeOff(bool home)
    {
        if (home || installed?.Home != true) return true;
        return MessageBox.Show(this, "Saves with more than three Home Points require this mod. Keep those saves and use a separate save with at most three points when running vanilla.\n\nContinue without changing any saves?", "Expanded save compatibility", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    async Task Apply()
    {
        if (!ConfirmHomeOff(config.UnlimitedHomePoints)) return;
        await Run("Preparing…", async () =>
        {
            var e = RequireEngine(); var selection = new Selection(config.MusicEnabled, config.UnlimitedHomePoints); SaveDraft();
            string work = Path.Combine(library.Store, "work", "music-" + Guid.NewGuid().ToString("N"));
            try
            {
                var assets = await Task.Run(() => { e.Idle(); return library.PrepareRuntime(config, work); });
                await Task.Run(() => e.Apply(selection, assets, new Progress<string>(stage => Dispatcher.InvokeAsync(() => Status.Text = stage))));
                MessageBox.Show(this, "Changes applied and verified. Launch Crystal Project normally through Steam.", "Ready to play");
            }
            finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
            await RefreshGame();
        });
    }
    async Task Restore()
    {
        if (!ConfirmHomeOff(false)) return;
        await Run("Restoring the verified original…", async () => { var e = RequireEngine(); await Task.Run(() => e.Apply(new(false, false))); await RefreshGame(); });
    }
    async Task Repair()
    {
        // Rebuild the deployed state, not unapplied editor changes.
        await Run("Recovering installation…", async () =>
        {
            var e = RequireEngine();
            await Task.Run(() =>
            {
                e.Recover(); var assets = new Dictionary<string,string>();
                foreach (var pair in e.Record()?.RuntimeHashes ?? [])
                {
                    string source = pair.Key.EndsWith("config.json") ? Path.Combine(library.Store, "runtime", pair.Value + ".json") : library.TrackPath(pair.Value);
                    if (!File.Exists(source) || MusicLibrary.Hash(source) != pair.Value) throw new IOException("Repair needs a verified managed copy: " + Path.GetFileName(pair.Key) + ". Locate missing music before trying again.");
                    assets[pair.Key] = source;
                }
                e.Apply(e.Inspect().Mods ?? throw new IOException("Unsupported game version. Verify game files in Steam."), assets, repairManagedFiles: true);
            });
            await RefreshGame();
        });
    }
    void SaveDraft() { library.Save(config); Status.Text = "Draft saved · Apply Changes when you're ready."; }
    async Task Run(string message, Func<Task> operation)
    {
        if (busy) return; busy = true; Navigation.IsEnabled = PageContent.IsEnabled = false; Status.Text = message;
        OperationProgress.Value = 0; OperationProgress.IsIndeterminate = true; OperationProgress.Visibility = Visibility.Visible;
        try { await operation(); }
        catch (Exception e) { Error(e); }
        finally { busy = false; OperationProgress.Visibility = Visibility.Collapsed; Navigation.IsEnabled = PageContent.IsEnabled = true; ShowPage(page); }
    }
    void Error(Exception e, string? friendly = null)
    {
        try { Directory.CreateDirectory(Path.Combine(library.Store, "logs")); File.AppendAllText(Path.Combine(library.Store, "logs", "manager.log"), DateTime.UtcNow.ToString("O") + " " + e + Environment.NewLine); } catch { }
        string message = friendly ?? (e is UnauthorizedAccessException ? "Access to the game or data folder was denied. Check folder permissions and try again." : e is System.Text.Json.JsonException ? "This configuration could not be read. Choose a valid Mod Manager configuration." : e.Message);
        MessageBox.Show(this, message, "Operation stopped", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    void Launch() { if (config.GamePath == null) throw new IOException("Select the game installation first."); Process.Start(new ProcessStartInfo("steam://rungameid/1637730") { UseShellExecute = true }); }
    static void Open(string? path) { if (string.IsNullOrWhiteSpace(path) || (!Directory.Exists(path) && !File.Exists(path))) throw new IOException("This folder or file is not available yet."); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    async Task RenderPages()
    {
        Directory.CreateDirectory(renderDirectory!);
        if (Environment.GetCommandLineArgs().Contains("--small")) { Width = 880; Height = 620; }
        if (Environment.GetCommandLineArgs().Contains("--populated"))
        {
            string fixtures = Path.Combine(Path.GetDirectoryName(renderDirectory!)!, "fixtures");
            var a = library.Import(Path.Combine(fixtures, "sine-a.ogg")); var b = library.Import(Path.Combine(fixtures, "sine-b.ogg"));
            a.Filename = "探索 — " + new string('M', 100) + ".ogg";
            var missing = new LibraryTrack { Id = new string('f', 64), Filename = "Missing audio.ogg", Duration = 120 };
            config.Library[a.Id] = a; config.Library[b.Id] = b; config.Library[missing.Id] = missing;
            currentCue = MusicLibrary.Cues.Single(c => c.GameValue == 32);
            config.MusicPools[currentCue.Id] = new() { Tracks = [a.Id, b.Id, missing.Id] };
        }
        double dpi = Environment.GetCommandLineArgs().Contains("--dpi150") ? 144 : 96;
        if (Environment.GetCommandLineArgs().Contains("--verify-browser")) VerifyMusicBrowser();
        if (config.GamePath != null) await LoadEnemySprites();
        foreach (string name in new[] { "Home", "Music", "Enemy Sprites", "Home Points", "Installation / Game", "Backups & Repair", "Settings", "About" })
        {
            ShowPage(name); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(ActualWidth * dpi / 96), (int)(ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(renderDirectory!, name.Replace(" / ", "-").Replace(" & ", "-") + ".png")); encoder.Save(stream);
        }
        Close();
    }
}
