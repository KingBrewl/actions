using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!clip" (a Chat Message trigger works too). Open to everyone by
// default; flip ModsOnly if chat gets carried away.
//
//   !clip                 -> clips the last 30 seconds and drops the link in chat
//   !clip 15 / !clip 60   -> any length from 5 to 60 seconds
//   !clip <title>         -> names the clip, e.g. !clip that jump was clean
//   !clip 15 <title>      -> both
//   !clip last            -> the most recent clip's link again
//   !clip help            -> all of the above, in chat
//
// Length and title need a Streamer.bot with CreateClip(title, duration) — Twitch added both in
// December 2025. An older build still clips, at 30 seconds and untitled, and says so.
//
// Twitch clips the seconds *before* the command, so !clip after the moment is the right
// order. Two things have to be true for it to work at all: the stream must be live — there is
// no buffer to cut from otherwise — and Streamer.bot's Twitch account needs the clips:edit
// scope (Platforms -> Twitch -> re-authorise the broadcaster account if it does not). Neither
// is something this action can fix, which is why a failure says so in chat rather than
// silently doing nothing.
public class CPHInline
{
    private const bool ModsOnly = false;

    // A burst of !clip inside this window is one moment, not five. The second and later ones
    // get handed the link that was just made rather than cutting a near-identical clip; this
    // is the only spam control the command needs.
    private const int CooldownSeconds = 30;

    // Twitch's limits on a clip's length, and what a bare !clip asks for.
    private const int DefaultSeconds = 30;
    private const int MinSeconds = 5;
    private const int MaxSeconds = 60;

    // Off makes everything after the length ignored, as before titles existed. A title is the
    // one piece of viewer text this action passes on — to Twitch, where it shows on the clips
    // page, not to chat — and Twitch runs it through AutoMod and rejects what fails.
    private const bool AllowTitles = true;
    private const int MaxTitleLength = 100;

    // Persisted so the last clip survives a Streamer.bot restart mid-stream — that is the link
    // someone always asks for again five minutes later.
    private const string LastUrlGlobal = "clip.lastUrl";
    private const string LastAtGlobal = "clip.lastAt";
    private const string LastByGlobal = "clip.lastBy";

    // Set when a clip method ran and reported success but handed back no link — see ClipUrl.
    private bool clipMade;

    // Set when the method that made the clip couldn't take a length and title — an older
    // Streamer.bot — so chat can be told the clip is 30 seconds and untitled.
    private bool optionsIgnored;

    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");

        // A Command trigger puts only the tail in rawInput; a plain Chat Message trigger hands
        // over the whole line, so the command word comes off that instead.
        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!"))
        {
            int space = raw.IndexOf(' ');
            raw = space < 0 ? "" : raw.Substring(space + 1).Trim();
        }
        string lower = raw.ToLowerInvariant();

        if (lower == "help" || lower == "?") { Help(); return true; }
        if (lower == "last" || lower == "link") { Last(); return true; }

