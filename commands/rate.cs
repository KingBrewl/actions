using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command, with "!pp" and "!rizz" in the command box — one action serves
// both, the way links.cs serves every link. Chat-only.
//
//   !pp / !rizz               -> rates the asker
//   !pp @name / !rizz @name   -> rates someone else
//
// Each result is fixed for the day. The number comes from the viewer's login, the command and
// the date rather than a fresh roll, so asking again — or someone else asking about you — gives
// the same answer until the day turns over at DayStartsAtHour. A measurement that changed every
// time you asked wouldn't be one, and spamming the command can't fish for a better number.
//
// A name goes through the same check as hug.cs and pillory.cs: only a user Twitch knows is
// posted, spelled as Twitch spells it. Everything else in the reply comes from the lists below.
public class CPHInline
{
    /* -----------------------------------------------------------------------------------
     * The ratings. Each command picks a number from Min to Max, then the first line whose
     * UpTo is at or above it. {target} is who's being rated and {n} the number. Adding a
     * command is a new entry here plus the name in the trigger's command box.
     *
     * Lean tilts the odds: 1 keeps every number equally likely, above 1 leans toward Max
     * (1.25 is gentle - the average moves from the middle to about 56% of the way up).
     * ----------------------------------------------------------------------------------- */
    private class Rating
    {
        public int Min, Max;
        public double Lean = 1;
        public int[] UpTo;
        public string[] Lines;
    }

    private static readonly Dictionary<string, Rating> Ratings = new Dictionary<string, Rating>
    {
        ["pp"] = new Rating
        {
            Min = 1,
            Max = 12,
            UpTo = new[] { 1, 4, 7, 11, 12 },
            Lines = new[]
            {
                "{target}'s pp is 1 inch. The royal ruler is being checked for faults. kingbre3LOL",
                "{target}'s pp is {n} inches. The tavern offers its condolences. kingbre3LOL",
                "{target}'s pp is {n} inches. Respectable. The barkeep nods. kingbre3Howdy",
                "{target}'s pp is {n} inches. The bards will sing of it. kingbre3King",
                "{target}'s pp is 12 inches. By royal decree, it is legendary. kingbre3Golden"
            }
        },
        ["rizz"] = new Rating
        {
            Min = 0,
            Max = 100,
            Lean = 1.25,
            UpTo = new[] { 0, 25, 50, 75, 99, 100 },
            Lines = new[]
            {
                "{target} has 0% rizz. Even the goose won't look at them. kingbre3LOL",
                "{target} has {n}% rizz. The bard is playing a sad song for them. kingbre3LOL",
                "{target} has {n}% rizz. Enough for a second drink, not a second look. kingbre3Howdy",
                "{target} has {n}% rizz. The whole table laughs at their jokes. kingbre3Cool",
                "{target} has {n}% rizz. Hearts flutter across the tavern. kingbre3Heart",
                "{target} has 100% rizz. Unspoken. The whole tavern blushes. kingbre3Golden"
            }
        }
    };

    // The day turns over at this hour, local time, rather than at midnight, so a late stream
    // doesn't re-measure everyone halfway through.
    private const int DayStartsAtHour = 6;

    // Per person, per command: repeats inside it are ignored, silently. The answer wouldn't
    // change anyway — this only keeps one viewer from filling chat with it. 0 switches it off.
    private const int CooldownSeconds = 30;

    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        string command = Invoked();
        Rating rating;
        if (!Ratings.TryGetValue(command, out rating))
        {
            CPH.LogWarn("[rate] no rating for \"" + command + "\" - add it to Ratings");
            return true;
        }

        string targetName, targetLogin;
        if (!Target(user, login, out targetName, out targetLogin))
        {
            CPH.SendMessage(user + ", couldn't find that villager - try !" + command + " @name");
            return true;
        }

        if (OnCooldown(login, command)) return true;

