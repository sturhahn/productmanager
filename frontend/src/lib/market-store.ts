/**
 * The client's chosen market per workspace, persisted so the Market screens
 * (Segments, Products, Coverage, …) keep their context across navigation and
 * reloads. Mirrors the activeWorkspaceStore pattern in lib/api/client.ts.
 */

const KEY = "productmanager.activeMarketByWorkspace";
const EVENT = "productmanager:market-changed";

function readMap(): Record<string, string> {
  if (typeof window === "undefined") return {};
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as unknown;
    return typeof parsed === "object" && parsed !== null
      ? (parsed as Record<string, string>)
      : {};
  } catch {
    return {};
  }
}

function notify() {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new Event(EVENT));
  }
}

export const activeMarketStore = {
  get(workspaceId: string): string | null {
    return readMap()[workspaceId] ?? null;
  },
  set(workspaceId: string, marketId: string) {
    try {
      const map = readMap();
      map[workspaceId] = marketId;
      window.localStorage.setItem(KEY, JSON.stringify(map));
    } catch {
      // storage unavailable — selection is session-only
    }
    notify();
  },
  subscribe(callback: () => void): () => void {
    window.addEventListener(EVENT, callback);
    window.addEventListener("storage", callback);
    return () => {
      window.removeEventListener(EVENT, callback);
      window.removeEventListener("storage", callback);
    };
  },
};
