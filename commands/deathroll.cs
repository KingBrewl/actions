using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!deathroll" (add "!dr" too for short). Chat-only.
//
// The World of Warcraft gambling game. Two players take turns rolling: the first roll is
// 1-100, and every roll after that is from 1 to whatever the last roll was. Whoever rolls a 1
// dies, and the other player takes the pot.
//
//   !deathroll @name 5       -> challenge someone, 5 coppers each. Leave the number off to
//                               play for honour alone
//   !deathroll accept        -> the challenged player takes it on; both bets go in the pot
//   !deathroll decline       -> or doesn't
//   !deathroll cancel        -> the challenger takes it back before it's answered
//   !deathroll               -> on your turn, rolls. Otherwise says whose turn it is
//   !deathroll stop          -> mods: ends the game and hands both bets back
//   !deathroll help          -> the rules, in chat
//
// The challenger rolls first. One game runs at a time, channel-wide.
//
// Bets come out of both purses on accept and sit in the pot until someone dies, so nobody can
// spend their stake mid-game. The game is saved, so a Streamer.bot restart mid-game carries on
// where it left off.
//
// Nothing here runs on a timer, so the time limits are checked whenever someone next types
// !deathroll:
//   - A challenge nobody answers within AcceptSeconds lapses. Nothing was in the pot yet.
//   - A player who hasn't rolled within TurnSeconds can be claimed against: their opponent
//     types !deathroll and wins by forfeit. A new challenge from anyone does the same, so an
//     abandoned game never blocks the next.
//
// Put this action in the "Coppers" queue with the others — see the README.
public class CPHInline
{
    // The first roll is 1 to this.
    private const int StartNumber = 100;

    private const int AcceptSeconds = 60;
    private const int TurnSeconds = 120;

    // The biggest bet a challenge can carry. 0 means no limit beyond the players' purses.
    private const int MaxWager = 0;

    private static readonly Random Rng = new Random();

    // The game in progress, saved as one global so it's always read and written whole.
    private const string StateVar = "deathroll.game";

    private class Game
    {
        public string Phase = "";        // "", "challenge" or "playing"
        public string P1Login = "", P1Name = "", P2Login = "", P2Name = "";
        public int Wager;                // each player's stake; the pot is twice this
        public int Turn = 1;             // 1 or 2: whose roll it is
        public int Max = StartNumber;    // the current roll is 1 to this
        public long At;                  // when the challenge, accept or last roll happened

        public string TurnName { get { return Turn == 1 ? P1Name : P2Name; } }
        public string WaitingName { get { return Turn == 1 ? P2Name : P1Name; } }
        public string TurnLogin { get { return Turn == 1 ? P1Login : P2Login; } }
    }

    private string user;
    private string login;

    public bool Execute()
    {
        user = Arg(new[] { "user", "userName" }, "someone");
        login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();
        if (login.Length == 0)
        {
            CPH.SendMessage("!deathroll needs a viewer - try it from chat");
            return true;
        }

        string[] words = Words();
        string sub = words.Length > 0 ? words[0].ToLowerInvariant() : "";

        Game game = Load();
        if (game.Phase == "challenge" && Age(game) > AcceptSeconds)
        {
            Clear();
            game = new Game();
        }

        switch (sub)
        {
            case "help":
                CPH.SendMessage("Deathroll: !deathroll @name 5 to challenge. Take turns typing !deathroll - first roll is 1-" +
                    StartNumber + ", then 1 to the last roll. Roll a 1 and you die; the other takes the pot. kingbre3Pew");
                return true;
            case "accept":
                Accept(game);
                return true;
            case "decline":
                if (game.Phase == "challenge" && login == game.P2Login)
                {
                    Clear();
                    CPH.SendMessage(game.P2Name + " declines " + game.P1Name + "'s deathroll. Wise, or cowardly? kingbre3Lol");
                }
                return true;
            case "cancel":
                if (game.Phase == "challenge" && login == game.P1Login)
                {
                    Clear();
                    CPH.SendMessage(game.P1Name + " withdraws the deathroll challenge.");
                }
                return true;
            case "stop":
                Stop(game);
                return true;
            case "":
            case "roll":
                Roll(game);
                return true;
        }

        Challenge(game, words);
        return true;
    }

    // ---- the game -------------------------------------------------------------------------

