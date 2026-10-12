using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

// Trigger: Twitch -> Command, with "!death" and "!deaths" both in the command box — one action
// serves both, and the plural is the read-only one so a viewer asking for the score can never
// add to it.
//
//   !death                   -> +1, "Death #12"                                    (mods)
//   !death undo / -1         -> take one back, for a misfire                       (mods)
//   !death 3                 -> +3, for a spiral nobody was counting through       (mods)
//   !death set <n>           -> absolute, after counting off-stream                (mods)
//   !death reset [name]      -> back to zero, optionally naming the new run        (mods)
//   !death challenge <name>  -> rename the run without resetting it                (mods)
//   !death hide              -> take the counter off screen entirely               (mods)
//   !deaths                  -> "12 deaths so far (Elden Ring no-hit)"           (anyone)
//
// A bare argument is always a *delta* — `!death 3` adds three. `set` is the only absolute,
// which is what stops "!death 12" silently wiping eleven deaths when a mod meant to add some.
//
// The count itself lives in persisted globals (death.count / death.label), which is what
// survives a Streamer.bot restart mid-run. Everything on screen is a copy of that, written by
// Show() — by default an OBS Text source, addressed by name over Streamer.bot's OBS
// connection. OBS saves a text source's contents with the scene collection, so that copy
// survives an OBS restart too, and there is nothing to replay on startup.
//
// The alerts overlay's corner counter is the other way to show the same number, off by
// default — see ShowOnOverlay below, and counters-refresh.cs, which only matters if it is on.
public class CPHInline
{
    // Only mods and the broadcaster can change the count; anyone can ask for it. Off lets chat
    // add deaths, which is funny for about ninety seconds.
    private const bool ModsOnly = true;

    // ---- where the number is shown --------------------------------------------------------
    //
    // Two independent ways out, and neither is required — with both off, !death is a chat
    // command that keeps a tally, which is a perfectly reasonable thing to want.

    // An OBS Text source, written straight through Streamer.bot's OBS connection. The default
    // rig: no browser source, nothing to refresh, and OBS keeps the text in the scene
    // collection, so the count is still on screen after an OBS restart.
    private const bool UpdateObsText = true;

    // The source's name in OBS — its name, not the scene's, so the same counter can sit in as
    // many scenes as you like. Must match exactly. Works for Text (GDI+) and FreeType2 alike.
    private const string ObsTextSource = "Deaths";

    // Which OBS connection, for a rig with more than one (Platforms -> OBS Studio in SB).
    private const int ObsConnection = 0;

    // {count} and {label} are substituted. An unnamed run leaves {label} empty, so anything
    // wrapped around it — " ({label})" — is trimmed off the end when there is no name.
    private const string ObsTextFormat = "Deaths: {count}";

    // The alerts overlay's corner counter (src/alerts/). Off by default: it is the second way
    // to show the same number, and running both means two of them on screen.
    private const bool ShowOnOverlay = false;

    // A plain text file for an OBS Text source with "Read from file" ticked. Empty means off.
    // The alternative to the OBS connection above rather than a companion to it — useful if
    // Streamer.bot is not connected to OBS, since a file needs no connection at all.
    private const string TextFilePath = "";

    // A run this long is a typo, not a challenge. Caps what `set` will accept.
    private const int MaxCount = 100000;
    private const int MaxLabelLength = 40;

    // The overlay keys its counters by name; this is the address the display is drawn under.
    private const string CounterKey = "deaths";

    private const string CountVar = "death.count";
    private const string LabelVar = "death.label";

    // Whether this run came from chat at all. A Hot Key — the natural way to add a death while
    // you are playing — hands over no user and no mod flag, so the ModsOnly check has to know
    // the difference between "a viewer who isn't a mod" and "nobody, because you pressed a
    // button on your own machine". A channel point redeem still carries a user, and is still
    // screened.
    private bool fromChat;

