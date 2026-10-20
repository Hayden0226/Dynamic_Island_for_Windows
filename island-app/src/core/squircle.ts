const rad = (deg: number) => (deg * Math.PI) / 180;

const round = (value: number) => Number(value.toFixed(2));

// Figma-style continuous-curvature squircle.
function squirclePath(width: number, height: number, cornerRadius: number, smoothingInput: number): string {
  const budget = Math.min(width, height) / 2;
  const radius = Math.min(Math.max(cornerRadius, 0), budget);
  if (radius <= 0) return `M 0 0 H ${round(width)} V ${round(height)} H 0 Z`;

  const originalSmoothing = Math.min(Math.max(smoothingInput, 0), 1);
  const maxCornerSmoothing = Math.max(0, budget / radius - 1);
  const smoothing = Math.min(originalSmoothing, maxCornerSmoothing);
  const p = Math.min((1 + smoothing) * radius, budget);

  const arcMeasure = 90 * (1 - smoothing);
  const arcSectionLength = Math.sin(rad(arcMeasure / 2)) * radius * Math.SQRT2;
  const angleAlpha = (90 - arcMeasure) / 2;
  const p3ToP4Distance = radius * Math.tan(rad(angleAlpha / 2));
  const angleBeta = 45 * smoothing;
  const c = p3ToP4Distance * Math.cos(rad(angleBeta));
  const d = c * Math.tan(rad(angleBeta));
  const b = (p - arcSectionLength - c - d) / 3;
  const a = 2 * b;

  const path: string[] = [];

  path.push(`M ${round(width - p)} 0`);
  path.push(`C ${round(width - p + a)} 0 ${round(width - p + a + b)} 0 ${round(width - p + a + b + c)} ${round(d)}`);

  let endX = width - p + a + b + c;
  let endY = d;
  let arcEndX = endX + arcSectionLength;
  let arcEndY = endY + arcSectionLength;
  path.push(`A ${round(radius)} ${round(radius)} 0 0 1 ${round(arcEndX)} ${round(arcEndY)}`);
  path.push(`C ${round(arcEndX + d)} ${round(arcEndY + c)} ${round(arcEndX + d)} ${round(arcEndY + b + c)} ${round(arcEndX + d)} ${round(arcEndY + a + b + c)}`);

  path.push(`L ${round(width)} ${round(height - p)}`);
  path.push(`C ${round(width)} ${round(height - p + a)} ${round(width)} ${round(height - p + a + b)} ${round(width - d)} ${round(height - p + a + b + c)}`);

  endX = width - d;
  endY = height - p + a + b + c;
  arcEndX = endX - arcSectionLength;
  arcEndY = endY + arcSectionLength;
  path.push(`A ${round(radius)} ${round(radius)} 0 0 1 ${round(arcEndX)} ${round(arcEndY)}`);
  path.push(`C ${round(arcEndX - c)} ${round(arcEndY + d)} ${round(arcEndX - (b + c))} ${round(arcEndY + d)} ${round(arcEndX - (a + b + c))} ${round(arcEndY + d)}`);

  path.push(`L ${round(p)} ${round(height)}`);
  path.push(`C ${round(p - a)} ${round(height)} ${round(p - (a + b))} ${round(height)} ${round(p - (a + b + c))} ${round(height - d)}`);

  endX = p - (a + b + c);
  endY = height - d;
  arcEndX = endX - arcSectionLength;
  arcEndY = endY - arcSectionLength;
  path.push(`A ${round(radius)} ${round(radius)} 0 0 1 ${round(arcEndX)} ${round(arcEndY)}`);
  path.push(`C ${round(arcEndX - d)} ${round(arcEndY - c)} ${round(arcEndX - d)} ${round(arcEndY - (b + c))} ${round(arcEndX - d)} ${round(arcEndY - (a + b + c))}`);

  path.push(`L 0 ${round(p)}`);
  path.push(`C 0 ${round(p - a)} 0 ${round(p - (a + b))} ${round(d)} ${round(p - (a + b + c))}`);

  endX = d;
  endY = p - (a + b + c);
  arcEndX = endX + arcSectionLength;
  arcEndY = endY - arcSectionLength;
  path.push(`A ${round(radius)} ${round(radius)} 0 0 1 ${round(arcEndX)} ${round(arcEndY)}`);
  path.push(`C ${round(arcEndX + c)} ${round(arcEndY - d)} ${round(arcEndX + (b + c))} ${round(arcEndY - d)} ${round(arcEndX + (a + b + c))} ${round(arcEndY - d)}`);
  path.push("Z");

  return path.join(" ");
}

export function islandPath(width: number, height: number, cornerRadius: number, smoothing = 0.6): string {
  const budget = Math.min(width, height) / 2;
  const radius = Math.min(Math.max(cornerRadius, 0), budget);
  if (radius <= 0) return `M 0 0 H ${round(width)} V ${round(height)} H 0 Z`;
  return squirclePath(width, height, radius, smoothing);
}
