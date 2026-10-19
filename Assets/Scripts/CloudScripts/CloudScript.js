var StarterBonusStateKey = "starter_bonus_state";
var StarterBonusStateAvailable = "available";
var StarterBonusStateGranted = "granted";
var DailyBonusStateKey = "daily_bonus_state";
var DailyBonusConfigTitleDataKey = "daily_bonus_config";

var DailyBonusBoosterKeys = {
    Letter: "boost_letter",
    Slowdown: "boost_slow",
    Eraser: "boost_eraser",
    Mixer: "boost_mixer",
    Swap: "boost_swap",
    Hint: "boost_hint"
};

function getStarterBonusState(data) {
    if (data && data[StarterBonusStateKey]) {
        return data[StarterBonusStateKey].Value || "";
    }

    return "";
}

function getStarterBonusGifts() {
    return {
        "boost_letter": 1,
        "boost_slow": 1,
        "boost_eraser": 1,
        "boost_mixer": 1,
        "boost_swap": 1,
        "boost_hint": 1,
    };
}

function readBoosterTotals(data, keys) {
    var totals = {};

    for (var i = 0; i < keys.length; i++) {
        var key = keys[i];
        totals[key] = 0;

        if (data && data[key]) {
            totals[key] = parseInt(data[key].Value) || 0;
        }
    }

    return totals;
}

handlers.PrepareStarterBonus = function (args, context) {
    var result = server.GetUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Keys: [StarterBonusStateKey]
    });

    var state = getStarterBonusState(result.Data);

    if (!state) {
        state = StarterBonusStateAvailable;
        server.UpdateUserReadOnlyData({
            PlayFabId: currentPlayerId,
            Data: { [StarterBonusStateKey]: state }
        });
    }

    return {
        ok: true,
        state: state,
        isAvailable: state === StarterBonusStateAvailable
    };
};

handlers.GrantStarterGift = function (args, context) {
    const gifts = getStarterBonusGifts();

    const keys = Object.keys(gifts);
    const dataKeys = keys.slice();
    dataKeys.push(StarterBonusStateKey);

    const getDataResult = server.GetUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Keys: dataKeys
    });

    const state = getStarterBonusState(getDataResult.Data);

    if (state === StarterBonusStateGranted) {
        return {
            ok: true,
            granted: false,
            alreadyGranted: true,
            isAvailable: false,
            amounts: gifts,
            totals: readBoosterTotals(getDataResult.Data, keys)
        };
    }

    if (state !== StarterBonusStateAvailable) {
        return {
            ok: true,
            granted: false,
            unavailable: true,
            isAvailable: false,
            amounts: gifts,
            totals: readBoosterTotals(getDataResult.Data, keys)
        };
    }

    let updateData = {};
    let totals = {};

    for (let i = 0; i < keys.length; i++) {
        const key = keys[i];
        let currentValue = 0;

        if (getDataResult.Data && getDataResult.Data[key]) {
            currentValue = parseInt(getDataResult.Data[key].Value);
        }

        const newValue = currentValue + gifts[key];
        updateData[key] = newValue.toString();
        totals[key] = newValue;
    }

    updateData[StarterBonusStateKey] = StarterBonusStateGranted;

    server.UpdateUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Data: updateData
    });

    return {
        ok: true,
        granted: true,
        alreadyGranted: false,
        isAvailable: false,
        amounts: gifts,
        totals: totals
    };
};

function getDailyBonusConfigResult() {
    try {
        var titleData = server.GetTitleData({
            Keys: [DailyBonusConfigTitleDataKey]
        });

        if (!titleData.Data || !titleData.Data[DailyBonusConfigTitleDataKey]) {
            return {
                ok: false,
                error: "daily_bonus_config_missing",
                config: null
            };
        }

        return {
            ok: true,
            error: "",
            config: normalizeDailyBonusConfig(JSON.parse(titleData.Data[DailyBonusConfigTitleDataKey]))
        };
    } catch (e) {
        log.error("Failed to load daily bonus config", { error: e });
        return {
            ok: false,
            error: "daily_bonus_config_invalid",
            config: null
        };
    }
}