        if (ModsOnly && !Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" }))
        {
            CPH.SendMessage(user + ", only mods can use !clip");
            return true;
        }

        int seconds;
        string title;
        if (!ParseOptions(raw, out seconds, out title))
        {
            CPH.SendMessage(user + ", clips can be " + MinSeconds + " to " + MaxSeconds + " seconds long - e.g. !clip 15");
            return true;
        }

        // The cooldown re-serves whatever was just cut, whatever length was asked for: a burst
        // of !clip 15 and !clip 60 at the same moment is still one moment.
        string recent = RecentClip();
        if (recent.Length > 0)
        {
            string by = Global(LastByGlobal);
            CPH.SendMessage(user + ", " + (by.Length == 0 ? "that's already" : by + " already") + " clipped that - " + recent);
            return true;
        }

        object clip = CreateClip(title, seconds);

        // Twitch refuses the whole clip when AutoMod rejects its title. The moment matters more
        // than the name, so it's cut again untitled rather than lost.
        bool titleRefused = false;
        if (clip == null && !clipMade && title.Length > 0)
        {
            clip = CreateClip("", seconds);
            titleRefused = clip != null || clipMade;
            if (titleRefused) title = "";
        }

        string url = ClipUrl(clip);

        if (url.Length == 0)
        {
            // A method ran and said yes but gave us nothing to link to. The clip exists; only
            // the url is missing, and saying "no clip" about a clip sitting in the dashboard is
            // the more expensive lie.
            if (clipMade)
            {
                CPH.SendMessage(user + " clipped it! (couldn't read the link back - it's on twitch.tv/" + Broadcaster() + "/clips)");
                return true;
            }
            CPH.SendMessage("Couldn't make a clip - Twitch only allows it while the stream is live.");
            return true;
        }

        CPH.SetGlobalVar(LastUrlGlobal, url, true);
        CPH.SetGlobalVar(LastAtGlobal, DateTime.UtcNow.Ticks.ToString(), true);
        CPH.SetGlobalVar(LastByGlobal, user, true);

        bool askedForOptions = seconds != DefaultSeconds || title.Length > 0;
        string note = optionsIgnored && askedForOptions
            ? " (this Streamer.bot can't set a clip's length or title yet, so it's 30s - update Streamer.bot for those)"
            : titleRefused ? " (Twitch didn't accept that title, so it's untitled)" : "";
        CPH.SendMessage(user + " clipped it! " + url + note);

        return true;
    }

    /**
     * Reads "!clip 15 what a shot" as a length and a title.
     *
     * A first word that's a number (15, or 15s) is the length if it's in range. Out of range
     * on its own — "!clip 90" — is a mistake worth saying so about, and returns false. Out of
     * range with words after it — "!clip 3 kills in one shot" — was never a length, so the
     * whole thing is the title.
     */
    private bool ParseOptions(string raw, out int seconds, out string title)
    {
        seconds = DefaultSeconds;
        title = "";
        if (raw.Length == 0) return true;

        int space = raw.IndexOf(' ');
        string first = space < 0 ? raw : raw.Substring(0, space);
        string rest = space < 0 ? "" : raw.Substring(space + 1).Trim();

        int n;
        string digits = first.ToLowerInvariant().TrimEnd('s');
        if (digits.EndsWith("sec")) digits = digits.Substring(0, digits.Length - 3);
        if (int.TryParse(digits, out n))
        {
            if (n >= MinSeconds && n <= MaxSeconds) { seconds = n; title = rest; }
            else if (rest.Length == 0) return false;
            else title = raw;
        }
        else title = raw;

        if (!AllowTitles) title = "";
        title = title.Replace("\r", " ").Replace("\n", " ").Trim();
        if (title.Length > MaxTitleLength) title = title.Substring(0, MaxTitleLength).Trim();
        return true;
    }

    private void Help()
    {
        CPH.SendMessage("!clip (last 30s) | !clip 15 or !clip 60 (any length, " + MinSeconds + "-" + MaxSeconds + "s)"
            + (AllowTitles ? " | !clip <title> | !clip 15 <title>" : "")
            + " | !clip last (the latest link) - it clips what already happened, so clip right after the moment");
    }

    // The latest clip's link, whenever it was made — the cooldown doesn't apply to asking.
    private void Last()
    {
        string url = Global(LastUrlGlobal);
        if (url.Length == 0) { CPH.SendMessage("No clips yet - !clip makes one"); return; }
        string by = Global(LastByGlobal);
        CPH.SendMessage("Latest clip" + (by.Length > 0 ? " (by " + by + ")" : "") + ": " + url);
    }

    // The last clip's url, but only while it is still inside the cooldown. Empty means "go make
    // a new one" — including when the clock is unreadable, because failing open costs a
    // duplicate clip and failing closed costs the moment.
    private string RecentClip()
    {
        string url = Global(LastUrlGlobal);
        if (url.Length == 0) return "";

        long ticks;
        if (!long.TryParse(Global(LastAtGlobal), out ticks)) return "";

        TimeSpan since = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        // A negative age is a clock that moved backwards, not a clip from the future.
        if (since.TotalSeconds < 0) return "";
        return since.TotalSeconds < CooldownSeconds ? url : "";
    }

