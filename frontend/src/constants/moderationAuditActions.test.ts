import { describe, expect, it } from 'vitest'
import {
  ALL_MODERATION_AUDIT_ACTIONS,
  MODERATION_AUDIT_ACTION_FILTER_OPTIONS,
  moderationAuditActionQueryValue,
} from './moderationAuditActions'

describe('moderationAuditActions', () => {
  it('全部操作时不传 action 参数', () => {
    expect(moderationAuditActionQueryValue(ALL_MODERATION_AUDIT_ACTIONS)).toBeUndefined()
    expect(moderationAuditActionQueryValue('report.resolve')).toBe('report.resolve')
  })

  it('筛选选项首项为全部操作', () => {
    expect(MODERATION_AUDIT_ACTION_FILTER_OPTIONS[0]).toEqual({
      value: ALL_MODERATION_AUDIT_ACTIONS,
      label: '全部操作',
    })
  })
})