function normalizeDailyBonusConfig(config) {
    var result = {
        cycleLength: parseInt(config && config.cycleLength) || 7,
        days: []
    };

    if (result.cycleLength <= 0)
        result.cycleLength = 7;

    var days = config && config.days ? config.days : [];
    for (var i = 0; i < days.length; i++) {
        var dayConfig = normalizeDailyBonusDayConfig(days[i], result);
        if (dayConfig)
            result.days.push(dayConfig);
    }

    return result;
}

function normalizeDailyBonusDayConfig(dayConfig, config) {
    if (!dayConfig)
        return null;

        return {
        day: normalizeDailyBonusDay(dayConfig.day, config),
        rewardKind: normalizeDailyBonusString(dayConfig.rewardKind || dayConfig.type || "fixed").toLowerCase(),
        rewards: normalizeDailyBonusRewards(dayConfig.rewards),
        chestDrops: normalizeDailyBonusChestDrops(dayConfig.chestDrops)
    };
}

function normalizeDailyBonusChestDrops(chestDrops) {
    var result = [];
    var source = chestDrops || [];

    for (var i = 0; i < source.length; i++) {
        var drop = source[i];
        if (!drop)
            continue;

        var weight = parseInt(drop.weight) || 0;
        if (weight <= 0)
            continue;

        var multiplier = parseInt(drop.multiplier) || 1;
        if (multiplier <= 0)
            multiplier = 1;

        result.push({
            weight: weight,
            mode: normalizeDailyBonusString(drop.mode || (drop.isJackpot ? "jackpot" : "randomSingle")).toLowerCase(),
            multiplier: multiplier,
            pool: normalizeDailyBonusBoosterPool(drop.pool),
            rewards: normalizeDailyBonusRewards(drop.rewards)
        });
    }

    return result;
}

function normalizeDailyBonusRewards(rewards) {
    var result = [];
    var source = rewards || [];

    for (var i = 0; i < source.length; i++) {
        var reward = source[i];
        if (!reward)
            continue;

        var boosterType = normalizeDailyBonusBoosterType(reward.boosterType || reward.itemId || reward.type);
        var amount = parseInt(reward.amount) || 0;

        if (!DailyBonusBoosterKeys[boosterType] || amount <= 0)
            continue;

        result.push(createDailyBonusReward(boosterType, amount));
    }

    return result;
}

function normalizeDailyBonusBoosterPool(pool) {
    var result = [];
    var source = pool || [];

    for (var i = 0; i < source.length; i++) {
        var boosterType = normalizeDailyBonusBoosterType(source[i]);
        if (DailyBonusBoosterKeys[boosterType])
            result.push(boosterType);
    }

    return result;
}

function normalizeDailyBonusString(value) {
    return value ? String(value).trim() : "";
}

function normalizeDailyBonusBoosterType(value) {
    var normalized = normalizeDailyBonusString(value).toLowerCase();

    if (normalized === "letter" || normalized === "boost_letter" || normalized === "1")
        return "Letter";

    if (normalized === "slowdown" || normalized === "slow" || normalized === "boost_slow" || normalized === "2")
        return "Slowdown";

    if (normalized === "eraser" || normalized === "boost_eraser" || normalized === "3")
        return "Eraser";

    if (normalized === "mixer" || normalized === "boost_mixer" || normalized === "4")
        return "Mixer";

    if (normalized === "swap" || normalized === "boost_swap" || normalized === "5")
        return "Swap";

    if (normalized === "hint" || normalized === "boost_hint" || normalized === "6")
        return "Hint";

    return "";
}

function normalizeDailyBonusDay(day, config) {
    var cycleLength = config && config.cycleLength ? config.cycleLength : 7;
    var value = parseInt(day) || 1;

    if (value <= 0)
        return 1;

    return ((value - 1) % cycleLength) + 1;
}

function getNextDailyBonusDay(day, config) {
    var value = normalizeDailyBonusDay(day, config);
    var cycleLength = config && config.cycleLength ? config.cycleLength : 7;
    return value >= cycleLength ? 1 : value + 1;
}

function getPreviousDailyBonusDay(day, config) {
    var value = normalizeDailyBonusDay(day, config);
    var cycleLength = config && config.cycleLength ? config.cycleLength : 7;
    return value <= 1 ? cycleLength : value - 1;
}

