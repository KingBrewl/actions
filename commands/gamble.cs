using System;
using System.Collections.Generic;

// Trigger: Twitch -> Command "!gamble". Chat-only.
//
//   !gamble 5      -> bets 5 coppers
//   !gamble half   -> bets half your purse, rounded up
//   !gamble all    -> bets the lot
//   !gamble        -> how it works, and what's in your purse
//
// A roll from 1 to 100 decides it:
//
//   1-52     lose the bet
//   53-97    win the bet back, plus the same again
//   98-100   jackpot: the bet back, plus twice as much again
//
// The house wins slightly more than it loses (about 1% of every copper bet over time), so
// gambling drains coppers rather than printing them. Checking in and supporting the channel
// stay the only ways to get rich. Move LoseUpTo or JackpotFrom to change that.
//
// Put this action in the "Coppers" queue with the others — see the README.
public class CPHInline
{
    // Rolls up to this lose. Above it, up to JackpotFrom - 1, win the bet.
    private const int LoseUpTo = 52;
    // Rolls from this up to 100 win twice the bet.
    private const int JackpotFrom = 98;

    // Per person: repeats inside it get no reply, which keeps one viewer from turning chat
    // into a slot machine. 0 switches it off.
    private const int CooldownSeconds = 15;

    private static readonly Random Rng = new Random();

    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();
        if (login.Length == 0)
        {
            CPH.SendMessage("!gamble needs a viewer - try it from chat");
            return true;
        }

        string[] words = Words();
        string word = words.Length > 0 ? words[0].ToLowerInvariant() : "";
        int purse = Balance(login);

        if (word.Length == 0 || word == "help")
        {
            CPH.SendMessage(user + ", !gamble <amount>, half or all. Roll " + (LoseUpTo + 1) + "+ to double it, " + JackpotFrom + "+ to triple it. Purse: " + purse + " kingbre3Golden");
            return true;
        }

        if (purse == 0)
        {
            CPH.SendMessage(user + ", your purse is empty. Check in with Crowns to earn coppers! kingbre3Howdy");
            return true;
        }

        int bet;
        if (word == "all") bet = purse;
        else if (word == "half") bet = (purse + 1) / 2;
        else if (!int.TryParse(word, out bet) || bet <= 0)
        {
            CPH.SendMessage(user + ", try !gamble 5, !gamble half or !gamble all");
            return true;
        }

        if (bet > purse)
        {
            CPH.SendMessage(user + ", you only have " + Coppers(purse) + ". kingbre3O7");
            return true;
        }

        if (OnCooldown(login)) return true;

        int roll = Rng.Next(1, 101);
        bool allIn = bet == purse;

        if (roll <= LoseUpTo)
        {
            purse -= bet;
            SetBalance(login, user, purse);
            CPH.SendMessage(allIn
                ? user + " went all in, rolled " + roll + " and lost the lot. A moment of silence, please. kingbre3O7"
                : user + " rolled " + roll + " and lost " + Coppers(bet) + ". Purse: " + purse + " kingbre3O7");
            return true;
        }

        bool jackpot = roll >= JackpotFrom;
        int won = jackpot ? bet * 2 : bet;
        purse += won;
        SetBalance(login, user, purse);
        CPH.SendMessage(jackpot
            ? user + " rolled " + roll + "! JACKPOT! +" + Coppers(won) + ". Purse: " + purse + " kingbre3Golden"
            : user + " rolled " + roll + " and won " + Coppers(won) + "! Purse: " + purse + (allIn ? " The bold are rewarded! kingbre3LetsGo" : " kingbre3LetsGo"));
        return true;
    }

    // Kept in a user variable that isn't saved, so a restart forgets it. Checked only once
    // the bet is good, so a typo doesn't cost anyone their turn. An unreadable or backwards
    // clock fails open.
    private bool OnCooldown(string login)
    {
        if (CooldownSeconds <= 0) return false;
        const string key = "gamble.lastAt";
        DateTime now = DateTime.UtcNow;

        long ticks;
        if (long.TryParse(CPH.GetTwitchUserVar<string>(login, key, false), out ticks))
        {
            double since = (now - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            if (since >= 0 && since < CooldownSeconds) return true;
        }
        CPH.SetTwitchUserVar(login, key, now.Ticks.ToString(), false);
        return false;
    }

    // ---- the coppers purse ----------------------------------------------------------------
    // The same block is in rewards/checkin.cs, alerts/coppers-events.cs, coppers.cs and
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
