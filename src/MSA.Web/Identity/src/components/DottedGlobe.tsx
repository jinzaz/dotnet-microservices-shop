// Pointillist globe (see SKILL.md › Dotted Globe Visual): white dots only, no fills, density by opacity.
// The "continents" are a deterministic noise field, so the picture is stable between renders and builds.
const RADIUS = 180;
const STEP = 6;

function land(lat: number, lon: number): number {
  const a = Math.sin(lat * 0.11 + 1.3) + Math.cos(lon * 0.07 - 0.4);
  const b = Math.sin((lat + lon) * 0.05) * 0.8;
  return a + b;
}

const dots = (() => {
  const result: { x: number; y: number; opacity: number }[] = [];
  for (let lat = -78; lat <= 78; lat += STEP) {
    const phi = (lat * Math.PI) / 180;
    const ringStep = STEP / Math.max(Math.cos(phi), 0.25);
    for (let lon = -180; lon < 180; lon += ringStep) {
      const lambda = ((lon - 20) * Math.PI) / 180;
      const depth = Math.cos(phi) * Math.cos(lambda);
      if (depth <= 0.05) continue; // back side of the sphere
      const isLand = land(lat, lon) > 0.55;
      result.push({
        x: RADIUS + RADIUS * Math.cos(phi) * Math.sin(lambda),
        y: RADIUS - RADIUS * Math.sin(phi),
        opacity: Number(((isLand ? 0.42 : 0.12) + depth * (isLand ? 0.28 : 0.1)).toFixed(2)),
      });
    }
  }
  return result;
})();

export function DottedGlobe({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox={`0 0 ${RADIUS * 2} ${RADIUS * 2}`} aria-hidden="true" focusable="false">
      {dots.map((dot, index) => (
        <circle key={index} cx={dot.x.toFixed(1)} cy={dot.y.toFixed(1)} r="1.5" fill="#ffffff" opacity={dot.opacity} />
      ))}
    </svg>
  );
}