function getDailyBonusDateKey(date) {
    var value = date instanceof Date ? date : new Date(date);

    if (isNaN(value.getTime()))
        return "";

    var month = value.getUTCMonth() + 1;
    var day = value.getUTCDate();

    return value.getUTCFullYear() + "-" +
        (month < 10 ? "0" + month : month) + "-" +
        (day < 10 ? "0" + day : day);
}

function isDailyBonusNextUtcDay(lastClaimUtc, now) {
    if (!lastClaimUtc)
        return true;

    var lastDateKey = getDailyBonusDateKey(lastClaimUtc);
    if (!lastDateKey)
        return true;

    return lastDateKey < getDailyBonusDateKey(now);
}

function normalizeDailyBonusState(raw, config) {
    return {
        dailyRewardDay: normalizeDailyBonusDay(raw && raw.dailyRewardDay, config),
        lastClaimUtc: raw && raw.lastClaimUtc ? String(raw.lastClaimUtc) : "",
        claimAvailable: raw && (raw.claimAvailable === true || raw.claimAvailable === "true")
    };
}

function createDailyBonusInitialState() {
    return {
        dailyRewardDay: 1,
        lastClaimUtc: "",
        claimAvailable: true
    };
}

function readDailyBonusState(data, config) {
    if (!data || !data[DailyBonusStateKey] || !data[DailyBonusStateKey].Value)
        return null;

    try {
        return normalizeDailyBonusState(JSON.parse(data[DailyBonusStateKey].Value), config);
    } catch (e) {
        log.error("Failed to parse daily bonus state", { error: e });
        return null;
    }
}

function writeDailyBonusState(state) {
    var update = {};
    update[DailyBonusStateKey] = JSON.stringify(state);

    server.UpdateUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Data: update
    });
}

function writeDailyBonusDebugState(state) {
    var update = {};
    update[DailyBonusStateKey] = JSON.stringify(state);
    update[StarterBonusStateKey] = StarterBonusStateGranted;

    server.UpdateUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Data: update
    });
}

function prepareDailyBonusState(config) {
    var now = new Date();
    var result = server.GetUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Keys: [StarterBonusStateKey, DailyBonusStateKey]
    });

    var starterState = getStarterBonusState(result.Data);
    if (starterState !== StarterBonusStateGranted) {
        return {
            starterGranted: false,
            state: null
        };
    }

    var state = readDailyBonusState(result.Data, config);
    var shouldSave = false;

    if (!state) {
        state = createDailyBonusInitialState();
        shouldSave = true;
    } else if (!state.claimAvailable && isDailyBonusNextUtcDay(state.lastClaimUtc, now)) {
        state.dailyRewardDay = getNextDailyBonusDay(state.dailyRewardDay, config);
        state.claimAvailable = true;
        shouldSave = true;
    }

    if (shouldSave)
        writeDailyBonusState(state);

    return {
        starterGranted: true,
        state: state
    };
}

function createDailyBonusReward(boosterType, amount) {
    return {
        boosterType: boosterType,
        key: DailyBonusBoosterKeys[boosterType],
        amount: amount
    };
}

function getDailyBonusDayConfig(config, day) {
    var normalizedDay = normalizeDailyBonusDay(day, config);
    var days = config && config.days ? config.days : [];

    for (var i = 0; i < days.length; i++) {
        if (days[i] && days[i].day === normalizedDay)
            return days[i];
    }

    return null;
}

function pickDailyBonusSingleBooster(pool) {
    var boosters = pool && pool.length > 0 ? pool : ["Letter", "Eraser", "Slowdown", "Swap"];
    return boosters[Math.floor(Math.random() * boosters.length)];
}

function pickDailyBonusChestDrop(chestDrops) {
    if (!chestDrops || chestDrops.length === 0)
        return null;

    var totalWeight = 0;
    for (var i = 0; i < chestDrops.length; i++)
        totalWeight += chestDrops[i].weight;

    if (totalWeight <= 0)
        return null;

    var roll = Math.random() * totalWeight;
    var cursor = 0;

    for (var j = 0; j < chestDrops.length; j++) {
        cursor += chestDrops[j].weight;
        if (roll < cursor)
            return chestDrops[j];
    }

    return chestDrops[chestDrops.length - 1];
}

