"use client";

import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";

type Role = "Manager" | "Cashier" | "Employee";
type User = { id: number; name: string; email: string; role: Role; companyId: number; employeeId: number | null };
type CashSession = { id: number; openedOn: string; closedOn: string | null; openingCash: number; closingCash: number | null; status: string };
type Collaborator = { id: number; name: string; cpf?: string | null; email?: string | null };
type Service = { id: number; name: string; description: string; price: number; commissionPercentage: number };
type OrderItem = { itemId: number; serviceName: string; collaboratorName: string; unitPrice: number; catalogPrice: number };
type Order = { id: number; clientName: string; status: string; totalValue: number; items: OrderItem[]; payments: Array<{ id: number; amount: number; paymentMethod: string; paymentMethodId: number }>; closedAt?: string | null };
type PaymentMethod = { id: number; name: string; requiresManagerAuthorization: boolean };
type CommissionItem = { serviceOrderItemId: number; orderId: number; clientName: string | null; serviceName: string; orderClosedAt: string; chargedAmount: number; commissionPercentage: number; commissionAmount: number };
type CommissionPreview = { collaboratorId: number; collaboratorName: string; period: { year: number; month: number; half: number; startDate: string; endDate: string }; itemCount: number; totalAmount: number; items: CommissionItem[] };
type EmployeeCommission = { collaboratorId: number; collaboratorName: string; period: CommissionPreview["period"]; itemCount: number; totalCommission: number; paidCommission: number; unpaidCommission: number; items: Array<CommissionItem & { isPaid: boolean; payoutId: number | null; paymentDate: string | null; paymentMethod: string | null }> };
type Reconciliation = { status: string; openingCash: number; cashSales: number; cashIn: number; cashOut: number; pixSales: number; debitCardSales: number; creditCardSales: number; fiadoDebt: number; expectedCash: number; countedCash: number | null; difference: number | null; isBalanced: boolean | null };
type ApiResponse<T> = { data?: T; error?: string; status?: number; networkError?: boolean };

const money = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const dateTime = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short", timeZone: "America/Bahia" });
const dateOnly = new Intl.DateTimeFormat("pt-BR", { dateStyle: "long", timeZone: "America/Bahia" });
const roleName: Record<Role, string> = { Manager: "Gerente", Cashier: "Caixa", Employee: "Funcionário" };

