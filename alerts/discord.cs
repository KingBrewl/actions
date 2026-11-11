using System;

// Trigger: Core -> Timed Actions -> "Discord Promo Timer" (repeat, every 2700 seconds = 45
// minutes). Chat-only. Promotes the Discord server on a timer.
//
// The invite is also in links.cs (!discord). If it changes, update both files.
public class CPHInline
{
    private const string Discord = "https://discord.gg/WVffXrJt8h";

    public bool Execute()
    {
        CPH.SendMessage("The tavern never closes! Alerts, clips and good company on the Discord: " + Discord + " kingbre3King");
        return true;
    }
}
