/* Maps Streamer.bot Twitch.* WebSocket payloads to the overlay alert shape. */
(function (root) {
  function get(obj, path) {
    return path.split('.').reduce((o, k) => (o && o[k] !== undefined ? o[k] : undefined), obj);
  }

  function first(obj, paths) {
    for (const p of paths) {
      const v = get(obj, p);
      // Skip objects: text fields must never resolve to a nested object.
      if (v !== undefined && v !== null && v !== '' && typeof v !== 'object') return v;
    }
    return undefined;
  }

  function tierLabel(raw) {
    if (raw === undefined || raw === null || raw === '') return '';
    const s = String(raw).toLowerCase();
    if (s.includes('prime')) return 'Prime';
    const m = s.match(/^(\d)000$/) || s.match(/(\d)/);
    return m ? `Tier ${m[1]}` : '';
  }

  const USER = ['user.name', 'user.display_name', 'user.login', 'user_name', 'userName', 'user'];
  const AVATAR = ['user.profileImageUrl', 'user.profile_image_url', 'profileImageUrl', 'avatarUrl'];

  function mapTwitchEvent(name, data) {
    const d = data || {};
    const base = {
      platform: 'twitch',
      userName: first(d, USER) || 'Someone',
      userProfileUrl: first(d, AVATAR) || ''
    };
    const tier = tierLabel(first(d, ['sub_tier', 'subTier', 'tier']));

    switch (name) {
      case 'Follow':
        return { ...base, alertType: 'follow', triggerName: 'New Follower' };
      case 'Sub':
        return { ...base, alertType: 'sub', triggerName: 'New Subscriber', levelName: tier };
      case 'ReSub':
        return {
          ...base, alertType: 'milestone', triggerName: 'Resub Milestone', levelName: tier,
          months: first(d, ['cumulative', 'cumulativeMonths', 'months']) || 1,
          message: first(d, ['text', 'message.message', 'message']) || ''
        };
      case 'GiftBomb':
        return {
          ...base, alertType: 'gift', triggerName: 'Gift Subs',
          count: first(d, ['gifts', 'total', 'totalGifts', 'count']) || 1
        };
      case 'GiftSub': {
        if (first(d, ['fromCommunitySubGift', 'from_community_sub_gift'])) return null;
        return {
          ...base, alertType: 'giftRec', triggerName: 'Gift Sub Received',
          userName: first(d, ['recipient.name', 'recipient.display_name', 'recipient.login', 'recipientUser']) || 'Someone',
          gifterUser: first(d, USER) || 'Anonymous'
        };
      }
      case 'Cheer':
        return {
          ...base, alertType: 'cheer', triggerName: 'Bits Cheered',
          userName: first(d, ['message.displayName', 'message.username', ...USER]) || 'Someone',
          amount: first(d, ['message.bits', 'bits']) || 0,
          message: first(d, ['message.message', 'text']) || ''
        };
      case 'Raid':
        return {
          ...base, alertType: 'raid', triggerName: 'Incoming Raid',
          userName: first(d, ['from_broadcaster_user_name', 'fromBroadcasterUserName', ...USER]) || 'Someone',
          count: first(d, ['viewers', 'viewerCount']) || 0
        };
      default:
        return null;
    }
  }

  const api = { mapTwitchEvent, tierLabel };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.GTATwitchEvents = api;
})(typeof window !== 'undefined' ? window : globalThis);
