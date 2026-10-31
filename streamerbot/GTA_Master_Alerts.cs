using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class CPHInline
{
    // Shared recipient arg names for Twitch GiftSub (used by avatar lookup and the giftRec case).
    private static readonly string[] RecipientArgs = { "recipientUserLogin", "recipientLogin", "recipientUserName", "recipientUser", "recipient", "targetUser" };

    private object ArgAny(params string[] keys)
    {
        foreach (string k in keys)
            if (args.ContainsKey(k) && args[k] != null && args[k].ToString() != "") return args[k];
        return null;
    }

    public bool Execute()
    {
        // 1. Identify which trigger fired this action
        string triggerName = args.ContainsKey("triggerName") ? args["triggerName"].ToString() : "Unknown Event";
        bool isTest = args.ContainsKey("isTest") && args["isTest"].ToString().ToLower() == "true";
        string alertType = "generic";

        string eventSource = args.ContainsKey("eventSource") ? args["eventSource"].ToString().ToLower() : "";
        string tNameLower0 = triggerName.ToLower();
        bool isTwitch = eventSource.Contains("twitch") || tNameLower0.Contains("twitch")
                     || tNameLower0.Contains("cheer") || tNameLower0.Contains("raid") || tNameLower0.Contains("follow");
        string platform = isTwitch ? "twitch" : "youtube";

        // Fix old YouTube terminology
        if (triggerName.IndexOf("New Sponsor", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            triggerName = "New Member";
        }

        string tNameLower = triggerName.ToLower();

        // 2. Map Trigger Names to Overlay Alert Types
        if (isTwitch)
        {
            if (tNameLower.Contains("cheer")) alertType = "cheer";
            else if (tNameLower.Contains("raid")) alertType = "raid";
            else if (tNameLower.Contains("follow")) alertType = "follow";
            else if (tNameLower.Contains("resub")) alertType = "milestone";
            else if (tNameLower.Contains("gift bomb") || tNameLower.Contains("community")) alertType = "gift";
            else if (tNameLower.Contains("gift")) alertType = "giftRec";
            else if (tNameLower.Contains("sub")) alertType = "sub";
        }
        else if (tNameLower.Contains("super chat")) alertType = "superchat";
        else if (tNameLower.Contains("super sticker")) alertType = "supersticker";
        else if (tNameLower.Contains("subscriber")) alertType = "sub";
        else if (tNameLower.Contains("milestone")) alertType = "milestone";
        else if (tNameLower.Contains("received")) alertType = "giftRec"; 
        else if (tNameLower.Contains("membership gift") || tNameLower.Contains("gift membership")) alertType = "gift";
        else if (tNameLower.Contains("member") || tNameLower.Contains("sponsor")) alertType = "member";
        else if (tNameLower.Contains("jewel")) alertType = "jewels";

        // Drop single GiftSubs that belong to a gift bomb (mirrors js/twitch-events.js) so a bomb doesn't flood.
        // NOTE: these arg names are guesses; confirm against real Streamer.bot args.
        if (isTwitch && alertType == "giftRec")
        {
            object bombObj = ArgAny("fromCommunitySubGift", "fromGiftBomb", "fromSubBomb", "fromCommunityGift");
            string bombStr = bombObj != null ? bombObj.ToString() : "";
            bool fromBomb;
            if (!bool.TryParse(bombStr, out fromBomb)) fromBomb = bombStr == "1";
            if (fromBomb)
            {
                CPH.LogInfo("[GTA-Alert] Skipping GiftSub that is part of a gift bomb.");
                return true;
            }
        }

        var payload = new Dictionary<string, object>
        {
            { "event", "GTA_Alert" },
            { "platform", platform },
            { "alertType", alertType },
            { "triggerName", triggerName }
        };

        // Standard User Variables (From Trigger Arguments)
        payload["userName"] = args.ContainsKey("user") ? args["user"].ToString() : 
                             (args.ContainsKey("userName") ? args["userName"].ToString() : (isTwitch ? "Twitch Viewer" : "YouTube Member"));
        
        string userProfileUrl = args.ContainsKey("userProfileUrl") ? args["userProfileUrl"].ToString() : "";
        
        // ALWAYS force valid test image if isTest == true
        if (isTest)
        {
            userProfileUrl = isTwitch
                ? "https://i.pravatar.cc/300?img=47"
                : "https://yt3.ggpht.com/XhCX8VaSQAaxcZhSahLCjdB19626R1VNRKKTbCd_PSlepkyqEJYGbybrAbiLHtSDwBN9KN72vYE=s600-c-k-c0x00ffffff-no-rj";
        }
        else if (isTwitch && string.IsNullOrEmpty(userProfileUrl))
        {
            try
            {
                object loginObj = (alertType == "giftRec")
                    ? ArgAny(RecipientArgs)
                    : ArgAny("userLogin", "userName", "user");
                string login = loginObj != null ? loginObj.ToString() : payload["userName"].ToString();
                var info = CPH.TwitchGetExtendedUserInfoByLogin(login);
                if (info != null) userProfileUrl = info.ProfileImageUrl ?? "";
            }
            catch (Exception ex) { CPH.LogWarn($"[GTA-Alert] Twitch avatar lookup failed: {ex.Message}"); }
        }
        else if (!isTwitch && !string.IsNullOrEmpty(userProfileUrl))
        {
            userProfileUrl = Regex.Replace(userProfileUrl, @"=s\d+-", "=s600-");
        }
        payload["userProfileUrl"] = userProfileUrl;

        // 3. Map Event-Specific Argument Data
        switch (alertType)
        {
            case "sub":
                payload["levelName"] = ArgAny("tier", "subTier", "tierName") ?? "";
                break;

            case "follow":
                break;

            case "cheer":
                payload["amount"] = ArgAny("bits", "amount") ?? 0;
                payload["message"] = ArgAny("message", "rawInput") ?? "";
                break;

            case "raid":
                payload["count"] = ArgAny("viewers", "viewerCount") ?? 0;
                break;

            case "member":
                payload["levelName"] = args.ContainsKey("tierName") ? args["tierName"] : "";
                break;
                
            case "milestone":
                payload["levelName"] = ArgAny("tier", "tierName") ?? "";
                payload["months"] = ArgAny("cumulative", "months", "monthStreak") ?? 1;
                payload["message"] = args.ContainsKey("message") ? args["message"] : "";
                break;
                
            case "gift":
                string countStr = ArgAny("gifts", "totalGifts", "count", "amount")?.ToString() ?? "";
                if (isTest && string.IsNullOrEmpty(countStr)) countStr = "5";
                if (string.IsNullOrEmpty(countStr)) countStr = "1";
                payload["count"] = countStr;
                break;
                
            case "giftRec":
                if (isTwitch)
                {
                    object recipient = ArgAny(RecipientArgs);
                    if (recipient != null) payload["userName"] = recipient.ToString();
                    payload["gifterUser"] = args.ContainsKey("user") ? args["user"].ToString() : "Anonymous";
                    break;
                }
                if (args.ContainsKey("gifterUser") && !string.IsNullOrEmpty(args["gifterUser"].ToString()))
                {
                    payload["gifterUser"] = args["gifterUser"].ToString();
                }
                else if (args.ContainsKey("senderName") && !string.IsNullOrEmpty(args["senderName"].ToString()))
                {
                    payload["gifterUser"] = args["senderName"].ToString();
                }
                else if (args.ContainsKey("sender"))
                {
                    try 
                    {
                        var senderObj = JObject.FromObject(args["sender"]);
                        payload["gifterUser"] = senderObj["name"]?.ToString() ?? "Anonymous";
                    }
                    catch { payload["gifterUser"] = "Anonymous"; }
                }
                else
                {
                    payload["gifterUser"] = isTest ? "Test Gifter" : "Anonymous";
                }
                break;
                
            case "superchat":
                payload["amount"] = args.ContainsKey("amount") ? args["amount"] : "";
                payload["message"] = args.ContainsKey("message") ? args["message"] : "";
                break;
                
            case "supersticker":
                payload["amount"] = args.ContainsKey("amount") ? args["amount"] : "";
                payload["message"] = args.ContainsKey("message") ? args["message"] : "";
                
                string stickerUrl = args.ContainsKey("stickerUrl") ? args["stickerUrl"].ToString() : "";
                if (isTest) 
                {
                    stickerUrl = "https://www.gstatic.com/youtube/img/pdg/gift/assets/loot_box.webp";
                }
                payload["stickerImageUrl"] = stickerUrl;
                break;
                
            case "jewels":
                payload["jewelsAmount"] = args.ContainsKey("gift.jewelsAmount") ? args["gift.jewelsAmount"] : "";
                payload["jewelsName"] = args.ContainsKey("gift.name") ? args["gift.name"] : "";
                
                string jewelsUrl = args.ContainsKey("gift.url") ? args["gift.url"].ToString() : "";
                if (isTest) 
                {
                    jewelsUrl = "https://www.gstatic.com/youtube/img/pdg/gift/assets/controller.webp";
                }
                payload["jewelsUrl"] = jewelsUrl;
                break;
        }

        // 4. Broadcast JSON via WebSockets
        string jsonPayload = JsonConvert.SerializeObject(payload);
        CPH.LogInfo($"[GTA-Alert] Broadcasting: {jsonPayload}");
        CPH.WebsocketBroadcastJson(jsonPayload);

        return true;
    }
}
