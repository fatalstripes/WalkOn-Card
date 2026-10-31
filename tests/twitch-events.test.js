const test = require('node:test');
const assert = require('node:assert/strict');
const { mapTwitchEvent, tierLabel } = require('../js/twitch-events.js');

test('tierLabel normalizes Streamer.bot and EventSub tiers', () => {
  assert.equal(tierLabel('tier 1'), 'Tier 1');
  assert.equal(tierLabel('Tier 3'), 'Tier 3');
  assert.equal(tierLabel('2000'), 'Tier 2');
  assert.equal(tierLabel('prime'), 'Prime');
  assert.equal(tierLabel(undefined), '');
});

test('Follow maps to follow', () => {
  const a = mapTwitchEvent('Follow', { user_name: 'Lucia' });
  assert.deepEqual(
    { p: a.platform, t: a.alertType, n: a.userName, trig: a.triggerName },
    { p: 'twitch', t: 'follow', n: 'Lucia', trig: 'New Follower' }
  );
});

test('Sub maps to sub with tier', () => {
  const a = mapTwitchEvent('Sub', { user: { name: 'Jason' }, sub_tier: 'tier 2' });
  assert.equal(a.alertType, 'sub');
  assert.equal(a.userName, 'Jason');
  assert.equal(a.levelName, 'Tier 2');
});

test('ReSub maps to milestone with months and message', () => {
  const a = mapTwitchEvent('ReSub', { user: { name: 'Jason' }, cumulative: 14, sub_tier: 'prime', text: 'hi' });
  assert.equal(a.alertType, 'milestone');
  assert.equal(a.months, 14);
  assert.equal(a.levelName, 'Prime');
  assert.equal(a.message, 'hi');
});

test('GiftBomb maps to gift with count', () => {
  const a = mapTwitchEvent('GiftBomb', { user: { name: 'Tommy' }, gifts: 10 });
  assert.equal(a.alertType, 'gift');
  assert.equal(a.count, 10);
  assert.equal(a.userName, 'Tommy');
});

test('single GiftSub maps to giftRec; bomb-part GiftSub is dropped', () => {
  const one = mapTwitchEvent('GiftSub', { user: { name: 'Tommy' }, recipient: { name: 'Lucia' } });
  assert.equal(one.alertType, 'giftRec');
  assert.equal(one.userName, 'Lucia');
  assert.equal(one.gifterUser, 'Tommy');
  assert.equal(mapTwitchEvent('GiftSub', { user: { name: 'Tommy' }, recipient: { name: 'Lucia' }, fromCommunitySubGift: true }), null);
});

test('Cheer maps to cheer with bits and message', () => {
  const a = mapTwitchEvent('Cheer', { message: { displayName: 'Lucia', bits: 500, message: 'take it' } });
  assert.equal(a.alertType, 'cheer');
  assert.equal(a.amount, 500);
  assert.equal(a.message, 'take it');
  assert.equal(a.userName, 'Lucia');
});

test('Raid maps to raid with viewers', () => {
  const a = mapTwitchEvent('Raid', { from_broadcaster_user_name: 'BigStreamer', viewers: 42 });
  assert.equal(a.alertType, 'raid');
  assert.equal(a.userName, 'BigStreamer');
  assert.equal(a.count, 42);
});

test('object user with no name fields falls back to Someone', () => {
  const a = mapTwitchEvent('Follow', { user: { id: '123' } });
  assert.equal(a.userName, 'Someone');
  assert.equal(typeof a.userName, 'string');
});

test('ReSub with object message yields empty message string', () => {
  const a = mapTwitchEvent('ReSub', { user: { name: 'Jason' }, cumulative: 3, message: { bits: 1 } });
  assert.equal(a.message, '');
});

test('Cheer falls back to message.username when displayName is absent', () => {
  const a = mapTwitchEvent('Cheer', { message: { username: 'lucia_x', bits: 100 } });
  assert.equal(a.userName, 'lucia_x');
});

test('avatar path maps user.profileImageUrl to userProfileUrl', () => {
  const a = mapTwitchEvent('Sub', { user: { name: 'Lucia', profileImageUrl: 'https://img.example/lucia.png' }, sub_tier: '1000' });
  assert.equal(a.userProfileUrl, 'https://img.example/lucia.png');
});

test('unknown event and empty data are safe', () => {
  assert.equal(mapTwitchEvent('Nope', {}), null);
  assert.equal(mapTwitchEvent('Follow', undefined).userName, 'Someone');
});
