export interface ForumMutedPayload {
  mutedUntilUtc?: string | null
}

export function formatForumMutedMessage(result: {
  message?: string
  code?: string
  data?: unknown
}): string {
  if (result.code !== 'FORUM_MUTED') return result.message ?? '操作受限'
  const data = result.data as ForumMutedPayload | undefined
  const until = data?.mutedUntilUtc
  if (!until) return '您已被禁言，暂时无法发布内容'
  const d = new Date(until)
  if (Number.isNaN(d.getTime())) return '您已被禁言，暂时无法发布内容'
  return `您已被禁言，将于 ${d.toLocaleString('zh-CN')} 解除`
}
