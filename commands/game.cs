using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!game" (a Chat Message trigger works too). Chat-only.
//
//   !game   -> "KingBrewl is currently braving the dangerous realms of Elden Ring! ..."
//
// The category is read live from Twitch through Streamer.bot's own credentials, found by
// shape exactly the way bad.cs finds it — so a %gameName% argument from some earlier
// sub-action is used when there is one, and nothing needs to be in the action besides this.
public class CPHInline
{
    public bool Execute()
    {
        string game = Arg(new[] { "gameName", "game" }, "");
        if (game.Length == 0) game = CurrentGame();

        if (game.Length == 0)
            CPH.SendMessage("Even the tavern's mapmakers can't say which realm KingBrewl is braving right now. kingbre3PayRespects");
        else
            CPH.SendMessage("KingBrewl is currently braving the dangerous realms of " + game + "! Gear up and prepare for battle. kingbre3PayRespects");
        return true;
    }

    // The category the channel is set to right now, or "" if this build won't say. See
    // bad.cs: the getter and its property have both been renamed across Streamer.bot
    // versions, and nothing that looks like a mutator is ever invoked.
    private string CurrentGame()
    {
        string login = Arg(new[] { "broadcastUserName", "broadcastUser", "channel" }, "");
        if (login.Length == 0) return "";

        object info = Invoke("channelinfo", "login", login);
        if (info == null) info = Invoke("channelinfo", null, login);
        if (info == null) return "";

        return Category(info);
    }

    private string Category(object source)
    {
        foreach (var member in Members(source))
        {
            string name = member.Key.ToLowerInvariant();
            if (name != "gamename" && name != "game" && name != "categoryname" && name != "category") continue;
            if (member.Value == null) continue;

            // Some builds box the category as an object with a Name rather than flattening it.
            string text = member.Value is string
                ? Text(member.Value)
                : Prop(member.Value, new[] { "Name", "GameName", "DisplayName", "Title" }, "");
            if (text.Length > 0) return text;
        }
        return "";
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    private static readonly string[] Mutators = { "set", "update", "create", "add", "delete", "remove", "ban", "timeout", "clear", "send", "start", "stop", "run", "raid" };

    // `prefer` disambiguates the ByLogin/ById pair, which take the same shape and quietly
    // return nothing when handed the other one's argument. Pass null to try either.
    private object Invoke(string nameContains, string prefer, string argument)
    {
        var type = CPH.GetType();
        var methods = new List<MethodInfo>(type.GetMethods());
        foreach (var contract in type.GetInterfaces()) methods.AddRange(contract.GetMethods());

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;

            string name = method.Name.ToLowerInvariant();
            if (!name.Contains(nameContains)) continue;
            if (prefer != null && !name.Contains(prefer)) continue;

            bool mutates = false;
            foreach (string word in Mutators) if (name.Contains(word)) { mutates = true; break; }
            if (mutates) continue;

            try
            {
                object result = method.Invoke(CPH, new object[] { argument });
                if (result != null) return result;
            }
            catch (Exception error)
            {
                CPH.LogWarn("[game] " + method.Name + " threw: " + (error.InnerException ?? error).Message);
            }
        }
        return null;
    }

    // Streamer.bot's records are not consistent about properties versus fields between
    // versions, and a walk that only knows about properties sees a field-based record as empty.
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

    private string Prop(object source, string[] memberNames, string fallback)
    {
        var members = Members(source);
        foreach (string wanted in memberNames)
            foreach (var member in members)
            {
                if (!member.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
                string text = Text(member.Value);
                if (text.Length > 0) return text;
            }
        return fallback;
    }

    private string Text(object value)
    {
        return value == null ? "" : Convert.ToString(value).Trim();
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (string key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
