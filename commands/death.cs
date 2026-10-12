using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

// Trigger: Twitch -> Command, with "!death" and "!deaths" both in the command box — one action
// serves both, and the plural is the read-only one so a viewer asking for the score can never
// add to it.
//
//   !death                   -> +1, "Death #12"                                 (mods, VIPs)
//   !death undo / -1         -> take one back, for a misfire                    (mods, VIPs)
//   !death 3                 -> +3, for a spiral nobody was counting through    (mods, VIPs)
//   !death set <n>           -> absolute, after counting off-stream             (mods, VIPs)
//   !death switch <name>     -> change to another counter, or start a new one   (mods, VIPs)
//   !death reset [name]      -> back to zero; with a name, that counter         (mods, VIPs)
//   !death rename <name>     -> rename the current counter, keeping its count   (mods, VIPs)
//   !death forget <name>     -> delete a counter that isn't the current one     (mods, VIPs)
//   !death hide              -> take the counter off screen; nothing is lost    (mods, VIPs)
//   !deaths                  -> "12 deaths so far (hc-minecraft-all-bosses)"     (anyone)
//   !deaths <name>           -> the count for another counter                    (anyone)
//   !deaths list             -> every counter and its count                      (anyone)
//   !death help [command]    -> what's available, or what one command does        (anyone)
//
// A bare argument is always a *delta* — `!death 3` adds three. `set` is the only absolute,
// which is what stops "!death 12" silently wiping eleven deaths when a mod meant to add some.
//
// Each counter has a name — "hc-minecraft-all-bosses", "hc-wow" — and its own count, kept in
// the persisted global death.saved.<name>, so switching games never loses a run. The current
// one is death.run. death.count / death.label always mirror the current counter, which is what
// bad.cs and the overlay read, so neither needs to know there is more than one.
//
// Everything on screen is a copy of the current count, written by Show() — by default an OBS
// Text source, addressed by name over Streamer.bot's OBS connection. OBS saves a text source's
// contents with the scene collection, so that copy survives an OBS restart too.
public class CPHInline
{
    // Only mods, VIPs and the broadcaster can change the count; anyone can ask for it. Off lets
    // chat add deaths, which is funny for about ninety seconds.
    private const bool ModsOnly = true;

    // VIPs count as mods for the death counter. Off makes it mods and the broadcaster only.
    private const bool VipsCanEdit = true;

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

    // {count} and {label} are substituted; {label} is the counter's name. An unnamed counter
    // leaves {label} empty, so anything wrapped around it — " ({label})" — is trimmed off.
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
    private const int MaxNameLength = 40;

    // Twitch cuts a chat message off at 500 characters; `!deaths list` stops short of that.
    private const int MaxListLength = 450;

    // The overlay keys its counters by name; this is the address the display is drawn under.
    private const string CounterKey = "deaths";

    // The current counter, mirrored for bad.cs and the overlay. Unset while hidden.
    private const string CountVar = "death.count";
    private const string LabelVar = "death.label";

    // Which counter is current, every counter's name, and each one's count.
    private const string RunVar = "death.run";
    private const string RunsVar = "death.runs";
    private const string SavedPrefix = "death.saved.";

    // The counter that has no name — what you get by just typing !death on a fresh install.
    // It is shown with no name in chat and on screen, like before counters had names.
    private const string Unnamed = "unnamed";

    // Whether this run came from chat at all. A Hot Key — the natural way to add a death while
    // you are playing — hands over no user and no mod flag, so the ModsOnly check has to know
    // the difference between "a viewer who isn't a mod" and "nobody, because you pressed a
    // button on your own machine". A channel point redeem still carries a user, and is still
    // screened.
    private bool fromChat;

    public bool Execute()
    {
        // What Streamer.bot actually handed over, for when a command seems to do nothing:
        // Logs tab, filter on [death].
        CPH.LogInfo("[death] command=\"" + Arg(new[] { "command" }, "") + "\" rawInput=\"" + Arg(new[] { "rawInput" }, "")
            + "\" message=\"" + Arg(new[] { "message" }, "") + "\" user=\"" + Arg(new[] { "userName", "user" }, "") + "\"");

        // A failure says so in chat rather than leaving the command looking dead.
        try
        {
            return Run();
        }
        catch (Exception error)
        {
            CPH.LogWarn("[death] failed: " + error);
            CPH.SendMessage("The death counter hit an error - the Streamer.bot log has the details");
            return true;
        }
    }

