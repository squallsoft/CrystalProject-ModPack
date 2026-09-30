using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace CrystalProjectModInstaller;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
    internal static IEnumerable<string> Detect()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[] { Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null), Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") })
            if (value is string path && Directory.Exists(path)) roots.Add(path);
        foreach (var root in roots.ToArray())
        {
            string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(file)) foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"path\"\\s+\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            foreach (string name in new[] { "SteamLibrary", "Steam", @"Games\Steam" }) roots.Add(Path.Combine(drive.RootDirectory.FullName, name));
        foreach (string root in roots)
        {
            string game = Path.Combine(root, "steamapps", "common", "Crystal Project");
            string manifest = Path.Combine(root, "steamapps", "appmanifest_1637730.acf");
            if (File.Exists(manifest))
            {
                var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                if (m.Success) game = Path.Combine(root, "steamapps", "common", m.Groups[1].Value);
            }
            if (File.Exists(Path.Combine(game, "Crystal Project.exe"))) yield return game;
        }
    }
}

internal sealed class MainForm : Form
{
    readonly TextBox path = new() { Dock = DockStyle.Fill };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(650, 0) };
    readonly Label hash = new() { AutoSize = true, MaximumSize = new Size(650, 0) };
    readonly CheckBox music = new() { Text = "Random Battle / Boss / Victory Music (custom OGG folders)", AutoSize = true };
    readonly CheckBox home = new() { Text = "Unlimited Home Points (requires Enhanced Home Point in game)", AutoSize = true };
    readonly Button apply = new() { Text = "Install / Apply Changes", AutoSize = true };
    readonly Button restore = new() { Text = "Restore Vanilla", AutoSize = true };
    readonly Button repair = new() { Text = "Repair", AutoSize = true };
    readonly Button clean = new() { Text = "Clean Legacy Mod Files", AutoSize = true };
    Engine? engine;
    public MainForm()
    {
        Text = "Crystal Project Mod Installer"; MinimumSize = new Size(740, 490); Size = new Size(800, 510); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 9 };
        layout.Controls.Add(new Label { Text = "Crystal Project Mod Installer", Font = new Font(Font.FontFamily, 19, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label { Text = "Release candidate · Native gameplay validation required", AutoSize = true });
        var folder = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true }; folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Browse…", AutoSize = true }; folder.Controls.Add(path); folder.Controls.Add(browse); layout.Controls.Add(folder);
        layout.Controls.Add(status); layout.Controls.Add(music); layout.Controls.Add(home);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top }; buttons.Controls.AddRange([apply, restore, repair, clean]); layout.Controls.Add(buttons);
        var details = new Button { Text = "About / Details", AutoSize = true }; layout.Controls.Add(details); layout.Controls.Add(hash); Controls.Add(layout);
        path.Text = Program.Detect().FirstOrDefault() ?? "";
        browse.Click += (_, _) => { using var d = new FolderBrowserDialog { Description = "Select the Crystal Project game folder", InitialDirectory = path.Text }; if (d.ShowDialog() == DialogResult.OK) { path.Text = d.SelectedPath; RefreshState(); } };
        path.Leave += (_, _) => RefreshState();
        apply.Click += async (_, _) => { var selected = new Selection(music.Checked, home.Checked); if (!selected.Home && !ConfirmDisableHome()) return; await Run(e => { e.Apply(selected); return "Changes applied and verified."; }); };
        restore.Click += async (_, _) => { if (!ConfirmDisableHome()) return; await Run(e => { e.Apply(new(false, false)); return "Vanilla executable restored. Saves were not changed. Keep Home Points enabled when loading saves containing more than three points."; }); };
        repair.Click += async (_, _) => await Run(e => { e.Recover(); e.Apply(e.Inspect().Mods ?? throw new IOException("Unsupported version. Use Steam's Verify integrity of game files.")); return "Installed configuration rebuilt and verified."; });
        clean.Click += async (_, _) => await Run(e => "Legacy files archived and removed. Report: " + e.CleanLegacy());
        details.Click += (_, _) => MessageBox.Show(this, $"Installer: {Engine.Version}\nSupported game: 1.6.9.0 (exact hash only)\nMusic: 0.2.3 nested LoopPoint runtime\nHome Points: 0.1.0-rc1\nInstalled: {engine?.Record()?.Mods}\nBackup: {engine?.FindOriginal() ?? "missing"}\nLog: {engine?.LogPath}\n\nThis build requests Administrator access at launch for Program Files installations. Backups use the elevated account's LocalAppData.\n\nBefore disabling Home Points, retain an expanded save and use a separate vanilla save. Vanilla cannot safely sanitize expanded arrays.", "About / Details");
        Shown += (_, _) => RefreshState();
    }
    bool ConfirmDisableHome()
    {
        if (engine?.Inspect().Mods?.Home != true) return true;
        return MessageBox.Show(this, "Saves with more than three Home Points require this mod. Vanilla cannot safely load them. Keep those saves and use a separate save with at most three points.\n\nDisable Unlimited Home Points without changing any saves?", "Expanded save compatibility", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }
    void RefreshState()
    {
        try
        {
            engine = new Engine(path.Text); var s = engine.Inspect(); status.Text = s.Status;
            if (s.Mods != null) { music.Checked = s.Mods.Music; home.Checked = s.Mods.Home; }
            apply.Enabled = restore.Enabled = clean.Enabled = s.Mods != null; repair.Enabled = true;
            hash.Text = "Installer " + Engine.Version + "\nEXE SHA-256: " + (File.Exists(engine.Exe) ? Patches.Hash(engine.Exe) : "not found");
        }
        catch (Exception e) { status.Text = e.Message; apply.Enabled = restore.Enabled = clean.Enabled = false; }
    }
    async Task Run(Func<Engine, string> action)
    {
        if (engine == null) return;
        Enabled = false; UseWaitCursor = true;
        try { string message = await Task.Run(() => action(engine)); MessageBox.Show(this, message, "Crystal Project Mod Installer"); }
        catch (Exception e) { engine.Log(e.ToString()); MessageBox.Show(this, e.Message + "\n\nLog: " + engine.LogPath, "Operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Enabled = true; UseWaitCursor = false; RefreshState(); }
    }
}
