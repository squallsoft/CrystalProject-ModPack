using System.IO;
using CrystalProjectModManager.Sprites;

namespace CrystalProjectModManager;

public partial class MainWindow
{
    // Runs in the isolated render workspace and never edits the source game or PNGs.
    async Task VerifySpriteScan()
    {
        var savedConfig = config;
        string? savedScanGame = spriteScanGame;
        var savedPlan = spritePlan;
        string savedStatus = spriteScanStatus;
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add("PASS " + name); }
        try
        {
            string fixture = Path.Combine(renderDirectory!, "scan-fixture");
            string game = Path.Combine(fixture, "game"), textures = Path.Combine(game, "Content", "Textures"); Directory.CreateDirectory(textures);
            File.Copy(Path.Combine(config.GamePath!, "Content", "Textures", "Monster.dat"), Path.Combine(textures, "Monster.dat"));
            var service = new EnemySprites(game, Path.Combine(fixture, "data")); string folder = service.ExtractAll(Path.Combine(fixture, "editing"));
            var entry = service.Catalog()[0]; byte[] oversized = entry.Png.ToArray();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(oversized.AsSpan(16, 4), 8193);
            File.WriteAllBytes(Path.Combine(folder, entry.Filename), oversized);
            config = new() { GamePath = game, EnemySpritesFolder = folder }; spriteScanGame = game;
            ShowPage("Enemy Sprites");
            var scan = CheckEnemySpriteEdits();
            Check(spriteScanning && Navigation.IsEnabled && PageContent.IsEnabled && !busy, "Scan keeps navigation and page controls available");
            ShowPage("Settings"); Check(page == "Settings", "User can navigate away during a scan");
            Check(!await scan && spritePlan == null && spriteScanStatus.Contains(entry.Filename) && spriteScanStatus.Contains("8193"), "Oversized image produces inline filename and size feedback");
            ShowPage("Enemy Sprites");
            Check(spriteApplyButton?.IsEnabled == false && PageContent.IsEnabled, "Invalid images block Apply while browsing stays enabled");
            var superseded = CheckEnemySpriteEdits(); var latest = CheckEnemySpriteEdits(); await superseded; await latest;
            Check(spriteScanStatus.Contains(entry.Filename) && !spriteScanStatus.Contains("operation is in progress"), "Rapid rescan waits for cancelled scan and keeps the latest result");
            var cancelling = CheckEnemySpriteEdits(); spriteScanCancellation!.Cancel(); await cancelling;
            Check(!spriteScanning && spriteScanStatus.Contains("cancelled"), "User can cancel the background scan");
            File.WriteAllBytes(Path.Combine(folder, entry.Filename), entry.Png);
            Check(await CheckEnemySpriteEdits() && spritePlan != null, "Fixing an invalid file allows a fresh scan to succeed");
            File.WriteAllLines(Path.Combine(renderDirectory!, "sprite-scan-checks.txt"), checks);
        }
        finally { config = savedConfig; spriteScanGame = savedScanGame; spritePlan = savedPlan; spriteScanStatus = savedStatus; ShowPage("Home"); }
    }
}
