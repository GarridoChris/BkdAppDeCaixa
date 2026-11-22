import { NextRequest, NextResponse } from "next/server";
import { apiBaseUrl, apiErrorTitle } from "@/lib/server-api";
import { getSession, revokeSession } from "@/lib/session-store";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const routeRules: Array<{ pattern: RegExp; methods: string[] }> = [
  { pattern: /^auth\/me$/, methods: ["GET"] },
  { pattern: /^cash-sessions$/, methods: ["POST"] },
  { pattern: /^cash-sessions\/current$/, methods: ["GET"] },
  { pattern: /^cash-sessions\/\d+$/, methods: ["GET"] },
  { pattern: /^cash-sessions\/\d+\/close$/, methods: ["POST"] },
  { pattern: /^cash-sessions\/\d+\/movements$/, methods: ["GET", "POST"] },
  { pattern: /^cash-sessions\/\d+\/reconciliation$/, methods: ["GET"] },
  { pattern: /^orders$/, methods: ["POST"] },
  { pattern: /^orders\/\d+$/, methods: ["GET"] },
  { pattern: /^orders\/\d+\/(?:items|payments)$/, methods: ["POST"] },
  { pattern: /^payment-methods$/, methods: ["GET"] },
  { pattern: /^services$/, methods: ["GET"] },
  { pattern: /^collaborators$/, methods: ["GET", "POST"] },
  { pattern: /^collaborators\/\d+\/commissions$/, methods: ["GET"] },
  { pattern: /^collaborators\/\d+\/commission-payouts$/, methods: ["POST"] },
  { pattern: /^me\/commissions$/, methods: ["GET"] },
];

async function handle(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const session = getSession(request.cookies.get("salon_session")?.value);
  if (!session) return NextResponse.json({ error: "Sua sessão expirou. Entre novamente." }, { status: 401 });

  const method = request.method.toUpperCase();
  if (method !== "GET") {
    const expectedOrigin = process.env.APP_ORIGIN ?? "http://localhost:3000";
    if (request.headers.get("origin") !== expectedOrigin || request.headers.get("x-csrf-token") !== session.csrfToken) {
      return NextResponse.json({ error: "Não foi possível validar esta solicitação. Atualize a página e tente novamente." }, { status: 403 });
    }
  }

  const { path } = await context.params;
  const apiPath = path.join("/");
  const rule = routeRules.find((entry) => entry.pattern.test(apiPath) && entry.methods.includes(method));
  if (!rule) return NextResponse.json({ error: "Esta operação não está disponível nesta versão." }, { status: 404 });

  const target = new URL(`/api/${apiPath}`, apiBaseUrl);
  target.search = new URL(request.url).search;
  try {
    const response = await fetch(target, {
      method,
      headers: {
        Authorization: `Bearer ${session.accessToken}`,
        ...(method === "GET" ? {} : { "Content-Type": "application/json" }),
      },
      ...(method === "GET" ? {} : { body: await request.text() }),
      cache: "no-store",
    });
    const body = await response.text();
    if (response.status === 401) {
      revokeSession(session.id);
      const expired = NextResponse.json({ error: "Sua sessão expirou. Entre novamente." }, { status: 401 });
      expired.cookies.set("salon_session", "", { httpOnly: true, secure: true, sameSite: "strict", path: "/", expires: new Date(0) });
      return expired;
    }
    return new NextResponse(body, {
      status: response.status,
      headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json", "Cache-Control": "no-store" },
    });
  } catch {
    return NextResponse.json({ error: "A conexão com a API falhou. Consulte o estado antes de repetir qualquer pagamento." }, { status: 503 });
  }
}

export const GET = handle;
export const POST = handle;