    public bool Execute()
    {
        fromChat = args.ContainsKey("userName") || args.ContainsKey("user");
        string user = Arg(new[] { "userName", "user" }, "someone");

        // A Command trigger puts only the tail in rawInput; a plain Chat Message trigger hands
        // over the whole line, so strip the leading word off that instead.
        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!death", StringComparison.OrdinalIgnoreCase))
        {
            int space = raw.IndexOf(' ');
            raw = space < 0 ? "" : raw.Substring(space + 1).Trim();
        }

        string invoked = Arg(new[] { "command", "message" }, "").TrimStart('!');
        bool readOnly = invoked.StartsWith("deaths", StringComparison.OrdinalIgnoreCase);

        if (raw.Length == 0)
        {
            if (readOnly) Report(); else Add(1, user);
            return true;
        }

        int delta;
        if (int.TryParse(raw, out delta)) { Add(delta, user); return true; }

        string lower = raw.ToLowerInvariant();
        if (lower == "+") { Add(1, user); return true; }
        if (lower == "-" || lower == "undo" || lower == "oops") { Add(-1, user); return true; }
        if (lower == "count" || lower == "show" || lower == "?") { Report(); return true; }

        int n;
        if (lower.StartsWith("set ") && TryTail(raw, 4, out n)) { Set(n, user); return true; }
        if (lower == "reset" || lower == "new") { Reset("", user); return true; }
        if (lower.StartsWith("reset ")) { Reset(raw.Substring(6).Trim(), user); return true; }
        if (lower.StartsWith("new ")) { Reset(raw.Substring(4).Trim(), user); return true; }
        if (lower.StartsWith("challenge ")) { Rename(raw.Substring(10).Trim(), user); return true; }
        if (lower.StartsWith("run ")) { Rename(raw.Substring(4).Trim(), user); return true; }
        if (lower == "hide" || lower == "off" || lower == "stop") { Hide(user); return true; }

        CPH.SendMessage(user + ", usage: !deaths | !death | !death undo | !death set <n> | !death reset [name]");
        return true;
    }

    private void Add(int delta, string user)
    {
        if (!MayEdit(user)) return;
        if (delta == 0) { Report(); return; }

        // You cannot have died a negative number of times. An undo one past the start is a
        // miscount being corrected, not something to refuse.
        int count = Clamp(GetCount() + delta);
        Store(count);

        if (delta < 0) CPH.SendMessage("Un-died. Back to " + count + Suffix());
        else if (delta > 1) CPH.SendMessage("💀 +" + delta + " — that's " + count + " now" + Suffix());
        else CPH.SendMessage("💀 Death #" + count + Suffix());
    }

    private void Set(int n, string user)
    {
        if (!MayEdit(user)) return;
        if (n < 0 || n > MaxCount) { CPH.SendMessage(user + ", pick a number between 0 and " + MaxCount); return; }
        Store(n);
        CPH.SendMessage("Death count set to " + n + Suffix());
    }

    // The start of a run as much as the end of one: zero is a number worth showing, so the
    // overlay keeps the counter on screen at 0 rather than treating it as nothing to draw.
    private void Reset(string label, string user)
    {
        if (!MayEdit(user)) return;
        int was = GetCount();
        if (label.Length > 0) SetLabel(label);
        Store(0);

        string now = GetLabel();
        CPH.SendMessage(now.Length > 0
            ? "Deaths reset to 0 for " + now + " (last run: " + was + ")"
            : "Deaths reset to 0 (was " + was + ")");
    }

    // Renaming without resetting: the run carries on, it just gets called something. Clearing
    // the name is a legitimate thing to want, so an empty argument is accepted rather than
    // bounced back as a usage error.
    private void Rename(string label, string user)
    {
        if (!MayEdit(user)) return;
        SetLabel(label);
        int count = GetCount();
        Store(count);
        CPH.SendMessage(label.Length > 0
            ? "Now counting deaths for " + GetLabel() + " (at " + count + ")"
            : "Run name cleared (at " + count + ")");
    }

    // Between challenges there is nothing to count, and a stale "DEATHS 0" in the corner of a
    // just-chatting stream is worse than no counter at all. Unsets the globals too, so a
    // reconnecting overlay doesn't ask for it back a second later.
    private void Hide(string user)
    {
        if (!MayEdit(user)) return;
        CPH.UnsetGlobalVar(CountVar, true);
        CPH.UnsetGlobalVar(LabelVar, true);
        // Empty text renders as nothing, which is the whole trick — no scene item to hide, no
        // visibility to put back, and the source can stay in every scene it already sits in.
        Show("");
        Broadcast("clear", 0, "");
        CPH.SendMessage("Death counter is off. !death reset <name> starts a new run.");
    }

