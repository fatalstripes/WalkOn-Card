using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

public class CPHInline
{
    public bool Execute()
    {
        // 1. Grab arguments
        CPH.TryGetArg<string>("userType", out string userType);
        CPH.TryGetArg<string>("rawInput", out string rawInput);
        CPH.TryGetArg<string>("successMessage", out string successMessage);
        CPH.TryGetArg<string>("failMessage", out string failMessage);

        if (string.IsNullOrEmpty(userType) && CPH.TryGetArg<string>("eventSource", out string src) && !string.IsNullOrEmpty(src))
            userType = src;
        userType = userType?.ToLower() ?? "youtube";

        // Clean the target username for the database lookup
        string targetName = (rawInput ?? "").Trim().TrimStart('@');

        // Create the ALL CAPS version for the chat and widget display
        string displayUser = targetName.ToUpper();

        // If they just typed "!so" with no name, break silently
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return false; 
        }

        string storedUserId = "";
        string storedAvatarUrl = "";
        string storedChannelLink = "";

        // 2. Retrieve persistent variables using the original targetName
        if (userType == "youtube")
        {
            storedUserId = CPH.GetYouTubeUserVar<string>(targetName, "userId", true);
            storedAvatarUrl = CPH.GetYouTubeUserVar<string>(targetName, "avatarUrl", true)
                           ?? CPH.GetYouTubeUserVar<string>(targetName, "userProfileUrl", true);
            storedChannelLink = CPH.GetYouTubeUserVar<string>(targetName, "channelLink", true);
        }
        else if (userType == "twitch")
        {
            storedUserId = CPH.GetTwitchUserVar<string>(targetName, "userId", true);
            storedAvatarUrl = CPH.GetTwitchUserVar<string>(targetName, "avatarUrl", true);
            storedChannelLink = CPH.GetTwitchUserVar<string>(targetName, "channelLink", true);

            // Fall back to the Twitch API when the user variables were never populated
            if (string.IsNullOrEmpty(storedAvatarUrl))
            {
                try
                {
                    var info = CPH.TwitchGetExtendedUserInfoByLogin(targetName);
                    if (info != null)
                    {
                        storedUserId = info.UserId;
                        storedAvatarUrl = info.ProfileImageUrl;
                        storedChannelLink = $"https://twitch.tv/{targetName.ToLower()}";
                    }
                }
                catch (Exception ex) { CPH.LogWarn($"[GTA-Shoutout] Twitch lookup failed: {ex.Message}"); }
            }
        }

        // 3. Verify user exists in the database
        bool userExists = !string.IsNullOrEmpty(storedAvatarUrl) || !string.IsNullOrEmpty(storedChannelLink);
        CPH.SetArgument("userExists", userExists);

        // 4. Handle FAILURE (User not found)
        if (!userExists)
        {
            if (!string.IsNullOrEmpty(failMessage))
            {
                string finalFailMsg = failMessage.Replace("{user}", displayUser);
                SendPlatformMessage(finalFailMsg, userType);
            }

            return false; // Kills the action queue
        }

        // Upscale YouTube avatar URL from =s88 to =s600 for crisp overlay rendering
        if (userType == "youtube" && !string.IsNullOrEmpty(storedAvatarUrl))
        {
            storedAvatarUrl = Regex.Replace(storedAvatarUrl, @"=s\d+-", "=s600-");
        }

        // 5. Handle SUCCESS Message Formatting
        if (!string.IsNullOrEmpty(successMessage))
        {
            string finalSuccessMsg = successMessage
                .Replace("{user}", displayUser)
                .Replace("{link}", storedChannelLink ?? "");

            SendPlatformMessage(finalSuccessMsg, userType);
        }

        // 6. Set variables for any sub-actions you still want to run after this script
        if (!string.IsNullOrEmpty(storedUserId)) CPH.SetArgument("userId", storedUserId);
        if (!string.IsNullOrEmpty(storedAvatarUrl)) CPH.SetArgument("avatarUrl", storedAvatarUrl);
        if (!string.IsNullOrEmpty(storedChannelLink)) CPH.SetArgument("channelLink", storedChannelLink);

        // 7. Build the JSON for the GTA VI Overlay Suite using the ALL CAPS name
        var shoutoutData = new 
        {
            @event = "GTA_Shoutout",
            user = displayUser, 
            avatar = storedAvatarUrl ?? "",
            channelLink = storedChannelLink ?? "",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // 8. Serialize, update the persisted Global Variable ('viShoutOuts'), AND broadcast via WebSocket
        string jsonOutput = JsonConvert.SerializeObject(shoutoutData);
        CPH.SetGlobalVar("viShoutOuts", jsonOutput, true);
        CPH.WebsocketBroadcastJson(jsonOutput);

        return true;
    }

    // --- Helper Method to Route Chat Messages ---
    private void SendPlatformMessage(string msg, string platform)
    {
        if (platform == "youtube")
        {
            CPH.SendYouTubeMessage(msg);
        }
        else if (platform == "twitch")
        {
            CPH.SendMessage(msg, true);
        }
        else
        {
            CPH.SendMessage(msg);
        }
    }
}
