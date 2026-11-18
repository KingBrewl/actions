using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

// Trigger: Twitch -> Command, with "!uptime", "!followage", "!accountage" and "!schedule" all
// in the command box — one action serves all four, the way links.cs does. Chat-only.
//
//   !uptime              -> how long the stream has been live, or that it's offline
//   !followage [@name]   -> how long the asker (or @name) has followed the channel
//   !accountage [@name]  -> how old the asker's (or @name's) Twitch account is
//   !schedule            -> the next streams on the channel's Twitch schedule
//
// The answers come from Twitch itself, through Streamer.bot's own Twitch login, so nothing is
// sent to any other service. !uptime, !followage and !schedule ask Twitch's API directly;
// !accountage uses the user lookup Streamer.bot already does for @names.
//
// !followage needs the broadcaster account to have the moderator:read:followers permission,
// which Streamer.bot asks for when the broadcaster account is connected. If Twitch refuses,
// chat gets "couldn't check" and the Streamer.bot log says why.
public class CPHInline
{
    // Said by !schedule when the Twitch schedule is empty or switched off. Fill in your usual
    // days, e.g. "Tue, Thu & Sat from 7pm ET", or leave it to point at following.
    private const string ScheduleFallback = "No streams on the Twitch schedule right now - follow so you get pinged when the tavern opens! kingbre3Howdy";

    // How many upcoming streams !schedule lists.
    private const int ScheduleCount = 2;

    // The time zone label after schedule times, e.g. "ET". Empty works it out from the PC's
    // own time zone: "Eastern Standard Time" becomes "EST".
    private const string TimeZoneLabel = "";

    // A Twitch request that hasn't answered in this long is given up on.
    private const int TimeoutMs = 5000;

    private const string Helix = "https://api.twitch.tv/helix/";

    public bool Execute()
    {
        string command = Invoked();
        try
        {
            switch (command)
            {
                case "uptime": Uptime(); break;
                case "followage": case "followed": Followage(); break;
                case "accountage": case "age": AccountAge(); break;
                case "schedule": case "sched": Schedule(); break;
                default:
                    CPH.LogWarn("[info] no command \"" + command + "\" - attach !uptime, !followage, !accountage or !schedule");
                    break;
            }
        }
        catch (Exception error)
        {
            CPH.LogWarn("[info] !" + command + " failed: " + error);
            CPH.SendMessage("Couldn't reach Twitch for that just now - try again in a bit.");
        }
        return true;
    }

    // ---- the commands ---------------------------------------------------------------------

    private void Uptime()
    {
        string channel;
        string channelId = Broadcaster(out channel);
        string body = Get("streams?user_id=" + channelId);
        if (body == null)
        {
            CPH.SendMessage("Couldn't check the stream just now - the Streamer.bot log says why.");
            return;
        }

        StreamsReply reply = Parse<StreamsReply>(body);
        if (reply.data == null || reply.data.Count == 0)
        {
            CPH.SendMessage(channel + " is offline right now - the tavern's closed. kingbre3Lurk");
            return;
        }

        DateTime started = Utc(reply.data[0].started_at);
        CPH.SendMessage(channel + " has been live for " + Clock(DateTime.UtcNow - started) + ". kingbre3Cool");
    }

    private void Followage()
    {
        string name, login, userId;
        if (!Who(out name, out login, out userId)) return;

        string channel;
        string channelId = Broadcaster(out channel);
        if (userId == channelId)
        {
            CPH.SendMessage(name + " doesn't follow the tavern - " + name + " IS the tavern. kingbre3King");
            return;
        }

        string body = Get("channels/followers?broadcaster_id=" + channelId + "&user_id=" + userId);
        if (body == null)
        {
            CPH.SendMessage("Couldn't check follows just now - the Streamer.bot log says why.");
            return;
        }

        FollowersReply reply = Parse<FollowersReply>(body);
        if (reply.data == null || reply.data.Count == 0)
        {
            CPH.SendMessage(name + " hasn't followed yet - the door's always open! kingbre3Howdy");
            return;
        }

        DateTime since = Utc(reply.data[0].followed_at);
        CPH.SendMessage(name + " has followed " + channel + " for " + Span(since, DateTime.UtcNow) +
            " (since " + since.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + "). kingbre3Heart");
    }

