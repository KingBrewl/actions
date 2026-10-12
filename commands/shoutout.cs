using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

// Trigger: Twitch -> Command "!so" / "!shoutout" (mods, VIPs and the broadcaster). The same
// action also works on a Twitch -> Raid trigger: it sees the raid's `fromUser` argument, shouts
// the raider out automatically, and skips the mod check, since nobody typed anything.
//
//   !so ponytailgaming     !so @PonytailGaming     (case and @ don't matter)
//
// Profile data comes from Twitch through Streamer.bot's own credentials, so nothing here
// needs an API key. The card degrades field by field: a channel with no bio, no category, or
// a default avatar still gets a shoutout, just a shorter one.
public class CPHInline
{
    // Twitch allows 300 characters of bio. The card clamps to three lines anyway, so this is
    // about not shipping a paragraph over the websocket.
    private const int BioLimit = 200;

    // Twitch's own /shoutout — it puts the channel in viewers' sidebars, which the overlay
    // card cannot do. Rate limited by Twitch to one per 2 minutes (and 60s per target), so a
    // failure here is routine and must not take the alert down with it.
    private const bool SendNativeShoutout = true;

    private const bool PostChatMessage = true;

    // Twitch's category art is 3:4. 285x380 is what the directory itself serves.
    private const string BoxArtSize = "-285x380.jpg";

