import { NextRequest, NextResponse } from "next/server";
import { getSession } from "@/lib/session-store";

export async function GET(request: NextRequest) {
  const session = getSession(request.cookies.get("salon_session")?.value);
  if (!session) return NextResponse.json({ user: null }, { status: 401, headers: { "Cache-Control": "no-store" } });
  return NextResponse.json(
    { user: session.user, csrfToken: session.csrfToken },
    { headers: { "Cache-Control": "no-store" } },
  );
}