    private void AccountAge()
    {
        string name, login, userId;
        object record;
        if (!Who(out name, out login, out userId, out record)) return;

        object created = Value(record, new[] { "CreatedAt" });
        if (!(created is DateTime))
        {
            CPH.SendMessage("Couldn't find when " + name + "'s account was made.");
            return;
        }

        DateTime made = ((DateTime)created).ToUniversalTime();
        CPH.SendMessage(name + "'s account is " + Span(made, DateTime.UtcNow) + " old (made " +
            made.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + ").");
    }

    private void Schedule()
    {
        string channel;
        string channelId = Broadcaster(out channel);

        // Twitch answers 404 when the channel has no schedule at all; Get returns null for it.
        string body = Get("schedule?broadcaster_id=" + channelId + "&first=10");
        ScheduleReply reply = Parse<ScheduleReply>(body);
        List<Segment> segments = reply == null || reply.data == null ? null : reply.data.segments;

        var lines = new List<string>();
        if (segments != null)
        {
            foreach (Segment segment in segments)
            {
                if (lines.Count >= ScheduleCount) break;
                // A cancelled stream stays on the schedule with canceled_until set.
                if (segment.canceled_until.HasValue) continue;
                DateTime start = Utc(segment.start_time);
                if (start < DateTime.UtcNow) continue;

                string what = segment.category != null && segment.category.name != null ? segment.category.name.Trim() : "";
                if (what.Length == 0 && segment.title != null) what = segment.title.Trim();
                lines.Add(When(start) + (what.Length > 0 ? " - " + what : ""));
            }
        }

        if (lines.Count == 0)
        {
            CPH.SendMessage(ScheduleFallback);
            return;
        }

        CPH.SendMessage("Next at the tavern: " + string.Join(" | ", lines.ToArray()) +
            " - twitch.tv/" + channel.ToLowerInvariant() + "/schedule kingbre3Howdy");
    }

    // ---- who's being asked about ----------------------------------------------------------

    private bool Who(out string name, out string login, out string userId)
    {
        object record;
        return Who(out name, out login, out userId, out record);
    }

    /**
     * The asker, or the @name after the command. The record is Twitch's user info, looked up
     * for an @name and for !accountage (the only command that needs more than the id).
     * False, having told chat, when the @name isn't a Twitch user.
     */
    private bool Who(out string name, out string login, out string userId, out object record)
    {
        record = null;
        string word = FirstWord().TrimStart('@');

        if (word.Length == 0)
        {
            name = Arg(new[] { "user", "userName" }, "");
            login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();
            userId = Arg(new[] { "userId" }, "");
            if (login.Length == 0)
            {
                CPH.SendMessage("Try it from chat, or add @name");
                userId = "";
                return false;
            }
            // Only !accountage needs the full record; the others make do with the id chat gave.
            if (userId.Length > 0 && Invoked() != "accountage" && Invoked() != "age") return true;
            word = login;
        }

        name = word;
        login = word.ToLowerInvariant();
        userId = "";
        if (LooksLikeLogin(word))
        {
            try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
            catch (Exception error) { CPH.LogWarn("[info] user lookup threw: " + error.Message); }
        }
        if (record == null)
        {
            CPH.SendMessage("Couldn't find a Twitch user called " + (LooksLikeLogin(word) ? word : "that") + ".");
            return false;
        }

        name = Prop(record, new[] { "UserName", "DisplayName", "UserLogin" }, word);
        login = Prop(record, new[] { "UserLogin", "Login" }, word).ToLowerInvariant();
        userId = Prop(record, new[] { "UserId", "Id" }, "");
        return true;
    }

