import "server-only";

export const apiBaseUrl = (process.env.API_BASE_URL ?? "http://localhost:5088").replace(/\/$/, "");

export function apiErrorTitle(body: unknown, fallback: string): string {
  if (typeof body === "object" && body !== null) {
    const problem = body as { title?: unknown; detail?: unknown; errors?: Record<string, string[]> };
    const validation = Object.values(problem.errors ?? {}).flat().find(Boolean);
    if (typeof validation === "string") return validation;
    if (typeof problem.title === "string") return problem.title;
    if (typeof problem.detail === "string") return problem.detail;
  }
  return fallback;
}
