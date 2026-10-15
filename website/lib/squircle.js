// Exact Figma corner geometry from the supplied design handoff.
  const f = (n) => Math.round(n * 100) / 100;
  const rad = (d) => d * Math.PI / 180;

  // Figma corner smoothing. Points are [along incoming edge, inward] from the corner vertex.
  const geom = (R, sm) => {
    const p = (1 + sm) * R;
    const arcMeasure = 90 * (1 - sm);
    const arcLen = Math.sin(rad(arcMeasure / 2)) * R * Math.SQRT2;
    const alpha = (90 - arcMeasure) / 2;
    const p3p4 = R * Math.tan(rad(alpha / 2));
    const beta = 45 * sm;
    const c = p3p4 * Math.cos(rad(beta));
    const d = c * Math.tan(rad(beta));
    const b = (p - arcLen - c - d) / 3;
    const a = 2 * b;
    return { R, pts: [[p, 0], [p - a, 0], [p - a - b, 0], [p - a - b - c, d], [d, p - a - b - c], [0, p - a - b], [0, p - a], [0, p]], arc: arcLen > 0.01 };
  };
  const corner = (g, map) => {
    if (!g) return { start: map(0, 0), d: '' };
    const P = g.pts.map(([x, y]) => map(x, y));
    let d = ` C${f(P[1][0])},${f(P[1][1])} ${f(P[2][0])},${f(P[2][1])} ${f(P[3][0])},${f(P[3][1])}`;
    if (g.arc) d += ` A${f(g.R)},${f(g.R)} 0 0 1 ${f(P[4][0])},${f(P[4][1])}`;
    d += ` C${f(P[5][0])},${f(P[5][1])} ${f(P[6][0])},${f(P[6][1])} ${f(P[7][0])},${f(P[7][1])}`;
    return { start: P[0], d };
  };
  export const squirclePath = (W, H, radii, sm = 0.8, x0 = 0, y0 = 0) => {
    const lim = Math.max(0, Math.min(W, H) / 2);
    const g = (r) => (r <= 0.01 ? null : geom(Math.min(r, lim / (1 + sm)), sm));
    const [tl, tr, br, bl] = radii;
    const c = [
      corner(g(tr), (a, b) => [x0 + W - a, y0 + b]),
      corner(g(br), (a, b) => [x0 + W - b, y0 + H - a]),
      corner(g(bl), (a, b) => [x0 + a, y0 + H - b]),
      corner(g(tl), (a, b) => [x0 + b, y0 + a])
    ];
    let d = `M${f(c[0].start[0])},${f(c[0].start[1])}${c[0].d}`;
    for (let i = 1; i < 4; i++) d += ` L${f(c[i].start[0])},${f(c[i].start[1])}${c[i].d}`;
    return d + ' Z';
  };