    private void Challenge(Game game, string[] words)
    {
        if (game.Phase == "playing")
        {
            if (Age(game) <= TurnSeconds)
            {
                CPH.SendMessage(user + ", " + game.P1Name + " and " + game.P2Name + " are mid-deathroll. Wait your turn! kingbre3Pew");
                return;
            }
            Forfeit(game);
        }
        else if (game.Phase == "challenge")
        {
            CPH.SendMessage(user + ", " + game.P1Name + " has challenged " + game.P2Name + " - wait for an answer.");
            return;
        }

        string targetName, targetLogin;
        if (!Lookup(words[0], out targetName, out targetLogin))
        {
            CPH.SendMessage(user + ", couldn't find that villager - try !deathroll @name 5");
            return;
        }
        if (targetLogin == login)
        {
            CPH.SendMessage(user + ", you can't deathroll yourself. Well, you can, but you'd lose. kingbre3Lol");
            return;
        }

        int wager = 0;
        if (words.Length > 1 && (!int.TryParse(words[1], out wager) || wager < 0))
        {
            CPH.SendMessage(user + ", try !deathroll @name 5, or !deathroll @name to play for honour");
            return;
        }
        if (MaxWager > 0 && wager > MaxWager)
        {
            CPH.SendMessage(user + ", the tavern caps deathrolls at " + Coppers(MaxWager) + ".");
            return;
        }
        if (Balance(login) < wager)
        {
            CPH.SendMessage(user + ", you only have " + Coppers(Balance(login)) + ". kingbre3O");
            return;
        }
        if (Balance(targetLogin) < wager)
        {
            CPH.SendMessage(user + ", " + targetName + " only has " + Coppers(Balance(targetLogin)) + ".");
            return;
        }

        var challenge = new Game
        {
            Phase = "challenge",
            P1Login = login, P1Name = user,
            P2Login = targetLogin, P2Name = targetName,
            Wager = wager,
            At = DateTime.UtcNow.Ticks
        };
        Save(challenge);

        CPH.SendMessage("kingbre3Pew " + user + " challenges " + targetName + " to a deathroll " + Stakes(wager) +
            "! " + targetName + ": !deathroll accept or decline (" + AcceptSeconds + "s)");
    }

    private void Accept(Game game)
    {
        if (game.Phase != "challenge" || login != game.P2Login)
        {
            CPH.SendMessage(user + ", there's no deathroll waiting on you.");
            return;
        }

        // Either purse may have changed since the challenge.
        if (Balance(game.P1Login) < game.Wager || Balance(game.P2Login) < game.Wager)
        {
            Clear();
            CPH.SendMessage("Someone's purse came up short of " + Coppers(game.Wager) + " - the deathroll is off. kingbre3O");
            return;
        }

        SetBalance(game.P1Login, game.P1Name, Balance(game.P1Login) - game.Wager);
        SetBalance(game.P2Login, game.P2Name, Balance(game.P2Login) - game.Wager);

        game.Phase = "playing";
        game.Turn = 1;
        game.Max = StartNumber;
        game.At = DateTime.UtcNow.Ticks;
        Save(game);

        string pot = game.Wager > 0 ? Coppers(game.Wager * 2) + " in the pot" : "For honour alone";
        CPH.SendMessage(game.P2Name + " accepts! " + pot + ". " + game.P1Name + ", roll first: !deathroll (1-" + StartNumber + ") kingbre3Pew");
    }

    private void Roll(Game game)
    {
        bool playing = game.Phase == "playing";
        bool inGame = playing && (login == game.P1Login || login == game.P2Login);

        if (!inGame)
        {
            if (playing)
                CPH.SendMessage(game.P1Name + " vs " + game.P2Name + ": " + game.TurnName + " to roll 1-" + game.Max + ".");
            else if (game.Phase == "challenge" && login == game.P2Login)
                CPH.SendMessage(user + ", " + game.P1Name + " challenged you: !deathroll accept or decline");
            else
                CPH.SendMessage(user + ", challenge someone with !deathroll @name 5 - !deathroll help for the rules");
            return;
        }

        if (login != game.TurnLogin)
        {
            if (Age(game) > TurnSeconds) Forfeit(game);
            else CPH.SendMessage(user + ", it's " + game.TurnName + "'s roll.");
            return;
        }

        int max = game.Max;
        int roll = Rng.Next(1, max + 1);

        if (roll == 1)
        {
            string loser = game.TurnName;
            string winner = game.WaitingName;
            Win(game, game.Turn == 1 ? 2 : 1);
            CPH.SendMessage(loser + " rolls 1 (1-" + max + ") and DIES! kingbre3O " + winner + Prize(game.Wager) + " kingbre3King");
            return;
        }

        string rolled = game.TurnName;
        game.Max = roll;
        game.Turn = game.Turn == 1 ? 2 : 1;
        game.At = DateTime.UtcNow.Ticks;
        Save(game);

        CPH.SendMessage(rolled + " rolls " + roll + " (1-" + max + "). " + game.TurnName + ", your turn: !deathroll (1-" + roll + ")");
    }