    private bool Run()
    {
        fromChat = args.ContainsKey("userName") || args.ContainsKey("user");
        string user = Arg(new[] { "userName", "user" }, "someone");

        Migrate();

        // A Command trigger puts only the tail in rawInput; a plain Chat Message trigger hands
        // over the whole line, so strip the leading word off that instead.
        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!death", StringComparison.OrdinalIgnoreCase))
        {
            int space = raw.IndexOf(' ');
            raw = space < 0 ? "" : raw.Substring(space + 1).Trim();
        }
        string lower = raw.ToLowerInvariant();

        // !deaths only ever reads, whatever follows it — "!deaths 3" from a mod is a question,
        // not three deaths.
        string invoked = Arg(new[] { "command", "message" }, "").TrimStart('!');
        if (lower == "help" || lower.StartsWith("help ")) { Help(lower.Substring(4).Trim()); return true; }

        if (invoked.StartsWith("deaths", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length == 0) Report(Active());
            else if (lower == "list" || lower == "all" || lower == "runs") List();
            else Report(Name(raw));
            return true;
        }

        if (raw.Length == 0) { Add(1, user); return true; }

        int delta;
        if (int.TryParse(raw, out delta)) { Add(delta, user); return true; }

        if (lower == "+") { Add(1, user); return true; }
        if (lower == "-" || lower == "undo" || lower == "oops") { Add(-1, user); return true; }
        if (lower == "count" || lower == "show" || lower == "?") { Report(Active()); return true; }
        if (lower == "list" || lower == "all" || lower == "runs") { List(); return true; }

        int n;
        if (lower.StartsWith("set ") && TryTail(raw, 4, out n)) { Set(n, user); return true; }
        if (lower == "reset" || lower == "new") { Reset("", user); return true; }
        if (lower.StartsWith("reset ")) { Reset(raw.Substring(6).Trim(), user); return true; }
        if (lower.StartsWith("new ")) { Reset(raw.Substring(4).Trim(), user); return true; }
        if (lower.StartsWith("switch ")) { Switch(raw.Substring(7).Trim(), user); return true; }
        if (lower.StartsWith("run ")) { Switch(raw.Substring(4).Trim(), user); return true; }
        if (lower.StartsWith("rename ")) { Rename(raw.Substring(7).Trim(), user); return true; }
        if (lower.StartsWith("challenge ")) { Rename(raw.Substring(10).Trim(), user); return true; }
        if (lower.StartsWith("forget ")) { Forget(raw.Substring(7).Trim(), user); return true; }
        if (lower.StartsWith("delete ")) { Forget(raw.Substring(7).Trim(), user); return true; }
        if (lower == "hide" || lower == "off" || lower == "stop") { Hide(user); return true; }

        CPH.SendMessage(user + ", I don't know that one - !death help lists everything");
        return true;
    }

    // ---- help -----------------------------------------------------------------------------

    // One line per subcommand, for `!death help <command>`. Every alias the parser accepts
    // points at the same entry, so `!death help oops` is answered too.
    private static readonly Dictionary<string, string> HelpTopics = new Dictionary<string, string>
    {
        ["add"] = "!death adds one death to the current counter. !death 3 adds three, for a spiral nobody was counting through.",
        ["undo"] = "!death undo (or -1, oops) takes one death back, for a misfire.",
        ["set"] = "!death set <n> sets the count exactly, e.g. after counting off-stream. A bare number like !death 12 adds 12 instead.",
        ["switch"] = "!death switch <name> changes to another counter, or starts a new one at 0. The one you leave keeps its count. e.g. !death switch hc-wow",
        ["reset"] = "!death reset puts the current counter back to 0. !death reset <name> zeroes (or starts) that counter and switches to it - the one you leave is saved.",
        ["rename"] = "!death rename <name> renames the current counter and keeps its count. Names are lowercase with dashes: \"HC WoW\" becomes hc-wow.",
        ["forget"] = "!death forget <name> deletes an old counter for good. It won't delete the current one - switch away first.",
        ["hide"] = "!death hide takes the counter off screen. Nothing is lost - the next !death brings it back where it was.",
        ["list"] = "!deaths list shows every counter and its count. !deaths <name> shows one counter without switching to it.",
        ["deaths"] = "!deaths shows the current count. Anyone can use it, and it never changes anything."
    };

