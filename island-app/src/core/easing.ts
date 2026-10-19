export function clamp01(t: number): number {
  return Math.min(1, Math.max(0, t));
}

export function outCubic(t: number): number {
  const inverse = 1 - clamp01(t);
  return 1 - inverse * inverse * inverse;
}

export function inOutCubic(t: number): number {
  const x = clamp01(t);
  return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2;
}

function bezier(t: number, x1: number, x2: number): number {
  const u = 1 - t;
  return 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t;
}

function cubicBezier(t: number, x1: number, y1: number, x2: number, y2: number): number {
  const x = clamp01(t);
  if (x <= 0) return 0;
  if (x >= 1) return 1;
  let u = x;
  for (let i = 0; i < 6; i++) {
    const nx = bezier(u, x1, x2);
    const dx = 3 * (1 - u) * (1 - u) * (x1 - 0) + 6 * (1 - u) * u * (x2 - x1) + 3 * u * u * (1 - x2);
    if (Math.abs(nx - x) < 1e-6) break;
    u -= (nx - x) / dx;
  }
  return bezier(u, y1, y2);
}

// Balanced size easing: fast start, long gentle settle, no bounce.
export function balanced(t: number): number {
  return cubicBezier(t, 0.32, 0, 0.24, 1);
}