    public bool Execute()
    {
        bool fromRaid = Has(new[] { "fromUser", "fromUserName", "fromUsername" });
        string login = fromRaid
            ? Arg(new[] { "fromUserName", "fromUsername", "fromUser" }, "")
            : FirstWord(Arg(new[] { "rawInput", "input0", "message" }, ""));

        if (!fromRaid && !Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator", "isVip", "vip" }))
        {
            CPH.SendMessage(Arg(new[] { "userName", "user" }, "you") + ", only mods and VIPs can use !so");
            return true;
        }

        login = login.TrimStart('@').Trim();
        if (login.Length == 0) { CPH.SendMessage("Usage: !so <streamer>"); return true; }
        if (login.StartsWith("!")) { CPH.SendMessage("Usage: !so <streamer>"); return true; }

        var user = CPH.TwitchGetExtendedUserInfoByLogin(login);
        if (user == null)
        {
            CPH.SendMessage("Couldn't find a Twitch channel called \"" + login + "\"");
            return true;
        }

        // Property names on TwitchUserInfoEx have moved between Streamer.bot versions, so read
        // them by name with fallbacks rather than betting the action on one spelling.
        string name = Prop(user, new[] { "UserName", "DisplayName", "UserLogin", "Login" }, login);
        string handle = Prop(user, new[] { "UserLogin", "Login", "UserName" }, login);
        string avatar = Prop(user, new[] { "ProfileImageUrl", "ProfileImage", "Avatar" }, "");
        string bio = Truncate(Prop(user, new[] { "Description", "ChannelDescription", "Bio" }, ""), BioLimit);
        string userId = Prop(user, new[] { "UserId", "Id" }, "");
        object channel = ChannelRecord(login, userId);
        string game = Category(channel);
        if (string.IsNullOrEmpty(game)) game = Category(user);
        string gameArt = BoxArt(channel, game);

        var payload = new Dictionary<string, object>
        {
            ["cmd"] = "alert",
            ["variant"] = "shoutout",
            ["id"] = "shoutout",
            ["title"] = name,
            ["body"] = bio,
            ["game"] = game,
            ["gameArt"] = gameArt,
            ["handle"] = handle,
            ["avatar"] = avatar,
            ["icon"] = "📣"
        };
        CPH.WebsocketBroadcastJson(JsonConvert.SerializeObject(payload));

        // %targetUser% is the display name, %targetUserName% the login — this action already
        // holds both, so the text is built here rather than left to SB's substitution.
        if (PostChatMessage)
        {
            CPH.SendMessage(
                "Draw your weapons and raise a toast for " + name +
                "! Go pledge your allegiance and check out their legend over at https://twitch.tv/" + handle +
                " kingbre3Crown");
        }

        // Last, and swallowed: the card is already on screen, and Twitch rejects a second
        // /shoutout inside its cooldown as a matter of course. Sent with `handle` rather than
        // what chat typed — that is the canonical login Twitch just handed back.
        if (SendNativeShoutout) CallVoid(new[] { "TwitchSendShoutoutByLogin", "TwitchSendShoutout" }, handle);

        return true;
    }

    // The category lives on a channel-info record rather than the user record, and Streamer.bot
    // has renamed both that getter and its property across builds. Spelling them out is what
    // made this come back empty, so nothing is spelled out any more: any single-string method
    // that looks like a channel-info getter is tried, then any property on the result that
    // looks like a category, then the user record as a last resort. If all of that finds
    // nothing it writes what it *did* see to the Streamer.bot log, so one test run names the
    // method and property this build actually has.
    private object ChannelRecord(string login, string userId)
    {
        object info = Invoke("channelinfo", "login", login);
        if (info == null && !string.IsNullOrEmpty(userId)) info = Invoke("channelinfo", "id", userId);
        if (info == null) info = Invoke("channelinfo", null, login);

        if (info == null)
            CPH.LogWarn("[shoutout] no channel-info getter on this build. Single-string methods matching \"channel\": " + MethodNames("channel"));

        return info;
    }

    /**
     * Twitch serves category art off a predictable path, so the game's id is the whole job —
     * no second API call and no key. The id is much preferred: the older name-keyed form does
     * not cover every category (compound names, and anything with a slash, miss) and a miss
     * does not 404 — it redirects to a grey "404 boxart" placeholder that loads as a perfectly
     * valid image. The overlay screens that out on its way in; verifying it here would mean an
     * HTTP call, and Streamer.bot's inline compiler references only mscorlib, so System.Net is
     * out of reach without asking every user to add an assembly reference.
     */
    private string BoxArt(object channel, string game)
    {
        string id = GameId(channel);
        if (string.IsNullOrEmpty(id)) id = GameIdByName(game);
        if (!string.IsNullOrEmpty(id)) return "https://static-cdn.jtvnw.net/ttv-boxart/" + id + BoxArtSize;

        if (string.IsNullOrEmpty(game)) return "";

        // Worth a warning rather than a note: without an id the name-keyed url is a coin flip
        // that modern categories lose. The property dump is what says which name to reach for.
        CPH.LogWarn("[shoutout] no category id found — falling back to the name-keyed box art, which only "
            + "covers older categories. Channel record: "
            + (channel == null ? "(none)" : channel.GetType().Name + " -> " + PropertyNames(channel))
            + " | game methods: " + GameMethods());

        return "https://static-cdn.jtvnw.net/ttv-boxart/" + Escape(game) + BoxArtSize;
    }

    // Twitch's category id, whatever this build calls it — pattern-matched for the same reason
    // the getters are. "0" counts as absent; an unset id is written that way as often as null.
    private string GameId(object source)
    {
        // A flat id, whatever this build calls it: GameId, CategoryId, game_id.
        foreach (var member in Members(source))
        {
            string name = member.Key.ToLowerInvariant();
            if (!name.Contains("id")) continue;
            if (!name.Contains("game") && !name.Contains("categor")) continue;

            string text = Text(member.Value);
            if (text.Length > 0 && text != "0") return text;
        }

        // A boxed one. If Game is an object rather than a string -- the same shape Category
        // already allows for -- the id sits a level down and no name up here contains "id".
        foreach (var member in Members(source))
        {
            string name = member.Key.ToLowerInvariant();
            if (!name.Contains("game") && !name.Contains("categor")) continue;
            if (IsFlat(member.Value)) continue;

            string id = Prop(member.Value, new[] { "Id", "GameId", "CategoryId" }, "");
            if (id.Length > 0 && id != "0") return id;
        }

        return "";
    }

    // Some builds keep the id off the channel record and expose a category lookup instead.
    // Getters only, and `Id` is unambiguous on a record that only describes one game.
    private string GameIdByName(string game)
    {
        if (string.IsNullOrEmpty(game)) return "";

        object record = Invoke("game", "get", game);
        if (record == null) record = Invoke("categor", "get", game);
        if (record == null) return "";

        string id = GameId(record);
        return string.IsNullOrEmpty(id) ? Prop(record, new[] { "Id" }, "") : id;
    }

    // Uri.EscapeDataString lives in System.dll, which the inline compiler does not reference,
    // so the percent-encoding is done by hand. Encoding the whole string up front rather than
    // character by character keeps multi-byte names (Pokémon) and surrogate pairs intact.
    private string Escape(string text)
    {
        var escaped = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            char c = (char)b;
            bool unreserved = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '~';
            if (unreserved) escaped.Append(c);
            else escaped.Append('%').Append(b.ToString("X2"));
        }
        return escaped.ToString();
    }

    // Anything named like a category. Some builds box it as an object with a Name rather than
    // flattening it to a string, so a non-string value gets one more hop.
    private string Category(object source)
    {
        foreach (var member in Members(source))
        {
            string name = member.Key.ToLowerInvariant();
            if (name != "gamename" && name != "game" && name != "categoryname" && name != "category") continue;
            if (member.Value == null) continue;

            string text = member.Value is string
                ? Text(member.Value)
                : Prop(member.Value, new[] { "Name", "GameName", "DisplayName", "Title" }, "");
            if (text.Length > 0) return text;
        }
        return "";
    }