    private static readonly Dictionary<string, string> HelpAliases = new Dictionary<string, string>
    {
        ["+"] = "add", ["death"] = "add",
        ["-"] = "undo", ["-1"] = "undo", ["oops"] = "undo",
        ["new"] = "reset", ["run"] = "switch", ["challenge"] = "rename", ["delete"] = "forget",
        ["off"] = "hide", ["stop"] = "hide", ["runs"] = "list", ["all"] = "list"
    };

    /**
     * `!death help` lists what the asker can actually use — the whole set for mods and VIPs,
     * just the reading side for everyone else, so a viewer isn't handed a list of commands
     * that would only tell them no. `!death help <command>` explains one, for anyone.
     */
    private void Help(string topic)
    {
        // "!death help !deaths" and "!death help deaths" are the same question.
        string key = topic.Split(' ')[0].TrimStart('!');
        if (key.Length > 0)
        {
            if (HelpAliases.ContainsKey(key)) key = HelpAliases[key];
            CPH.SendMessage(HelpTopics.ContainsKey(key)
                ? HelpTopics[key]
                : "No help for that one - try add, undo, set, switch, reset, rename, forget, hide or list");
            return;
        }

        if (CanEdit())
            CPH.SendMessage("💀 !death (+1) | !death 3 (+3) | !death undo | !death set <n> | !death switch <name> | !death reset [name] | !death rename <name> | !death forget <name> | !death hide | !deaths [name|list] — !death help <command> for more");
        else
            CPH.SendMessage("💀 !deaths shows the count | !deaths <name> shows another counter | !deaths list shows them all. Mods" + (VipsCanEdit ? " and VIPs" : "") + " keep score with !death — !death help <command> for more");
    }

    private void Add(int delta, string user)
    {
        if (!MayEdit(user)) return;
        if (delta == 0) { Report(Active()); return; }

        // You cannot have died a negative number of times. An undo one past the start is a
        // miscount being corrected, not something to refuse.
        int count = Clamp(CountOf(Active()) + delta);
        Store(count);

        if (delta < 0) CPH.SendMessage("Un-died. Back to " + count + Suffix(Active()));
        else if (delta > 1) CPH.SendMessage("💀 +" + delta + " — that's " + count + " now" + Suffix(Active()));
        else CPH.SendMessage("💀 Death #" + count + Suffix(Active()));
    }

    private void Set(int n, string user)
    {
        if (!MayEdit(user)) return;
        if (n < 0 || n > MaxCount) { CPH.SendMessage(user + ", pick a number between 0 and " + MaxCount); return; }
        Store(n);
        CPH.SendMessage("Death count set to " + n + Suffix(Active()));
    }

    /**
     * Change to another counter, starting it at 0 if it's new. The one being left keeps its
     * count where it was — this is how one game's run is put away and another's picked up.
     */
    private void Switch(string name, string user)
    {
        if (!MayEdit(user)) return;
        string id = Name(name);
        if (id.Length == 0) { CPH.SendMessage(user + ", which counter? !death switch <name>"); return; }

        string was = Active();
        if (id == was && Shown()) { Report(id); return; }

        bool isNew = !Runs().Contains(id);
        bool leaving = id != was && Runs().Contains(was);
        int left = CountOf(was);
        CPH.SetGlobalVar(RunVar, id, true);
        int count = CountOf(id);
        Store(count);

        string start = isNew
            ? "New death counter: " + Display(id) + ", starting at 0"
            : "Now counting deaths for " + Display(id) + " (at " + count + ")";
        CPH.SendMessage(leaving ? start + " — " + Display(was) + " saved at " + left : start);
    }

    // The start of a run as much as the end of one: zero is a number worth showing, so the
    // counter stays on screen at 0 rather than treating it as nothing to draw. With a name,
    // it's that counter that goes to zero, and it becomes the current one.
    private void Reset(string name, string user)
    {
        if (!MayEdit(user)) return;
        string id = name.Length == 0 ? Active() : Name(name);
        if (id.Length == 0) { CPH.SendMessage(user + ", that's not a usable name - letters, numbers and dashes"); return; }

        string previous = Active();
        bool isNew = name.Length > 0 && !Runs().Contains(id);
        bool leaving = id != previous && Runs().Contains(previous);
        int was = CountOf(id);

        CPH.SetGlobalVar(RunVar, id, true);
        Store(0);

        string line = isNew
            ? "New death counter: " + Display(id) + ", starting at 0"
            : "Deaths reset to 0" + Suffix(id) + " (was " + was + ")";
        if (leaving) line += " — " + Display(previous) + " saved at " + CountOf(previous);
        CPH.SendMessage(line);
    }

