"use client";

import * as React from "react";
import { api, tokenStore } from "@/lib/api/client";

interface AuthContextValue {
  authenticated: boolean;
  signIn: (password: string) => Promise<void>;
  signOut: () => void;
}

const AuthContext = React.createContext<AuthContextValue | null>(null);

const getSnapshot = () => tokenStore.get() !== null;
const getServerSnapshot = () => false;

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const authenticated = React.useSyncExternalStore(
    tokenStore.subscribe,
    getSnapshot,
    getServerSnapshot,
  );

  const signIn = React.useCallback(async (password: string) => {
    const token = await api.auth.token(password);
    tokenStore.set(token.accessToken);
  }, []);

  const signOut = React.useCallback(() => {
    tokenStore.clear();
  }, []);

  const value = React.useMemo(
    () => ({ authenticated, signIn, signOut }),
    [authenticated, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = React.useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
