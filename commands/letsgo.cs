using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

// Trigger: Twitch -> Command, with "!letsgo" and "!lg" both in the command box — every
// subcommand works under either name. Chat-only by default. Counts how many times the
// streamer says "let's go" — chat is the one listening, so chat does the counting.
//
//   !letsgo               -> +1, "LET'S GOOO! #14 this stream, 1,203 all time"     (anyone)
//   !letsgo count / ?     -> the numbers, without adding one                        (anyone)
//   !letsgo undo / -1     -> take one back                                     (mods, VIPs)
//   !letsgo set <n>       -> set this stream's count exactly                   (mods, VIPs)
//   !letsgo setall <n>    -> set the all-time count exactly                    (mods, VIPs)
//   !letsgo help [command] -> what's available, or what one command does          (anyone)
//
// Five people typing !letsgo at the same "let's go" are five people agreeing, not five — so a
// burst inside BurstSeconds counts once, and the calls after the first are silent. Mods and
// VIPs are not exempt: they are hearing the same "let's go" as everyone else.
//
// "This stream" needs no Stream Online trigger: a count after more than NewStreamAfterHours of
// quiet starts the stream count over. The all-time count never resets on its own.
public class CPHInline
{
    // Off: anyone can count. On makes adding one a mods-and-VIPs-only button too.
    private const bool ModsOnly = false;

    // One "let's go", however many people type it. Long enough for chat to react, short
    // enough that two in quick succession — it happens — can both be counted.
    private const int BurstSeconds = 10;

    // A gap this long between counts means a new stream.
    private const int NewStreamAfterHours = 6;

    // An OBS Text source, as death.cs does it. Off by default — make a Text source named
    // "LetsGo" in OBS first, then switch this on.
    private const bool UpdateObsText = false;
    private const string ObsTextSource = "LetsGo";
    private const int ObsConnection = 0;
    // {stream} and {total} are substituted.
    private const string ObsTextFormat = "Let's go: {stream}";

    private const int MaxCount = 1000000;

    // All-time counts that get a line of their own instead of the usual one.
    private static readonly Dictionary<int, string> Milestones = new Dictionary<int, string>
    {
        [100] = "🎉 LET'S GOOO #100! A hundred times, and every one of them meant.",
        [250] = "🎉 LET'S GOOO #250! At this point it's less a phrase and more a lifestyle.",
        [500] = "🎉 LET'S GOOO #500! Five hundred. The tavern is engraving it over the door.",
        [1000] = "🎉 LET'S GOOO #1,000! One thousand. Bards will sing of this. Loudly. Probably saying let's go.",
        [5000] = "🎉 LET'S GOOO #5,000! Linguists are now studying this channel."
    };

    // Persisted, so a Streamer.bot restart mid-stream changes nothing.
    private const string StreamVar = "letsgo.stream";
    private const string TotalVar = "letsgo.total";
    private const string LastAtVar = "letsgo.lastAt";

    // A Hot Key carries no user — that is you, at your own keyboard, and it skips the mod check
    // the way death.cs's hotkey does.
    private bool fromChat;

    public bool Execute()
    {
        try
        {
            return Run();
        }
        catch (Exception error)
        {
            CPH.LogWarn("[letsgo] failed: " + error);
            CPH.SendMessage("The let's go counter hit an error - the Streamer.bot log has the details");
            return true;
        }
    }

    private bool Run()
    {
        fromChat = args.ContainsKey("userName") || args.ContainsKey("user");
        string user = Arg(new[] { "user", "userName" }, "someone");

        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        // Whichever name it was typed as — !letsgo or !lg — the command word itself goes.
        if (raw.StartsWith("!"))
        {
            int space = raw.IndexOf(' ');
            raw = space < 0 ? "" : raw.Substring(space + 1).Trim();
        }
        string lower = raw.ToLowerInvariant();

        // Anything a viewer tacks on — "!letsgo LETS GOOOO" — is cheering, not a subcommand.
        int n;
        if (lower == "count" || lower == "?" || lower == "show") { Report(); return true; }
        if (lower == "help" || lower.StartsWith("help ")) { Help(lower.Substring(4).Trim()); return true; }
        if (lower == "undo" || lower == "-1" || lower == "-" || lower == "oops") { Undo(user); return true; }
        if (lower.StartsWith("set ") && int.TryParse(raw.Substring(4).Trim(), out n)) { Set(StreamVar, n, user); return true; }
        if (lower.StartsWith("setall ") && int.TryParse(raw.Substring(7).Trim(), out n)) { Set(TotalVar, n, user); return true; }

        Add(user);
        return true;
    }

