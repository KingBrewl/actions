using System;
using System.Collections.Generic;

// Pays coppers for supporting the channel. One action, with all of these Twitch triggers:
//
//   Twitch -> Subscriptions -> Subscription        SubCoppers (Prime counts as tier 1)
//   Twitch -> Subscriptions -> Resubscription      SubCoppers
//   Twitch -> Subscriptions -> Gift Subscription   GiftCoppers per gift, to the gifter
//   Twitch -> Cheer                                1 per BitsPerCopper bits
//   Twitch -> Raid                                 RaidCoppers, to the raider
//   Twitch -> Follow                               FollowCoppers, once ever per viewer
//
// Don't add the Gift Bomb trigger: Twitch sends one Gift Subscription for every sub in a bomb
// as well, and those already pay. Anonymous gifts pay nobody.
//
// Tier 2 and 3 subs pay 2x and 5x, the same ratio as their price. The amounts are counted in
// check-ins (5 coppers each), so a sub is worth five streams of checking in — enough to be
// worth having, not enough to drown out the regulars who earned theirs a stream at a time.
//
// Bits that don't make a whole copper carry over: two cheers of 50 add up to one copper.
//
// Put this action in the "Coppers" queue with the others — see the README.
public class CPHInline
{
    private const int SubCoppers = 25;
    private const int GiftCoppers = 10;
    private const int BitsPerCopper = 100;
    private const int RaidCoppers = 25;
    private const int FollowCoppers = 5;

    // Post a line in chat for each payout. Twitch's own alerts already celebrate the event, so
    // this only says what it earned.
    private const bool Announce = true;

    // Per viewer: bits not yet turned into a copper, and whether a follow has paid.
    private const string BitsLeftVar = "coppers.bitsLeft";
    private const string FollowPaidVar = "coppers.followPaid";

    public bool Execute()
    {
        try
        {
            return Run();
        }
        catch (Exception error)
        {
            CPH.LogWarn("[coppers-events] failed: " + error);
            return true;
        }
    }

    private bool Run()
    {
        string user = Arg(new[] { "user", "userName" }, "");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();
        string kind = Kind();

        if (login.Length == 0 || kind.Length == 0)
        {
            CPH.LogWarn("[coppers-events] no user, or not an event this pays for (" + Arg(new[] { "__source", "triggerName" }, "?") + ")");
            return true;
        }

        switch (kind)
        {
            case "gift":
                if (Flag(new[] { "anonymous", "isAnonymous" })) return true;
                Pay(user, login, GiftCoppers * TierMultiplier(), "for the gift sub");
                return true;
            case "sub":
                Pay(user, login, SubCoppers * TierMultiplier(), "for the sub");
                return true;
            case "cheer":
                Cheer(user, login);
                return true;
            case "raid":
                Pay(user, login, RaidCoppers, "for the raid");
                return true;
            case "follow":
                if (UserVar(login, FollowPaidVar) == "1") return true;
                CPH.SetTwitchUserVar(login, FollowPaidVar, "1", true);
                Pay(user, login, FollowCoppers, "for the follow");
                return true;
        }
        return true;
    }

    private void Cheer(string user, string login)
    {
        int bits;
        int.TryParse(Arg(new[] { "bits" }, "0"), out bits);
        if (bits <= 0) return;

        int total = Int(UserVar(login, BitsLeftVar)) + bits;
        int coppers = total / BitsPerCopper;
        CPH.SetTwitchUserVar(login, BitsLeftVar, (total % BitsPerCopper).ToString(), true);

        if (coppers > 0) Pay(user, login, coppers, "for the bits");
    }

    private void Pay(string user, string login, int coppers, string reason)
    {
        if (coppers <= 0) return;
        int purse = Balance(login) + coppers;
        SetBalance(login, user, purse);
        if (Announce)
            CPH.SendMessage(user + " earns " + Coppers(coppers) + " " + reason + "! Purse: " + purse + " kingbre3Golden");
    }

    // Which event fired. Streamer.bot names it in a few arguments, and which ones are filled
    // has varied between versions (see checkin.cs), so all of them are searched. Order matters:
    // "giftsub" and "resub" both contain "sub", and a gift bomb is ignored on purpose.
    private string Kind()
    {
        string names = "";
        foreach (string key in new[] { "__source", "triggerName", "triggerCategory", "eventSource" })
            names += " " + Arg(new[] { key }, "").ToLowerInvariant().Replace(" ", "");

        if (names.Contains("bomb")) return "";
        if (names.Contains("gift")) return "gift";
        if (names.Contains("sub")) return "sub";
        if (names.Contains("cheer")) return "cheer";
        if (names.Contains("raid")) return "raid";
        if (names.Contains("follow")) return "follow";
        return "";
    }

    // Tier 1 (and Prime) 1x, tier 2 2x, tier 3 5x. Streamer.bot has passed the tier as
    // "tier 1", "1000" and "3" in different versions, so only its first digit is read.
    private int TierMultiplier()
    {
        string tier = Arg(new[] { "tier", "subTier" }, "");
        foreach (char c in tier)
        {
            if (c == '2') return 2;
            if (c == '3') return 5;
            if (c >= '0' && c <= '9') return 1;
        }
        return 1;
    }

    // ---- the coppers purse ----------------------------------------------------------------
    // The same block is in rewards/checkin.cs, commands/coppers.cs, gamble.cs and deathroll.cs.
    // All five read and write the same variables, so a change to one copy goes in all of them.

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