function rollDailyBonusChest(dayConfig) {
    var drop = pickDailyBonusChestDrop(dayConfig.chestDrops);
    if (!drop) {
        return {
            rewards: [],
            jackpot: false,
            multiplier: 1,
            selectedBooster: ""
        };
    }

    if (drop.mode === "jackpot" || drop.mode === "all") {
        return {
            rewards: drop.rewards,
            jackpot: drop.mode === "jackpot",
            multiplier: drop.multiplier,
            selectedBooster: ""
        };
    }

    var selectedBooster = pickDailyBonusSingleBooster(drop.pool);
    return {
        rewards: [createDailyBonusReward(selectedBooster, drop.multiplier)],
        jackpot: false,
        multiplier: drop.multiplier,
        selectedBooster: selectedBooster
    };
}

function rollDailyBonusReward(config, day) {
    var dayConfig = getDailyBonusDayConfig(config, day);

    if (!dayConfig) {
        return {
            rewards: [],
            jackpot: false,
            multiplier: 1,
            selectedBooster: ""
        };
    }

    if (dayConfig.rewardKind === "chest")
        return rollDailyBonusChest(dayConfig);

    return {
        rewards: dayConfig.rewards,
        jackpot: false,
        multiplier: 1,
        selectedBooster: ""
    };
}

function applyDailyBonusRewards(rewards, state, now) {
    var keys = [];
    var amountsByKey = {};
    var boosterByKey = {};

    for (var i = 0; i < rewards.length; i++) {
        var reward = rewards[i];
        if (!reward || !reward.key || reward.amount <= 0)
            continue;

        if (!amountsByKey[reward.key]) {
            amountsByKey[reward.key] = 0;
            boosterByKey[reward.key] = reward.boosterType;
            keys.push(reward.key);
        }

        amountsByKey[reward.key] += reward.amount;
    }

    var current = server.GetUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Keys: keys
    });

    var update = {};
    var totals = {};

    for (var j = 0; j < keys.length; j++) {
        var key = keys[j];
        var currentValue = 0;

        if (current.Data && current.Data[key])
            currentValue = parseInt(current.Data[key].Value) || 0;

        var total = currentValue + amountsByKey[key];
        update[key] = total.toString();
        totals[key] = total;
    }

    state.lastClaimUtc = now.toISOString();
    state.claimAvailable = false;
    update[DailyBonusStateKey] = JSON.stringify(state);

    server.UpdateUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Data: update
    });

    return totals;
}

handlers.RefreshDailyBonus = function (args, context) {
    var configResult = getDailyBonusConfigResult();
    if (!configResult.ok) {
        return JSON.stringify({
            ok: false,
            starterGranted: false,
            error: configResult.error,
            state: null,
            config: null
        });
    }

    var config = configResult.config;
    var prepared = prepareDailyBonusState(config);

    return JSON.stringify({
        ok: true,
        starterGranted: prepared.starterGranted,
        state: prepared.state,
        config: config
    });
};

handlers.ClaimDailyBonus = function (args, context) {
    var configResult = getDailyBonusConfigResult();
    if (!configResult.ok) {
        return JSON.stringify({
            ok: false,
            granted: false,
            error: configResult.error,
            state: null,
            config: null
        });
    }

    var config = configResult.config;
    var prepared = prepareDailyBonusState(config);

    if (!prepared.starterGranted) {
        return JSON.stringify({
            ok: true,
            granted: false,
            error: "starter_bonus_not_granted",
            state: null,
            config: config
        });
    }

    var state = prepared.state;
    if (!state || !state.claimAvailable) {
        return JSON.stringify({
            ok: true,
            granted: false,
            error: "claim_not_available",
            state: state,
            config: config
        });
    }

    var now = new Date();
    var day = normalizeDailyBonusDay(state.dailyRewardDay, config);
    state.dailyRewardDay = day;

    var roll = rollDailyBonusReward(config, day);
    if (!roll.rewards || roll.rewards.length === 0) {
        return JSON.stringify({
            ok: false,
            granted: false,
            error: "reward_config_missing",
            state: state,
            config: config
        });
    }

    var totals = applyDailyBonusRewards(roll.rewards, state, now);

    return JSON.stringify({
        ok: true,
        granted: true,
        day: day,
        rewards: roll.rewards,
        totals: totals,
        jackpot: roll.jackpot,
        multiplier: roll.multiplier,
        selectedBooster: roll.selectedBooster,
        state: state,
        config: config
    });
};

