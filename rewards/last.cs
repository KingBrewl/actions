using System;
using System.Collections.Generic;

// Channel point reward: "Last". The consolation prize for everyone who didn't get First —
// names tonight's First winner and reminds the redeemer that if you ain't first, you're last.
//
//   Ponytail stumbles in behind Mouse, who claimed the hearth ages ago. If you ain't first, you're last! kingbre3Hi
//
// Any sound or video on screen comes from an alert tool (Tangia, or Twitch's own alerts as
// Check-in uses), which sees the redemption from Twitch on its own, so nothing here talks to it.
//
// "Once per person per stream" is the reward's own Max Per User Per Stream setting, which
// Twitch enforces. Nothing is stored here — the First winner is read from what first.cs saved.
//
// Which First is tonight's: the one saved against Check-in's current stream number (see
// checkin.cs). Check-in only moves that number on at Stream Online, or on the first check-in
// after a quiet NewStreamAfterHours, so it can lag behind — a First claimed before anyone has
// checked in, on a night Stream Online was missed, gets the old number. To cover that, a First
// claimed in the last NewStreamAfterHours also counts, and one older than MaxStreamHours never
// does, whatever its number says.
public class CPHInline
{
    private const int NewStreamAfterHours = 6;   // keep in step with checkin.cs
    private const int MaxStreamHours = 24;

    // Someone who didn't get beaten to it: {user} is the redeemer, {first} tonight's First.
    private static readonly string[] Lines =
    {
        "{user} stumbles in behind {first}, who claimed the hearth ages ago. If you ain't first, you're last! kingbre3Hi",
        "{user} arrives to find {first}'s tankard already empty. If you ain't first, you're last!",
        "{first} took the best stool, the warmest spot and the first round. {user} gets the draughty corner by the door. If you ain't first, you're last!",
        "The bard has already written a song about {first}. {user} gets a footnote. If you ain't first, you're last!",
        "{user} bursts through the tavern doors to a standing ovation... for {first}, who got here first. If you ain't first, you're last!"
    };

    // Nobody has claimed First tonight. {user} is the redeemer.
    private static readonly string[] NoFirstLines =
    {
        "{user} redeemed Last when nobody's even claimed First yet. Bold strategy. The hearth is still up for grabs!",
        "{user} is last... out of nobody? The First reward is sitting right there, friend."
    };

    // The First winner redeeming Last as well. {user} is them.
    private static readonly string[] BothLines =
    {
        "{user} claimed First AND Last?! You can't be first and last, that's not how lines work. The barkeep is very confused.",
        "{user} has lapped the entire tavern. First through the door, last through the door. Make up your mind!",
        "{user}, you were FIRST. Why are you in the back of the queue? Get back to your hearth!",
        "{user} walked in first, walked out, and walked in again last. The bouncer has questions."
    };

    // Written by first.cs.
    private const string WinnerIdVar = "first.userId";
    private const string WinnerNameVar = "first.user";
    private const string WinnerStreamVar = "first.stream";
    private const string WinnerAtVar = "first.at";

    // Owned by checkin.cs; only read here.
    private const string CheckinStreamVar = "checkin.stream";

    // Static, because a fresh Random is seeded off the clock: two built inside the same tick
    // hand back the same number — see bad.cs.
    private static readonly Random Rng = new Random();

    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");
        string id = Arg(new[] { "userId" }, Arg(new[] { "userName", "user" }, "").ToLowerInvariant());

        try
        {
            CPH.SendMessage(Line(user, id));
        }
        catch (Exception error)
        {
            // A viewer who spent their points still gets a line.
            CPH.LogWarn("[last] failed: " + error);
            CPH.SendMessage(user + " is last! If you ain't first, you're last!");
        }
        return true;
    }

    private string Line(string user, string id)
    {
        string first = Global(WinnerNameVar);
        if (first.Length == 0 || !IsTonight())
            return Pick(NoFirstLines).Replace("{user}", user);

        string firstId = Global(WinnerIdVar);
        if (id.Length > 0 && string.Equals(id, firstId, StringComparison.OrdinalIgnoreCase))
            return Pick(BothLines).Replace("{user}", user);

        return Pick(Lines).Replace("{user}", user).Replace("{first}", first);
    }

    // Whether the saved First belongs to this stream. See the comment at the top.
    private bool IsTonight()
    {
        long ticks;
        if (!long.TryParse(Global(WinnerAtVar), out ticks)) return false;
        double hours = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalHours;
        if (hours < 0 || hours >= MaxStreamHours) return false;
        if (hours < NewStreamAfterHours) return true;

        string stream = Global(CheckinStreamVar);
        return stream.Length > 0 && stream == Global(WinnerStreamVar);
    }

    private string Pick(string[] lines)
    {
        return lines[Rng.Next(lines.Length)];
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    // Read back with <string>: <object> runs the stored value through a JSON parser that
    // throws on anything that isn't valid JSON — see death.cs.
    private string Global(string key)
    {
        string value = CPH.GetGlobalVar<string>(key, true);
        return value == null ? "" : value.Trim();
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
