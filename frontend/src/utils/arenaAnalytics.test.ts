import { describe, it, expect } from 'vitest'
import {
  ARENA_ANALYTICS_ALLOWED_KEYS,
  assertAllowedAnalyticsKeys,
  buildArenaAccusationEvent,
  sanitizeArenaAnalytics,
  hashAccuserId,
  getArenaWeekKey,
} from './arenaAnalytics'

describe('arenaAnalytics 白名单守门', () => {
  it('白名单仅含 ts / accuserIdHash / weekKey', () => {
    expect(ARENA_ANALYTICS_ALLOWED_KEYS).toEqual(['ts', 'accuserIdHash', 'weekKey'])
  })

  it('buildArenaAccusationEvent 仅产出白名单字段，绝不携带 targetUserIdHash', () => {
    const ev = buildArenaAccusationEvent({ accuserId: 'user-123', weekKey: '2026-11-16' })
    expect(Object.keys(ev).sort()).toEqual(['accuserIdHash', 'ts', 'weekKey'])
    expect(ev).not.toHaveProperty('targetUserIdHash')
    expect(ev).not.toHaveProperty('targetUserId')
    expect(ev.weekKey).toBe('2026-11-16')
    expect(typeof ev.ts).toBe('number')
  })

  it('assertAllowedAnalyticsKeys 在出现 targetUserIdHash 时抛错', () => {
    expect(() =>
      assertAllowedAnalyticsKeys({ ts: 1, accuserIdHash: 'abc', weekKey: 'w', targetUserIdHash: 'x' }),
    ).toThrow(/targetUserIdHash|disallowed key/)
  })

  it('assertAllowedAnalyticsKeys 放行纯白名单负载', () => {
    expect(() =>
      assertAllowedAnalyticsKeys({ ts: 1, accuserIdHash: 'abc', weekKey: 'w' }),
    ).not.toThrow()
  })

  it('hashAccuserId 确定性且不可逆（不回显明文）', () => {
    const a = hashAccuserId('user-123')
    const b = hashAccuserId('user-123')
    const c = hashAccuserId('user-456')
    expect(a).toBe(b)
    expect(a).not.toBe(c)
    expect(a).not.toContain('user-123')
    expect(a).toMatch(/^[0-9a-f]{8}$/)
  })

  it('sanitizeArenaAnalytics 裁剪掉 targetUserIdHash', () => {
    const out = sanitizeArenaAnalytics({
      ts: 1,
      accuserIdHash: 'abc',
      weekKey: 'w',
      targetUserIdHash: 'secret',
      targetUserId: 'user-9',
    })
    expect(Object.keys(out).sort()).toEqual(['accuserIdHash', 'ts', 'weekKey'])
    expect(out).not.toHaveProperty('targetUserIdHash')
    expect(out).not.toHaveProperty('targetUserId')
  })

  it('getArenaWeekKey 返回周一日期且稳定', () => {
    // 2026-11-18 是周三 → 周一为 2026-11-16
    const k = getArenaWeekKey(new Date(2026, 10, 18))
    expect(k).toBe('2026-11-16')
    // 同一周的周日也应归到同一周一
    expect(getArenaWeekKey(new Date(2026, 10, 22))).toBe('2026-11-16')
  })
})
