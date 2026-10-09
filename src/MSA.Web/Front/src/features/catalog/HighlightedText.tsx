// The search service marks hits with STX/ETX control characters instead of HTML tags, so the
// highlight is turned into React nodes here and never goes through innerHTML (product names are
// typed by back-office staff and must not be trusted as markup).
const START = '\u0002';
const END = '\u0003';

export function HighlightedText({ text, highlight }: { text: string; highlight?: string | null }) {
  if (!highlight || !highlight.includes(START)) return <>{text}</>;

  const parts: { text: string; hit: boolean }[] = [];
  for (const segment of highlight.split(START)) {
    const end = segment.indexOf(END);
    if (end < 0) {
      if (segment) parts.push({ text: segment, hit: false });
      continue;
    }
    parts.push({ text: segment.slice(0, end), hit: true });
    if (end + 1 < segment.length) parts.push({ text: segment.slice(end + 1), hit: false });
  }

  return (
    <>
      {parts.map((part, index) => (part.hit ? <mark key={index}>{part.text}</mark> : <span key={index}>{part.text}</span>))}
    </>
  );
}