    /**
     * Cuts the clip, via whatever this Streamer.bot build calls the method.
     *
     * Nothing is spelled out, for the reason bad.cs spells nothing out: the Twitch
     * wrappers have been renamed across versions, and CPH is handed out as an interface, so a
     * method declared only there is not always reachable by exact name on the concrete type.
     * Unlike the getters elsewhere in this folder, this one is *meant* to be a mutator — which
     * is why the name has to contain "clip" as well, so the pattern cannot wander onto some
     * other create-shaped method.
     *
     * Shapes are tried richest first: CreateClip(title, duration) — current builds — then a
     * title alone, then the older no-argument and post-to-chat-flag forms. Only the first can
     * honour a length, and anything but the first two drops the title; optionsIgnored records
     * that, so chat can be told.
     */
    private object CreateClip(string title, int seconds)
    {
        // Null asks Streamer.bot for its default — the stream title — rather than an empty one.
        object titleArg = title.Length > 0 ? title : null;

        var candidates = new List<KeyValuePair<MethodInfo, object[]>>[4];
        for (int i = 0; i < candidates.Length; i++) candidates[i] = new List<KeyValuePair<MethodInfo, object[]>>();

        foreach (var method in Methods())
        {
            string name = method.Name.ToLowerInvariant();
            if (!name.Contains("clip")) continue;
            if (!name.Contains("create") && !name.Contains("make")) continue;

            var p = method.GetParameters();
            if (p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(int))
                candidates[0].Add(new KeyValuePair<MethodInfo, object[]>(method, new object[] { titleArg, seconds }));
            else if (p.Length == 1 && p[0].ParameterType == typeof(string))
                candidates[1].Add(new KeyValuePair<MethodInfo, object[]>(method, new object[] { titleArg }));
            else if (p.Length == 0)
                candidates[2].Add(new KeyValuePair<MethodInfo, object[]>(method, new object[0]));
            // Some builds take a "post to chat" flag. We write our own chat line, so it is
            // always false — otherwise the link lands in chat twice.
            else if (p.Length == 1 && p[0].ParameterType == typeof(bool))
                candidates[3].Add(new KeyValuePair<MethodInfo, object[]>(method, new object[] { false }));
        }

        for (int tier = 0; tier < candidates.Length; tier++)
        {
            foreach (var candidate in candidates[tier])
            {
                object result = Attempt(candidate.Key, candidate.Value);
                if (result == null && !clipMade) continue;

                // The first method that makes a clip is the last one called — trying another
                // after a success would cut the moment twice. A bare "true" with no record is
                // still a clip; ClipUrl reports it as one without a link.
                // A length is only honoured by tier 0; a title by tiers 0 and 1.
                optionsIgnored = tier > 0 && (seconds != DefaultSeconds || (tier > 1 && title.Length > 0));
                return result;
            }
        }

        CPH.LogWarn("[clip] no clip method returned anything on this build. Methods matching \"clip\": " + ClipMethods());
        return null;
    }

    // One clip method, invoked: the clip record, or null for "no clip here, try the next".
    // A build that returns a bare bool has made the clip without saying where it went, which
    // sets clipMade and returns null.
    private object Attempt(MethodInfo method, object[] arguments)
    {
        try
        {
            object result = method.Invoke(CPH, arguments);
            if (result is bool)
            {
                if ((bool)result) clipMade = true;
                return null;
            }
            return result;
        }
        catch (Exception error)
        {
            CPH.LogWarn("[clip] " + method.Name + " threw: " + (error.InnerException ?? error).Message);
            return null;
        }
    }

