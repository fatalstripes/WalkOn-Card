using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class CPHInline
{
    private object SafeGetTwitchVar(string user, string varName)
    {
        try { var s = CPH.GetTwitchUserVar<string>(user, varName, true); if (s != null) return s; } catch { }
        try { var i = CPH.GetTwitchUserVar<int?>(user, varName, true); if (i.HasValue) return i.Value; } catch { }
        try { var l = CPH.GetTwitchUserVar<long?>(user, varName, true); if (l.HasValue) return l.Value; } catch { }
        try { var d = CPH.GetTwitchUserVar<double?>(user, varName, true); if (d.HasValue) return d.Value; } catch { }
        return null;
    }

    private object FindUserVarWithAliases(string user, string primaryKey, string[] aliases)
    {
        var keys = new List<string> { primaryKey };
        if (aliases != null) foreach (string a in aliases) if (!keys.Contains(a)) keys.Add(a);
        foreach (string k in keys)
        {
            object v = SafeGetTwitchVar(user, k);
            if (v != null) return v;
        }
        return null;
    }

    public bool Execute()
    {
        string targetUser = args.ContainsKey("targetUser") ? args["targetUser"].ToString().Trim() : "";
        string requestedVarsCsv = args.ContainsKey("requestedVars")
            ? args["requestedVars"].ToString()
            : "avatarUrl,levelName,months,memberSince,loyaltyLevel";
        string eventName = args.ContainsKey("eventName") ? args["eventName"].ToString() : "MemberWalkOn";

        if (string.IsNullOrEmpty(targetUser))
        {
            CPH.LogWarn("[GTA-LookupTwitch] targetUser was empty.");
            return false;
        }

        var result = new Dictionary<string, object>
        {
            { "event", "GTA_UserLookupResult" },
            { "type", eventName },
            { "platform", "twitch" },
            { "targetUser", targetUser },
            { "userName", targetUser }
        };

        var aliasMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "avatarUrl",    new[] { "userProfileUrl", "avatar", "profileImageUrl", "profilePic" } },
            { "levelName",    new[] { "tier", "tierName", "subTier", "role" } },
            { "months",       new[] { "tenure", "subMonths", "cumulative", "totalMonths" } },
            { "memberSince",  new[] { "joined", "joinDate", "since", "followDate" } },
            { "loyaltyLevel", new[] { "loyalty", "rank", "userLevel", "level" } }
        };

        foreach (string rawVar in requestedVarsCsv.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string varName = rawVar.Trim();
            object val = FindUserVarWithAliases(targetUser, varName, aliasMap.ContainsKey(varName) ? aliasMap[varName] : null);
            if (val != null) result[varName] = val;
            else CPH.LogWarn($"[GTA-LookupTwitch] '{varName}' returned null for {targetUser}.");
        }

        // Fall back to the Twitch API for the profile image
        if (!result.ContainsKey("avatarUrl"))
        {
            try
            {
                var info = CPH.TwitchGetExtendedUserInfoByLogin(targetUser);
                if (info != null && !string.IsNullOrEmpty(info.ProfileImageUrl)) result["avatarUrl"] = info.ProfileImageUrl;
            }
            catch (Exception ex) { CPH.LogWarn($"[GTA-LookupTwitch] API lookup failed: {ex.Message}"); }
        }

        object storedName = FindUserVarWithAliases(targetUser, "userName", new[] { "user", "displayName", "name" });
        if (storedName != null && !string.IsNullOrEmpty(storedName.ToString())) result["userName"] = storedName.ToString();

        string json = JsonConvert.SerializeObject(result);
        CPH.LogInfo($"[GTA-LookupTwitch] Broadcasting JSON: {json}");
        CPH.WebsocketBroadcastJson(json);
        return true;
    }
}
