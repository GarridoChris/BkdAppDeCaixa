import { NextRequest, NextResponse } from "next/server";
import { getSession, revokeSession } from "@/lib/session-store";

export async function POST(request: NextRequest) {
  const expectedOrigin = process.env.APP_ORIGIN ?? "http://localhost:3000";
  const id = request.cookies.get("salon_session")?.value;
  const session = getSession(id);
  if (request.headers.get("origin") !== expectedOrigin || (session && request.headers.get("x-csrf-token") !== session.csrfToken)) {
    return NextResponse.json({ error: "Não foi possível validar esta solicitação." }, { status: 403 });
  }
  revokeSession(id);
  const result = NextResponse.json({ ok: true });
  result.cookies.set("salon_session", "", {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: "/",
    expires: new Date(0),
  });
  return result;
}