handlers.DebugSetDailyBonusState = function (args, context) {
    var configResult = getDailyBonusConfigResult();
    var config = configResult.ok ? configResult.config : { cycleLength: 7, days: [] };
    var mode = normalizeDailyBonusString(args && args.mode).toLowerCase();
    var requestedDay = normalizeDailyBonusDay(args && args.day, config);
    var now = new Date();
    var state = null;

    if (!mode)
        mode = "active_day";

    if (mode === "active_day") {
        state = {
            dailyRewardDay: requestedDay,
            lastClaimUtc: now.toISOString(),
            claimAvailable: true
        };
    } else if (mode === "claimed_today") {
        state = {
            dailyRewardDay: requestedDay,
            lastClaimUtc: now.toISOString(),
            claimAvailable: false
        };
    } else if (mode === "next_day_ready") {
        var yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
        state = {
            dailyRewardDay: getPreviousDailyBonusDay(requestedDay, config),
            lastClaimUtc: yesterday.toISOString(),
            claimAvailable: false
        };
    } else if (mode === "reset") {
        state = createDailyBonusInitialState();
        requestedDay = state.dailyRewardDay;
    } else {
        return JSON.stringify({
            ok: false,
            error: "unknown_daily_bonus_debug_mode",
            mode: mode,
            requestedDay: requestedDay,
            state: null
        });
    }

    writeDailyBonusDebugState(state);

    return JSON.stringify({
        ok: true,
        mode: mode,
        requestedDay: requestedDay,
        state: state
    });
};


handlers.ConsumeBooster = function (args, context) {
    const key = args.key;
    const amount = args.amount || 1;

    const getDataResult = server.GetUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Keys: [key]
    });

    let currentValue = 0;
    if (getDataResult.Data && getDataResult.Data[key]) {
        currentValue = parseInt(getDataResult.Data[key].Value);
    }

    if (currentValue < amount) {
        return { ok: false, key: key, total: currentValue };
    }

    const newValue = currentValue - amount;

    server.UpdateUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Data: { [key]: newValue.toString() }
    });

    return { ok: true, key: key, total: newValue };
};

handlers.AddBooster = function (args, context) {
    const key = args.key;
    const amount = args.amount || 1;

    const result = server.GetUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Keys: [key]
    });

    let current = 0;
    if (result.Data && result.Data[key]) {
        current = parseInt(result.Data[key].Value);
    }

    const total = current + amount;

    server.UpdateUserReadOnlyData({
        PlayFabId: currentPlayerId,
        Data: { [key]: total.toString() }
    });

    return { ok: true, key: key, total: total };
};

