import assert from "node:assert/strict";
import test from "node:test";
import { createFixture } from "./fixture.mjs";

test("Shared orders through the Next.js BFF and PostgreSQL", async t => {
  const { web, password, sql } = await createFixture(t);
  async function login(name) {
    const response = await fetch(web + "/api/auth/login", {
      method: "POST", headers: { Origin: web, "Content-Type": "application/json" },
      body: JSON.stringify({ email: name + "@example.test", password }),
    });
    assert.equal(response.status, 200, "login " + name);
    const session = await response.json();
    assert.equal(session.token, undefined);
    const cookie = response.headers.get("set-cookie");
    for (const flag of [/HttpOnly/i, /Secure/i, /SameSite=strict/i]) assert.match(cookie, flag);
    return { Cookie: cookie.split(";")[0], Origin: web, "X-CSRF-Token": session.csrfToken };
  }

  const manager = await login("manager");
  const cashier = await login("cashier");
  const other = await login("other");
  const employee = await login("employee");
  async function request(actor, path, body, expected = 200) {
    const res = await fetch(web + "/api/bff/" + path, {
      headers: { ...actor, "Content-Type": "application/json" },
      method: body === undefined ? "GET" : "POST", body: JSON.stringify(body),
    });
    const data = await res.text();
    assert.ok([expected].flat().includes(res.status), path + " HTTP " + res.status + " " + data);
    return { status: res.status, data: data ? JSON.parse(data) : null };
  }
  const get = async (actor, path) => (await request(actor, path)).data;
  const list = actor => get(actor, "orders?status=open");
  const session = (await request(manager, "cash-sessions", { openingCash: 10 }, 201)).data;
  const closeCash = status => request(cashier, "cash-sessions/" + session.id + "/close", { countedCash: 10 }, status);
  const services = await get(cashier, "services");
  const collaboratorId = (await get(cashier, "collaborators"))[0].id;
  const paymentMethodId = (await get(cashier, "payment-methods")).find(m => m.name === "Pix").id;
  const item = (serviceName, extra) => ({ serviceId: services.find(s => s.name === serviceName).id, collaboratorId, ...extra });
  const pay = (actor, order, status) => request(actor, "orders/" + order.id + "/payments", {
    paymentMethodId, expectedOrder: { totalValue: order.totalValue, itemCount: order.items.length },
  }, status);
  const create = async (actor, clientName) => (await request(actor, "orders", { clientName }, 201)).data;
  const [a, b, c] = await Promise.all([create(manager, "Ana"), create(cashier, "Bia"), create(manager, "Clara")]);
  const details = (actor, order) => get(actor, "orders/" + order.id);
  const add = (actor, order, service, expected = 200) => request(actor, "orders/" + order.id + "/items", service, expected);

  await t.test("three clients share one cash session across two operators", async () => {
    const summaries = await list(cashier);
    assert.deepEqual(summaries, await list(manager));
    assert.deepEqual(summaries.map(o => o.clientName).sort(), ["Ana", "Bia", "Clara"]);
    for (const order of summaries) {
      assert.equal(order.cashSessionId, session.id);
      assert.equal(order.status, "Open");
      assert.equal(order.totalValue, 0);
      assert.ok(order.openedAt);
    }
    await closeCash(409);
  });

  await t.test("tenant and role isolation, CSRF and supported filter", async () => {
    await request({}, "orders?status=open", undefined, 401);
    await request(employee, "orders?status=open", undefined, 403);
    await request(manager, "orders?status=closed", undefined, 400);
    assert.deepEqual(await get(other, "orders?status=open&company_id=" + session.companyId), []);
    await request(other, "orders/" + a.id, undefined, 404);
    await add(other, a, item("Corte"), 404);
    await pay(other, a, 404);
    await request({ ...cashier, "X-CSRF-Token": "" }, "orders", { clientName: "Forbidden" }, 403);
    await request({ ...cashier, Origin: "https://invalid.example" }, "orders", { clientName: "Forbidden" }, 403);
  });

  await t.test("concurrent additions survive switching orders and stale payment is rejected", async () => {
    await Promise.all([add(manager, a, item("Corte")), add(cashier, a, item("Escova")),
      add(manager, b, item("Escova")), add(cashier, c, item("Tratamento"))]);
    for (const [order, amount] of [[a, 71], [b, 40], [c, 55], [a, 71]]) {
      const detail = await details(cashier, order);
      assert.equal(detail.totalValue, amount);
      assert.equal(detail.clientName, order.clientName);
      assert.equal(detail.items.reduce((sum, i) => sum + i.unitPrice, 0), amount);
    }
    const reviewed = await details(manager, a);
    assert.deepEqual(reviewed.items.map(i => i.serviceName).sort(), ["Corte", "Escova"]);
    await add(cashier, a, item("Corte", { priceOverride: 0, adjustmentReason: "Cortesia teste" }));
    await pay(manager, reviewed, 409); // Same price, different set of services.
    await pay(cashier, b, 409); // Price changed since opening.
    const updated = await details(manager, a);
    assert.equal(updated.items.length, 3);
    assert.equal(updated.items[2].adjustmentReason, "Cortesia teste");
    assert.equal(updated.items[2].catalogPrice, 31);
    assert.deepEqual(updated.payments, []);
  });

  await t.test("simultaneous payments close only one order and produce a single receipt", async () => {
    const reviewed = await details(cashier, a);
    const attempts = await Promise.all([pay(manager, reviewed, [200,409]), pay(cashier, reviewed, [200,409])]);
    assert.deepEqual(attempts.map(r => r.status).sort(), [200,409]);
    await add(cashier, a, item("Corte"), 409);
    await pay(manager, reviewed, 409);
    const closed = await details(cashier, a);
    assert.equal(closed.status, "Closed");
    assert.deepEqual(closed.payments.map(p => p.amount), [71]);
    assert.deepEqual((await list(manager)).map(o => o.id).sort(), [b.id,c.id].sort());
    await closeCash(409);
  });

  await t.test("a service racing payment is either included with review or rejected", async () => {
    const reviewed = await details(manager, b);
    const [addition, payment] = await Promise.all([
      add(cashier, b, item("Corte"), [200,409]), pay(manager, reviewed, [200,409]),
    ]);
    assert.deepEqual([addition.status, payment.status].sort(), [200,409]);
    if (addition.status === 200) {
      const latest = await details(manager, b);
      assert.equal(latest.totalValue, 71);
      await pay(manager, latest, 200);
    }
    const paid = await details(cashier, b);
    assert.deepEqual(paid.payments.map(p => p.amount), [addition.status === 200 ? 71 : 40]);
    await pay(cashier, await details(cashier, c), 200);
  });

  await t.test("historical commission, rounding, reconciliation and closing cash", async () => {
    sql("UPDATE servicos SET porcentagem=10"); // Fixture catalog after the services have been recorded.
    const orders = await Promise.all([a,b,c].map(order => details(manager, order)));
    const [year, month, day] = orders[0].closedAt.slice(0,10).split("-").map(Number);
    const preview = await get(manager, "collaborators/" + collaboratorId + "/commissions?year=" + year + "&month=" + month + "&half=" + (day <= 15 ? 1 : 2));
    const amounts = orders.flatMap(o => o.items.map(i => Math.floor((i.unitPrice * 50 + 50) / 100)));
    assert.deepEqual(preview.items.map(i => i.commissionAmount).sort(), amounts.sort());
    assert.ok(preview.items.every(i => i.commissionPercentage === 50));
    assert.equal(preview.totalAmount, amounts.reduce((sum, x) => sum+x, 0));
    assert.deepEqual(await list(cashier), []);
    await closeCash(200);
    const totals = await get(manager, "cash-sessions/" + session.id + "/reconciliation");
    assert.equal(totals.pixSales, orders.reduce((sum, o) => sum + o.totalValue, 0));
    assert.equal(totals.difference, 0);
  });

  if (process.env.ORDER_TEST_BROWSER) {
    const { browserScenario } = await import("./browser.mjs");
    await t.test("desktop and phone views switch, synchronize and close orders", () => browserScenario(web, manager, cashier));
  }
});