    // Renaming without resetting: the run carries on, it just gets called something else.
    // Refuses to land on a name another counter already has, which would silently merge two.
    private void Rename(string name, string user)
    {
        if (!MayEdit(user)) return;
        string id = name.Length == 0 ? Unnamed : Name(name);
        if (id.Length == 0) { CPH.SendMessage(user + ", that's not a usable name - letters, numbers and dashes"); return; }

        string was = Active();
        if (id == was) { Report(id); return; }
        if (Runs().Contains(id))
        {
            CPH.SendMessage(user + ", there's already a counter called " + Display(id) + " - !death switch " + id + " to go to it");
            return;
        }

        int count = CountOf(was);
        Drop(was);
        CPH.SetGlobalVar(RunVar, id, true);
        Store(count);
        CPH.SendMessage("Renamed " + Display(was) + " to " + Display(id) + " (at " + count + ")");
    }

    // Deleting is for old counters only. The current one has to be switched away from first,
    // so a typo in the name can never take out the run in progress.
    private void Forget(string name, string user)
    {
        if (!MayEdit(user)) return;
        string id = Name(name);
        if (!Runs().Contains(id)) { CPH.SendMessage(user + ", there's no death counter called " + (id.Length > 0 ? id : "that")); return; }
        if (id == Active()) { CPH.SendMessage(user + ", that's the current counter - !death switch to another one first"); return; }

        int count = CountOf(id);
        Drop(id);
        CPH.SendMessage("Forgot the " + Display(id) + " death counter (it was at " + count + ")");
    }

    // Between challenges there is nothing to count, and a stale "Deaths: 0" in the corner of a
    // just-chatting stream is worse than no counter at all. Only the screen and the mirror are
    // cleared — every counter keeps its count, and the next !death carries on from it.
    private void Hide(string user)
    {
        if (!MayEdit(user)) return;
        CPH.UnsetGlobalVar(CountVar, true);
        CPH.UnsetGlobalVar(LabelVar, true);
        // Empty text renders as nothing, which is the whole trick — no scene item to hide, no
        // visibility to put back, and the source can stay in every scene it already sits in.
        Show("");
        Broadcast("clear", 0, "");
        CPH.SendMessage("Death counter is off screen (" + Display(Active()) + " saved at " + CountOf(Active()) + "). !death or !death switch <name> brings it back.");
    }

    private void Report(string id)
    {
        if (id.Length == 0 || (id != Active() && !Runs().Contains(id)))
        {
            CPH.SendMessage("There's no death counter called " + (id.Length > 0 ? id : "that") + " - !deaths list shows them all");
            return;
        }
        int count = CountOf(id);
        if (count == 0) { CPH.SendMessage("No deaths yet" + Suffix(id) + ". Suspicious."); return; }
        CPH.SendMessage("💀 " + count + (count == 1 ? " death" : " deaths") + " so far" + Suffix(id));
    }

    private void List()
    {
        var runs = Runs();
        if (runs.Count == 0) { CPH.SendMessage("No death counters yet - !death starts one"); return; }

        string active = Active();
        var line = "💀 Death counters:";
        foreach (string id in runs)
        {
            string entry = " " + Display(id) + " " + CountOf(id) + (id == active ? " (current)" : "") + ",";
            if (line.Length + entry.Length > MaxListLength) { line += " …"; break; }
            line += entry;
        }
        CPH.SendMessage(line.TrimEnd(','));
    }

    /** " (hc-minecraft-all-bosses)", or nothing at all for the unnamed counter. */
    private string Suffix(string id)
    {
        string label = Label(id);
        return label.Length > 0 ? " (" + label + ")" : "";
    }

    private bool MayEdit(string user)
    {
        if (CanEdit()) return true;
        CPH.SendMessage(user + ", only mods" + (VipsCanEdit ? " and VIPs" : "") + " can change the death count — !deaths shows it");
        return false;
    }

