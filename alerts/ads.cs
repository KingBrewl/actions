using System;

// Trigger: Twitch -> Upcoming Ad, with 1 minute ticked. Chat-only. Warns chat that an ad break
// is coming so nobody thinks the stream died.
//
// Twitch sends the break's length as %adLength% (in seconds). If a build doesn't supply it,
// the message leaves the length out.
public class CPHInline
{
    public bool Execute()
    {
        string length = Arg(new[] { "adLength", "length" }, "");
        string what = length.Length > 0 ? "a " + length + "s ad break" : "an ad break";

        CPH.SendMessage("⚠️ Hear ye! The bard takes the stage in one minute for " + what + " - refill your tankards! kingbre3Pizza");
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