async function api<T>(path: string, method = "GET", body?: unknown, csrfToken?: string): Promise<ApiResponse<T>> {
  try {
    const response = await fetch(`/api/bff/${path}`, {
      method,
      cache: "no-store",
      headers: { ...(body === undefined ? {} : { "Content-Type": "application/json" }), ...(method === "GET" ? {} : { "X-CSRF-Token": csrfToken ?? "" }) },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    const data = await response.json().catch(() => undefined);
    if (!response.ok) {
      if (response.status === 401 && typeof window !== "undefined") window.dispatchEvent(new Event("salon-session-expired"));
      const firstValidation = Object.values(data?.errors ?? {}).flat().find(Boolean);
      return { error: data?.error ?? firstValidation ?? data?.title ?? "A solicitação não foi concluída.", status: response.status };
    }
    return { data: data as T, status: response.status };
  } catch {
    return { error: "A conexão caiu. Consulte o estado na API antes de tentar novamente.", networkError: true };
  }
}

function parseBusinessTimestamp(value: string) {
  if (/Z$|[+-]\d{2}:?\d{2}$/.test(value)) return new Date(value);
  return new Date(`${value}-03:00`);
}

function parseBusinessDate(value: string) {
  return new Date(`${value}T12:00:00Z`);
}

function localToday() {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Bahia", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(new Date());
  const pick = (type: string) => parts.find((part) => part.type === type)?.value ?? "";
  return `${pick("year")}-${pick("month")}-${pick("day")}`;
}

export function SalonApp() {
  const [user, setUser] = useState<User | null>(null);
  const [csrfToken, setCsrfToken] = useState("");
  const [checkingSession, setCheckingSession] = useState(true);
  const [authMessage, setAuthMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ kind: "success" | "error" | "warning"; text: string } | null>(null);
  const [activeView, setActiveView] = useState("operacao");
  const [cashSession, setCashSession] = useState<CashSession | null>(null);
  const [services, setServices] = useState<Service[]>([]);
  const [collaborators, setCollaborators] = useState<Collaborator[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([]);
  const [order, setOrder] = useState<Order | null>(null);
  const [reconciliation, setReconciliation] = useState<Reconciliation | null>(null);
  const [commission, setCommission] = useState<CommissionPreview | null>(null);
  const [employeeCommission, setEmployeeCommission] = useState<EmployeeCommission | null>(null);
  const [selectedCollaboratorId, setSelectedCollaboratorId] = useState("");
  const [period, setPeriod] = useState(() => {
    const today = localToday();
    const [year, month, day] = today.split("-").map(Number);
    return { year, month, half: day <= 15 ? 1 : 2 };
  });
  const [pendingPayoutCheck, setPendingPayoutCheck] = useState<CommissionItem[] | null>(null);
  const [pendingPaymentOrderId, setPendingPaymentOrderId] = useState<number | null>(null);

  const notify = (kind: "success" | "error" | "warning", text: string) => setNotice({ kind, text });

  const loadSession = useCallback(async () => {
    setCheckingSession(true);
    try {
      const response = await fetch("/api/auth/session", { cache: "no-store" });
      const result = await response.json().catch(() => null);
      if (response.ok && result?.user) {
        setUser(result.user as User);
        setCsrfToken(result.csrfToken ?? "");
        setActiveView(result.user.role === "Employee" ? "comissoes" : "operacao");
      } else {
        setUser(null);
        setCsrfToken("");
      }
    } catch {
      setUser(null);
      setCsrfToken("");
      setAuthMessage("Não foi possível verificar sua sessão. Confira se a interface está em execução e tente entrar novamente.");
    }
    setCheckingSession(false);
  }, []);

  useEffect(() => { void loadSession(); }, [loadSession]);

  useEffect(() => {
    const expired = () => {
      setUser(null);
      setCsrfToken("");
      setAuthMessage("Sua sessão expirou. Entre novamente para continuar.");
    };
    window.addEventListener("salon-session-expired", expired);
    return () => window.removeEventListener("salon-session-expired", expired);
  }, []);

  const refreshOperationalData = useCallback(async () => {
    if (!user || user.role === "Employee") return;
    const [session, serviceList, collaboratorList, methods] = await Promise.all([
      api<CashSession>("cash-sessions/current"),
      api<Service[]>("services"),
      api<Collaborator[]>("collaborators"),
      api<PaymentMethod[]>("payment-methods"),
    ]);
    setCashSession(session.data ?? null);
    setServices(serviceList.data ?? []);
    setCollaborators(collaboratorList.data ?? []);
    setPaymentMethods(methods.data ?? []);
    if (session.status !== 404 && session.error) notify("error", session.error);
    if (collaboratorList.data?.length && !selectedCollaboratorId) setSelectedCollaboratorId(String(collaboratorList.data[0].id));
  }, [user, selectedCollaboratorId]);

  useEffect(() => { void refreshOperationalData(); }, [refreshOperationalData]);

  useEffect(() => {
    if (!user || user.role === "Employee") return;
    try {
      const saved = JSON.parse(sessionStorage.getItem("salon_active_order") ?? "null") as { userId?: number; orderId?: number } | null;
      if (!saved || saved.userId !== user.id || !saved.orderId) return;
      void api<Order>(`orders/${saved.orderId}`).then((response) => {
        if (response.data) setOrder(response.data);
        else sessionStorage.removeItem("salon_active_order");
      });
    } catch {
      sessionStorage.removeItem("salon_active_order");
    }
  }, [user]);

  const signOut = async () => {
    await fetch("/api/auth/logout", { method: "POST", headers: { "X-CSRF-Token": csrfToken } });
    setUser(null); setOrder(null); setCashSession(null); setNotice(null); setAuthMessage("");
    sessionStorage.removeItem("salon_active_order");
  };

  const afterMutation = <T,>(response: ApiResponse<T>, success: string, onSuccess?: (data: T) => void) => {
    if (response.data !== undefined) {
      onSuccess?.(response.data);
      notify("success", success);
      return true;
    }
    if (response.status === 401) {
      void loadSession();
      notify("warning", "Sua sessão expirou. Entre novamente; confira o resultado da operação antes de repeti-la.");
    } else {
      notify(response.networkError ? "warning" : "error", response.error ?? "A solicitação não foi concluída.");
    }
    return false;
  };

  const openCashSession = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true);
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const response = await api<CashSession>("cash-sessions", "POST", { openingCash: Number(form.get("openingCash")) }, csrfToken);
    if (afterMutation(response, "Caixa aberto com sucesso.", setCashSession)) formElement.reset();
    setBusy(false);
  };

  const openOrder = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true);
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const response = await api<Order>("orders", "POST", { clientName: String(form.get("clientName") ?? "").trim() }, csrfToken);
    if (afterMutation(response, `Comanda #${response.data?.id ?? ""} aberta.`, (created) => {
      setOrder(created);
      sessionStorage.setItem("salon_active_order", JSON.stringify({ userId: user?.id, orderId: created.id }));
    })) formElement.reset();
    setBusy(false);
  };

  const addItem = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!order) return;
    setBusy(true);
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const override = String(form.get("priceOverride") ?? "").trim();
    const reason = String(form.get("adjustmentReason") ?? "").trim();
    if ((override && !reason) || (!override && reason)) {
      notify("error", "Informe o motivo junto com o novo preço; deixe os dois vazios para usar o preço do catálogo.");
      setBusy(false); return;
    }
    const body: Record<string, unknown> = {
      serviceId: Number(form.get("serviceId")), collaboratorId: Number(form.get("collaboratorId")),
      observation: String(form.get("observation") ?? "").trim() || null,
    };
    if (override) { body.priceOverride = Number(override); body.adjustmentReason = reason; }
    const response = await api<Order>(`orders/${order.id}/items`, "POST", body, csrfToken);
    if (afterMutation(response, "Serviço adicionado à comanda.", setOrder)) formElement.reset();
    setBusy(false);
  };

  const refreshOrder = async (id = order?.id) => {
    if (!id) return;
    const response = await api<Order>(`orders/${id}`);
    if (response.data) { setOrder(response.data); setPendingPaymentOrderId(null); notify("success", `Comanda #${id} atualizada pela API.`); }
    else notify("warning", response.error ?? "Não foi possível confirmar a comanda. Não repita o pagamento ainda.");
  };

  const recordPayment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!order) return;
    setBusy(true);
    const form = new FormData(event.currentTarget);
    const chosen = paymentMethods.find((method) => method.id === Number(form.get("paymentMethodId")));
    if (!chosen) { notify("error", "Selecione uma forma de pagamento disponível."); setBusy(false); return; }
    if (chosen.requiresManagerAuthorization && user?.role !== "Manager") {
      notify("error", "Fiado exige autorização de um gerente."); setBusy(false); return;
    }
    const response = await api<Order>(`orders/${order.id}/payments`, "POST", { paymentMethodId: chosen.id }, csrfToken);
    if (response.data) {
      const check = await api<Order>(`orders/${order.id}`);
      if (check.data) {
        setOrder(check.data);
        if (check.data.status.toLowerCase() === "closed") sessionStorage.removeItem("salon_active_order");
        notify("success", "Pagamento registrado e comanda confirmada pela API.");
      } else notify("warning", "A API confirmou o pagamento, mas não foi possível carregar os detalhes da comanda. Atualize antes de qualquer nova ação.");
    }
    else if (response.networkError || response.status === 401 || (response.status !== undefined && response.status >= 500)) {
      setPendingPaymentOrderId(order.id);
      const check = await api<Order>(`orders/${order.id}`);
      if (check.data) {
        setOrder(check.data);
        if (check.data.status.toLowerCase() === "closed") sessionStorage.removeItem("salon_active_order");
        setPendingPaymentOrderId(null);
        if (check.data.payments?.some((payment) => payment.paymentMethodId === chosen.id && payment.amount === order.totalValue)) {
          notify("success", "A API confirmou o pagamento. A comanda já está finalizada.");
        } else {
          notify("warning", "A API consultada não mostra esse pagamento. Confira o valor e a forma antes de decidir se fará uma nova tentativa.");
        }
      } else {
        notify("warning", "O envio do pagamento pode ter sido concluído. Não tente novamente até confirmar a comanda na API.");
      }
    } else notify("error", response.error ?? "O pagamento não foi registrado.");
    setBusy(false);
  };

  const closeCashSession = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!cashSession) return;
    setBusy(true);
    const form = new FormData(event.currentTarget);
    const count = Number(form.get("countedCash"));
    const response = await api<CashSession>(`cash-sessions/${cashSession.id}/close`, "POST", { countedCash: count }, csrfToken);
    if (response.data) { setCashSession(response.data); notify("success", "Caixa fechado."); }
    else if (response.networkError || (response.status !== undefined && response.status >= 500)) {
      const check = await api<CashSession>(`cash-sessions/${cashSession.id}`);
      if (check.data) {
        setCashSession(check.data);
        if (check.data.closedOn) notify("success", "A API confirmou o fechamento do caixa.");
        else notify("warning", "O caixa continua aberto na API. Confira antes de tentar fechar novamente.");
      } else notify("warning", "Não foi possível confirmar se o caixa fechou. Atualize a sessão antes de repetir.");
    } else notify("error", response.error ?? "Não foi possível fechar o caixa.");
    const reconciliationResult = await api<Reconciliation>(`cash-sessions/${cashSession.id}/reconciliation`);
    setReconciliation(reconciliationResult.data ?? null);
    setBusy(false);
  };

  const loadCommission = async (event?: FormEvent<HTMLFormElement>) => {
    event?.preventDefault();
    const collaboratorId = Number(selectedCollaboratorId);
    if (!collaboratorId) { notify("error", "Cadastre ou selecione um profissional."); return; }
    setBusy(true);
    const response = await api<CommissionPreview>(`collaborators/${collaboratorId}/commissions?year=${period.year}&month=${period.month}&half=${period.half}`);
    if (response.data) { setCommission(response.data); setPendingPayoutCheck(null); }
    else notify("error", response.error ?? "Não foi possível carregar as comissões.");
    setBusy(false);
  };

  const checkPayout = async () => {
    if (!commission) return;
    setBusy(true);
    const response = await api<CommissionPreview>(`collaborators/${commission.collaboratorId}/commissions?year=${period.year}&month=${period.month}&half=${period.half}`);
    if (!response.data) {
      notify("warning", "Não foi possível confirmar pela API. Não registre o mesmo pagamento novamente."); setBusy(false); return;
    }
    const pendingIds = new Set(response.data.items.map((item) => item.serviceOrderItemId));
    const itemsStillPending = pendingPayoutCheck?.filter((item) => pendingIds.has(item.serviceOrderItemId)) ?? [];
    setCommission(response.data);
    if (itemsStillPending.length === 0 && (pendingPayoutCheck?.length ?? 0) > 0) {
      notify("warning", "Os itens selecionados não aparecem mais como pendentes. A API não oferece consulta de histórico para o gerente; confirme o vínculo do pagamento na conta do funcionário.");
      setPendingPayoutCheck(null);
    } else notify("warning", "Ainda há itens pendentes. Revise a quinzena e o status antes de repetir o pagamento.");
    setBusy(false);
  };

  const payCommission = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!commission) return;
    setBusy(true);
    const form = new FormData(event.currentTarget);
    const method = String(form.get("paymentMethod"));
    const note = String(form.get("note") ?? "").trim() || null;
    const expectedItems = commission.items;
    const response = await api<{
      id: number; collaboratorId: number; collaboratorName: string; period: CommissionPreview["period"];
      paymentDate: string; totalAmount: number; paymentMethod: string; items: CommissionItem[];
    }>(`collaborators/${commission.collaboratorId}/commission-payouts`, "POST", { ...period, paymentMethod: method, note }, csrfToken);
    if (response.data) {
      setPendingPayoutCheck(expectedItems);
      setCommission(null);
      const confirm = await api<CommissionPreview>(`collaborators/${response.data.collaboratorId}/commissions?year=${period.year}&month=${period.month}&half=${period.half}`);
      setCommission(confirm.data ?? null);
      if (confirm.data && !confirm.data.items.some((item) => expectedItems.some((paid) => paid.serviceOrderItemId === item.serviceOrderItemId))) {
        setPendingPayoutCheck(null);
        notify("success", `Pagamento #${response.data.id} confirmado pela API para ${response.data.collaboratorName}, ${response.data.period.half}ª quinzena: ${money.format(response.data.totalAmount)} via ${response.data.paymentMethod}. O recibo retornou ${response.data.items.length} serviço(s).`);
      } else {
        notify("warning", `A API registrou o pagamento #${response.data.id}, mas ainda há itens a conferir. Não repita o pagamento; confira o histórico pela conta do funcionário.`);
      }
    } else if (response.networkError || response.status === 401 || (response.status !== undefined && response.status >= 500)) {
      setPendingPayoutCheck(expectedItems);
      notify("warning", "O resultado do pagamento não chegou ao navegador. Consulte a quinzena pela API antes de qualquer nova tentativa.");
    } else notify("error", response.error ?? "Não foi possível registrar o pagamento.");
    setBusy(false);
  };

  const loadEmployeeCommission = async () => {
    setBusy(true);
    const response = await api<EmployeeCommission>(`me/commissions?year=${period.year}&month=${period.month}&half=${period.half}`);
    if (response.data) setEmployeeCommission(response.data);
    else notify("error", response.error ?? "Não foi possível consultar suas comissões.");
    setBusy(false);
  };

  const viewItems = useMemo(() => order?.items ?? [], [order]);

  if (checkingSession) return <main className="loading-screen"><div className="spinner" />Carregando sua sessão…</main>;
  if (!user) return <Login message={authMessage} onLogin={(nextUser, nextCsrf) => {
    try {
      const saved = JSON.parse(sessionStorage.getItem("salon_active_order") ?? "null") as { userId?: number } | null;
      if (saved && saved.userId !== nextUser.id) { sessionStorage.removeItem("salon_active_order"); setOrder(null); }
    } catch { sessionStorage.removeItem("salon_active_order"); setOrder(null); }
    setUser(nextUser); setCsrfToken(nextCsrf); setActiveView(nextUser.role === "Employee" ? "comissoes" : "operacao"); setNotice(null); setAuthMessage("");
  }} />;

  const navItems = user.role === "Employee"
    ? [{ id: "comissoes", title: "Minhas comissões", icon: "◷" }]
    : [
        { id: "operacao", title: "Operação do salão", shortTitle: "Operação", icon: "▦" },
        ...(user.role === "Manager" ? [{ id: "comissoes", title: "Comissões", icon: "◷" }] : []),
      ];
  const isOperator = user.role !== "Employee";
  const isOperating = activeView === "operacao" && isOperator;
  const isOrderClosed = order?.status.toLowerCase() === "closed";
  const navigationTitle = navItems.find((item) => item.id === activeView)?.title;
  const hasMultipleViews = navItems.length > 1;

  function changeView(view: string) {
    if (view === activeView) return;
    setActiveView(view);
    setNotice(null);
    window.scrollTo({ top: 0, left: 0 });
    window.requestAnimationFrame(() => {
      document.getElementById("screen-title")?.focus({ preventScroll: true });
    });
  }

  return (
    <div className={hasMultipleViews ? "app-shell has-mobile-nav" : "app-shell"}>
      <aside className="app-sidebar">
        <div className="sidebar-brand"><Brand /></div>
        <nav className={hasMultipleViews ? "app-nav" : "app-nav single-view"} aria-label="Navegação principal">
          {navItems.map((item) => (
            <button
              key={item.id}
              type="button"
              className="nav-link"
              aria-current={activeView === item.id ? "page" : undefined}
              onClick={() => changeView(item.id)}
            >
              <span className="nav-icon" aria-hidden="true">{item.icon}</span>
              <span className="nav-label-compact">{"shortTitle" in item ? item.shortTitle : item.title}</span>
              <span className="nav-label-wide">{item.title}</span>
            </button>
          ))}
        </nav>
      </aside>

      <div className="main-area">
        <header className="topbar">
          <div className="topbar-brand"><Brand /></div>
          <div className="breadcrumb">{navigationTitle}</div>
          <div className="user-menu">
            <div className="user-identity">
              <b>{user.name}</b>
              <small>{roleName[user.role]}</small>
            </div>
            <button type="button" className="signout" onClick={() => void signOut()}>Sair</button>
          </div>
        </header>

        <main className="page-content" aria-labelledby="screen-title">
          {notice && (
            <div role="status" className={"notice " + notice.kind}>
              <span aria-hidden="true">{notice.kind === "success" ? "✓" : "!"}</span>
              <p>{notice.text}</p>
              <button type="button" onClick={() => setNotice(null)} aria-label="Fechar aviso">×</button>
            </div>
          )}

          {isOperating ? (
            <>
              <div className="page-heading">
                <div>
                  <p className="eyebrow">{businessDay.format(new Date())}</p>
                  <h1 id="screen-title" tabIndex={-1}>Operação do salão</h1>
                </div>
                <button className="button secondary" onClick={() => void refreshOperationalData()}>
                  <span aria-hidden="true">↻</span> Atualizar dados
                </button>
              </div>

              <section className="session-banner" aria-labelledby="session-title">
                <div className="session-icon" aria-hidden="true">R$</div>
                <div className="session-info">
                  <h2 id="session-title">
                    {cashSession ? (cashSession.closedOn ? "Caixa fechado" : "Caixa aberto") : "Nenhum caixa aberto"}
                  </h2>
                  <p>{cashSession
                    ? ["Sessão #" + cashSession.id, money.format(cashSession.openingCash) + " na abertura", dateOnly.format(parseBusinessDate(cashSession.openedOn))].join(" · ")
                    : "Abra o caixa para registrar comandas."}</p>
                </div>
                {!cashSession && (
                  <form className="opening-form form-stack" onSubmit={openCashSession}>
                    <label htmlFor="openingCash">
                      Valor de abertura (R$)
                      <input id="openingCash" name="openingCash" type="number" inputMode="numeric" min="0" step="1" defaultValue="0" required />
                    </label>
                    <button className="button primary" disabled={busy}>Abrir caixa</button>
                  </form>
                )}
              </section>

              <div className="work-grid">
                <section className="card order-card">
                  <h2>{order ? "Comanda" : "Nova comanda"}</h2>
                  {!cashSession || cashSession.closedOn ? (
                    <p className="empty-state">Abra um caixa para iniciar uma comanda.</p>
                  ) : !order ? (
                    <form onSubmit={openOrder} className="form-stack">
                      <label>
                        Nome do cliente
                        <input name="clientName" autoCapitalize="words" enterKeyHint="go" placeholder="Ex.: Maria Silva" maxLength={120} required />
                      </label>
                      <button className="button primary full" disabled={busy}>Iniciar comanda</button>
                    </form>
                  ) : (
                    <>
                      <div className="active-order">
                        <div className="order-id">Comanda #{order.id}</div>
                        <strong className="client-name">{order.clientName || "Cliente sem nome"}</strong>
                        <p className="order-meta">{isOrderClosed ? "Finalizada" : "Aberta"} · {order.items.length} serviço(s)</p>
                      </div>

                      {viewItems.length > 0 && (
                        <>
                          <ul className="items-list">
                            {viewItems.map((item) => (
                              <li className="order-item" key={item.itemId}>
                                <div><b>{item.serviceName}</b><small>{item.collaboratorName}</small></div>
                                <strong>{money.format(item.unitPrice)}</strong>
                              </li>
                            ))}
                          </ul>
                          <div className="order-total"><span>Total</span><b>{money.format(order.totalValue)}</b></div>
                        </>
                      )}

                      {!isOrderClosed && (
                        <>
                          <form onSubmit={addItem} className="form-stack separated">
                            <h3>Adicionar serviço</h3>
                            <label>
                              Serviço
                              <select name="serviceId" required defaultValue="">
                                <option value="" disabled>{services.length === 0 ? "Nenhum serviço cadastrado" : "Selecione um serviço"}</option>
                                {services.map((service) => (
                                  <option key={service.id} value={service.id}>{service.name} · {money.format(service.price)}</option>
                                ))}
                              </select>
                            </label>
                            <label>
                              Profissional
                              <select name="collaboratorId" required defaultValue="">
                                <option value="" disabled>{collaborators.length === 0 ? "Nenhum profissional cadastrado" : "Selecione um profissional"}</option>
                                {collaborators.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
                              </select>
                            </label>
                            <details className="service-options">
                              <summary>Ajustar preço ou incluir observação</summary>
                              <div className="form-stack service-options-fields">
                                <div className="two-fields">
                                  <label>Novo valor (R$)<input name="priceOverride" type="number" inputMode="numeric" min="0" step="1" placeholder="Preço do serviço" /></label>
                                  <label>Motivo do ajuste<input name="adjustmentReason" maxLength={500} placeholder="Desconto, adicional…" /></label>
                                </div>
                                <label>Observação<input name="observation" maxLength={500} placeholder="Detalhe do atendimento (opcional)" /></label>
                              </div>
                            </details>
                            <button className="button secondary full" disabled={busy || services.length === 0 || collaborators.length === 0}>
                              Adicionar serviço
                            </button>
                          </form>

                          {order.items.length > 0 && (
                            <form onSubmit={recordPayment} className="form-stack separated">
                              <h3>Receber pagamento</h3>
                              <label>
                                Forma de pagamento
                                <select name="paymentMethodId" required defaultValue="">
                                  <option value="" disabled>Selecione a forma de pagamento</option>
                                  {paymentMethods.filter((method) => method.name.toLowerCase() !== "fiado" || user.role === "Manager").map((method) => (
                                    <option key={method.id} value={method.id}>{method.name}</option>
                                  ))}
                                </select>
                              </label>
                              <button className="button primary full" disabled={busy}>
                                Registrar {money.format(order.totalValue)} e finalizar
                              </button>
                            </form>
                          )}
                        </>
                      )}

                      {pendingPaymentOrderId && (
                        <button type="button" className="button warning full next-action" onClick={() => void refreshOrder(pendingPaymentOrderId)}>
                          Consultar pagamento na API antes de tentar novamente
                        </button>
                      )}
                      {isOrderClosed && (
                        <>
                          <div className="paid-badge">Comanda finalizada · {order.payments.map((payment) => payment.paymentMethod).join(", ")}</div>
                          <button type="button" className="button primary full next-action" onClick={() => {
                            setOrder(null);
                            sessionStorage.removeItem("salon_active_order");
                          }}>
                            Abrir próxima comanda
                          </button>
                        </>
                      )}
                    </>
                  )}
                </section>

                <section className="card cash-card">
                  <h2>Fechamento de caixa</h2>
                  {!cashSession ? (
                    <p className="empty-state">A conferência estará disponível após abrir o caixa.</p>
                  ) : (
                    <>
                      <dl className="cash-summary">
                        <div><dt>Abertura</dt><dd>{money.format(cashSession.openingCash)}</dd></div>
                        <div><dt>Status</dt><dd className={cashSession.closedOn ? undefined : "text-green"}>{cashSession.closedOn ? "Fechado" : "Aberto"}</dd></div>
                      </dl>
                      {reconciliation && (
                        <div className="reconciliation">
                          <h3>Conferência da sessão #{cashSession.id}</h3>
                          <dl>
                            <div><dt>Esperado em dinheiro</dt><dd>{money.format(reconciliation.expectedCash)}</dd></div>
                            <div><dt>Vendas em Pix</dt><dd>{money.format(reconciliation.pixSales)}</dd></div>
                            <div><dt>Cartão de débito</dt><dd>{money.format(reconciliation.debitCardSales)}</dd></div>
                            <div><dt>Cartão de crédito</dt><dd>{money.format(reconciliation.creditCardSales)}</dd></div>
                            {reconciliation.difference !== null && (
                              <div className="rec-difference"><dt>Diferença</dt><dd>{money.format(reconciliation.difference)}</dd></div>
                            )}
                          </dl>
                        </div>
                      )}
                      {!cashSession.closedOn ? (
                        <form className="form-stack separated" onSubmit={closeCashSession}>
                          <label>Dinheiro contado (R$)<input name="countedCash" type="number" inputMode="numeric" min="0" step="1" required /></label>
                          <button className="button dark full" disabled={busy}>Conferir e fechar caixa</button>
                          <p className="microcopy">Finalize as comandas abertas antes de fechar o caixa.</p>
                        </form>
                      ) : (
                        <button type="button" className="button secondary full next-action" onClick={() => void api<Reconciliation>("cash-sessions/" + cashSession.id + "/reconciliation").then((result) => setReconciliation(result.data ?? null))}>
                          Atualizar conferência
                        </button>
                      )}
                    </>
                  )}
                </section>
              </div>
            </>
          ) : user.role === "Manager" ? (
            <>
              <div className="page-heading"><h1 id="screen-title" tabIndex={-1}>Comissões</h1></div>
              <section className="card commission-card">
                <h2>Consultar profissional</h2>
                <form className="commission-filters" onSubmit={loadCommission}>
                  <label className="collaborator-filter">
                    Profissional
                    <select value={selectedCollaboratorId} onChange={(event) => setSelectedCollaboratorId(event.target.value)} required>
                      {collaborators.length === 0 && <option value="">Nenhum profissional cadastrado</option>}
                      {collaborators.map((person) => <option value={person.id} key={person.id}>{person.name}</option>)}
                    </select>
                  </label>
                  <PeriodFields period={period} onChange={setPeriod} />
                  <button className="button primary" disabled={busy}>Consultar</button>
                </form>

                {commission && (
                  <div className="commission-result">
                    <div className="commission-total">
                      <div>
                        <h3>{commission.collaboratorName}</h3>
                        <p>{formatPeriod(commission.period)}</p>
                      </div>
                      <div><small>Comissão pendente</small><strong>{money.format(commission.totalAmount)}</strong></div>
                    </div>
                    <CommissionTable variant="manager" items={commission.items} />
                    {commission.items.length > 0 && (
                      <form className="payout-form" onSubmit={payCommission}>
                        <h3>Registrar pagamento da comissão</h3>
                        <div className="payout-fields form-stack">
                          <label>
                            Forma de pagamento
                            <select name="paymentMethod" required><option value="Pix">Pix</option><option value="Dinheiro">Dinheiro</option></select>
                          </label>
                          <label>Observação (opcional)<input name="note" maxLength={500} placeholder="Ex.: transferência realizada" /></label>
                          <button className="button dark" disabled={busy}>Pagar {money.format(commission.totalAmount)}</button>
                        </div>
                      </form>
                    )}
                    {pendingPayoutCheck && (
                      <div className="confirm-panel">
                        <p>Pagamento enviado. Confira a quinzena antes de tentar novamente.</p>
                        <button type="button" className="button secondary" onClick={() => void checkPayout()} disabled={busy}>Verificar quinzena</button>
                      </div>
                    )}
                  </div>
                )}
              </section>
              <p className="api-note">O recibo aparece após o registro. O funcionário pode consultar o vínculo do pagamento na própria conta.</p>
            </>
          ) : (
            <>
              <div className="page-heading"><h1 id="screen-title" tabIndex={-1}>Minhas comissões</h1></div>
              <section className="card commission-card">
                <h2>Escolha o período</h2>
                <form className="commission-filters employee-filters" onSubmit={(event) => {
                  event.preventDefault();
                  void loadEmployeeCommission();
                }}>
                  <PeriodFields period={period} onChange={setPeriod} />
                  <button className="button primary" disabled={busy}>Consultar</button>
                </form>

                {employeeCommission && (
                  <div className="commission-result">
                    <h3 className="employee-period">{formatPeriod(employeeCommission.period)}</h3>
                    <dl className="employee-totals">
                      <div><dt>Total da quinzena</dt><dd>{money.format(employeeCommission.totalCommission)}</dd></div>
                      <div><dt>Já pago</dt><dd className="text-green">{money.format(employeeCommission.paidCommission)}</dd></div>
                      <div><dt>A pagar</dt><dd>{money.format(employeeCommission.unpaidCommission)}</dd></div>
                    </dl>
                    <CommissionTable variant="employee" items={employeeCommission.items} />
                  </div>
                )}
              </section>
            </>
          )}
        </main>
      </div>
    </div>
  );
}