    private bool CanEdit()
    {
        if (!ModsOnly || !fromChat) return true;
        if (Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" })) return true;
        return VipsCanEdit && Flag(new[] { "isVip", "vip" });
    }

    private static int Clamp(int value)
    {
        if (value < 0) return 0;
        return value > MaxCount ? MaxCount : value;
    }

    /** Write the record for the current counter, mirror it, then show it. Every path that changes a count ends here. */
    private void Store(int count)
    {
        string id = Active();
        // Persisted (the `true`), so a Streamer.bot restart in the middle of a run doesn't hand
        // the challenge a clean slate it didn't earn.
        CPH.SetGlobalVar(SavedPrefix + id, count, true);
        Remember(id);

        CPH.SetGlobalVar(CountVar, count, true);
        CPH.SetGlobalVar(LabelVar, Label(id), true);
        Show(Format(count, Label(id)));
        Broadcast("set", count, Label(id));
    }

    /**
     * One-time carry-over from before counters had names: a death.count with no death.run is
     * the run that was in progress, and it becomes a counter named after its old label (or the
     * unnamed one), with its count intact.
     */
    private void Migrate()
    {
        if (Str(RunVar).Length > 0 || Runs().Count > 0) return;
        if (Str(CountVar).Length == 0 && Str(LabelVar).Length == 0) return;

        string id = Name(Str(LabelVar));
        if (id.Length == 0) id = Unnamed;

        int count;
        int.TryParse(Str(CountVar), out count);
        CPH.SetGlobalVar(RunVar, id, true);
        CPH.SetGlobalVar(SavedPrefix + id, Clamp(count), true);
        Remember(id);
        CPH.SetGlobalVar(LabelVar, Label(id), true);
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

    // ---- counters -------------------------------------------------------------------------

    private string Active()
    {
        string id = Str(RunVar);
        return id.Length > 0 ? id : Unnamed;
    }

    // The mirror is unset only by Hide, so its presence is what "on screen" means.
    private bool Shown()
    {
        return Str(CountVar).Length > 0;
    }

    private int CountOf(string id)
    {
        int n;
        int.TryParse(Str(SavedPrefix + id), out n);
        return Clamp(n);
    }

    private List<string> Runs()
    {
        var runs = new List<string>();
        foreach (string part in Str(RunsVar).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            if (part.Trim().Length > 0 && !runs.Contains(part.Trim())) runs.Add(part.Trim());
        return runs;
    }

    private void Remember(string id)
    {
        var runs = Runs();
        if (runs.Contains(id)) return;
        runs.Add(id);
        CPH.SetGlobalVar(RunsVar, string.Join(",", runs.ToArray()), true);
    }

    private void Drop(string id)
    {
        var runs = Runs();
        runs.Remove(id);
        CPH.SetGlobalVar(RunsVar, string.Join(",", runs.ToArray()), true);
        CPH.UnsetGlobalVar(SavedPrefix + id, true);
    }

    // The name as chat and the screen see it; the unnamed counter has none.
    private string Label(string id)
    {
        return id == Unnamed ? "" : id;
    }

    // The name for messages that need to say *something*, unnamed or not.
    private string Display(string id)
    {
        return id == Unnamed ? "the unnamed counter" : id;
    }

    /**
     * "HC Minecraft: All Bosses" -> "hc-minecraft-all-bosses". Counter names are the key into
     * the globals and are typed in chat, so they are lowercase, spaces become dashes, and
     * anything but letters, digits, dashes and underscores is dropped — which also keeps the
     * comma that separates death.runs out of them. "" when nothing usable is left.
     */
    private string Name(string raw)
    {
        var name = new System.Text.StringBuilder();
        foreach (char c in raw.Trim().ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') name.Append(c);
            else if ((c == '-' || c == ' ' || c == '\t') && name.Length > 0 && name[name.Length - 1] != '-') name.Append('-');
            if (name.Length >= MaxNameLength) break;
        }
        return name.ToString().Trim('-');
    }

    // Explicit `true`: this SB build only exposes GetGlobalVar<T>(string, bool). Read as
    // <string> first: <object> runs the stored value through a JSON parser, which is fine for
    // a count (47 is valid JSON) and throws on a bare name (unnamed is not). <object> is the
    // fallback for a build that won't hand an int back as a string.
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