handlers.GrantPack = function (args, context) {
    // args: { source, productId, receiptJson, signature, currencyCode, purchasePrice, transactionId, debugSecret, sessionTicket }

    var source = args.source || "";
    var productId = args.productId;
    var transactionId = args.transactionId || "";

    // --- DEBUG gate ---
    if (source === "debug") {
        var ti = server.GetTitleInternalData({ Keys: ["debug_shop_secret", "debug_shop_allowlist"] });

        var secret = ti.Data && ti.Data.debug_shop_secret ? ti.Data.debug_shop_secret : "";
        if (!secret || args.debugSecret !== secret)
            throw "Debug purchase is disabled (bad secret)";

        // optional allowlist
        if (ti.Data && ti.Data.debug_shop_allowlist) {
            var allow = JSON.parse(ti.Data.debug_shop_allowlist);
            if (allow && allow.length > 0 && allow.indexOf(currentPlayerId) < 0)
                throw "Debug purchase not allowed for this player";
        }
    }
    else if (source === "google") {
        var receipt = JSON.parse(args.receiptJson || "{}");
        if (receipt.productId !== productId || !receipt.purchaseToken || !transactionId)
            throw "Receipt does not match the product or transaction is missing";
    }
    else {
        throw "Unknown purchase source";
    }

    // Resolve the configured payload before consuming a receipt in PlayFab.
    var rewards = null;
    if (productId !== "remove_interstitial_ads") {
        var td = server.GetTitleData({ Keys: ["shop_rewards"] });
        if (!td.Data || !td.Data.shop_rewards)
            throw "TitleData shop_rewards is missing";

        var rewardsMap = JSON.parse(td.Data.shop_rewards);
        rewards = rewardsMap[productId];
        if (!rewards)
            throw "Unknown productId: " + productId;

    }

    // Use the signed purchase token for Google; retries cannot change the grant identity.
    if (!transactionId) throw "Missing purchase transaction";
    var token = source === "google" ? "google:" + receipt.purchaseToken : "debug:" + transactionId;
    var ledgerKey = "purchase_tokens_v2";
    var ledgerData = server.GetUserReadOnlyData({ PlayFabId: currentPlayerId, Keys: [ledgerKey] });
    var ledger = ledgerData.Data && ledgerData.Data[ledgerKey]
        ? JSON.parse(ledgerData.Data[ledgerKey].Value) : [];
    for (var entry = 0; entry < ledger.length; entry++) {
        if (ledger[entry].token === token) {
            if (ledger[entry].productId !== productId) throw "Purchase token product mismatch";
            return { ok: true, duplicate: true, productId: productId };
        }
    }
    // Retain compatibility with transactions recorded by the previous revision.
    var legacy = server.GetUserInternalData({ PlayFabId: currentPlayerId, Keys: ["purchase_tokens"] });
    var used = legacy.Data && legacy.Data.purchase_tokens ? JSON.parse(legacy.Data.purchase_tokens.Value) : [];
    if (used.indexOf(transactionId) >= 0)
        return { ok: true, duplicate: true, productId: productId };

    // Legacy CloudScript exposes Server APIs only. Receipt validation is a Client
    // API, so invoke its authenticated HTTPS endpoint after checking ticket ownership.
    // Never log the ticket, request body, signature or HTTP response.
    var validationKey = "shop_validated_receipts";
    var validationData = server.GetUserInternalData({ PlayFabId: currentPlayerId, Keys: [validationKey] });
    var validated = validationData.Data && validationData.Data[validationKey]
        ? JSON.parse(validationData.Data[validationKey].Value) : [];
    var proof = validated.filter(function (item) { return item.token === token && item.productId === productId; });
    if (source === "google" && proof.length === 0) {
        if (!args.sessionTicket) throw "Purchase session is missing";
        var auth = server.AuthenticateSessionTicket({ SessionTicket: args.sessionTicket });
        if (auth.IsSessionTicketExpired || !auth.UserInfo || auth.UserInfo.PlayFabId !== currentPlayerId)
            throw "Purchase session belongs to another player or has expired";
        if (validated.length >= 50) throw "Too many pending validated purchases";
        var response = JSON.parse(http.request(
            "https://" + script.titleId + ".playfabapi.com/Client/ValidateGooglePlayPurchase",
            "post", JSON.stringify({
                ReceiptJson: args.receiptJson, Signature: args.signature,
                CurrencyCode: args.currencyCode || "USD", PurchasePrice: args.purchasePrice || 0
            }), "application/json", { "X-Authorization": args.sessionTicket }, false));
        if (response.code !== 200 || response.error)
            throw "Google receipt validation failed: " + (response.error || response.code);
        // Keep verified-but-unfulfilled receipts across a wallet write failure.
        // ReceiptAlreadyUsed alone is never accepted as proof of this player's purchase.
        validated.push({ token: token, productId: productId });
        var validationUpdate = {};
        validationUpdate[validationKey] = JSON.stringify(validated);
        server.UpdateUserInternalData({ PlayFabId: currentPlayerId, Data: validationUpdate });
    }

    // --- special: remove interstitial ads entitlement ---
    if (productId === "remove_interstitial_ads") {
        server.UpdateUserData({
            PlayFabId: currentPlayerId,
            Data: { "no_interstitial_ads": "1" }
        });

        if (source === "google") {
            var entitlementCleanup = {};
            entitlementCleanup[validationKey] = JSON.stringify(validated.filter(function (item) { return item.token !== token; }));
            server.UpdateUserInternalData({ PlayFabId: currentPlayerId, Data: entitlementCleanup });
        }

        return { ok: true, productId: productId, entitlement: "no_interstitial_ads" };
    }

    // --- apply rewards to UserReadOnlyData ---
    var keys = Object.keys(rewards);
    var cur = server.GetUserReadOnlyData({ PlayFabId: currentPlayerId, Keys: keys });

    var update = {};
    var totals = {};
    for (var i = 0; i < keys.length; i++) {
        var k = keys[i];
        var add = parseInt(rewards[k]) || 0;

        var base = 0;
        if (cur.Data && cur.Data[k]) base = parseInt(cur.Data[k].Value);

        var total = base + add;
        update[k] = total.toString();
        totals[k] = total;
    }

    // Commit balances and the consumed identity in the same write. A failed update
    // must not permanently mark an ungranted purchase as consumed.
    ledger.unshift({ token: token, productId: productId });
    if (ledger.length > 50) ledger = ledger.slice(0, 50);
    update[ledgerKey] = JSON.stringify(ledger);
    server.UpdateUserReadOnlyData({ PlayFabId: currentPlayerId, Data: update });

    if (source === "google") {
        var clearValidation = {};
        clearValidation[validationKey] = JSON.stringify(validated.filter(function (item) { return item.token !== token; }));
        server.UpdateUserInternalData({ PlayFabId: currentPlayerId, Data: clearValidation });
    }

    return { ok: true, duplicate: false, productId: productId, totals: totals };
};