    private void Add(string user)
    {
        if (ModsOnly && !MayEdit(user)) return;

        DateTime now = DateTime.UtcNow;
        TimeSpan? since = SinceLast(now);
        if (fromChat && since != null && since.Value.TotalSeconds < BurstSeconds) return;

        bool newStream = since == null || since.Value.TotalHours >= NewStreamAfterHours;
        int stream = newStream ? 1 : Clamp(Count(StreamVar) + 1);
        int total = Clamp(Count(TotalVar) + 1);

        CPH.SetGlobalVar(StreamVar, stream, true);
        CPH.SetGlobalVar(TotalVar, total, true);
        CPH.SetGlobalVar(LastAtVar, now.Ticks.ToString(), true);
        Show(stream, total);

        CPH.SendMessage(Milestones.ContainsKey(total)
            ? Milestones[total]
            : "LET'S GOOO! #" + Num(stream) + " this stream, " + Num(total) + " all time");
    }

    private void Undo(string user)
    {
        if (!MayEdit(user)) return;
        int stream = Clamp(Count(StreamVar) - 1);
        int total = Clamp(Count(TotalVar) - 1);
        CPH.SetGlobalVar(StreamVar, stream, true);
        CPH.SetGlobalVar(TotalVar, total, true);
        Show(stream, total);
        CPH.SendMessage("Un-let's-go'd. Back to " + Num(stream) + " this stream, " + Num(total) + " all time");
    }

    // `set` and `setall` move one number only, so fixing one never quietly changes the other.
    private void Set(string var, int n, string user)
    {
        if (!MayEdit(user)) return;
        if (n < 0 || n > MaxCount) { CPH.SendMessage(user + ", pick a number between 0 and " + Num(MaxCount)); return; }
        CPH.SetGlobalVar(var, n, true);
        // Setting the stream count is "we're mid-stream", so it shouldn't be wiped by the
        // new-stream gap on the very next !letsgo.
        if (var == StreamVar) CPH.SetGlobalVar(LastAtVar, DateTime.UtcNow.Ticks.ToString(), true);
        Show(Count(StreamVar), Count(TotalVar));
        CPH.SendMessage((var == StreamVar ? "This stream's" : "The all-time") + " let's go count is now " + Num(n));
    }

    private void Report()
    {
        // A stream count from a previous stream is zero for this one.
        TimeSpan? since = SinceLast(DateTime.UtcNow);
        int stream = since == null || since.Value.TotalHours >= NewStreamAfterHours ? 0 : Count(StreamVar);
        int total = Count(TotalVar);
        CPH.SendMessage(total == 0
            ? "Not a single let's go on record yet. Suspicious."
            : "Let's go count: " + Num(stream) + " this stream, " + Num(total) + " all time");
    }

    // One line per subcommand, for `!letsgo help <command>`, the way death.cs does it. Every
    // alias the parser accepts points at the same entry.
    private static readonly Dictionary<string, string> HelpTopics = new Dictionary<string, string>
    {
        ["add"] = "!letsgo counts one let's go - anyone can use it. If a few people type it at once, it only counts once.",
        ["count"] = "!letsgo count shows this stream's let's go count and the all-time count, without adding one.",
        ["undo"] = "!letsgo undo takes one let's go back, for a miscount. Mods and VIPs.",
        ["set"] = "!letsgo set <n> sets this stream's count exactly. The all-time count isn't touched. Mods and VIPs.",
        ["setall"] = "!letsgo setall <n> sets the all-time count exactly. This stream's count isn't touched. Mods and VIPs."
    };

