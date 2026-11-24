import assert from "node:assert/strict";
import { createRequire } from "node:module";

export async function browserScenario(web, manager, cashier) {
  const { chromium } = createRequire(import.meta.url)("playwright");
  const browser = await chromium.launch({ executablePath: process.env.ORDER_TEST_BROWSER });
  try {
    async function pageFor(actor, viewport) {
      const context = await browser.newContext({ viewport });
      await context.addCookies([{ name: "salon_session", value: actor.Cookie.split("=")[1], url: web,
        secure: true, httpOnly: true, sameSite: "Strict" }]);
      const page = await context.newPage();
      await page.goto(web);
      await page.getByRole("heading", { name: "Comandas abertas" }).waitFor();
      return page;
    }
    const desktop = await pageFor(manager, { width: 1366, height: 768 });
    const phone = await pageFor(cashier, { width: 390, height: 844 });
    const switchTo = async (page, name) => {
      await page.locator(".open-orders button").filter({ hasText: name }).click();
      await page.locator(".client-name").filter({ hasText: name }).waitFor();
    };
    const focus = page => page.evaluate(() => window.dispatchEvent(new Event("focus")));
    const create = async (page, name) => {
      await page.locator("input[name=clientName]").fill(name);
      await page.getByRole("button", { name: "Iniciar comanda" }).click();
      await page.getByText(name, { exact: true }).waitFor();
    };
    const add = async (page, service) => {
      await page.locator("select[name=serviceId]").selectOption({ label: await page.locator("select[name=serviceId] option").filter({ hasText: service }).innerText() });
      await page.locator("select[name=collaboratorId]").selectOption({ index: 1 });
      await page.getByRole("button", { name: "Adicionar serviço" }).click();
      await page.locator(".items-list").getByText(service, { exact: true }).waitFor();
    };
    const pay = async page => {
      await page.locator("select[name=paymentMethodId]").selectOption({ label: "Pix" });
      await page.getByRole("button", { name: /Registrar .* e finalizar/ }).click();
      await page.locator(".paid-badge").waitFor();
    };
    await desktop.getByRole("button", { name: "Abrir caixa" }).click();
    await create(desktop, "Cliente Desktop");
    await focus(phone);
    await switchTo(phone, "Cliente Desktop");
    await add(phone, "Corte");
    // No forced refresh: the desktop must receive the change from the periodic read.
    await desktop.locator(".items-list").getByText("Corte").waitFor({ timeout: 30000 });
    await create(phone, "Cliente Celular");
    await add(phone, "Escova");
    await focus(desktop);
    await switchTo(desktop, "Cliente Celular");
    await desktop.locator(".items-list").getByText("Escova").waitFor();
    await switchTo(desktop, "Cliente Desktop");
    assert.equal(await desktop.locator(".order-item").count(), 1);
    await desktop.locator("[name=countedCash]").fill("0");
    await desktop.getByRole("button", { name: "Conferir e fechar caixa" }).click();
    await desktop.getByText(/Fechamento recusado/).waitFor();
    await desktop.getByRole("link", { name: /Ver comandas pendentes/ }).click();
    await pay(desktop);
    await focus(phone);
    await phone.locator(".open-orders button").filter({ hasText: "Cliente Desktop" }).waitFor({ state: "detached" });
    await switchTo(desktop, "Cliente Celular");
    await pay(phone);
    await focus(desktop);
    await desktop.locator(".paid-badge").waitFor();
    assert.equal(await desktop.locator("select[name=paymentMethodId]").count(), 0);
    await desktop.getByRole("button", { name: "Conferir e fechar caixa" }).click();
    await desktop.getByRole("heading", { name: "Caixa fechado" }).waitFor();
  } finally {
    await browser.close();
  }
}