//***************************************
function normalizeLanguage(language) {
    if (!language) return "ru";
    return String(language).trim().toLowerCase();
}

function normalizeWord(word) {
    if (!word) return null;

    var value = String(word).trim().toUpperCase();

    if (!value || value.length < 2 || value.length > 32)
        return null;

    if (value.indexOf(" ") >= 0)
        return null;

    return value;
}

//*************************************** NEW WORDS
var PendingWordsKey = "pending_words_";

function makeStorageKey(key, language) {
    return key + normalizeLanguage(language);
}

function loadPendingWordsCollection(language) {
    var key = makeStorageKey(PendingWordsKey, language);

    var result = server.GetTitleInternalData({
        Keys: [key]
    });

    if (!result.Data || !result.Data[key]) {
        return { words: [] };
    }

    try {
        var parsed = JSON.parse(result.Data[key]);
        if (!parsed || !parsed.words)
            return { words: [] };

        return parsed;
    } catch (e) {
        log.error("Failed to parse pending words JSON", { key: key, error: e });
        return { words: [] };
    }
}

function savePendingWordsCollection(language, collection) {
    var key = makeStorageKey(PendingWordsKey, language);

    server.SetTitleInternalData({
        Key: key,
        Value: JSON.stringify(collection)
    });
}

handlers.AddPendingWord = function (args, context) {
    var language = normalizeLanguage(args && args.language);
    var normalizedWord = normalizeWord(args && args.word);

    if (!normalizedWord) {
        return JSON.stringify({
            success: false,
            status: "Invalid",
            normalizedWord: null
        });
    }

    var collection = loadPendingWordsCollection(language);

    for (var i = 0; i < collection.words.length; i++) {
        if (collection.words[i].word === normalizedWord) {
            return JSON.stringify({
                success: true,
                status: "AlreadyExists",
                normalizedWord: normalizedWord
            });
        }
    }

    collection.words.push({
        word: normalizedWord
    });

    savePendingWordsCollection(language, collection);

    return JSON.stringify({
        success: true,
        status: "Added",
        normalizedWord: normalizedWord
    });
};

handlers.GetPendingWords = function (args, context) {
    var language = normalizeLanguage(args && args.language);
    var collection = loadPendingWordsCollection(language);

    return JSON.stringify({
        success: true,
        language: language,
        words: collection.words || []
    });
};

