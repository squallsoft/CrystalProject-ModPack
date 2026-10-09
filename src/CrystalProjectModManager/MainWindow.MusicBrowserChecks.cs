using CrystalProjectModManager.Core;
using System.IO;

namespace CrystalProjectModManager;

public partial class MainWindow
{
    // Runs only in the isolated --render workspace; never uses player assignments.
    void VerifyMusicBrowser()
    {
        var saved = config;
        var savedCue = currentCue;
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Music browser check failed: " + name);
            checks.Add("PASS " + name);
        }
        try
        {
            config = new Configuration();
            var cues = MusicLibrary.Cues.OrderBy(c => c.DisplayName).Take(3).ToArray();
            string trackId = new('a', 64);
            config.Library[trackId] = new() { Id = trackId, Filename = "Browser check.ogg" };
            config.MusicPools[cues[1].Id] = new() { Tracks = [trackId] };
            config.MusicPools[cues[2].Id] = new() { Enabled = false, Tracks = [trackId] };
            ShowPage("Music");
            Check(cueList!.Items.Count == MusicLibrary.Cues.Count, "All cues visible");
            replacementFilter!.SelectedItem = "Replaced";
            Check(cueList.Items.Count == 1 && ((CueRow)cueList.Items[0]).Cue == cues[1], "Only enabled nonempty pools are replaced");
            replacementFilter.SelectedItem = "Not replaced";
            Check(cueList.Items.Count == MusicLibrary.Cues.Count - 1 && cueList.Items.Cast<CueRow>().Any(r => r.Cue == cues[2]), "Empty and disabled pools are not replaced");
            replacementFilter.SelectedItem = "All songs";
            cueSort!.SelectedItem = "Replaced first";
            Check(((CueRow)cueList.Items[0]).Cue == cues[1], "Replaced first sorting");
            cueSort.SelectedItem = "Not replaced first";
            Check(((CueRow)cueList.Items[^1]).Cue == cues[1], "Not replaced first sorting");
            cueSort.SelectedItem = "Name A–Z";
            Check(cueList.Items.Cast<CueRow>().Select(r => r.Name).SequenceEqual(MusicLibrary.Cues.OrderBy(c => c.DisplayName).Select(c => c.DisplayName)), "Alphabetical sorting");
            config.MusicEnabled = false;
            replacementFilter.SelectedItem = "Replaced";
            Check(cueList.Items.Count == 0 && currentCue == null && trackList == null, "Global music off and empty results clear editor");
            config.MusicEnabled = true;
            replacementFilter.SelectedItem = "All songs";
            category!.SelectedItem = cues[1].Category;
            search!.Text = cues[1].GameCue;
            Check(cueList.Items.Count == 1, "Search combines with category");
            ShowPage("Home"); ShowPage("Music");
            Check(search!.Text == cues[1].GameCue && category!.SelectedItem.ToString() == cues[1].Category && cueList!.Items.Count == 1, "Browser choices survive page navigation");
            config.MusicPools[cues[1].Id].Tracks.Clear();
            replacementFilter!.SelectedItem = "Replaced";
            Check(cueList!.Items.Count == 0, "Cleared pool leaves replaced filter");
            File.WriteAllLines(Path.Combine(renderDirectory!, "music-browser-checks.txt"), checks);
        }
        finally
        {
            config = saved; currentCue = savedCue;
            musicSearch = ""; musicCategory = "All categories"; musicFilter = "All songs"; musicSort = "Name A–Z";
        }
    }
}
