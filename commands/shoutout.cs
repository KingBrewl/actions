using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!so" / "!shoutout" (mods, VIPs and the broadcaster). The same
// action also works on a Twitch -> Raid trigger: it sees the raid's `fromUser` argument, shouts
// the raider out automatically, and skips the mod check, since nobody typed anything.
//
//   !so ponytailgaming     !so @PonytailGaming     (case and @ don't matter)
//
// The channel is looked up through Streamer.bot's own credentials, so nothing here needs an
// API key, and the chat line uses the name and login Twitch hands back rather than what was typed.
public class CPHInline
{
    // Twitch's own /shoutout — it puts the channel in viewers' sidebars. Rate limited by Twitch
    // to one per 2 minutes (and 60s per target), so a failure here is routine and ignored.
    private const bool SendNativeShoutout = true;

    private const bool PostChatMessage = true;

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

        // %targetUser% is the display name, %targetUserName% the login — this action already
        // holds both, so the text is built here rather than left to SB's substitution.
        if (PostChatMessage)
        {
            CPH.SendMessage(
                "Draw your weapons and raise a toast for " + name +
                "! Go pledge your allegiance and check out their legend over at https://twitch.tv/" + handle +
                " kingbre3Crown");
        }

        // Last, and swallowed: Twitch rejects a second /shoutout inside its cooldown as a matter
        // of course. Sent with `handle` rather than what chat typed — that is the canonical login
        // Twitch just handed back.
        if (SendNativeShoutout) CallVoid(new[] { "TwitchSendShoutoutByLogin", "TwitchSendShoutout" }, handle);

        return true;
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

    // Streamer.bot's records are not consistent about properties versus fields between
    // versions, so both are checked.
    private string Prop(object source, string[] memberNames, string fallback)
    {
        if (source == null) return fallback;
        var type = source.GetType();
        foreach (var wanted in memberNames)
        {
            object value = null;
            var property = type.GetProperty(wanted, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null) { try { value = property.GetValue(source, null); } catch { } }
            else
            {
                var field = type.GetField(wanted, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null) { try { value = field.GetValue(source); } catch { } }
            }
            string text = value == null ? "" : Convert.ToString(value).Trim();
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