handlers.DeletePendingWord = function (args, context) {
    var language = normalizeLanguage(args && args.language);
    var normalizedWord = normalizeWord(args && args.word);

    if (!normalizedWord) {
        return JSON.stringify({
            success: false,
            status: "Invalid",
            normalizedWord: null
        });
    }

    var collection = loadPendingWordsCollection(language);
    var originalCount = collection.words.length;

    collection.words = collection.words.filter(function (item) {
        return item.word !== normalizedWord;
    });

    if (collection.words.length === originalCount) {
        return JSON.stringify({
            success: true,
            status: "NotFound",
            normalizedWord: normalizedWord
        });
    }

    savePendingWordsCollection(language, collection);

    return JSON.stringify({
        success: true,
        status: "Deleted",
        normalizedWord: normalizedWord
    });
};

handlers.ClearPendingWords = function(args, context) {

    var language = normalizeLanguage(args && args.language);
    var key = makeStorageKey(PendingWordsKey, language);

    server.SetTitleInternalData({
        Key: key,
        Value: JSON.stringify({ words: [] })
    });

    return JSON.stringify({
        success: true
    });
};

//*************************************** REPORT WORDS
var ReportWordsKey = "report_words_";

function loadReportWordsCollection(language) {
    var key = makeStorageKey(ReportWordsKey, language);

    var result = server.GetTitleInternalData({
        Keys: [key]
    });

    if (!result.Data || !result.Data[key]) {
        return { words: [] };
    }

    try {
        var parsed = JSON.parse(result.Data[key]);
        if (!parsed || !parsed.words)
            return { words: [] };

        return parsed;
    } catch (e) {
        log.error("Failed to parse pending words JSON", { key: key, error: e });
        return { words: [] };
    }
}

function saveReportWordsCollection(language, collection) {
    var key = makeStorageKey(ReportWordsKey, language);

    server.SetTitleInternalData({
        Key: key,
        Value: JSON.stringify(collection)
    });
}

handlers.AddReportWord = function (args, context) {
    var language = normalizeLanguage(args && args.language);
    var reason = args.reason;
    var normalizedWord = normalizeWord(args && args.word);

    if (!normalizedWord) {
        return JSON.stringify({
            success: false,
            status: "Invalid",
            normalizedWord: null
        });
    }

    var collection = loadReportWordsCollection(language);

    for (var i = 0; i < collection.words.length; i++) {
        if (collection.words[i].word === normalizedWord) {
            return JSON.stringify({
                success: true,
                status: "AlreadyExists",
                normalizedWord: normalizedWord
            });
        }
    }

    collection.words.push({
        word: normalizedWord,
        reason: reason
    });

    saveReportWordsCollection(language, collection);

    return JSON.stringify({
        success: true,
        status: "Added",
        normalizedWord: normalizedWord
    });
};

handlers.GetReportWords = function (args, context) {
    var language = normalizeLanguage(args && args.language);
    var collection = loadReportWordsCollection(language);

    return JSON.stringify({
        success: true,
        language: language,
        words: collection.words || []
    });
};

handlers.DeleteReportWord = function (args, context) {
    var language = normalizeLanguage(args && args.language);
    var normalizedWord = normalizeWord(args && args.word);

    if (!normalizedWord) {
        return JSON.stringify({
            success: false,
            status: "Invalid",
            normalizedWord: null
        });
    }

    var collection = loadReportWordsCollection(language);
    var originalCount = collection.words.length;

    collection.words = collection.words.filter(function (item) {
        return item.word !== normalizedWord;
    });

    if (collection.words.length === originalCount) {
        return JSON.stringify({
            success: true,
            status: "NotFound",
            normalizedWord: normalizedWord
        });
    }

    saveReportWordsCollection(language, collection);

    return JSON.stringify({
        success: true,
        status: "Deleted",
        normalizedWord: normalizedWord
    });
};

handlers.ClearReportWords = function(args, context) {

    var language = normalizeLanguage(args && args.language);
    var key = makeStorageKey(ReportWordsKey, language);

    server.SetTitleInternalData({
        Key: key,
        Value: JSON.stringify({ words: [] })
    });

    return JSON.stringify({
        success: true
    });
};
