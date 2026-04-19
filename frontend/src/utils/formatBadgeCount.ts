export function formatBadgeCount(count: number, max = 99): number | string {
  if (!Number.isFinite(count)) return 0
  const c = Math.max(0, Math.floor(count))
  const m = Math.max(0, Math.floor(max))
  if (c <= m) return c
  return `${m}+`
}

