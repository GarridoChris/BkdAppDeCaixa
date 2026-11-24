import "server-only";
import { randomBytes, randomUUID } from "node:crypto";

export type SessionUser = {
  id: number;
  name: string;
  email: string;
  role: "Manager" | "Cashier" | "Employee";
  companyId: number;
  employeeId: number | null;
};

export type ServerSession = {
  id: string;
  accessToken: string;
  expiresAt: number;
  csrfToken: string;
  user: SessionUser;
};

type SessionRegistry = Map<string, ServerSession>;
const globalStore = globalThis as typeof globalThis & { __salonSessions?: SessionRegistry };
const sessions = globalStore.__salonSessions ?? new Map<string, ServerSession>();
globalStore.__salonSessions = sessions;

export function createSession(accessToken: string, expiresAt: string, user: SessionUser): ServerSession {
  const session: ServerSession = {
    id: randomUUID(),
    accessToken,
    expiresAt: new Date(expiresAt).getTime(),
    csrfToken: randomBytes(32).toString("base64url"),
    user,
  };
  sessions.set(session.id, session);
  return session;
}

export function getSession(id: string | undefined): ServerSession | null {
  if (!id) return null;
  const session = sessions.get(id);
  if (!session) return null;
  if (session.expiresAt <= Date.now()) {
    sessions.delete(id);
    return null;
  }
  return session;
}

export function revokeSession(id: string | undefined): void {
  if (id) sessions.delete(id);
}
