using System.Text.Json;

namespace NomadModDownloader.Core;

/// <summary>
/// "Find the bad mod": halves the list of suspects each round. Half of them are switched off (plus any mod that needs
/// one of them, since it couldn't start anyway), the player plays, and says whether the problem is still there.
/// Only DLLs that were on at the start are ever switched, and the state is saved after every step, so closing the app
/// (or the game crashing) never leaves mods half switched off: Stop puts every one of them back.
/// </summary>
public sealed class BadModFinder
{
    public sealed class Mod
    {
        public string Rel { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Dlls { get; set; } = new();            // were on at the start (full paths)
        public List<string> Guids { get; set; } = new();
        public List<string> Needs { get; set; } = new();           // hard dependencies (plugin ids)
    }

    public string GameRoot { get; set; } = "";
    public DateTime Started { get; set; }
    public List<Mod> Mods { get; set; } = new();
    public List<string> Suspects { get; set; } = new();            // Rel of the mods that may be the cause
    public List<string> Off { get; set; } = new();                 // Rel of the mods switched off this round
    public List<string> Tested { get; set; } = new();              // Rel of the suspects this round tests (switched off)
    public int Round { get; set; }
    public string? Result { get; set; }                            // Rel of the bad mod once found, "" = not a single mod
    public bool Confirmed { get; set; }                            // the last suspect was shown to be it (the problem went away with it off)

    private static string StateFile => Path.Combine(AppPaths.Root, "find-bad-mod.json");

    public static BadModFinder? Load()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<BadModFinder>(File.ReadAllText(StateFile)) : null; }
        catch { return null; }
    }

    public void Save() => File.WriteAllText(StateFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public static BadModFinder Start(GameInstall game)
    {
        if (game.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive first, then try again."));
        var mods = ModEntries.Scan(game).Where(e => e.On).Select(e => new Mod
        {
            Rel = e.Rel, Name = e.Name, Dlls = e.Dlls.ToList(),
            Guids = e.Plugins.Where(p => e.Dlls.Contains(p.File)).Select(p => p.Guid).ToList(),
            Needs = e.Plugins.Where(p => e.Dlls.Contains(p.File)).SelectMany(p => p.HardDependencies).Distinct().ToList(),
        }).ToList();
        if (mods.Count == 0) throw new InvalidOperationException(L.T("There are no mods switched on to test."));
        var f = new BadModFinder { GameRoot = game.Root, Started = DateTime.Now, Mods = mods, Suspects = mods.Select(m => m.Rel).ToList() };
        f.NextRound(game);
        return f;
    }

    public Mod? Find(string rel) => Mods.FirstOrDefault(m => m.Rel == rel);

    /// <summary>The mods these depend on, directly or not: with one of those off, these can't start either.</summary>
    private HashSet<string> WithDependents(IEnumerable<string> rels)
    {
        var off = new HashSet<string>(rels);
        bool grew;
        do
        {
            grew = false;
            var offGuids = Mods.Where(m => off.Contains(m.Rel)).SelectMany(m => m.Guids).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Mods.Where(m => !off.Contains(m.Rel) && m.Needs.Any(offGuids.Contains)))
            { off.Add(m.Rel); grew = true; }
        } while (grew);
        return off;
    }

    /// <summary>Picks the next half to test and switches exactly that set off (everything else from the start back on).</summary>
    private void NextRound(GameInstall game)
    {
        // Done when nothing is left (not one mod on its own), or the one left was proven: switching it off made the
        // problem go away. A last suspect that wasn't proven yet gets a round of its own.
        if (Suspects.Count == 0 || (Suspects.Count == 1 && Confirmed)) { Result = Suspects.FirstOrDefault() ?? ""; Apply(game, new HashSet<string>()); Save(); return; }
        Round++;
        Tested = Suspects.Take((Suspects.Count + 1) / 2).ToList();
        var off = WithDependents(Tested);
        Apply(game, off);
        Off = off.ToList();
        Save();
    }

    /// <summary>Switches off the mods in <paramref name="off"/> and back on every other mod that was on at the start.</summary>
    private void Apply(GameInstall game, HashSet<string> off)
    {
        if (game.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive first, then try again."));
        foreach (var m in Mods)
        {
            if (off.Contains(m.Rel)) ModEntries.SwitchOff(m.Dlls);
            else ModEntries.SwitchOn(m.Dlls);
        }
    }

    /// <summary>The player's answer after playing with this round's mods off.</summary>
    public void Answer(GameInstall game, bool problemStillThere)
    {
        if (Result != null) return;
        var offNow = Off.ToHashSet();
        // Still there: none of the mods that were off caused it. Gone: it's one of them (a tested suspect, or a mod
        // that was off only because it needs one).
        Suspects = problemStillThere ? Suspects.Where(s => !offNow.Contains(s)).ToList() : Suspects.Where(offNow.Contains).ToList();
        Confirmed = !problemStillThere && Suspects.Count == 1;
        NextRound(game);
    }

    /// <summary>Puts every mod back on as it was at the start (optionally leaving the bad one off) and forgets the session.</summary>
    public void Stop(GameInstall game, string? leaveOff = null)
    {
        if (game.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive first, then try again."));
        foreach (var m in Mods)
        {
            if (m.Rel == leaveOff) ModEntries.SwitchOff(m.Dlls);
            else ModEntries.SwitchOn(m.Dlls);
        }
        try { File.Delete(StateFile); } catch { }
    }
}
