"use client";

import * as React from "react";
import * as echarts from "echarts";
import { useTheme } from "next-themes";

/**
 * Theme-resolved colors for chart building. CSS variables hold oklch()
 * values that echarts' own color math can't parse, so each token is
 * resolved to an rgb() string through the browser's style engine and
 * re-resolved whenever the theme flips.
 */
export interface ChartTokens {
  foreground: string;
  mutedForeground: string;
  border: string;
  chart: [string, string, string, string, string];
  /** Bumps when the theme changes so chart options can be rebuilt. */
  themeKey: string;
}

const FALLBACK: ChartTokens = {
  foreground: "rgb(40, 44, 63)",
  mutedForeground: "rgb(115, 120, 140)",
  border: "rgb(220, 222, 230)",
  chart: [
    "rgb(80, 100, 220)",
    "rgb(45, 150, 190)",
    "rgb(150, 90, 220)",
    "rgb(40, 160, 120)",
    "rgb(210, 90, 80)",
  ],
  themeKey: "ssr",
};

function resolveCssColor(probe: HTMLElement, variable: string): string {
  probe.style.color = `var(${variable})`;
  return getComputedStyle(probe).color;
}

function readTokens(themeKey: string): ChartTokens {
  if (typeof window === "undefined") return FALLBACK;
  const probe = document.createElement("span");
  probe.style.display = "none";
  document.body.appendChild(probe);
  try {
    return {
      foreground: resolveCssColor(probe, "--foreground"),
      mutedForeground: resolveCssColor(probe, "--muted-foreground"),
      border: resolveCssColor(probe, "--border"),
      chart: [
        resolveCssColor(probe, "--chart-1"),
        resolveCssColor(probe, "--chart-2"),
        resolveCssColor(probe, "--chart-3"),
        resolveCssColor(probe, "--chart-4"),
        resolveCssColor(probe, "--chart-5"),
      ],
      themeKey,
    };
  } finally {
    probe.remove();
  }
}

export function useChartTokens(): ChartTokens {
  const { resolvedTheme } = useTheme();
  const [tokens, setTokens] = React.useState<ChartTokens>(FALLBACK);
  React.useEffect(() => {
    // Read after paint so the flipped theme class has actually applied.
    const frame = requestAnimationFrame(() => {
      setTokens(readTokens(resolvedTheme ?? "light"));
    });
    return () => cancelAnimationFrame(frame);
  }, [resolvedTheme]);
  return tokens;
}

/** Shared cartesian scaffolding (axes, grid, tooltip) in the app's palette. */
export function baseChartOption(
  tokens: ChartTokens,
  xLabels: string[],
  valueFormatter: (value: number) => string,
): echarts.EChartsOption {
  return {
    animationDuration: 250,
    grid: { left: 56, right: 16, top: 28, bottom: 28 },
    tooltip: {
      trigger: "axis",
      valueFormatter: (v) => (typeof v === "number" ? valueFormatter(v) : "—"),
      backgroundColor: "rgba(24, 26, 38, 0.92)",
      borderWidth: 0,
      textStyle: { color: "rgb(240, 241, 248)", fontSize: 12 },
    },
    legend: {
      show: false,
    },
    xAxis: {
      type: "category",
      data: xLabels,
      boundaryGap: false,
      axisLine: { lineStyle: { color: tokens.border } },
      axisTick: { show: false },
      axisLabel: { color: tokens.mutedForeground, fontSize: 11 },
    },
    yAxis: {
      type: "value",
      scale: true,
      axisLabel: {
        color: tokens.mutedForeground,
        fontSize: 11,
        formatter: (v: number) => valueFormatter(v),
      },
      splitLine: { lineStyle: { color: tokens.border, type: "dashed" } },
    },
  };
}

export function EChart({
  option,
  className,
  ariaLabel,
}: {
  option: echarts.EChartsOption;
  className?: string;
  ariaLabel?: string;
}) {
  const containerRef = React.useRef<HTMLDivElement>(null);
  const chartRef = React.useRef<echarts.ECharts | null>(null);

  React.useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const chart = echarts.init(container);
    chartRef.current = chart;
    const observer = new ResizeObserver(() => chart.resize());
    observer.observe(container);
    return () => {
      observer.disconnect();
      chart.dispose();
      chartRef.current = null;
    };
  }, []);

  React.useEffect(() => {
    chartRef.current?.setOption(option, { notMerge: true });
  }, [option]);

  return (
    <div
      ref={containerRef}
      role="img"
      aria-label={ariaLabel}
      className={className ?? "h-64 w-full"}
    />
  );
}
