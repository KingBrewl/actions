using System;
using System.Collections.Generic;

// Trigger: Twitch -> Command, with "!lurk" and "!unlurk" both in the command box — one action
// serves both, the way !death and !deaths share death.cs. Chat-only.
//
//   !lurk     -> into the shadows, and the time is noted
//   !unlurk   -> back to the hearth, with how long they were gone when it is known
//
// The time lives in a persisted Twitch user variable (lurk.since), so a Streamer.bot restart
// mid-stream doesn't lose it. A lurk older than StaleHours is from some earlier stream that
// nobody unlurked from, and is dropped rather than reported as "after 26 hours".
public class CPHInline
{
    private const string SinceVar = "lurk.since";

    private const int StaleHours = 12;

    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        string invoked = Arg(new[] { "command", "message" }, "").TrimStart('!');
        if (invoked.StartsWith("unlurk", StringComparison.OrdinalIgnoreCase))
            Unlurk(user, login);
        else
            Lurk(user, login);
        return true;
    }

    private void Lurk(string user, string login)
    {
        // Lurking again while already lurking keeps the original time, so a second !lurk
        // doesn't knock an hour off the eventual !unlurk.
        if (login.Length > 0 && Since(login) == null)
            CPH.SetTwitchUserVar(login, SinceVar, DateTime.UtcNow.Ticks.ToString(), true);

        CPH.SendMessage("Step into the shadows of the Tavern, " + user + "! Pull up a stool by the fire—your stealthy presence keeps the hearth burning brightly. kingbre3PayRespects");
    }

    private void Unlurk(string user, string login)
    {
        TimeSpan? away = login.Length == 0 ? null : Since(login);
        if (login.Length > 0) CPH.UnsetTwitchUserVar(login, SinceVar, true);

        string after = away == null ? "" : " after " + Duration(away.Value);
        CPH.SendMessage(user + " emerges from the tavern shadows" + after + " and steps back into the light! Welcome back to the hearth, hero—draw a blade and join the fray! kingbre3Hello");
    }

    // How long ago they lurked, or null when they didn't, the stored time is unreadable or
    // backwards, or it is stale.
    private TimeSpan? Since(string login)
    {
        long ticks;
        string stored = CPH.GetTwitchUserVar<string>(login, SinceVar, true);
        if (!long.TryParse(stored, out ticks)) return null;

        TimeSpan away = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        if (away.TotalSeconds < 0 || away.TotalHours >= StaleHours) return null;
        return away;
    }

    private string Duration(TimeSpan away)
    {
        int hours = (int)away.TotalHours;
        int minutes = away.Minutes;
        if (hours == 0 && minutes == 0) return "less than a minute";

        var parts = new List<string>();
        if (hours > 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
        if (minutes > 0) parts.Add(minutes + (minutes == 1 ? " minute" : " minutes"));
        return string.Join(" ", parts.ToArray());
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