    private void Report()
    {
        int count = GetCount();
        if (count == 0) { CPH.SendMessage("No deaths yet" + Suffix() + ". Suspicious."); return; }
        CPH.SendMessage("💀 " + count + (count == 1 ? " death" : " deaths") + " so far" + Suffix());
    }

    /** " (Elden Ring no-hit)", or nothing at all when the run is unnamed. */
    private string Suffix()
    {
        string label = GetLabel();
        return label.Length > 0 ? " (" + label + ")" : "";
    }

    private bool MayEdit(string user)
    {
        if (!ModsOnly || !fromChat) return true;
        if (Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" })) return true;
        CPH.SendMessage(user + ", only mods can change the death count — !deaths shows it");
        return false;
    }

    private static int Clamp(int value)
    {
        if (value < 0) return 0;
        return value > MaxCount ? MaxCount : value;
    }

    /** Write the record, then show it. Every path that changes the count ends here. */
    private void Store(int count)
    {
        // Persisted (the `true`), so a Streamer.bot restart in the middle of a run doesn't hand
        // the challenge a clean slate it didn't earn.
        CPH.SetGlobalVar(CountVar, count, true);
        Show(Format(count, GetLabel()));
        Broadcast("set", count, GetLabel());
    }

    /**
     * "Deaths: 12", the line a viewer actually reads.
     *
     * An unnamed run substitutes an empty {label}, which leaves whatever was wrapped around it
     * behind — " ()" at the end of a format like "Deaths: {count} ({label})". Tidying that up
     * here means one format string covers both, instead of the file carrying two.
     */
    private string Format(int count, string label)
    {
        string text = ObsTextFormat.Replace("{count}", count.ToString()).Replace("{label}", label);
        if (label.Length == 0) text = text.Replace("()", "").Replace("[]", "").Replace("--", "").Replace("—", "");
        return text.Trim();
    }

    /** The OBS text source and the text file, whichever of them is switched on. */
    private void Show(string text)
    {
        if (UpdateObsText && ObsTextSource.Length > 0)
        {
            // SetInputSettings rather than CPH.ObsSetGdiText: it is addressed by source name
            // alone, so a counter shared across scenes needs no scene named here and cannot
            // quietly stop updating when you build a new scene around it. It also works for a
            // FreeType2 source, where the GDI+ wrapper does not.
            var settings = new Dictionary<string, object> { ["text"] = text };
            var request = new Dictionary<string, object>
            {
                ["inputName"] = ObsTextSource,
                ["inputSettings"] = settings
            };
            CPH.ObsSendRaw("SetInputSettings", JsonConvert.SerializeObject(request), ObsConnection);
        }

        if (TextFilePath.Length == 0) return;
        try
        {
            File.WriteAllText(TextFilePath, text);
        }
        catch (Exception e)
        {
            // A bad path is a one-time setup mistake, not something to tell chat about — it
            // would say it on every single death. The SB log is where it belongs.
            CPH.LogWarn("[death] could not write " + TextFilePath + ": " + e.Message);
        }
    }

    private void Broadcast(string op, int count, string label)
    {
        if (!ShowOnOverlay) return;
        var payload = new Dictionary<string, object>
        {
            ["cmd"] = "counter",
            ["key"] = CounterKey,
            ["op"] = op,
            ["value"] = count,
            ["label"] = label
        };
        CPH.WebsocketBroadcastJson(JsonConvert.SerializeObject(payload));
    }

    private void SetLabel(string label)
    {
        string clean = label.Replace("\r", " ").Replace("\n", " ").Trim();
        if (clean.Length > MaxLabelLength) clean = clean.Substring(0, MaxLabelLength).Trim();
        CPH.SetGlobalVar(LabelVar, clean, true);
    }

    // Explicit <T> required: this SB build only exposes GetGlobalVar<T>(string, bool), so the
    // one-arg form cannot infer T. Read as <object> because the count is stored boxed as an int
    // by Store() but may have been typed in by hand as a string in the globals tab.
    private int GetCount()
    {
        int n;
        int.TryParse(Convert.ToString(CPH.GetGlobalVar<object>(CountVar)), out n);
        return Clamp(n);
    }

    private string GetLabel()
    {
        return (Convert.ToString(CPH.GetGlobalVar<object>(LabelVar)) ?? "").Trim();
    }

    private bool TryTail(string raw, int skip, out int value)
    {
        return int.TryParse(raw.Substring(skip).Trim(), out value);
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
