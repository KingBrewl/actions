using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

// Trigger: Twitch -> Command "!clip" (a Chat Message trigger works too). Open to everyone by
// default; flip ModsOnly if chat gets carried away.
//
//   !clip     -> clips the last ~30 seconds and drops the link in chat
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

    private const bool PostChatMessage = true;

    // The overlay card. Off is a perfectly reasonable setting — the chat link is the payload,
    // and the card is for the viewers who are watching rather than reading.
    private const bool ShowOverlayCard = true;
    private const int AlertMs = 8000;

    // Persisted so the last clip survives a Streamer.bot restart mid-stream — that is the link
    // someone always asks for again five minutes later.
    private const string LastUrlGlobal = "clip.lastUrl";
    private const string LastAtGlobal = "clip.lastAt";
    private const string LastByGlobal = "clip.lastBy";

    // Set when a clip method ran and reported success but handed back no link — see ClipUrl.
    private bool clipMade;

    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");

        if (ModsOnly && !Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" }))
        {
            CPH.SendMessage(user + ", only mods can use !clip");
            return true;
        }

        string recent = RecentClip();
        if (recent.Length > 0)
        {
            string by = Global(LastByGlobal);
            CPH.SendMessage(user + ", " + (by.Length == 0 ? "that's already" : by + " already") + " clipped that - " + recent);
            return true;
        }

        object clip = CreateClip();
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

        if (PostChatMessage) CPH.SendMessage(user + " clipped it! " + url);
        if (ShowOverlayCard) Broadcast(user, url);

        return true;
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
     * Nothing is spelled out, for the reason shoutout.cs spells nothing out: the Twitch
     * wrappers have been renamed across versions, and CPH is handed out as an interface, so a
     * method declared only there is not always reachable by exact name on the concrete type.
     * Unlike the getters elsewhere in this folder, this one is *meant* to be a mutator — which
     * is why the name has to contain "clip" as well, so the pattern cannot wander onto some
     * other create-shaped method.
     */
    private object CreateClip()
    {
        foreach (var method in Methods())
        {
            string name = method.Name.ToLowerInvariant();
            if (!name.Contains("clip")) continue;
            if (!name.Contains("create") && !name.Contains("make")) continue;

            var parameters = method.GetParameters();
            object[] arguments;
            if (parameters.Length == 0) arguments = new object[0];
            // Some builds take a "post to chat" flag. We write our own chat line, so it is
            // always false — otherwise the link lands in chat twice.
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool)) arguments = new object[] { false };
            else continue;

            try
            {
                object result = method.Invoke(CPH, arguments);
                if (result == null) continue;

                // A build that returns a bare bool has made the clip without saying where it
                // went. Note it and keep looking for a method that returns the record.
                if (result is bool)
                {
                    if ((bool)result) clipMade = true;
                    continue;
                }
                return result;
            }
            catch (Exception error)
            {
                CPH.LogWarn("[clip] " + method.Name + " threw: " + (error.InnerException ?? error).Message);
            }
        }

        if (!clipMade)
            CPH.LogWarn("[clip] no clip method returned anything on this build. Methods matching \"clip\": " + ClipMethods());

        return null;
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

    private void Broadcast(string user, string url)
    {
        var payload = new Dictionary<string, object>
        {
            ["cmd"] = "alert",
            ["id"] = "clip",
            ["title"] = user + " clipped it!",
            // The card is read at a glance from across a room, and the scheme is the one part
            // of a url that never carries any information.
            ["body"] = Strip(url),
            ["icon"] = "🎬",
            ["ms"] = AlertMs
        };
        CPH.WebsocketBroadcastJson(JsonConvert.SerializeObject(payload));
    }

    // Only for the "it's on your clips page" fallback line, so a wrong guess is cosmetic.
    private string Broadcaster()
    {
        return Arg(new[] { "broadcastUserName", "broadcastUser", "channel" }, "yourchannel").ToLowerInvariant();
    }

    private string Strip(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url.Substring(8);
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return url.Substring(7);
        return url;
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