    // Streamer.bot hands CPH out as an interface. A method declared only on that interface is
    // not always reachable through the concrete type — the likeliest reason a correctly-named
    // getter still came back null before — so both are searched.
    private List<MethodInfo> Methods()
    {
        var type = CPH.GetType();
        var found = new List<MethodInfo>(type.GetMethods());
        foreach (var contract in type.GetInterfaces()) found.AddRange(contract.GetMethods());
        return found;
    }

    // Matching methods by name pattern is one coincidence away from calling a setter — and a
    // single-string "game" method could just as easily be one that rewrites the category live
    // on stream. Nothing that looks like it changes state is ever invoked here.
    private static readonly string[] Mutators = { "set", "update", "create", "add", "delete", "remove", "ban", "timeout", "clear", "send", "start", "stop", "run", "raid" };

    private bool IsMutator(string loweredName)
    {
        foreach (var word in Mutators)
            if (loweredName.Contains(word)) return true;
        return false;
    }

    // `prefer` disambiguates the ByLogin/ById pair, which take the same shape and quietly
    // return nothing when handed the other one's argument. Pass null to try either.
    private object Invoke(string nameContains, string prefer, string argument)
    {
        foreach (var method in Methods())
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;

            string name = method.Name.ToLowerInvariant();
            if (!name.Contains(nameContains)) continue;
            if (prefer != null && !name.Contains(prefer)) continue;
            if (IsMutator(name)) continue;

            try
            {
                object result = method.Invoke(CPH, new object[] { argument });
                if (result != null) return result;
            }
            catch (Exception error)
            {
                CPH.LogWarn("[shoutout] " + method.Name + " threw: " + (error.InnerException ?? error).Message);
            }
        }
        return null;
    }

    // Fire-and-forget by exact name: the native /shoutout returns void, so a null result means
    // it ran, not that it missed — stop at the first method that exists either way.
    private void CallVoid(string[] methodNames, string argument)
    {
        foreach (var method in Methods())
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;
            if (Array.IndexOf(methodNames, method.Name) < 0) continue;

            try { method.Invoke(CPH, new object[] { argument }); }
            catch (Exception error)
            {
                CPH.LogWarn("[shoutout] " + method.Name + " threw: " + (error.InnerException ?? error).Message);
            }
            return;
        }
    }

    private string MethodNames(string nameContains)
    {
        var names = new List<string>();
        foreach (var method in Methods())
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;
            if (!method.Name.ToLowerInvariant().Contains(nameContains)) continue;
            if (!names.Contains(method.Name)) names.Add(method.Name);
        }
        return names.Count == 0 ? "(none)" : string.Join(", ", names.ToArray());
    }

    private string PropertyNames(object source)
    {
        var names = new List<string>();
        foreach (var member in Members(source))
        {
            string shown;
            if (member.Value == null) shown = "null";
            else if (IsFlat(member.Value)) shown = Text(member.Value);
            else
            {
                // One level down only: enough to reveal a boxed game, not enough to walk an
                // object graph into the log file.
                var inner = new List<string>();
                foreach (var nested in Members(member.Value))
                    inner.Add(nested.Key + "=" + (IsFlat(nested.Value) ? Text(nested.Value) : nested.Value.GetType().Name));
                shown = member.Value.GetType().Name + "{" + string.Join(", ", inner.ToArray()) + "}";
            }
            names.Add(member.Key + "=" + shown);
        }
        return names.Count == 0 ? "(none)" : string.Join(", ", names.ToArray());
    }

    // Every method that could plausibly answer "what id is this category", with its signature,
    // so a build that keeps the id somewhere else entirely still leaves a trail.
    private string GameMethods()
    {
        var found = new List<string>();
        foreach (var method in Methods())
        {
            string name = method.Name.ToLowerInvariant();
            if (!name.Contains("game") && !name.Contains("categor")) continue;

            var types = new List<string>();
            foreach (var parameter in method.GetParameters()) types.Add(parameter.ParameterType.Name);
            string signature = method.Name + "(" + string.Join(", ", types.ToArray()) + ")";
            if (!found.Contains(signature)) found.Add(signature);
        }
        return found.Count == 0 ? "(none)" : string.Join(", ", found.ToArray());
    }

    // Streamer.bot's records are not consistent about properties versus fields between
    // versions, and a reflection walk that only knows about properties sees a field-based
    // record as an empty object. Every lookup here goes through this, so both are covered.
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

    private bool IsFlat(object value)
    {
        return value == null || value is string || value.GetType().IsPrimitive;
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

    // A Command trigger hands over just the tail in rawInput, a Chat Message trigger the whole
    // line — either way the channel is the first word that isn't the command itself.
    private string FirstWord(string raw)
    {
        var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
            if (!part.StartsWith("!")) return part;
        return "";
    }

    private string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }

    private bool Has(string[] keys)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return true;
        return false;
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
