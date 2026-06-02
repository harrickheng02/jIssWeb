import type { ModerationAuditActionFilter } from '@/api/clients'

export const ALL_MODERATION_AUDIT_ACTIONS = 'all' as const

export type ModerationAuditActionFilterValue =
  | typeof ALL_MODERATION_AUDIT_ACTIONS
  | ModerationAuditActionFilter

export const MODERATION_AUDIT_ACTION_OPTIONS: Array<{ value: ModerationAuditActionFilter; label: string }> = [
  { value: 'post.setSticky', label: '置顶帖子' },
  { value: 'post.unsetSticky', label: '取消置顶' },
  { value: 'post.setFeatured', label: '加精' },
  { value: 'post.unsetFeatured', label: '取消精华' },
  { value: 'post.lockReplies', label: '锁定回复' },
  { value: 'post.unlockReplies', label: '解除锁定回复' },
  { value: 'post.modDelete', label: '删除帖子' },
  { value: 'reply.modDelete', label: '删除回复' },
  { value: 'user.warn', label: '账号警告' },
  { value: 'user.mute', label: '账号禁言' },
  { value: 'user.unmute', label: '解除禁言' },
  { value: 'report.acknowledge', label: '标记举报已受理' },
  { value: 'report.resolve', label: '结案举报' },
  { value: 'report.reject', label: '驳回举报' },
]

export const MODERATION_AUDIT_ACTION_FILTER_OPTIONS: Array<{
  value: ModerationAuditActionFilterValue
  label: string
}> = [{ value: ALL_MODERATION_AUDIT_ACTIONS, label: '全部操作' }, ...MODERATION_AUDIT_ACTION_OPTIONS]

export function moderationAuditActionQueryValue(
  value: ModerationAuditActionFilterValue,
): ModerationAuditActionFilter | undefined {
  return value === ALL_MODERATION_AUDIT_ACTIONS ? undefined : value
}
