"use client";
import { useEffect, useRef } from "react";
import { mountPatternWaves } from "@/lib/pattern-waves";

/** The same React Bits shader and settings as RE Universe's catalog. */
export function PatternBackground({ enabled }: { enabled: boolean }) {
  const host = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!enabled || !host.current) return;
    const element = host.current;
    const frozen = new URLSearchParams(location.search).get("visual-check") === "1";
    try {
      const renderer = mountPatternWaves(element, () => {}, frozen);
      return () => renderer?.dispose();
    } catch {
      // Keep the page usable when WebGL is unavailable or initialization fails.
      const canvas = element.querySelector("canvas");
      canvas?.getContext("webgl2")?.getExtension("WEBGL_lose_context")?.loseContext();
      element.replaceChildren();
    }
  }, [enabled]);
  return enabled ? <div ref={host} className="site-waves" aria-hidden="true" /> : null;
}
