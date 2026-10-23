using System;
using System.Collections.Generic;

// Channel point reward: "First". Chat-only — greets whoever claimed it with an in-character
// line.
//
// "Only the first person" is not this action's job: the reward itself is capped at one
// redemption per stream (see the README), and Twitch enforces that server side, so there is
// no race to lose and nothing to store here.
public class CPHInline
{
    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");
        CPH.SendMessage(user + " is first through the tavern doors! The hearth is theirs to claim and the first round's on the house—raise a tankard, hero, the day's adventure begins! kingbre3Hello");
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
