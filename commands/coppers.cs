using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command, with "!coppers", "!copper" and "!give" in the command box.
// Chat-only.
//
//   !coppers                     -> your coppers and your place on the leaderboard
//   !coppers @name               -> someone else's
//   !coppers top                 -> the richest in the tavern
//   !give @name 5                -> hand 5 of your coppers to someone (!coppers give works too)
//   !coppers add @name 5         -> mods: hand out coppers, e.g. to put right a refund
//   !coppers take @name 5        -> mods: take some back
//   !coppers help                -> all of the above, in chat
//
// Coppers are the tavern's currency, kept apart from Crowns (Twitch channel points) so nobody
// mixes the two up. Viewers earn them by checking in (rewards/checkin.cs) and by supporting the
// channel (alerts/coppers-events.cs), and spend them on !gamble and !deathroll. They stay
// scarce on purpose.
//
// The leaderboard covers every viewer who has ever held a copper: they're listed in the
// coppers.ledger global, and each balance lives in that viewer's own user variable.
//
// Put this action in the "Coppers" queue with the others — see the README.
public class CPHInline
{
    // How many !coppers top shows.
    private const int TopCount = 5;

    // The largest add or take a mod can do in one go, so a typo can't mint a fortune.
    private const int MaxAdjust = 1000;

    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        string[] words = Words();
        string sub = words.Length > 0 ? words[0].ToLowerInvariant() : "";

        // !give @name 5 is !coppers give @name 5 under its own name.
        if (Invoked() == "give")
        {
            var withSub = new string[words.Length + 1];
            withSub[0] = "give";
            Array.Copy(words, 0, withSub, 1, words.Length);
            words = withSub;
            sub = "give";
        }

        switch (sub)
        {
            case "":
                if (login.Length == 0) { CPH.SendMessage("!coppers needs a viewer - try it from chat"); return true; }
                CPH.SendMessage(Purse(user, login, true));
                return true;
            case "top":
                CPH.SendMessage(Top());
                return true;
            case "help":
                CPH.SendMessage("!coppers shows your purse, !coppers @name someone else's, !coppers top the richest, " +
                    "!give @name 5 shares yours. Earn them by checking in and supporting the stream; spend them on !gamble and !deathroll. kingbre3Golden");
                return true;
            case "give":
                Give(user, login, words);
                return true;
            case "add":
            case "take":
                Adjust(user, words, sub == "add");
                return true;
        }

