"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { tokenStore } from "@/lib/api/client";
import { useAuth } from "@/lib/auth";
import { AppSidebar } from "@/components/app-sidebar";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const { authenticated } = useAuth();

  React.useEffect(() => {
    // Double-check the store directly to avoid a stale-snapshot redirect
    // right after hydration.
    if (!authenticated && tokenStore.get() === null) {
      router.replace("/login");
    }
  }, [authenticated, router]);

  if (!authenticated) {
    return <div className="min-h-dvh bg-background" />;
  }

  return (
    <div className="flex h-dvh flex-1 overflow-hidden bg-background">
      <AppSidebar />
      <main className="flex min-w-0 flex-1 flex-col">{children}</main>
    </div>
  );
}
