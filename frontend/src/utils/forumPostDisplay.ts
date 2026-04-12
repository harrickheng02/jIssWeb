export function formatPublishedUtc(iso: string) {
  const d = new Date(iso)
  const diff = Date.now() - d.getTime()
  const m = Math.floor(diff / 60000)
  if (m < 1) return '刚刚'
  if (m < 60) return `${m} 分钟前`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h} 小时前`
  const days = Math.floor(h / 24)
  if (days < 7) return `${days} 天前`
  return d.toLocaleDateString('zh-CN')
}

function shortAuthor(id: string) {
  return id.length <= 14 ? id : `${id.slice(0, 10)}…`
}

export function forumAuthorLabel(displayName: string | undefined, authorId: string) {
  const n = displayName?.trim()
  if (n) return n
  return shortAuthor(authorId)
}