    private static readonly Dictionary<string, string> HelpAliases = new Dictionary<string, string>
    {
        ["letsgo"] = "add", ["lg"] = "add", ["+"] = "add",
        ["?"] = "count", ["show"] = "count",
        ["-1"] = "undo", ["-"] = "undo", ["oops"] = "undo"
    };

    /**
     * `!letsgo help` lists what the asker can actually use — everything for mods and VIPs, just
     * counting and the score for everyone else. `!letsgo help <command>` explains one, for anyone.
     */
    private void Help(string topic)
    {
        string key = topic.Split(' ')[0].TrimStart('!');
        if (key.Length > 0)
        {
            if (HelpAliases.ContainsKey(key)) key = HelpAliases[key];
            CPH.SendMessage(HelpTopics.ContainsKey(key)
                ? HelpTopics[key]
                : "No help for that one - try count, undo, set or setall");
            return;
        }

        CPH.SendMessage(CanEdit()
            ? "!letsgo or !lg (+1, anyone) | !letsgo count | !letsgo undo | !letsgo set <n> (this stream) | !letsgo setall <n> (all time) - !letsgo help <command> for more"
            : "Hear a let's go? Type !letsgo (or !lg) to count it! !letsgo count shows the score - !letsgo help <command> for more");
    }

    // ---- display --------------------------------------------------------------------------

    private void Show(int stream, int total)
    {
        if (!UpdateObsText || ObsTextSource.Length == 0) return;
        string text = ObsTextFormat.Replace("{stream}", Num(stream)).Replace("{total}", Num(total));
        var settings = new Dictionary<string, object> { ["text"] = text };
        var request = new Dictionary<string, object>
        {
            ["inputName"] = ObsTextSource,
            ["inputSettings"] = settings
        };
        CPH.ObsSendRaw("SetInputSettings", JsonConvert.SerializeObject(request), ObsConnection);
    }

    // "1,203" whatever the PC's region settings, so chat sees the same number everywhere.
    private string Num(int n)
    {
        return n.ToString("N0", CultureInfo.InvariantCulture);
    }

    // ---- state ----------------------------------------------------------------------------

    // Time since the last counted let's go, or null when there's never been one or the clock
    // reads backwards — both of which are treated as "a new stream".
    private TimeSpan? SinceLast(DateTime now)
    {
        long ticks;
        if (!long.TryParse(Str(LastAtVar), out ticks)) return null;
        TimeSpan since = now - new DateTime(ticks, DateTimeKind.Utc);
        return since.TotalSeconds < 0 ? (TimeSpan?)null : since;
    }

    private int Count(string var)
    {
        int n;
        int.TryParse(Str(var), out n);
        return Clamp(n);
    }

    private static int Clamp(int value)
    {
        if (value < 0) return 0;
        return value > MaxCount ? MaxCount : value;
    }

    // Read as <string>: <object> runs the stored value through a JSON parser, which throws on
    // anything that isn't valid JSON — see death.cs. <object> is only the fallback.
    private string Str(string key)
    {
        try
        {
            string text = CPH.GetGlobalVar<string>(key, true);
            if (text != null) return text.Trim();
        }
        catch { }
        try
        {
            return (Convert.ToString(CPH.GetGlobalVar<object>(key, true)) ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    private bool MayEdit(string user)
    {
        if (CanEdit()) return true;
        CPH.SendMessage(user + ", only mods and VIPs can fix the let's go count - !letsgo count shows it");
        return false;
    }

    private bool CanEdit()
    {
        if (!fromChat) return true;
        return Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator", "isVip", "vip" });
    }

    private bool Flag(string[] keys)
    {
        foreach (var key in keys)
        {
            if (!args.ContainsKey(key) || args[key] == null) continue;
            string value = Convert.ToString(args[key]);
            if (value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1") return true;
        }
        return false;
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