const businessDay = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "full",
  timeZone: "America/Bahia",
});
const monthFormat = new Intl.DateTimeFormat("pt-BR", { month: "long", timeZone: "UTC" });
const months = Array.from({ length: 12 }, (_, index) => monthFormat.format(new Date(Date.UTC(2026, index, 1))));

type PeriodSelection = Pick<CommissionPreview["period"], "year" | "month" | "half">;

function formatPeriod(period: CommissionPreview["period"]) {
  return dateOnly.format(parseBusinessDate(period.startDate)) + " a " + dateOnly.format(parseBusinessDate(period.endDate));
}

function PeriodFields({ period, onChange }: { period: PeriodSelection; onChange: (period: PeriodSelection) => void }) {
  return (
    <>
      <label>
        Ano
        <input type="number" inputMode="numeric" min="1" max="9999" value={period.year} onChange={(event) => onChange({ ...period, year: Number(event.target.value) })} required />
      </label>
      <label>
        Mês
        <select value={period.month} onChange={(event) => onChange({ ...period, month: Number(event.target.value) })}>
          {months.map((month, index) => <option value={index + 1} key={month}>{month}</option>)}
        </select>
      </label>
      <label className="half-filter">
        Quinzena
        <select value={period.half} onChange={(event) => onChange({ ...period, half: Number(event.target.value) })}>
          <option value={1}>1ª · dias 1 a 15</option>
          <option value={2}>2ª · dia 16 ao fim do mês</option>
        </select>
      </label>
    </>
  );
}

