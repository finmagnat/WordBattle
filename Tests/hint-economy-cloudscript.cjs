// Offline tests of the actual CloudScript. No SDK, network, ads or production account.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const code = fs.readFileSync(path.join(root, 'Assets/Scripts/CloudScripts/CloudScript.js'), 'utf8');
const daily = JSON.parse(fs.readFileSync(path.join(root, 'Docs/HintStage3/PlayFab/daily_bonus_config.json'), 'utf8'));
const keys = ['boost_letter','boost_slow','boost_eraser','boost_mixer','boost_swap','boost_hint'];
const catalog = fs.readFileSync(path.join(root,'Assets/Configs/ShopCatalog.asset'),'utf8');
const packs = {};
for (const offer of catalog.split(/\n  - IsActive:/).slice(1)) {
  if (!/\n    Type: 0/.test(offer)) continue;
  const id = offer.match(/\n    ProductId: (\S+)/)?.[1];
  if (!id || id === 'remove_interstitial_ads') continue;
  packs[id] = {};
  for (const reward of offer.matchAll(/- ItemId: (\d+)\r?\n      Amount: (\d+)/g)) packs[id][keys[Number(reward[1])-1]] = Number(reward[2]);
}
let checks = 0;
function test(name, body) { body(); checks++; console.log('PASS: '+name); }
function fake() {
  const db = {}, internal = {}, td = {shop_rewards:JSON.stringify(packs), daily_bonus_config:JSON.stringify(daily)};
  let validates=0, writes=0, reject=false, failWrite=false;
  const server = {
    GetUserReadOnlyData: ({Keys}) => ({Data:Object.fromEntries(Keys.filter(k=>db[k]!==undefined).map(k=>[k,{Value:db[k]}]))}),
    UpdateUserReadOnlyData: ({Data}) => {if(failWrite) throw Error('simulated storage failure'); writes++; Object.assign(db,Data);},
    GetUserInternalData: ({Keys}) => ({Data:Object.fromEntries(Keys.filter(k=>internal[k]!==undefined).map(k=>[k,{Value:internal[k]}]))}),
    GetTitleData: () => ({Data:td}),
    GetTitleInternalData: () => ({Data:{debug_shop_secret:'test-secret',debug_shop_allowlist:'["offline-player"]'}}),
    AuthenticateSessionTicket: ({SessionTicket}) => ({IsSessionTicketExpired:SessionTicket==='expired-session',UserInfo:{PlayFabId:SessionTicket==='other-session'?'other-player':'offline-player'}}),
    UpdateUserInternalData: ({Data}) => Object.assign(internal,Data),
    UpdateUserData: () => {throw Error('unexpected entitlement write');}
  };
  const http = {request(url,method,body,type,headers,logging) {
    assert.equal(url,'https://ABC123.playfabapi.com/Client/ValidateGooglePlayPurchase');
    assert.equal(method,'post'); assert.equal(type,'application/json'); assert.equal(headers['X-Authorization'],'offline-session'); assert.equal(logging,false);
    assert.ok(JSON.parse(body).Signature); validates++;
    return JSON.stringify(reject?{code:400,error:reject==="used"?'ReceiptAlreadyUsed':'InvalidReceipt'}:{code:200,data:{Fulfillments:[]}});
  }};
  const sandbox = {handlers:{},server,http,script:{titleId:'ABC123'},currentPlayerId:'offline-player',log:{error(){},info(){}},Math:Object.create(Math)};
  vm.createContext(sandbox); vm.runInContext(code,sandbox);
  return {db,internal,td,sandbox,call(name,args={}) {const r=sandbox.handlers[name](args,{});return typeof r==='string'?JSON.parse(r):r;},
    get validates(){return validates;}, get writes(){return writes;}, set reject(v){reject=v;}, set failWrite(v){failWrite=v;}};
}
const purchase = (token='token-1', transaction='order-1') => ({source:'google', productId:'wordlet_pack_best',transactionId:transaction,
  sessionTicket:'offline-session',receiptJson:JSON.stringify({productId:'wordlet_pack_best',purchaseToken:token}),signature:'fake-signature',currencyCode:'USD',purchasePrice:2999});