    // The channel's id and display name.
    private string Broadcaster(out string name)
    {
        object me = CPH.TwitchGetBroadcaster();
        name = Prop(me, new[] { "UserName", "DisplayName", "UserLogin" }, "The stream");
        string id = Prop(me, new[] { "UserId", "Id" }, "");
        if (id.Length == 0) throw new Exception("Streamer.bot has no broadcaster account connected");
        return id;
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

    // ---- talking to Twitch ----------------------------------------------------------------

    /**
     * GETs a Twitch API path with Streamer.bot's broadcaster login and returns the reply, or
     * null when Twitch answers with an error (logged — a 401 means a missing permission).
     *
     * The web classes live in System.dll, which the inline compiler can't see, but Streamer.bot
     * itself has it loaded, so they are reached by name. Only mscorlib types cross the
     * boundary: strings, a Stream and the exception.
     */
    private string Get(string path)
    {
        string token = (CPH.TwitchOAuthToken ?? "").Trim();
        if (token.StartsWith("oauth:")) token = token.Substring(6);

        EnsureTls12();
        Type webRequest = Type.GetType("System.Net.WebRequest, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
        object request = webRequest.GetMethod("Create", new[] { typeof(string) }).Invoke(null, new object[] { Helix + path });
        Type type = request.GetType();
        type.GetProperty("Timeout").SetValue(request, TimeoutMs, null);
        object headers = type.GetProperty("Headers").GetValue(request, null);
        MethodInfo add = headers.GetType().GetMethod("Add", new[] { typeof(string), typeof(string) });
        add.Invoke(headers, new object[] { "Client-Id", CPH.TwitchClientId ?? "" });
        add.Invoke(headers, new object[] { "Authorization", "Bearer " + token });

        object response;
        try
        {
            response = type.GetMethod("GetResponse", Type.EmptyTypes).Invoke(request, null);
        }
        catch (TargetInvocationException error)
        {
            // A WebException: Twitch answered with an error status, or didn't answer at all.
            Exception web = error.InnerException ?? error;
            object failed = web.GetType().GetProperty("Response") == null ? null : web.GetType().GetProperty("Response").GetValue(web, null);
            if (failed == null) throw web;
            string status = Convert.ToString(failed.GetType().GetProperty("StatusCode").GetValue(failed, null));
            string detail = Read(failed);
            if (status != "NotFound") CPH.LogWarn("[info] Twitch said " + status + " to " + path + ": " + detail);
            return null;
        }
        return Read(response);
    }

    // Twitch only talks TLS 1.2+. Streamer.bot's .NET 4.7.2 leaves the choice to Windows (the
    // setting reads 0), which already includes it; a process pinned to older protocols gets
    // TLS 1.2 added rather than failing with "Could not create SSL/TLS secure channel".
    private void EnsureTls12()
    {
        const int Tls12 = 3072;
        Type manager = Type.GetType("System.Net.ServicePointManager, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
        PropertyInfo protocol = manager.GetProperty("SecurityProtocol");
        int current = Convert.ToInt32(protocol.GetValue(null, null));
        if (current != 0 && (current & Tls12) == 0)
            protocol.SetValue(null, Enum.ToObject(protocol.PropertyType, current | Tls12), null);
    }

    // A response's body, closing the response.
    private string Read(object response)
    {
        Type type = response.GetType();
        try
        {
            using (var stream = (Stream)type.GetMethod("GetResponseStream", Type.EmptyTypes).Invoke(response, null))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
        finally
        {
            ((IDisposable)response).Dispose();
        }
    }

    // ---- Twitch's replies -----------------------------------------------------------------
    // Read into plain classes with JsonConvert. Newtonsoft's JObject/JToken can't be used:
    // they need System.Core, which the inline compiler doesn't reference (error CS0012).
    // Only the fields used are declared; Twitch's times arrive as UTC and are parsed to
    // DateTime by Newtonsoft itself.

    private class StreamsReply { public List<LiveStream> data; }
    private class LiveStream { public DateTime started_at; }

    private class FollowersReply { public List<Follower> data; }
    private class Follower { public DateTime followed_at; }

    private class ScheduleReply { public ScheduleData data; }
    private class ScheduleData { public List<Segment> segments; }
    private class Segment
    {
        public DateTime start_time;
        public string title;
        public Category category;
        public DateTime? canceled_until;
    }
    private class Category { public string name; }

    private T Parse<T>(string body) where T : class
    {
        return body == null ? null : JsonConvert.DeserializeObject<T>(body);
    }

    private DateTime Utc(DateTime time)
    {
        return time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime();
    }

    // ---- saying times ---------------------------------------------------------------------

    // "3h 12m", or "12m" under an hour.
    private string Clock(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "less than a minute";
        int hours = (int)span.TotalHours;
        return hours > 0 ? hours + "h " + span.Minutes + "m" : span.Minutes + "m";
    }

    /**
     * Calendar time between two dates, in the two biggest units: "2 years, 3 months",
     * "5 months, 1 day", "3 days", "4 hours". Counted in calendar months, so following on
     * Jan 31 and asking on Mar 1 is "1 month, 1 day", the way people count.
     */
    private string Span(DateTime from, DateTime to)
    {
        if (to < from) to = from;
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (from.AddMonths(months) > to) months--;
        DateTime afterMonths = from.AddMonths(months);
        int days = (int)(to - afterMonths).TotalDays;

        var parts = new List<string>();
        if (months >= 12) parts.Add(Unit(months / 12, "year"));
        if (months % 12 > 0) parts.Add(Unit(months % 12, "month"));
        if (days > 0) parts.Add(Unit(days, "day"));

        if (parts.Count == 0)
        {
            TimeSpan left = to - from;
            if (left.TotalHours >= 1) return Unit((int)left.TotalHours, "hour");
            return Unit(Math.Max(1, (int)left.TotalMinutes), "minute");
        }
        return parts.Count == 1 ? parts[0] : parts[0] + ", " + parts[1];
    }

    private string Unit(int n, string unit)
    {
        return n + " " + unit + (n == 1 ? "" : "s");
    }

    // "Fri Nov 20, 7:00 PM EST (in 2 days)" in the PC's own time zone.
    private string When(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        string zone = TimeZoneLabel;
        if (zone.Length == 0)
        {
            string full = TimeZoneInfo.Local.IsDaylightSavingTime(local) ? TimeZoneInfo.Local.DaylightName : TimeZoneInfo.Local.StandardName;
            foreach (string word in full.Split(' '))
                if (word.Length > 0 && char.IsLetter(word[0])) zone += char.ToUpperInvariant(word[0]);
        }

        string text = local.ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture) + (zone.Length > 0 ? " " + zone : "");
        TimeSpan until = utc - DateTime.UtcNow;
        if (until.TotalHours < 1) return text + " (starting soon)";
        if (until.TotalHours < 23.5) return text + " (in " + Unit((int)Math.Round(until.TotalHours), "hour") + ")";
        return text + " (in " + Unit((int)Math.Round(until.TotalDays), "day") + ")";
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    // The command that fired, e.g. "!followage" -> "followage". A Chat Message trigger has no
    // command argument, so the first word of the message stands in for it.
    private string Invoked()
    {
        string invoked = Arg(new[] { "command", "message" }, "").Trim().TrimStart('!');
        int space = invoked.IndexOf(' ');
        if (space >= 0) invoked = invoked.Substring(0, space);
        return invoked.ToLowerInvariant();
    }

    // The first word after the command.
    private string FirstWord()
    {
        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        var words = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int first = words.Length > 0 && words[0].StartsWith("!") ? 1 : 0;
        return words.Length > first ? words[first] : "";
    }

    // Property names on Streamer.bot's user records have moved between versions - see
    // shoutout.cs - so they are read by name with fallbacks.
    private string Prop(object source, string[] memberNames, string fallback)
    {
        foreach (var wanted in memberNames)
        {
            object value = Value(source, new[] { wanted });
            string text = value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
            if (text.Length > 0) return text;
        }
        return fallback;
    }

    private object Value(object source, string[] memberNames)
    {
        if (source == null) return null;
        var type = source.GetType();
        foreach (var wanted in memberNames)
        {
            var property = type.GetProperty(wanted, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null) { try { return property.GetValue(source, null); } catch { } }
            var field = type.GetField(wanted, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null) { try { return field.GetValue(source); } catch { } }
        }
        return null;
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