    /**
     * The public url out of the clip record.
     *
     * Twitch's create-clip response carries only an id and an edit url — the plain view url is
     * derived, not sent — so which of the three a build surfaces depends on how much of that it
     * chose to fill in. All three lead to the same place.
     */
    private string ClipUrl(object clip)
    {
        if (clip == null) return "";

        // A build that hands back the url as a plain string rather than a record.
        if (clip is string)
        {
            string flat = Text(clip);
            if (!flat.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
            clipMade = true;
            return flat;
        }

        clipMade = true;

        string url = Prop(clip, new[] { "Url", "ClipUrl" }, "");
        if (url.Length > 0) return url;

        // https://clips.twitch.tv/<slug>/edit -> https://clips.twitch.tv/<slug>. The edit url
        // works for the broadcaster and nobody else, so it is never posted as-is.
        string edit = Prop(clip, new[] { "EditUrl", "Edit_Url" }, "");
        if (edit.Length > 0)
        {
            int cut = edit.IndexOf("/edit", StringComparison.OrdinalIgnoreCase);
            return cut > 0 ? edit.Substring(0, cut) : edit;
        }

        string id = Prop(clip, new[] { "Id", "ClipId", "Slug" }, "");
        if (id.Length > 0) return "https://clips.twitch.tv/" + id;

        CPH.LogWarn("[clip] clip record carried no url. " + clip.GetType().Name + " -> " + PropertyNames(clip));
        return "";
    }

    // Only for the "it's on your clips page" fallback line, so a wrong guess is cosmetic.
    private string Broadcaster()
    {
        return Arg(new[] { "broadcastUserName", "broadcastUser", "channel" }, "yourchannel").ToLowerInvariant();
    }

    // Explicit <T> required: this SB build only exposes GetGlobalVar<T>(string, bool), so the
    // one-arg form cannot infer T.
    private string Global(string key)
    {
        string value = CPH.GetGlobalVar<string>(key, true);
        return value == null ? "" : value.Trim();
    }

    private string ClipMethods()
    {
        var found = new List<string>();
        foreach (var method in Methods())
        {
            if (!method.Name.ToLowerInvariant().Contains("clip")) continue;

            var types = new List<string>();
            foreach (var parameter in method.GetParameters()) types.Add(parameter.ParameterType.Name);
            string signature = method.Name + "(" + string.Join(", ", types.ToArray()) + ")";
            if (!found.Contains(signature)) found.Add(signature);
        }
        return found.Count == 0 ? "(none)" : string.Join(", ", found.ToArray());
    }

    // Streamer.bot hands CPH out as an interface. A method declared only on that interface is
    // not always reachable through the concrete type, so both are searched.
    private List<MethodInfo> Methods()
    {
        var type = CPH.GetType();
        var found = new List<MethodInfo>(type.GetMethods());
        foreach (var contract in type.GetInterfaces()) found.AddRange(contract.GetMethods());
        return found;
    }

    // Streamer.bot's records are not consistent about properties versus fields between
    // versions, and a walk that only knows about properties sees a field-based record as an
    // empty object.
    private List<KeyValuePair<string, object>> Members(object source)
    {
        var members = new List<KeyValuePair<string, object>>();
        if (source == null) return members;

        var type = source.GetType();
        foreach (var property in type.GetProperties())
        {
            object value = null;
            try { value = property.GetValue(source, null); }
            catch { }
            members.Add(new KeyValuePair<string, object>(property.Name, value));
        }
        foreach (var field in type.GetFields())
        {
            object value = null;
            try { value = field.GetValue(source); }
            catch { }
            members.Add(new KeyValuePair<string, object>(field.Name, value));
        }
        return members;
    }

    private string PropertyNames(object source)
    {
        var names = new List<string>();
        foreach (var member in Members(source))
            names.Add(member.Key + "=" + (member.Value == null ? "null" : Text(member.Value)));
        return names.Count == 0 ? "(none)" : string.Join(", ", names.ToArray());
    }

    private string Text(object value)
    {
        return value == null ? "" : Convert.ToString(value).Trim();
    }

    private string Prop(object source, string[] memberNames, string fallback)
    {
        var members = Members(source);
        foreach (var wanted in memberNames)
            foreach (var member in members)
            {
                if (!member.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
                string text = Text(member.Value);
                if (text.Length > 0) return text;
            }
        return fallback;
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
