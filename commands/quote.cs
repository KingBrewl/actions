using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!quote" (a Chat Message trigger works too). Chat-only — replies
// with CPH.SendMessage. Quotes live in persisted globals
// (quote_count plus quote_1..n, and quote_game_1..n for the game being played when each was
// added), so they survive Streamer.bot restarts.
//
//   !quote              -> random quote      !quote add <text>  -> append (anyone)
//   !quote <n>          -> quote number n    !quote remove <n>  -> delete (mods only)
//
// A quote is shown as: #3: "text" — KingBrewl, Elden Ring. Quotes saved while Twitch couldn't
// say what was being played are shown without the game: #3: "text" — KingBrewl.
public class CPHInline
{
    private static readonly Random Rng = new Random();

    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");
        // A Command trigger puts the tail in rawInput; a plain Chat Message trigger hands over
        // the whole message, so strip the leading word off that instead.
        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!quote", StringComparison.OrdinalIgnoreCase))
            raw = raw.Substring(6).Trim();

        int n;
        if (raw.Length == 0)
        {
            int count = GetCount();
            if (count == 0) { CPH.SendMessage("No quotes yet - add one with \"!quote add <text>\""); return true; }
            Send(Rng.Next(1, count + 1));
            return true;
        }
        if (int.TryParse(raw, out n)) { Send(n); return true; }

        string lower = raw.ToLowerInvariant();
        if (lower == "add") { CPH.SendMessage(user + ", what's the quote? \"!quote add <text>\""); return true; }
        if (lower.StartsWith("add ")) { Add(raw.Substring(4).Trim(), user); return true; }
        if (lower.StartsWith("remove ")) { Remove(TailNumber(raw, 7), user); return true; }
        if (lower.StartsWith("delete ")) { Remove(TailNumber(raw, 7), user); return true; }

        CPH.SendMessage(user + ", usage: !quote | !quote <#> | !quote add <text> | !quote remove <#>");
        return true;
    }

    private void Send(int n)
    {
        int count = GetCount();
        if (count == 0) { CPH.SendMessage("No quotes yet - add one with \"!quote add <text>\""); return; }
        if (n < 1 || n > count) { CPH.SendMessage("There is no quote #" + n + " - we only have " + count); return; }
        string game = Game(n);
        string who = Arg(new[] { "broadcastUser", "broadcastUserName" }, "KingBrewl");
        CPH.SendMessage("#" + n + ": \"" + Quote(n) + "\" — " + who + (game.Length > 0 ? ", " + game : ""));
    }

    private void Add(string text, string user)
    {
        if (text.Length == 0) { CPH.SendMessage(user + ", what's the quote? \"!quote add <text>\""); return; }
        if (text.Length > 250) { CPH.SendMessage("That quote's too long - keep it under 250 characters"); return; }
        int count = GetCount() + 1;
        string game = CurrentGameOrEmpty();
        CPH.SetGlobalVar("quote_" + count, text);
        // Written even when empty, so a game left over from a removed quote never sticks to
        // the new one.
        CPH.SetGlobalVar("quote_game_" + count, game);
        CPH.SetGlobalVar("quote_count", count);
        CPH.SendMessage(user + " added quote #" + count + (game.Length > 0 ? " (" + game + ")" : ""));
    }

    private void Remove(int n, string user)
    {
        if (!Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" }))
        {
            CPH.SendMessage(user + ", only mods can remove quotes");
            return;
        }
        int count = GetCount();
        if (n < 1 || n > count) { CPH.SendMessage("There is no quote #" + n + " - we only have " + count); return; }
        for (int i = n; i < count; i++)
        {
            CPH.SetGlobalVar("quote_" + i, Quote(i + 1));
            CPH.SetGlobalVar("quote_game_" + i, Game(i + 1));
        }
        CPH.SetGlobalVar("quote_game_" + count, "");
        CPH.SetGlobalVar("quote_count", count - 1);
        CPH.SendMessage("Removed quote #" + n + " (" + (count - 1) + " left)");
    }

    private int TailNumber(string raw, int skip)
    {
        int n;
        int.TryParse(raw.Substring(skip).Trim(), out n);
        return n;
    }

    // Explicit <T> required: this SB build only exposes GetGlobalVar<T>(string, bool), so the
    // one-arg form cannot infer T. Quotes are written as strings by Add(); the count is read
    // as <object> because older test data may have stored it boxed as an int.
    private string Quote(int index)
    {
        return Convert.ToString(CPH.GetGlobalVar<string>("quote_" + index)) ?? "";
    }

    // "" for quotes added before games were recorded.
    private string Game(int index)
    {
        return (Convert.ToString(CPH.GetGlobalVar<string>("quote_game_" + index)) ?? "").Trim();
    }

    private int GetCount()
    {
        int n;
        int.TryParse(Convert.ToString(CPH.GetGlobalVar<object>("quote_count")), out n);
        return n;
    }

    // ---- the current game -----------------------------------------------------------------
    //
    // Found the same way game.cs finds it: a %gameName% argument if an earlier sub-action set
    // one, otherwise read live from Twitch through Streamer.bot's own credentials. A failed
    // lookup never stops the quote being saved; it's just saved without a game.

    private string CurrentGameOrEmpty()
    {
        try
        {
            string game = Arg(new[] { "gameName", "game" }, "").Trim();
            return game.Length > 0 ? game : CurrentGame();
        }
        catch (Exception error)
        {
            CPH.LogWarn("[quote] game lookup failed: " + error.Message);
            return "";
        }
    }

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

    // ---- Streamer.bot plumbing (as in game.cs) ---------------------------------------------

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
                CPH.LogWarn("[quote] " + method.Name + " threw: " + (error.InnerException ?? error).Message);
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