    // The player whose turn it is took too long; the one waiting wins.
    private void Forfeit(Game game)
    {
        string idle = game.TurnName;
        string winner = game.WaitingName;
        Win(game, game.Turn == 1 ? 2 : 1);
        CPH.SendMessage(idle + " took too long to roll and forfeits! " + winner + Prize(game.Wager) + " kingbre3King");
    }

    private void Win(Game game, int winner)
    {
        if (game.Wager > 0)
        {
            string winnerLogin = winner == 1 ? game.P1Login : game.P2Login;
            string winnerName = winner == 1 ? game.P1Name : game.P2Name;
            SetBalance(winnerLogin, winnerName, Balance(winnerLogin) + game.Wager * 2);
        }
        Clear();
    }

    // Mods only: ends whatever is going on and hands back what's in the pot.
    private void Stop(Game game)
    {
        if (!IsMod()) return;
        if (game.Phase == "")
        {
            CPH.SendMessage(user + ", there's no deathroll on.");
            return;
        }

        if (game.Phase == "playing" && game.Wager > 0)
        {
            SetBalance(game.P1Login, game.P1Name, Balance(game.P1Login) + game.Wager);
            SetBalance(game.P2Login, game.P2Name, Balance(game.P2Login) + game.Wager);
        }
        Clear();
        CPH.SendMessage("The barkeep breaks up the deathroll between " + game.P1Name + " and " + game.P2Name + ". Bets returned.");
    }

    private string Stakes(int wager)
    {
        return wager > 0 ? "for " + Coppers(wager) + " each" : "for honour";
    }

    private string Prize(int wager)
    {
        return wager > 0 ? " takes the pot of " + Coppers(wager * 2) + "!" : " wins the deathroll!";
    }

    // ---- saving the game ------------------------------------------------------------------
    // One global, fields split by '|'. Twitch names can't contain '|', so it never collides.

    private Game Load()
    {
        var parts = Global(StateVar).Split('|');
        if (parts.Length != 9) return new Game();
        var game = new Game
        {
            Phase = parts[0],
            P1Login = parts[1], P1Name = parts[2],
            P2Login = parts[3], P2Name = parts[4]
        };
        int.TryParse(parts[5], out game.Wager);
        int.TryParse(parts[6], out game.Turn);
        int.TryParse(parts[7], out game.Max);
        long.TryParse(parts[8], out game.At);
        if (game.Phase != "challenge" && game.Phase != "playing") return new Game();
        if (game.Turn != 2) game.Turn = 1;
        if (game.Max < 2) game.Max = StartNumber;
        return game;
    }

    private void Save(Game game)
    {
        CPH.SetGlobalVar(StateVar, string.Join("|", new[]
        {
            game.Phase, game.P1Login, game.P1Name, game.P2Login, game.P2Name,
            game.Wager.ToString(), game.Turn.ToString(), game.Max.ToString(), game.At.ToString()
        }), true);
    }

    private void Clear()
    {
        CPH.SetGlobalVar(StateVar, "", true);
    }

    // Seconds since the game last moved. A clock that has gone backwards counts as just now,
    // so nobody is forfeited by it.
    private double Age(Game game)
    {
        double since = (DateTime.UtcNow - new DateTime(game.At, DateTimeKind.Utc)).TotalSeconds;
        return since < 0 ? 0 : since;
    }

    // ---- the coppers purse ----------------------------------------------------------------
    // The same block is in rewards/checkin.cs, coppers.cs and gamble.cs. All four read and
    // write the same variables, so a change to one copy goes in all of them.

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
    private bool Lookup(string word, out string name, out string targetLogin)
    {
        name = "";
        targetLogin = "";
        word = word.TrimStart('@');
        if (!LooksLikeLogin(word)) return false;

        string argName = Arg(new[] { "targetUser", "targetUserName" }, "");
        string argLogin = Arg(new[] { "targetUserName", "targetUser" }, "");
        if (argLogin.Length > 0 && (argLogin.Equals(word, StringComparison.OrdinalIgnoreCase) || argName.Equals(word, StringComparison.OrdinalIgnoreCase)))
        {
            name = argName;
            targetLogin = argLogin.ToLowerInvariant();
            return true;
        }

        object record = null;
        try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
        catch (Exception error) { CPH.LogWarn("[deathroll] user lookup threw: " + error.Message); }
        if (record == null) return false;

        name = Prop(record, new[] { "UserName", "DisplayName", "UserLogin", "Login" }, word);
        targetLogin = Prop(record, new[] { "UserLogin", "Login" }, word).ToLowerInvariant();
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