type CommissionTableProps =
  | { variant: "manager"; items: CommissionItem[] }
  | { variant: "employee"; items: EmployeeCommission["items"] };

function CommissionTable(props: CommissionTableProps) {
  const managerView = props.variant === "manager";
  if (props.items.length === 0) {
    return <p className="empty-state commission-empty">
      {managerView ? "Nenhuma comissão pendente para esta quinzena." : "Nenhuma comissão registrada para este período."}
    </p>;
  }

  function serviceCell(item: CommissionItem) {
    return (
      <td role="cell" className="commission-service">
        <strong>{item.serviceName}</strong>
        <small>Comanda #{item.orderId}</small>
      </td>
    );
  }

  return (
    <table className="commission-table" role="table" aria-label="Serviços e comissões da quinzena">
      <thead role="rowgroup">
        <tr role="row">
          <th scope="col" role="columnheader">Serviço</th>
          <th scope="col" role="columnheader">Cliente</th>
          {managerView && <th scope="col" role="columnheader">Fechamento</th>}
          {managerView && <th scope="col" role="columnheader">Base</th>}
          <th scope="col" role="columnheader">Comissão</th>
          {!managerView && <th scope="col" role="columnheader">Situação</th>}
        </tr>
      </thead>
      <tbody role="rowgroup">
        {props.variant === "manager" ? props.items.map((item) => (
          <tr role="row" key={item.serviceOrderItemId}>
            {serviceCell(item)}
            <CommissionCell label="Cliente">{item.clientName ?? "—"}</CommissionCell>
            <CommissionCell label="Fechamento">{dateTime.format(parseBusinessTimestamp(item.orderClosedAt))}</CommissionCell>
            <CommissionCell label="Base">{money.format(item.chargedAmount)}</CommissionCell>
            <CommissionCell label="Comissão">
              <b>{money.format(item.commissionAmount)}</b>
              <small>{item.commissionPercentage}%</small>
            </CommissionCell>
          </tr>
        )) : props.items.map((item) => (
          <tr role="row" key={item.serviceOrderItemId}>
            {serviceCell(item)}
            <CommissionCell label="Cliente">{item.clientName ?? "—"}</CommissionCell>
            <CommissionCell label="Comissão"><b>{money.format(item.commissionAmount)}</b></CommissionCell>
            <CommissionCell label="Situação">
              <span className={item.isPaid ? "status-pill paid" : "status-pill pending"}>
                {item.isPaid ? (item.payoutId === null ? "Paga" : "Paga · #" + item.payoutId) : "Pendente"}
              </span>
              {item.isPaid && (
                <small>{[item.paymentMethod, item.paymentDate && dateOnly.format(parseBusinessDate(item.paymentDate))].filter(Boolean).join(" · ")}</small>
              )}
            </CommissionCell>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function CommissionCell({ label, children }: { label: string; children: React.ReactNode }) {
  return <td role="cell"><span className="cell-label" aria-hidden="true">{label}</span>{children}</td>;
}

function Brand() {
  return (
    <span className="brand">
      <span className="brand-mark" aria-hidden="true">t</span>
      <span>Trilha do Salão</span>
    </span>
  );
}

function Login({ onLogin, message }: { onLogin: (user: User, csrfToken: string) => void; message: string }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    const values = Object.fromEntries(new FormData(event.currentTarget));
    try {
      const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(values),
      });
      const data = await response.json();
      if (!response.ok) setError(data.error ?? "Não foi possível entrar.");
      else onLogin(data.user, data.csrfToken);
    } catch {
      setError("Não foi possível conectar à API. Confira se o backend está em execução.");
    }
    setBusy(false);
  };

  return (
    <main className="login-page">
      <section className="login-box" aria-labelledby="login-title">
        <Brand />
        <h1 id="login-title">Entre na sua conta</h1>
        {message && <p className="login-message" role="status">{message}</p>}
        <form onSubmit={submit} className="form-stack login-form">
          <label htmlFor="login-email">
            E-mail
            <input id="login-email" type="email" name="email" autoComplete="username" autoCapitalize="none" spellCheck={false} enterKeyHint="next" placeholder="voce@seusalao.com.br" required />
          </label>
          <div className="password-group">
            <label htmlFor="login-password">Senha</label>
            <div className="password-field">
              <input id="login-password" type={showPassword ? "text" : "password"} name="password" autoComplete="current-password" autoCapitalize="none" spellCheck={false} enterKeyHint="go" required />
              <button type="button" onClick={() => setShowPassword(!showPassword)}>
                {showPassword ? "Ocultar" : "Mostrar"}<span className="sr-only"> senha</span>
              </button>
            </div>
          </div>
          {error && <p className="login-message" role="alert">{error}</p>}
          <button className="button primary full" disabled={busy}>{busy ? "Entrando…" : "Entrar"}</button>
        </form>
        <p className="login-foot">Ainda não tem acesso? Peça ao gerente do seu salão.</p>
      </section>
    </main>
  );
}