test('paid catalog contains six x5 rewards, including boost_hint',()=>assert.deepEqual(packs.wordlet_pack_best,Object.fromEntries(keys.map(k=>[k,5]))));
test('legacy hint_10 still grants Letter x10',()=>assert.deepEqual(packs.wordlet_pack_hint_10,{boost_letter:10}));
test('new player starter grants all six x1 once',()=>{const f=fake();f.call('PrepareStarterBonus');const r=f.call('GrantStarterGift');assert.equal(r.granted,true); for(const k of keys) assert.equal(f.db[k],'1');assert.equal(f.db.starter_bonus_state,'granted');});
test('starter reinitialization cannot give a second Hint',()=>{const f=fake();f.call('PrepareStarterBonus');f.call('GrantStarterGift');f.call('PrepareStarterBonus');assert.equal(f.call('GrantStarterGift').alreadyGranted,true);assert.equal(f.db.boost_hint,'1');});
test('old granted account with missing Hint does not receive another starter',()=>{const f=fake();f.db.starter_bonus_state='granted';f.call('PrepareStarterBonus');assert.equal(f.call('GrantStarterGift').granted,false);assert.equal(f.db.boost_hint,undefined);});
test('zero Hint does not reset the starter flag',()=>{const f=fake();f.db.starter_bonus_state='granted';f.db.boost_hint='0';assert.equal(f.call('GrantStarterGift').alreadyGranted,true);assert.equal(f.db.boost_hint,'0');});
function readyDaily(f,day=6) { f.db.starter_bonus_state='granted'; f.db.daily_bonus_state=JSON.stringify({dailyRewardDay:day,lastClaimUtc:'',claimAvailable:true}); }
test('daily day6 preserves Eraser and adds Hint x1',()=>{const f=fake();readyDaily(f);const r=f.call('ClaimDailyBonus');assert.equal(r.granted,true);assert.equal(f.db.boost_hint,'1');assert.equal(f.db.boost_eraser,'1');assert.equal(f.db.boost_letter,undefined);});
test('fixed Hint-only test configuration increases only Hint',()=>{const f=fake();const cfg=structuredClone(daily);cfg.days[5].rewards=[{boosterType:'Hint',amount:2}];f.td.daily_bonus_config=JSON.stringify(cfg);readyDaily(f);f.call('ClaimDailyBonus');assert.equal(f.db.boost_hint,'2');for(const k of keys.slice(0,5)) assert.equal(f.db[k],undefined);});
test('daily duplicate claim does not issue Hint again',()=>{const f=fake();readyDaily(f);f.call('ClaimDailyBonus');assert.equal(f.call('ClaimDailyBonus').granted,false);assert.equal(f.db.boost_hint,'1');});
test('configured daily chest can select Hint x2',()=>{const f=fake();readyDaily(f,7);let rolls=[0,0.999];f.sandbox.Math.random=()=>rolls.shift();const r=f.call('ClaimDailyBonus');assert.equal(r.selectedBooster,'Hint');assert.equal(f.db.boost_hint,'2');});
test('configured daily chest can select Hint x3',()=>{const f=fake();readyDaily(f,7);let rolls=[0.75,0.999];f.sandbox.Math.random=()=>rolls.shift();f.call('ClaimDailyBonus');assert.equal(f.db.boost_hint,'3');});
test('daily jackpot grants six x1 including Letter and Hint',()=>{const f=fake();readyDaily(f,7);f.sandbox.Math.random=()=>0.95;const r=f.call('ClaimDailyBonus');assert.equal(r.jackpot,true);for(const k of keys) assert.equal(f.db[k],'1');});
test('daily raw drop weights 70/20/10 and all six pool entries remain',()=>{assert.deepEqual(daily.days[6].chestDrops.map(d=>d.weight),[70,20,10]);for(const d of daily.days[6].chestDrops.slice(0,2))assert.deepEqual(new Set(d.pool),new Set(['Letter','Slowdown','Eraser','Mixer','Swap','Hint']));});
test('validated paid pack includes Hint in the same server write',()=>{const f=fake();const r=f.call('GrantPack',purchase());assert.equal(r.ok,true);assert.equal(f.validates,1);assert.equal(f.writes,1);for(const k of keys)assert.equal(f.db[k],'5');assert.ok(f.db.purchase_tokens_v2);});
test('repeated transaction gives no second pack or validation',()=>{const f=fake();f.call('GrantPack',purchase());assert.equal(f.call('GrantPack',purchase()).duplicate,true);assert.equal(f.db.boost_hint,'5');assert.equal(f.validates,1);assert.equal(f.writes,1);});
test('changing client transaction ID cannot replay the signed token',()=>{const f=fake();f.call('GrantPack',purchase());assert.equal(f.call('GrantPack',purchase('token-1','another-order')).duplicate,true);assert.equal(f.db.boost_hint,'5');});
test('a different valid token is a separate purchase',()=>{const f=fake();f.call('GrantPack',purchase());f.call('GrantPack',purchase('token-2','order-2'));assert.equal(f.db.boost_hint,'10');});
test('rejected purchase grants nothing and records no consumed token',()=>{const f=fake();f.reject=true;assert.throws(()=>f.call('GrantPack',purchase()));assert.deepEqual(f.db,{});});
test('receipt product mismatch is rejected before grant',()=>{const f=fake();const p=purchase();p.productId='wordlet_pack_eraser_10';assert.throws(()=>f.call('GrantPack',p));assert.equal(f.writes,0);});
test('missing Google transaction is rejected',()=>{const f=fake();assert.throws(()=>f.call('GrantPack',purchase('token','')));assert.equal(f.writes,0);});
test('storage failure does not consume an ungranted transaction',()=>{const f=fake();f.failWrite=true;assert.throws(()=>f.call('GrantPack',purchase()));assert.deepEqual(f.db,{});f.failWrite=false;f.call('GrantPack',purchase());assert.equal(f.db.boost_hint,'5');assert.equal(f.validates,1);});
test('previous-revision transaction markers are respected',()=>{const f=fake();f.internal.purchase_tokens='["order-1"]';assert.equal(f.call('GrantPack',purchase()).duplicate,true);assert.equal(f.db.boost_hint,undefined);});
test('debug path requires the existing server secret',()=>{const f=fake();assert.throws(()=>f.call('GrantPack',{source:'debug',productId:'wordlet_pack_best',transactionId:'dev'}));assert.equal(f.writes,0);});
test('offline permitted debug purchase uses same full reward mapping',()=>{const f=fake();const p={source:'debug',productId:'wordlet_pack_best',transactionId:'dev',debugSecret:'test-secret'};f.call('GrantPack',p);f.call('GrantPack',p);assert.equal(f.db.boost_hint,'5');assert.equal(f.validates,0);});
test('unknown purchase sources cannot bypass validation',()=>{const f=fake();assert.throws(()=>f.call('GrantPack',{...purchase(),source:'client'}));assert.equal(f.writes,0);});
test('another player session cannot validate or grant the purchase',()=>{const f=fake();assert.throws(()=>f.call('GrantPack',{...purchase(),sessionTicket:'other-session'}));assert.equal(f.validates,0);assert.equal(f.writes,0);});
test('missing session cannot validate or grant the purchase',()=>{const f=fake();assert.throws(()=>f.call('GrantPack',{...purchase(),sessionTicket:''}));assert.equal(f.validates,0);assert.equal(f.writes,0);});
test('expired session cannot validate or grant the purchase',()=>{const f=fake();assert.throws(()=>f.call('GrantPack',{...purchase(),sessionTicket:'expired-session'}));assert.equal(f.validates,0);assert.equal(f.writes,0);});
test('missing reward config fails before receipt consumption',()=>{const f=fake();delete f.td.shop_rewards;assert.throws(()=>f.call('GrantPack',purchase()));assert.equal(f.validates,0);assert.equal(f.writes,0);});
test('ReceiptAlreadyUsed without own server proof grants nothing',()=>{const f=fake();f.reject='used';assert.throws(()=>f.call('GrantPack',purchase()));assert.deepEqual(f.db,{});});
test('session credentials are absent from persisted data and returned payload',()=>{const f=fake();const r=f.call('GrantPack',purchase());assert.ok(!JSON.stringify([r,f.db,f.internal]).includes('offline-session'));});
console.log(`PASSED: ${checks} offline CloudScript checks`);
