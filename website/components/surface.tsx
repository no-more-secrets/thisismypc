"use client";
import { createElement, useId, useLayoutEffect, useRef, useState, type ComponentPropsWithRef, type CSSProperties, type ElementType, type ReactNode } from "react";
import { flushSync } from "react-dom";
import { squirclePath } from "@/lib/squircle";

type Shadow = { inset: boolean; color: string; x: number; y: number; blur: number; spread: number };
function shadows(value: string): Shadow[] {
  return (value.match(/(?:[^,(]|\([^)]*\))+/g) || []).filter(s => s.trim() !== "none").map(s => {
    const color = s.match(/rgba?\([^)]*\)|#[\da-f]{3,8}/i)?.[0] || "#0008";
    const [x = 0, y = 0, blur = 0, spread = 0] = s.replace(color, "").replace("inset", "").trim().split(/\s+/).map(parseFloat);
    return { inset: s.includes("inset"), color, x, y, blur, spread };
  });
}
type Geometry = { width: number; height: number; radii: number[]; d: string; backgrounds: string[]; shadows: Shadow[]; image: string; size: string; blend: string };
export function Surface<T extends ElementType = "div">({ as, children, ...props }: { as?: T; children?: ReactNode } & ComponentPropsWithRef<T>) {
  const Tag = as || "div";
  const voidTag = Tag === "input" || Tag === "textarea" || Tag === "img";
  const host = useRef<HTMLElement>(null);
  const id = useId().replace(/[^a-z\d]/gi, "");
  const [hover, setHover] = useState(false), [pressed, setPressed] = useState(false);
  const [geometry, setGeometry] = useState<Geometry>({ width: 0, height: 0, radii: [], d: "", backgrounds: [], shadows: [], image: "none", size: "auto", blend: "normal" });
  useLayoutEffect(() => {
    const element = host.current;
    if (!element) return;
    function measure() {
      const box = element!.getBoundingClientRect(), cs = getComputedStyle(element!);
      const width = box.width, height = box.height;
      const half = Math.min(width, height) / 2 - .5;
      const radii = [cs.borderTopLeftRadius, cs.borderTopRightRadius, cs.borderBottomRightRadius, cs.borderBottomLeftRadius].map(raw => {
        const r = raw.includes("%") ? parseFloat(raw) * Math.min(width, height) / 100 : parseFloat(raw);
        return Math.max(0, Math.min(Math.max(r * 1.1, Math.min(r * 1.5, 12)), half / 1.8));
      });
      const tint = element!.querySelector(':scope > .design-tint');
      const styles = [cs, ...(tint ? [getComputedStyle(tint)] : [])];
      const next = { width, height, radii, d: squirclePath(width, height, radii, .8), backgrounds: [cs.getPropertyValue('--surface-base').trim(), ...styles.map(s => s.getPropertyValue('--surface-bg').trim())].filter(Boolean), shadows: styles.flatMap(s => shadows(s.getPropertyValue('--surface-shadow').trim())), image: cs.getPropertyValue("--surface-image").trim() || "none", size: cs.backgroundSize, blend: cs.backgroundBlendMode };
      setGeometry(previous => JSON.stringify(previous) === JSON.stringify(next) ? previous : next);
    }
    measure();
    const observer = new ResizeObserver(() => flushSync(measure));
    observer.observe(element);
    return () => observer.disconnect();
  }, [props.className, hover, pressed]);
  const { width: w, height: h, radii, d } = geometry;
  const clip = d ? `path("${d}")` : undefined;
  const layers = <>
    <svg data-sq-shadow="" aria-hidden="true" className="design-surface-outer" width={w} height={h} viewBox={`0 0 ${w} ${h}`}><defs><mask id={`${id}outside`} maskUnits="userSpaceOnUse" x={-w} y={-h} width={w * 3} height={h * 3}><rect x={-w} y={-h} width={w * 3} height={h * 3} fill="white" /><path d={d} fill="black" /></mask>{geometry.shadows.filter(s => !s.inset).map((s, i) => <filter key={i} id={`${id}o${i}`} x="-100%" y="-100%" width="300%" height="300%"><feGaussianBlur stdDeviation={s.blur / 2} /></filter>)}</defs><g mask={`url(#${id}outside)`}>{geometry.shadows.filter(s => !s.inset).map((s, i) => <path key={i} d={squirclePath(w + 2 * s.spread, h + 2 * s.spread, radii.map(r => Math.max(0, r + s.spread)), .8, -s.spread, -s.spread)} fill={s.color} transform={`translate(${s.x} ${s.y})`} filter={`url(#${id}o${i})`} />)}</g></svg>
    {geometry.backgrounds.map((background, i) => <span data-surface-layer="background" key={i} aria-hidden="true" className="design-surface-fill" style={{ clipPath: clip, background, ...(i === 0 && geometry.image !== "none" ? { backgroundImage: geometry.image, backgroundSize: geometry.size, backgroundBlendMode: geometry.blend } : {}) }} />)}
    <svg data-sq-inset="" aria-hidden="true" className="design-surface-inset" width={w} height={h} viewBox={`0 0 ${w} ${h}`} style={{ clipPath: clip }}><defs>{geometry.shadows.filter(s => s.inset && s.blur > 0).map((s, i) => <filter key={i} id={`${id}i${geometry.shadows.filter(v => v.inset).indexOf(s)}`} filterUnits="userSpaceOnUse" x={-s.blur * 2 - 16} y={-s.blur * 2 - 16} width={w + s.blur * 4 + 32} height={h + s.blur * 4 + 32}><feGaussianBlur stdDeviation={s.blur / 2} /></filter>)}</defs>{geometry.shadows.filter(s => s.inset).map((s, i) => {
      const pad = s.blur * 2 + Math.abs(s.x) + Math.abs(s.y) + 8;
      const hole = squirclePath(Math.max(0, w - 2 * s.spread), Math.max(0, h - 2 * s.spread), radii.map(r => Math.max(0, r - s.spread)), .8, s.spread, s.spread);
      return <path key={i} d={`M${-pad},${-pad} H${w + pad} V${h + pad} H${-pad} Z ${hole}`} fillRule="evenodd" fill={s.color} transform={`translate(${s.x} ${s.y})`} filter={s.blur ? `url(#${id}i${i})` : undefined} />;
    })}</svg>
  </>;
  const bind = (element: HTMLElement | null) => {
    host.current = element;
    if (voidTag) return;
    const forwarded = props.ref;
    if (typeof forwarded === 'function') forwarded(element);
    else if (forwarded) forwarded.current = element;
  };
  const events = {
    onPointerEnter: (event: React.PointerEvent<HTMLElement>) => { props.onPointerEnter?.(event); flushSync(() => setHover(true)); },
    onPointerLeave: (event: React.PointerEvent<HTMLElement>) => { props.onPointerLeave?.(event); flushSync(() => { setHover(false); setPressed(false); }); },
    onPointerDown: (event: React.PointerEvent<HTMLElement>) => { props.onPointerDown?.(event); flushSync(() => setPressed(true)); },
    onPointerUp: (event: React.PointerEvent<HTMLElement>) => { props.onPointerUp?.(event); flushSync(() => setPressed(false)); },
  };
  if (voidTag) return <span className="design-surface-field" style={{ borderRadius: 8 }} ref={bind} data-surface="" data-surface-d={d}>{layers}{createElement(Tag, { ...props, 'data-surface': '', 'data-surface-d': d, style: { ...props.style, clipPath: clip, boxShadow: 'none', background: 'transparent' } }, children)}</span>;
  return createElement(Tag, { ...props, ...events, ref: bind, 'data-surface': '', 'data-surface-d': d, 'data-surface-hover': hover || undefined, 'data-surface-pressed': pressed || undefined, style: { ...props.style, boxShadow: 'none', backgroundColor: 'transparent', backgroundImage: 'none', '--surface-clip': clip } as CSSProperties }, layers, children);
}
