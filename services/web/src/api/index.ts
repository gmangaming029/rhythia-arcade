import type { Chart, ChartSearch, Page } from "@rhythia/shared";
import { userManager } from "../auth/oidc";

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || "http://localhost:5080").replace(/\/+$/, "");

export type RankedNomination = {
  nominationId: string;
  chartId: string;
  playerId: string;
  rationale: string;
  status: string;
  createdAt: string;
};

export type RankedPlayer = { rank: number; playerId: string; rp: number };
export type PlayerRp = { playerId: string; rp: number };
export type ModerationReport = {
  reportId: string;
  chartId: string;
  reporterId: string;
  reason: string;
  createdAt: string;
};

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const user = await userManager?.getUser();
  const headers = new Headers(init.headers);
  if (user?.access_token) headers.set("Authorization", "Bearer " + user.access_token);
  const response = await fetch(`${apiBaseUrl}/v1${path}`, { ...init, headers });
  if (!response.ok) {
    let message = `Request failed (${response.status})`;
    try {
      const problem = await response.json() as { detail?: string; title?: string; error?: string };
      message = problem.detail || problem.error || problem.title || message;
    } catch {
      // Keep the useful HTTP status when an API error has no JSON body.
    }
    throw new Error(message);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export async function searchCharts(filters: ChartSearch): Promise<Page<Chart>> {
  const query = new URLSearchParams();
  if (filters.q) query.set("q", filters.q);
  if (filters.creator) query.set("creator", filters.creator);
  if (filters.difficulty) query.set("difficulty", filters.difficulty);
  for (const tag of filters.tag ?? []) query.append("tag", tag);
  if (filters.sort) query.set("sort", filters.sort);
  if (filters.page) query.set("page", String(filters.page));
  if (filters.pageSize) query.set("pageSize", String(filters.pageSize));
  return request<Page<Chart>>(`/charts?${query.toString()}`);
}

export function getChart(chartId: string): Promise<Chart> {
  return request<Chart>(`/charts/${encodeURIComponent(chartId)}`);
}

export async function downloadChartPackage(chart: Chart): Promise<void> {
  const user = await userManager?.getUser();
  const headers = new Headers();
  if (user?.access_token) headers.set("Authorization", "Bearer " + user.access_token);
  const response = await fetch(
    `${apiBaseUrl}/v1/charts/${encodeURIComponent(chart.chartId)}/versions/${encodeURIComponent(chart.versionId)}/download`,
    { headers },
  );
  if (!response.ok) throw new Error(`Package download failed (${response.status}).`);
  const expectedHash = response.headers.get("X-Chart-SHA256");
  if (!expectedHash) throw new Error("The API did not provide a package checksum.");
  const blob = await response.blob();
  const digest = await crypto.subtle.digest("SHA-256", await blob.arrayBuffer());
  const actualHash = Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
  if (actualHash.toLowerCase() !== expectedHash.toLowerCase()) {
    throw new Error("Package checksum verification failed; the file was not saved.");
  }
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = objectUrl;
  anchor.download = `${chart.chartId}-${chart.versionId}.zip`;
  anchor.click();
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
}

export async function uploadChartPackage(file: File): Promise<{ chartId: string; versionId: string; sha256: string; sizeBytes: number }> {
  const session = await request<{ uploadId: string; uploadUrl: string }>("/uploads", { method: "POST" });
  return request<{ chartId: string; versionId: string; sha256: string; sizeBytes: number }>(`/uploads/${encodeURIComponent(session.uploadId)}/package`, {
    method: "PUT",
    headers: { "Content-Type": "application/zip" },
    body: file,
  });
}

export function reportChart(chartId: string, reason: string): Promise<{ reportId: string; status: string }> {
  return request<{ reportId: string; status: string }>(`/charts/${encodeURIComponent(chartId)}/reports`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ reason }),
  });
}

export function nominateChart(chartId: string, rationale: string): Promise<{ nominationId: string; status: string }> {
  return request<{ nominationId: string; status: string }>(`/charts/${encodeURIComponent(chartId)}/ranked-nominations`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ rationale }),
  });
}

export function getRankedReviews(): Promise<Page<RankedNomination>> {
  return request<Page<RankedNomination>>("/ranked/reviews");
}

export function decideRankedReview(nominationId: string, decision: "approved" | "rejected", rationale: string): Promise<void> {
  return request<void>(`/ranked/reviews/${encodeURIComponent(nominationId)}/decision`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ decision, rationale }),
  });
}

export function getLeaderboard(): Promise<Page<RankedPlayer>> {
  return request<Page<RankedPlayer>>("/leaderboards/rp");
}

export function getPlayerRp(playerId: string): Promise<PlayerRp> {
  return request<PlayerRp>(`/players/${encodeURIComponent(playerId)}/rp`);
}

export function getModerationReports(): Promise<Page<ModerationReport>> {
  return request<Page<ModerationReport>>("/moderation/reports");
}

export function takedownChart(chartId: string, rationale: string): Promise<void> {
  return request<void>(`/moderation/charts/${encodeURIComponent(chartId)}/takedown`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ rationale }),
  });
}
