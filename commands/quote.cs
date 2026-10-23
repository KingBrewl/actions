using System;
using System.Collections.Generic;

// Trigger: Twitch -> Command "!quote" (a Chat Message trigger works too). Chat-only — replies
// with CPH.SendMessage. Quotes live in persisted globals
// (quote_count plus quote_1..n), so they survive Streamer.bot restarts.
//
//   !quote              -> random quote      !quote add <text>  -> append (anyone)
//   !quote <n>          -> quote number n    !quote remove <n>  -> delete (mods only)
public class CPHInline
{
    private static readonly Random Rng = new Random();

    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");
        // A Command trigger puts the tail in rawInput; a plain Chat Message trigger hands over
        // the whole message, so strip the leading word off that instead.
        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!quote", StringComparison.OrdinalIgnoreCase))
            raw = raw.Substring(6).Trim();

        int n;
        if (raw.Length == 0)
        {
            int count = GetCount();
            if (count == 0) { CPH.SendMessage("No quotes yet - add one with \"!quote add <text>\""); return true; }
            Send(Rng.Next(1, count + 1));
            return true;
        }
        if (int.TryParse(raw, out n)) { Send(n); return true; }

        string lower = raw.ToLowerInvariant();
        if (lower == "add") { CPH.SendMessage(user + ", what's the quote? \"!quote add <text>\""); return true; }
        if (lower.StartsWith("add ")) { Add(raw.Substring(4).Trim(), user); return true; }
        if (lower.StartsWith("remove ")) { Remove(TailNumber(raw, 7), user); return true; }
        if (lower.StartsWith("delete ")) { Remove(TailNumber(raw, 7), user); return true; }

        CPH.SendMessage(user + ", usage: !quote | !quote <#> | !quote add <text> | !quote remove <#>");
        return true;
    }

    private void Send(int n)
    {
        int count = GetCount();
        if (count == 0) { CPH.SendMessage("No quotes yet - add one with \"!quote add <text>\""); return; }
        if (n < 1 || n > count) { CPH.SendMessage("There is no quote #" + n + " - we only have " + count); return; }
        CPH.SendMessage("Quote #" + n + ": \"" + Quote(n) + "\"");
    }

    private void Add(string text, string user)
    {
        if (text.Length == 0) { CPH.SendMessage(user + ", what's the quote? \"!quote add <text>\""); return; }
        if (text.Length > 250) { CPH.SendMessage("That quote's too long - keep it under 250 characters"); return; }
        int count = GetCount() + 1;
        CPH.SetGlobalVar("quote_" + count, text);
        CPH.SetGlobalVar("quote_count", count);
        CPH.SendMessage(user + " added quote #" + count);
    }

    private void Remove(int n, string user)
    {
        if (!Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" }))
        {
            CPH.SendMessage(user + ", only mods can remove quotes");
            return;
        }
        int count = GetCount();
        if (n < 1 || n > count) { CPH.SendMessage("There is no quote #" + n + " - we only have " + count); return; }
        for (int i = n; i < count; i++)
            CPH.SetGlobalVar("quote_" + i, Quote(i + 1));
        CPH.SetGlobalVar("quote_count", count - 1);
        CPH.SendMessage("Removed quote #" + n + " (" + (count - 1) + " left)");
    }

    private int TailNumber(string raw, int skip)
    {
        int n;
        int.TryParse(raw.Substring(skip).Trim(), out n);
        return n;
    }

    // Explicit <T> required: this SB build only exposes GetGlobalVar<T>(string, bool), so the
    // one-arg form cannot infer T. Quotes are written as strings by Add(); the count is read
    // as <object> because older test data may have stored it boxed as an int.
    private string Quote(int index)
    {
        return Convert.ToString(CPH.GetGlobalVar<string>("quote_" + index)) ?? "";
    }

    private int GetCount()
    {
        int n;
        int.TryParse(Convert.ToString(CPH.GetGlobalVar<object>("quote_count")), out n);
        return n;
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

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
