"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  BuildingIcon,
  CalendarRangeIcon,
  CompassIcon,
  FoldersIcon,
  Grid3x3Icon,
  GlobeIcon,
  LogOutIcon,
  PackageIcon,
  PlayIcon,
  PuzzleIcon,
  SlidersHorizontalIcon,
  SwordsIcon,
  UsersIcon,
  WorkflowIcon,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useAuth } from "@/lib/auth";
import { Button } from "@/components/ui/button";
import { ThemeToggle } from "@/components/theme-toggle";
import { WorkspaceSwitcher } from "@/components/workspace-switcher";

const NAV_SECTIONS = [
  {
    label: "Plan",
    items: [
      { href: "/roadmap", label: "Roadmap", icon: CalendarRangeIcon },
      { href: "/initiatives", label: "Initiatives", icon: CompassIcon },
      { href: "/projects", label: "Projects", icon: FoldersIcon },
      { href: "/features", label: "Features", icon: PuzzleIcon },
      { href: "/teams", label: "Teams", icon: UsersIcon },
    ],
  },
  {
    label: "Market",
    items: [
      { href: "/markets", label: "Markets", icon: GlobeIcon },
      { href: "/segments", label: "Segments", icon: BuildingIcon },
      { href: "/products", label: "Products", icon: PackageIcon },
      { href: "/competitors", label: "Competitors", icon: SwordsIcon },
      { href: "/processes", label: "Processes", icon: WorkflowIcon },
      { href: "/coverage", label: "Coverage", icon: Grid3x3Icon },
    ],
  },
  {
    label: "Simulate",
    items: [
      { href: "/assumptions", label: "Assumptions", icon: SlidersHorizontalIcon },
      { href: "/simulate", label: "Runs", icon: PlayIcon },
    ],
  },
] as const;

export function AppSidebar() {
  const pathname = usePathname();
  const { signOut } = useAuth();

  return (
    <aside className="flex w-56 shrink-0 flex-col border-r border-sidebar-border bg-sidebar text-sidebar-foreground">
      <div className="px-5 pt-5 pb-4">
        <Link href="/roadmap" className="block select-none">
          <span className="font-heading text-[15px] font-semibold tracking-tight">
            Product
            <span className="text-sidebar-primary">Manager</span>
          </span>
        </Link>
        <div className="mt-0.5">
          <WorkspaceSwitcher />
        </div>
      </div>

      <nav className="flex flex-1 flex-col gap-4 overflow-y-auto px-3 pb-2">
        {NAV_SECTIONS.map((section) => (
          <div key={section.label} className="flex flex-col gap-0.5">
            <p className="px-2.5 pb-1 text-[10px] font-semibold tracking-[0.14em] text-muted-foreground/70 uppercase select-none">
              {section.label}
            </p>
            {section.items.map(({ href, label, icon: Icon }) => {
              const active = pathname.startsWith(href);
              return (
                <Link
                  key={href}
                  href={href}
                  aria-current={active ? "page" : undefined}
                  className={cn(
                    "flex items-center gap-2.5 rounded-lg px-2.5 py-1.5 text-sm font-medium transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50",
                    active
                      ? "bg-sidebar-accent text-sidebar-accent-foreground"
                      : "text-muted-foreground hover:bg-sidebar-accent/60 hover:text-sidebar-accent-foreground",
                  )}
                >
                  <Icon
                    className={cn(
                      "size-4",
                      active
                        ? "text-sidebar-primary"
                        : "text-muted-foreground/70",
                    )}
                  />
                  {label}
                </Link>
              );
            })}
          </div>
        ))}
      </nav>

      <div className="flex items-center justify-between border-t border-sidebar-border px-3 py-2.5">
        <ThemeToggle />
        <Button
          variant="ghost"
          size="sm"
          className="text-muted-foreground"
          onClick={signOut}
        >
          <LogOutIcon data-icon="inline-start" />
          Sign out
        </Button>
      </div>
    </aside>
  );
}