        string name, targetLogin;
        if (!Lookup(words[0], out name, out targetLogin))
        {
            CPH.SendMessage(user + ", couldn't find that villager - try !coppers @name");
            return true;
        }
        CPH.SendMessage(Purse(name, targetLogin, targetLogin == login));
        return true;
    }

    private string Purse(string name, string login, bool own)
    {
        int balance = Balance(login);
        if (balance == 0)
            return own
                ? name + ", your purse is empty. Check in with Crowns each stream to earn coppers! kingbre3Howdy"
                : name + "'s purse is empty. kingbre3O7";

        // Ties share a place: your rank is one more than the number of people richer than you.
        var board = Board();
        int richer = 0;
        foreach (var entry in board) if (entry.Value > balance) richer++;

        return name + " has " + Coppers(balance) + ", #" + (richer + 1) + " of " + board.Count + " in the tavern. kingbre3Golden";
    }

    private string Top()
    {
        var board = Board();
        if (board.Count == 0) return "Nobody has any coppers yet. Check in with Crowns to be the first! kingbre3Howdy";

        var parts = new List<string>();
        for (int i = 0; i < board.Count && i < TopCount; i++)
            parts.Add((i + 1) + ". " + board[i].Key + " " + board[i].Value);
        return "kingbre3King Richest in the tavern: " + string.Join(" | ", parts.ToArray());
    }

    // Everyone on the ledger with coppers to their name, richest first, as display name and
    // balance. An empty purse stays on the ledger but off the board.
    private List<KeyValuePair<string, int>> Board()
    {
        var board = new List<KeyValuePair<string, int>>();
        foreach (string login in Global(LedgerVar).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int balance = Balance(login);
            if (balance <= 0) continue;
            string name = UserVar(login, NameVar);
            board.Add(new KeyValuePair<string, int>(name.Length > 0 ? name : login, balance));
        }
        board.Sort((a, b) => b.Value != a.Value
            ? b.Value.CompareTo(a.Value)
            : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
        return board;
    }

    // !give @name <n>: moves coppers from the asker's purse to someone else's. words[0] is
    // "give". Nothing is created or destroyed, so it can't inflate anything.
    private void Give(string user, string login, string[] words)
    {
        if (login.Length == 0) return;

        int amount;
        if (words.Length < 3 || !int.TryParse(words[2], out amount) || amount <= 0)
        {
            CPH.SendMessage(user + ", try !give @name 5");
            return;
        }

        string name, targetLogin;
        if (!Lookup(words[1], out name, out targetLogin))
        {
            CPH.SendMessage(user + ", couldn't find that villager - try !give @name 5");
            return;
        }
        if (targetLogin == login)
        {
            CPH.SendMessage(user + " moves their coppers from one pocket to the other. Very generous. kingbre3LOL");
            return;
        }

        int mine = Balance(login);
        if (amount > mine)
        {
            CPH.SendMessage(user + ", you only have " + Coppers(mine) + ". kingbre3O7");
            return;
        }

        int theirs = Balance(targetLogin) + amount;
        SetBalance(login, user, mine - amount);
        SetBalance(targetLogin, name, theirs);

        CPH.SendMessage(user + " gives " + Coppers(amount) + " to " + name + "! kingbre3Heart (" +
            user + ": " + (mine - amount) + ", " + name + ": " + theirs + ")");
    }

    // !coppers add|take @name <n>, mods only. Viewers get no reply — the command just isn't
    // theirs, the same as !death.
    private void Adjust(string user, string[] words, bool add)
    {
        if (!IsMod()) return;

        string verb = add ? "add" : "take";
        int amount;
        if (words.Length < 3 || !int.TryParse(words[2], out amount) || amount <= 0 || amount > MaxAdjust)
        {
            CPH.SendMessage(user + ", try !coppers " + verb + " @name 5 (1 to " + MaxAdjust + ")");
            return;
        }

        string name, login;
        if (!Lookup(words[1], out name, out login))
        {
            CPH.SendMessage(user + ", couldn't find that villager");
            return;
        }

        int before = Balance(login);
        int after = add ? before + amount : Math.Max(0, before - amount);
        SetBalance(login, name, after);

        CPH.SendMessage(add
            ? name + " receives " + Coppers(after - before) + " from the tavern purse. Purse: " + after + " kingbre3Golden"
            : name + " pays " + Coppers(before - after) + " to the tavern. Purse: " + after);
    }

    // ---- the coppers purse ----------------------------------------------------------------
    // The same block is in rewards/checkin.cs, alerts/coppers-events.cs, gamble.cs and
    // deathroll.cs. All five read and write the same
    // variables, so a change to one copy goes in all of them.

    // Per viewer: their coppers, and their display name for the leaderboard.
    private const string BalanceVar = "coppers.balance";
    private const string NameVar = "coppers.name";
    // Every login that has ever held coppers, space-separated: what !coppers top ranks.
    private const string LedgerVar = "coppers.ledger";

    private int Balance(string login)
    {
        return Int(UserVar(login, BalanceVar));
    }

    private bool InLedger(string login)
    {
        return (" " + Global(LedgerVar) + " ").Contains(" " + login + " ");
    }

    // Sets a balance, and puts the viewer on the ledger the first time they hold any.
    private void SetBalance(string login, string name, int coppers)
    {
        if (coppers < 0) coppers = 0;
        CPH.SetTwitchUserVar(login, BalanceVar, coppers.ToString(), true);
        if (name.Length > 0) CPH.SetTwitchUserVar(login, NameVar, name, true);
        if (!InLedger(login)) CPH.SetGlobalVar(LedgerVar, (Global(LedgerVar) + " " + login).Trim(), true);
    }

    private string Coppers(int n)
    {
        return n + (n == 1 ? " copper" : " coppers");
    }

    // ---- finding a viewer -----------------------------------------------------------------

    /**
     * A Twitch user from a word chat typed. False when Twitch doesn't know it.
     *
     * Streamer.bot's own targetUser is used when it is the same user, saving a lookup.
     * Otherwise the word has to look like a login before it is looked up, which is what keeps
     * links, spaces and anything else chat can type out of the reply.
     */
    private bool Lookup(string word, out string name, out string login)
    {
        name = "";
        login = "";
        word = word.TrimStart('@');
        if (!LooksLikeLogin(word)) return false;

        string argName = Arg(new[] { "targetUser", "targetUserName" }, "");
        string argLogin = Arg(new[] { "targetUserName", "targetUser" }, "");
        if (argLogin.Length > 0 && (argLogin.Equals(word, StringComparison.OrdinalIgnoreCase) || argName.Equals(word, StringComparison.OrdinalIgnoreCase)))
        {
            name = argName;
            login = argLogin.ToLowerInvariant();
            return true;
        }

        object record = null;
        try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
        catch (Exception error) { CPH.LogWarn("[coppers] user lookup threw: " + error.Message); }
        if (record == null) return false;

        name = Prop(record, new[] { "UserName", "DisplayName", "UserLogin", "Login" }, word);
        login = Prop(record, new[] { "UserLogin", "Login" }, word).ToLowerInvariant();
        return true;
    }

    // Twitch logins are 4-25 of [A-Za-z0-9_]; a few legacy accounts are shorter, hence 1.
    // Checked by hand because Regex lives in System.dll, which the inline compiler can't see.
    private bool LooksLikeLogin(string word)
    {
        if (word.Length < 1 || word.Length > 25) return false;
        foreach (char c in word)
            if (!(c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                return false;
        return true;
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    // The command that fired, e.g. "!give" -> "give". A Chat Message trigger has no command
    // argument, so the first word of the message stands in for it.
    private string Invoked()
    {
        string invoked = Arg(new[] { "command", "message" }, "").Trim().TrimStart('!');
        int space = invoked.IndexOf(' ');
        if (space >= 0) invoked = invoked.Substring(0, space);
        return invoked.ToLowerInvariant();
    }

    // The words after the command. A Chat Message trigger hands over the whole message, so a
    // leading !command is dropped.
    private string[] Words()
    {
        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        var words = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && words[0].StartsWith("!"))
        {
            var rest = new string[words.Length - 1];
            Array.Copy(words, 1, rest, 0, rest.Length);
            return rest;
        }
        return words;
    }

    // A Hot Key or a test trigger carries no user — that's you.
    private bool IsMod()
    {
        bool fromChat = args.ContainsKey("userName") || args.ContainsKey("user");
        return !fromChat || Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" });
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

    // Property names on TwitchUserInfoEx have moved between Streamer.bot versions - see
    // shoutout.cs - so they are read by name with fallbacks.
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

    // Everything is stored as a string and read back with <string> — see checkin.cs.
    private string Global(string key)
    {
        string value = CPH.GetGlobalVar<string>(key, true);
        return value == null ? "" : value.Trim();
    }

    private string UserVar(string login, string key)
    {
        string value = CPH.GetTwitchUserVar<string>(login, key, true);
        return value == null ? "" : value.Trim();
    }

    private int Int(string text)
    {
        int n;
        return int.TryParse(text, out n) && n > 0 ? n : 0;
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
