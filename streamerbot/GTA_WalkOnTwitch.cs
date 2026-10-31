using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class CPHInline
{
    public bool Execute()
    {
        string user = args.ContainsKey("userName") ? args["userName"].ToString()
                    : (args.ContainsKey("user") ? args["user"].ToString() : "");
        if (string.IsNullOrWhiteSpace(user)) return false;

        string eventName = args.ContainsKey("eventName") ? args["eventName"].ToString() : "MemberWalkOn";
        bool isTest = args.ContainsKey("isTest") && args["isTest"].ToString().ToLower() == "true";

        string avatarUrl = isTest ? "https://i.pravatar.cc/300?img=47" : "";
        if (!isTest)
        {
            try
            {
                var info = CPH.TwitchGetExtendedUserInfoByLogin(user);
                if (info != null) avatarUrl = info.ProfileImageUrl ?? "";
            }
            catch (Exception ex) { CPH.LogWarn($"[GTA-WalkOnTwitch] Avatar lookup failed: {ex.Message}"); }
        }

        string levelName = "Viewer";
        if (Flag("isSubscribed")) levelName = "Subscriber";
        if (Flag("isVip")) levelName = "VIP";
        if (Flag("isModerator")) levelName = "Moderator";
        if (Flag("isBroadcaster")) levelName = "Broadcaster";

        var payload = new Dictionary<string, object>
        {
            { "event", eventName },
            { "platform", "twitch" },
            { "userName", user },
            { "avatarUrl", avatarUrl },
            { "levelName", levelName }
        };

        int? months = CPH.GetTwitchUserVar<int?>(user, "months", true);
        if (months.HasValue) payload["months"] = months.Value;

        string json = JsonConvert.SerializeObject(payload);
        CPH.LogInfo($"[GTA-WalkOnTwitch] Broadcasting: {json}");
        CPH.WebsocketBroadcastJson(json);
        return true;
    }

    private bool Flag(string key)
    {
        return args.ContainsKey(key) && args[key] != null && args[key].ToString().ToLower() == "true";
    }
}
