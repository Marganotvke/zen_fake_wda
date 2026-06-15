using System.Text.RegularExpressions;

namespace ZenFakeCapture.Prefs;

/// <summary>
/// Reads zen.fake_transparency.* prefs from prefs.js with throttled disk access.
/// </summary>
internal sealed class PrefWatcher
{
    private const string BranchPrefix = "zen.fake_transparency.";
    private const int DefaultPollIntervalMs = 5000;

    private readonly string _profilePath;
    private readonly string _prefsPath;
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private long _lastPollTicks;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    private static readonly Regex PrefLine = new(
        @"user_pref\(""([^""]+)"",\s*(.+?)\);\s*$",
        RegexOptions.Compiled
    );

    public PrefWatcher(string profilePath)
    {
        _profilePath = profilePath;
        _prefsPath = Path.Combine(profilePath, "prefs.js");
    }

    /// <summary>
    /// Polls prefs.js at most once per interval. Reads only mod-branch lines when changed.
    /// </summary>
    public bool TryReloadIfChanged(int minIntervalMs = DefaultPollIntervalMs)
    {
        var now = Environment.TickCount64;
        if (now - _lastPollTicks < minIntervalMs)
        {
            return false;
        }

        _lastPollTicks = now;

        if (!File.Exists(_prefsPath))
        {
            return false;
        }

        DateTime write;
        try
        {
            write = File.GetLastWriteTimeUtc(_prefsPath);
        }
        catch
        {
            return false;
        }

        if (write <= _lastWriteUtc)
        {
            return false;
        }

        _lastWriteUtc = write;
        ParseBranchPrefs();
        return true;
    }

    private void ParseBranchPrefs()
    {
        _cache.Clear();
        try
        {
            foreach (var line in File.ReadLines(_prefsPath))
            {
                if (!line.Contains(BranchPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var m = PrefLine.Match(line);
                if (!m.Success)
                {
                    continue;
                }

                var name = m.Groups[1].Value;
                if (name.StartsWith(BranchPrefix, StringComparison.Ordinal))
                {
                    _cache[name] = m.Groups[2].Value.Trim();
                }
            }
        }
        catch
        {
            // prefs.js may be mid-write; keep previous cache.
        }
    }

    public int GetInt(string name, int fallback)
    {
        if (!_cache.TryGetValue(name, out var raw))
        {
            return fallback;
        }

        raw = raw.Trim('"');
        return int.TryParse(raw, out var value) ? value : fallback;
    }

    public string GetString(string name, string fallback)
    {
        if (!_cache.TryGetValue(name, out var raw))
        {
            return fallback;
        }

        if (raw.StartsWith('"') && raw.EndsWith('"'))
        {
            return raw[1..^1];
        }

        return raw;
    }

    public string ProfilePath => _profilePath;
}
