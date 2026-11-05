using System;
using System.Collections.Generic;

// Channel point reward: "First". Chat-only — greets whoever claimed it with an in-character
// line, and remembers them so the Last reward (last.cs) can name who beat everyone to it.
//
// "Only the first person" is not this action's job: the reward itself is capped at one
// redemption per stream (see the README), and Twitch enforces that server side, so there is
// no race to lose.
//
// The winner is saved against Check-in's stream number (checkin.cs), which is how Last tells
// tonight's First from last week's. Check-in doesn't have to be set up for this to work: the
// time of the claim is saved too, and Last falls back on that.
public class CPHInline
{
    // Read by last.cs — keep the two files in step.
    private const string WinnerIdVar = "first.userId";
    private const string WinnerNameVar = "first.user";
    private const string WinnerStreamVar = "first.stream";
    private const string WinnerAtVar = "first.at";

    // Owned by checkin.cs; only read here.
    private const string CheckinStreamVar = "checkin.stream";

    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");
        string id = Arg(new[] { "userId" }, Arg(new[] { "userName", "user" }, "").ToLowerInvariant());

        // Saved before the message, and never allowed to stop it: a viewer who spent their
        // points still gets their greeting if the saving goes wrong.
        try
        {
            string stream = CPH.GetGlobalVar<string>(CheckinStreamVar, true);
            CPH.SetGlobalVar(WinnerIdVar, id, true);
            CPH.SetGlobalVar(WinnerNameVar, user, true);
            CPH.SetGlobalVar(WinnerStreamVar, stream == null ? "" : stream.Trim(), true);
            CPH.SetGlobalVar(WinnerAtVar, DateTime.UtcNow.Ticks.ToString(), true);
        }
        catch (Exception error)
        {
            CPH.LogWarn("[first] couldn't save the winner: " + error);
        }

        CPH.SendMessage(user + " is first through the tavern doors! The hearth is theirs to claim and the first round's on the house—raise a tankard, hero, the day's adventure begins! kingbre3Hi");
        return true;
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
