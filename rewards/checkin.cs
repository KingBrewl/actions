using System;
using System.Collections.Generic;

// Channel point reward: "Check-in". Chat-only — the arrival number, the viewer's visit count
// and a tavern greeting. Any clip or alert on screen is Tangia's job: it sees the same
// redemption from Twitch on its own, so nothing here talks to it. See the README.
//
//   Ponytail is the 4th to check in tonight! Welcome back to the hearth - that's 12 visits all told.
//
// No streaks: Twitch shows watch streaks natively, and a streak that a forgotten click resets
// is a penalty, not a reward. The visit count only ever goes up.
//
// Two triggers on this one action:
//
//   Twitch -> Channel Reward -> Check-in    a check-in
//   Twitch -> Stream Online                 starts a new stream: arrivals back to 0
//
// If Streamer.bot wasn't running when the stream started, a check-in after NewStreamAfterHours
// of none starts the new stream instead, so a missed trigger costs nothing. A second Stream
// Online within NewStreamAfterHours of the first — going live again after a crash — is the
// same stream, so the arrival count carries on rather than starting over.
//
// "Once per person per stream" is the reward's own Max Per User Per Stream setting, which
// Twitch enforces — this action only double-checks it.
public class CPHInline
{
    private const int NewStreamAfterHours = 6;

    // Visit counts that get a line of their own instead of the usual one.
    private static readonly Dictionary<int, string> VisitMilestones = new Dictionary<int, string>
    {
        [10] = "{user} checks in for the 10th time! The barkeep has started pouring yours before you reach the bar. kingbre3Hello",
        [25] = "{user} checks in for the 25th time! There's a stool by the fire with your name carved in it now. kingbre3Crown",
        [50] = "{user} checks in for the 50th time! Fifty. The tavern would like to formally adopt you. kingbre3Crown",
        [100] = "{user} checks in for the 100th time! One hundred! Songs are being written. Some of them are even good. kingbre3Crown",
        [250] = "{user} checks in for the 250th time! You don't visit the tavern - you ARE the tavern. kingbre3Crown"
    };

    // The channel-wide state: which stream this is, how many have checked in during it, and
    // when the last check-in (or Stream Online) happened.
    private const string StreamVar = "checkin.stream";
    private const string ArrivalsVar = "checkin.arrivals";
    private const string LastAtVar = "checkin.lastAt";
    private const string StartedAtVar = "checkin.startedAt";

    // Per viewer, in Twitch user variables: the stream they last checked in on, and how many
    // times they've checked in ever.
    private const string UserStreamVar = "checkin.lastStream";
    private const string UserTotalVar = "checkin.total";

    public bool Execute()
    {
        try
        {
            return Run();
        }
        catch (Exception error)
        {
            CPH.LogWarn("[checkin] failed: " + error);
            CPH.SendMessage("The check-in hit an error - the Streamer.bot log has the details");
            return true;
        }
    }

    private bool Run()
    {
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        if (login.Length == 0)
        {
            // No viewer behind it: Stream Online, or a test with nobody attached.
            if (IsStreamOnline())
            {
                // Going live again after a crash fires Stream Online again. That's the same
                // night, and a new stream would restart the arrival count and let everyone
                // who already checked in be counted a second time.
                if (SinceStart(DateTime.UtcNow).TotalHours >= NewStreamAfterHours) StartStream();
                else CPH.LogInfo("[checkin] Stream Online within " + NewStreamAfterHours + "h of the last one - same stream");
            }
            else CPH.LogWarn("[checkin] ran with no user and not from Stream Online - nothing to do");
            return true;
        }

        CheckIn(Arg(new[] { "user", "userName" }, login), login);
        return true;
    }

    private void CheckIn(string user, string login)
    {
        DateTime now = DateTime.UtcNow;
        if (IsStale(now)) StartStream();

        int stream = Int(Global(StreamVar));
        int lastStream = Int(UserVar(login, UserStreamVar));

        // Twitch's per-user limit should make this impossible; a reward set up without it, or
        // a refunded-and-redeemed-again, lands here rather than counting someone twice.
        if (lastStream == stream)
        {
            CPH.SendMessage(user + ", you've already checked in tonight - the tavern remembers! kingbre3Hello");
            return;
        }

        int arrivals = Int(Global(ArrivalsVar)) + 1;
        int total = Int(UserVar(login, UserTotalVar)) + 1;

        CPH.SetGlobalVar(ArrivalsVar, arrivals.ToString(), true);
        CPH.SetGlobalVar(LastAtVar, now.Ticks.ToString(), true);
        CPH.SetTwitchUserVar(login, UserStreamVar, stream.ToString(), true);
        CPH.SetTwitchUserVar(login, UserTotalVar, total.ToString(), true);

        CPH.SendMessage(Line(user, arrivals, total));
    }

    private string Line(string user, int arrivals, int total)
    {
        if (VisitMilestones.ContainsKey(total))
            return VisitMilestones[total].Replace("{user}", user);

        string arrived = user + " is the " + Ordinal(arrivals) + " to check in tonight!";
        if (total == 1)
            return arrived + " A new face at the Brewl Tavern - welcome, traveller, pull up a stool! kingbre3Hello";
        return arrived + " Welcome back to the hearth - that's " + total + " visits all told. kingbre3Hello";
    }

    // A new stream: the stream number moves on, which is what "already checked in tonight" is
    // measured against, and arrivals start over.
    private void StartStream()
    {
        CPH.SetGlobalVar(StreamVar, (Int(Global(StreamVar)) + 1).ToString(), true);
        CPH.SetGlobalVar(ArrivalsVar, "0", true);
        CPH.SetGlobalVar(LastAtVar, DateTime.UtcNow.Ticks.ToString(), true);
        CPH.SetGlobalVar(StartedAtVar, DateTime.UtcNow.Ticks.ToString(), true);
    }

    // Time since the current stream started; "forever" when it never has.
    private TimeSpan SinceStart(DateTime now)
    {
        long ticks;
        if (!long.TryParse(Global(StartedAtVar), out ticks)) return TimeSpan.MaxValue;
        TimeSpan since = now - new DateTime(ticks, DateTimeKind.Utc);
        return since.TotalSeconds < 0 ? TimeSpan.MaxValue : since;
    }

    // True when nothing has happened for NewStreamAfterHours, or ever — the fallback for a
    // Stream Online that Streamer.bot wasn't around to see.
    private bool IsStale(DateTime now)
    {
        long ticks;
        if (!long.TryParse(Global(LastAtVar), out ticks)) return true;
        TimeSpan since = now - new DateTime(ticks, DateTimeKind.Utc);
        return since.TotalSeconds < 0 || since.TotalHours >= NewStreamAfterHours;
    }

    // Streamer.bot names the trigger that fired in a couple of arguments, and which ones are
    // filled has varied between versions — any of them mentioning "online" will do.
    private bool IsStreamOnline()
    {
        foreach (string key in new[] { "__source", "triggerName", "triggerCategory", "eventSource" })
        {
            string value = Arg(new[] { key }, "").ToLowerInvariant();
            if (value.Contains("online")) return true;
        }
        return false;
    }

    // 1st, 2nd, 3rd, 4th ... 11th, 12th, 13th ... 21st.
    private string Ordinal(int n)
    {
        int lastTwo = n % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return n + "th";
        switch (n % 10)
        {
            case 1: return n + "st";
            case 2: return n + "nd";
            case 3: return n + "rd";
            default: return n + "th";
        }
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    // Everything is stored as a string and read back with <string>: <object> runs the stored
    // value through a JSON parser that throws on anything that isn't valid JSON — see death.cs.
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
