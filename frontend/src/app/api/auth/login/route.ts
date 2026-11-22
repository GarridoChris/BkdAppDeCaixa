import { NextRequest, NextResponse } from "next/server";
import { apiBaseUrl, apiErrorTitle } from "@/lib/server-api";
import { createSession, getSession, revokeSession, type SessionUser } from "@/lib/session-store";

export const runtime = "nodejs";

type SignInResponse = {
  token?: { value?: string; expiresAt?: string };
  user?: SessionUser;
};

export async function POST(request: NextRequest) {
  const expectedOrigin = process.env.APP_ORIGIN ?? "http://localhost:3000";
  if (request.headers.get("origin") !== expectedOrigin) {
    return NextResponse.json({ error: "Origem da solicitação não permitida." }, { status: 403 });
  }

  let input: { email?: string; password?: string };
  try {
    input = await request.json();
  } catch {
    return NextResponse.json({ error: "Informe e-mail e senha." }, { status: 400 });
  }
  if (typeof input?.email !== "string" || typeof input?.password !== "string") {
    return NextResponse.json({ error: "Informe e-mail e senha." }, { status: 400 });
  }

  try {
    const response = await fetch(`${apiBaseUrl}/api/auth/token`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: input.email, password: input.password }),
      cache: "no-store",
    });
    const body = await response.json().catch(() => null) as SignInResponse | null;
    if (!response.ok || !body?.token?.value || !body.token.expiresAt || !body.user) {
      return NextResponse.json(
        { error: apiErrorTitle(body, response.status === 401 ? "E-mail ou senha inválidos." : "Não foi possível entrar.") },
        { status: response.status || 502 },
      );
    }

    const currentId = request.cookies.get("salon_session")?.value;
    const current = getSession(currentId);
    revokeSession(currentId);
    const session = createSession(body.token.value, body.token.expiresAt, body.user);
    const result = NextResponse.json({ user: session.user, csrfToken: session.csrfToken });
    result.cookies.set("salon_session", session.id, {
      httpOnly: true,
      secure: true,
      sameSite: "strict",
      path: "/",
      expires: new Date(session.expiresAt),
    });
    if (current) result.headers.set("Cache-Control", "no-store");
    return result;
  } catch {
    return NextResponse.json({ error: "A API não está disponível. Confira se o backend está em execução." }, { status: 503 });
  }
}
