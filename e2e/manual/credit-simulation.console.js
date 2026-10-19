// Credit (spec 002) end-to-end simulation against a deployed environment.
//
// Paste into the DevTools console of the Utilities console, on a project's
// Subscriptions > Simulation page (it reuses the page's cookie + x-blocks-key).
//   1. await credsim.setup('<organizationId>')  -> creates 3 plans, subscribes, prints the checkout URL
//   2. pay the checkout (Stripe test card 4242 4242 4242 4242)
//   3. await credsim.run()                       -> runs every scenario and prints PASS/FAIL per check
// The organization must have no live org-wide subscription. Requires SubscriptionSimulation:Enabled.
// ponytail: plain console script, not Playwright, because step 2 is a real hosted checkout.
(() => {
  const api = async (method, path, body) => {
    const r = await fetch(path, {
      method, credentials: "include",
      headers: { accept: "application/json", "content-type": "application/json", "x-blocks-key": window.__BLOCKS_ENV__.BLOCKS_X_BLOCKS_KEY },
      body: body ? JSON.stringify(body) : undefined,
    });
    let j = null; try { j = await r.json(); } catch {}
    return { s: r.status, d: j?.data, e: j?.error?.code };
  };
  const S = JSON.parse(localStorage.getItem("__credsim") || "{}");
  const save = () => localStorage.setItem("__credsim", JSON.stringify(S));
  const results = [];
  const check = (name, ok, got) => { results.push({ check: name, result: ok ? "PASS" : "FAIL", got: JSON.stringify(got) }); };
  const q = () => `organizationId=${S.org}`;
  const key = () => crypto.randomUUID();
  const P = { Periodic: 0, Never: 1, CarryForward: 2 };
  const topUp = (cents, policy) => ({ enabled: true, unitPrices: [{ currencyCode: "USD", unitAmountMinor: cents }], minQuantity: 1, maxQuantity: 10000, expiryPolicy: P[policy] });
  // msg: overage allowed, Periodic credits. img: no overage, CarryForward. doc: no overage, Never (absent on lite).
  const meters = (msg, img, doc) => [
    { meterKey: "msg", displayName: "Messages", unitLabel: "message", includedQuantity: msg, overageAllowed: true, rateTables: [{ currencyCode: "USD", tiers: [{ upToQuantity: null, unitAmountMinor: 2 }] }], topUp: topUp(1, "Periodic") },
    { meterKey: "img", displayName: "Images", unitLabel: "image", includedQuantity: img, overageAllowed: false, rateTables: [], topUp: topUp(5, "CarryForward") },
    ...(doc == null ? [] : [{ meterKey: "doc", displayName: "Documents", unitLabel: "document", includedQuantity: doc, overageAllowed: false, rateTables: [], topUp: topUp(10, "Never") }]),
  ];

  const usage = async (meterKey) => (await api("GET", `/api/subscription-usage/current?${q()}`)).d.find((m) => m.meterKey === meterKey && !m.userId);
  const grants = async (meterKey) => (await api("GET", `/api/subscriptions/${S.sub}/credits?${q()}`)).d.filter((g) => g.meterKey === meterKey);
  const live = async (meterKey) => (await grants(meterKey)).filter((g) => g.status === "Active").reduce((n, g) => n + g.remaining, 0);
  const use = (meterKey, quantity, enforce = true) => api("POST", "/api/subscription-usage", { organizationId: S.org, meterKey, quantity, enforce, idempotencyKey: key() });
  const buy = (meterKey, quantity, idempotencyKey = key()) => api("POST", `/api/subscriptions/${S.sub}/credits`, { organizationId: S.org, meterKey, quantity, idempotencyKey });
  const changePlan = (k) => api("PUT", `/api/subscriptions/${S.sub}/plan`, { planCode: S.plans[k].code, priceId: S.plans[k].priceId, organizationId: S.org });
  const harness = (action, body = {}) => api("POST", `/api/subscription-simulation/subscriptions/${S.sub}/${action}`, { organizationId: S.org, ...body });

  async function setup(organizationId) {
    Object.assign(S, { org: organizationId, suffix: Date.now().toString(36), plans: {} });
    const defs = { lite: [1, 500, meters(50, 5, null)], basic: [2, 1000, meters(100, 10, 5)], pro: [3, 3000, meters(500, 50, 20)] };
    for (const [k, [rank, amount, m]] of Object.entries(defs)) {
      const code = `credsim_${k}_${S.suffix}`;
      const plan = await api("POST", "/api/subscription-plans", { code, displayName: `CreditSim ${k}`, subscriberScope: 0, usageInterval: 2, usageIntervalCount: 1, familyCode: `credsim_${S.suffix}`, familyRank: rank, trialRequiresPaymentMethod: false, requirePaymentMethodUpfront: true, meters: m });
      if (plan.s !== 200) throw new Error(`plan ${code}: ${plan.s} ${plan.e}`);
      await api("POST", "/api/subscription-plans/prices", { planId: plan.d.planId, currencyCode: "USD", unitAmountMinor: amount, interval: 2, intervalCount: 1, billingAlignment: "Anniversary" });
      const stored = (await api("GET", "/api/subscription-plans")).d.find((p) => p.planId === plan.d.planId);
      S.plans[k] = { code, planId: plan.d.planId, priceId: stored.prices[0].priceId };
    }
    const sub = await api("POST", "/api/subscriptions", { planCode: S.plans.basic.code, priceId: S.plans.basic.priceId, organizationId: S.org, timeZoneId: "UTC" });
    if (sub.s !== 200) throw new Error(`subscribe: ${sub.s} ${sub.e}`);
    S.sub = sub.d.subscriptionId; save();
    console.log("Pay here, then run credsim.run():\n" + (sub.d.checkoutUrl || sub.d.pendingCheckout?.checkoutUrl));
    return S;
  }

  async function run() {
    results.length = 0;
    const cur = (await api("GET", `/api/subscriptions/current?${q()}`)).d;
    check("subscription active after checkout", cur?.status === "Active", cur?.status);

    // Purchases, validation and idempotency.
    check("quantity above max refused", (await api("POST", `/api/subscriptions/${S.sub}/credits/preview`, { organizationId: S.org, meterKey: "msg", quantity: 20000 })).e === "subscription_credits_quantity_out_of_range");
    check("purchase without idempotency key refused", (await api("POST", `/api/subscriptions/${S.sub}/credits`, { organizationId: S.org, meterKey: "msg", quantity: 1 })).e === "subscription_credits_idempotency_key_required");
    const k = key(); const first = await buy("msg", 200, k); const again = await buy("msg", 200, k);
    check("purchase charges and grants", first.s === 200 && first.d.status === "Finalized", first.d?.status ?? first.e);
    check("same key replays the same payment", again.d?.paymentId === first.d?.paymentId, [first.d?.paymentId, again.d?.paymentId]);
    check("same key, different request refused", (await buy("msg", 300, k)).e === "subscription_credits_idempotency_conflict");
    await buy("img", 20); await buy("doc", 10);

    // Preference: plan allowance first, then credits, then overage / refusal.
    let u = await use("msg", 80);   check("inside allowance draws no credits", u.d?.purchasedConsumed === 0, u.d);
    u = await use("msg", 50);       check("past allowance draws credits (30)", u.d?.purchasedConsumed === 30, u.d);
    u = await use("msg", 170);      check("credits exhausted exactly (200)", u.d?.purchasedConsumed === 200 && u.d?.overage === 0, u.d);
    u = await use("msg", 25);       check("overage-allowed meter goes to overage", u.d?.allowed && u.d?.overage === 25, u.d);
    u = await use("img", 25);       check("no-overage meter: 10 allowance + 15 credits", u.d?.allowed && u.d?.purchasedConsumed === 15, u.d);
    u = await use("img", 10);       check("no-overage meter refuses whole when credits short", u.d?.allowed === false && (await live("img")) === 5, u.d);
    const userRow = (await api("GET", `/api/subscription-usage/current?${q()}`)).d.find((m) => m.meterKey === "img" && m.userId);
    check("per-user row unaffected by a refusal", userRow?.used === 25, userRow?.used);
    u = await use("doc", 8);        check("Never credits: 5 allowance + 3 credits", u.d?.purchasedConsumed === 3, u.d);

    // Expiry options.
    const msgGrant = (await grants("msg"))[0];
    check("Periodic credits expire at the usage window end", msgGrant.expiresAtUtc === (await usage("msg")).periodEndUtc, msgGrant.expiresAtUtc);
    // 50c is at Stripe's minimum: Stripe would move it to the customer balance instead of charging (#696).
    const small = await buy("img", 10);
    check("a 50c credit purchase is refused before charging", small.e === "subscription_credits_below_minimum_charge", small.e ?? small.d?.status);
    await buy("img", 20); // 100c, chargeable
    await api("POST", `/api/subscriptions/${S.sub}/credits/grants`, { organizationId: S.org, meterKey: "msg", quantity: 30, reason: "credsim free grant" });
    const adv = await harness("advance-renewal", { paymentOutcome: "Succeeded" });
    check("renewal advances", adv.s === 200, adv.e);
    check("CarryForward survives renewal (5 left + 20 bought)", (await live("img")) === 25);
    check("Never survives renewal", (await live("doc")) === 7);
    const close = await harness("close-usage-period", { paymentOutcome: "Succeeded", chargeInvoice: true });
    const inv = (await api("GET", `/api/subscription-simulation/subscriptions/${S.sub}/state?${q()}`)).d.usageInvoices.find((i) => i.totalAmountMinor > 0);
    check("only overage is billed (25 x 2c), not credit-covered use", close.s === 200 && inv?.totalAmountMinor === 50, inv?.totalAmountMinor);

    // Upgrade: CarryForward forfeited, Never kept.
    const up = await changePlan("pro");
    check("upgrade applies", up.s === 200, up.e);
    check("upgrade forfeits CarryForward credits", (await live("img")) === 0);
    check("upgrade keeps Never credits", (await live("doc")) === 7);
    u = await use("doc", 25); check("new allowance first, then Never credits", u.d?.purchasedConsumed === 5, u.d);

    // Downgrade to a plan without `doc`, applied at renewal.
    const down = await changePlan("lite");
    check("downgrade is scheduled", down.s === 200 && (await api("GET", `/api/subscriptions/current?${q()}`)).d.pendingPlanChange?.targetPlanCode === S.plans.lite.code);
    const pv = await api("POST", `/api/subscriptions/${S.sub}/credits/preview`, { organizationId: S.org, meterKey: "doc", quantity: 10 }); // 100c, above Stripe's floor so the minimum rule can't refuse it
    check("buying credits for a meter the pending plan drops is refused", pv.s !== 200, pv.e ?? pv.s);
    await harness("advance-renewal", { paymentOutcome: "Succeeded" });
    check("downgrade forfeits Never credits of a dropped meter", (await grants("doc")).every((g) => g.status !== "Active"));

    // Cancellation.
    await api("DELETE", `/api/subscriptions/${S.sub}?immediately=false&${q()}`);
    check("no purchases while cancellation scheduled", (await api("POST", `/api/subscriptions/${S.sub}/credits/preview`, { organizationId: S.org, meterKey: "msg", quantity: 1 })).e === "subscription_credits_cancellation_scheduled");
    await api("DELETE", `/api/subscriptions/${S.sub}/cancellation?${q()}`);
    await api("DELETE", `/api/subscriptions/${S.sub}?immediately=true&${q()}`);
    check("cancel forfeits every remaining credit", (await api("GET", `/api/subscriptions/${S.sub}/credits?${q()}`)).d.every((g) => g.status !== "Active"));
    check("no purchases after cancel", (await api("POST", `/api/subscriptions/${S.sub}/credits/preview`, { organizationId: S.org, meterKey: "msg", quantity: 1 })).e === "subscription_credits_not_allowed");
    check("no credit purchase left Pending", (await api("GET", `/api/subscriptions/${S.sub}/credits/purchases?${q()}`)).d.every((p) => p.status !== "Pending"));

    // One line per check, so the outcome survives tools that only show "Array(n)" for a table.
    console.log(results.map((r) => `${r.result} ${r.check}${r.result === "FAIL" ? ` (got ${r.got})` : ""}`).join("\n"));
    return results;
  }

  // run() takes ~2 minutes; read credsim.results afterwards instead of awaiting it from a tool.
  window.credsim = { setup, run, state: S, results };
})();