        int n = Score(targetLogin, command, rating);
        CPH.SendMessage(Line(rating, n).Replace("{target}", targetName).Replace("{n}", n.ToString()));
        return true;
    }

    private string Line(Rating rating, int n)
    {
        for (int i = 0; i < rating.UpTo.Length; i++)
            if (n <= rating.UpTo[i]) return rating.Lines[i];
        return rating.Lines[rating.Lines.Length - 1];
    }

    // The day's number for this login and command: the same inputs always give the same
    // number. string.GetHashCode isn't promised to stay the same between runs, so this is
    // FNV-1a with a final mix to spread it across the low bits the modulo reads.
    //
    // The hash depends on the multiplications wrapping around. Streamer.bot compiles with
    // overflow checking on, which throws instead, hence the explicit unchecked block.
    private int Score(string login, string command, Rating rating)
    {
        DateTime day = DateTime.Now.AddHours(-DayStartsAtHour);
        string key = login + "|" + command + "|" + day.Year + "-" + day.Month + "-" + day.Day;

        uint hash = 2166136261;
        unchecked
        {
            foreach (char c in key)
            {
                hash ^= c;
                hash *= 16777619;
            }
            hash ^= hash >> 16;
            hash *= 0x85ebca6b;
            hash ^= hash >> 13;
            hash *= 0xc2b2ae35;
            hash ^= hash >> 16;
        }

        uint span = (uint)(rating.Max - rating.Min + 1);
        if (rating.Lean == 1) return rating.Min + (int)(hash % span);

        // Leaning: turn the hash into a fraction from 0 up to (not including) 1, and raise it
        // to 1/Lean. That curve keeps 0 at 0 and 1 at 1 but bows upward in between, so more
        // of the range lands on the high side. The clamp covers floating-point rounding.
        double fraction = hash / 4294967296.0;
        double leaned = Math.Pow(fraction, 1.0 / rating.Lean);
        int step = (int)(leaned * span);
        if (step >= (int)span) step = (int)span - 1;
        return rating.Min + step;
    }

    // Kept in a user variable that isn't saved, so a restart forgets it — a cooldown doesn't
    // need to outlive Streamer.bot. An unreadable or backwards clock fails open.
    private bool OnCooldown(string login, string command)
    {
        if (CooldownSeconds <= 0 || login.Length == 0) return false;
        string key = "rate." + command + ".lastAt";
        DateTime now = DateTime.UtcNow;

        long ticks;
        string last = CPH.GetTwitchUserVar<string>(login, key, false);
        if (long.TryParse(last, out ticks))
        {
            double since = (now - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            if (since >= 0 && since < CooldownSeconds) return true;
        }
        CPH.SetTwitchUserVar(login, key, now.Ticks.ToString(), false);
        return false;
    }

    // The command that fired, e.g. "!pp" -> "pp". A Chat Message trigger has no command
    // argument, so the first word of the message stands in for it.
    private string Invoked()
    {
        string invoked = Arg(new[] { "command", "message" }, "").Trim().TrimStart('!');
        int space = invoked.IndexOf(' ');
        if (space >= 0) invoked = invoked.Substring(0, space);
        return invoked.ToLowerInvariant();
    }

    /**
     * Who is being rated: the asker when no name is given, or the named Twitch user. False for
     * a name Twitch doesn't know.
     *
     * Streamer.bot fills targetUser itself when the first word is a user it recognises, and
     * that is taken as-is. Otherwise the word has to look like a login before it is looked up,
     * which is what keeps links, spaces and anything else chat can type out of the reply.
     */
    private bool Target(string user, string login, out string name, out string targetLogin)
    {
        name = Arg(new[] { "targetUser", "targetUserName" }, "");
        targetLogin = Arg(new[] { "targetUserName", "targetUser" }, "").ToLowerInvariant();
        if (name.Length > 0) return true;

        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!")) raw = Rest(raw);

        string word = FirstWord(raw).TrimStart('@');
        if (word.Length == 0 || word.ToLowerInvariant() == login)
        {
            name = user;
            targetLogin = login;
            return true;
        }
        if (!LooksLikeLogin(word)) return false;

        object record = null;
        try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
        catch (Exception error) { CPH.LogWarn("[rate] user lookup threw: " + error.Message); }
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

    private string FirstWord(string raw)
    {
        var parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[0];
    }

    // Everything after the first word.
    private string Rest(string raw)
    {
        int space = raw.IndexOf(' ');
        return space < 0 ? "" : raw.Substring(space + 1).Trim();
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

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
