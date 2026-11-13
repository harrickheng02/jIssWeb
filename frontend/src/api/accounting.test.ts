import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

import {
  behaviorScan,
  getPointsBalance,
  getPointsHistory,
  getSkillCatalog,
  publicInquiry,
  purchaseSkill,
} from './accounting'

const base = 'http://localhost:3000/api-accounting'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('accounting API client', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    server.use(
      http.get(`${base}/points/balance`, () =>
        HttpResponse.json({ success: true, data: { balance: 120 } }),
      ),
      http.get(`${base}/points/history`, () =>
        HttpResponse.json({
          success: true,
          data: [
            { type: 'accusation_hit', delta: 50, balanceAfter: 120, occurredAtUtc: '2026-02-02T00:00:00Z' },
          ],
        }),
      ),
      http.get(`${base}/skills/catalog`, () =>
        HttpResponse.json({
          success: true,
          data: [
            { id: 'behavior_scan', name: '行为扫描', price: 30 },
            { id: 'death_shield', name: '免死护盾', price: 50 },
            { id: 'public_inquiry', name: '公开质询', price: 80 },
          ],
        }),
      ),
    )
  })

  it('getPointsBalance 解析余额', async () => {
    const r = await getPointsBalance()
    expect(r.success).toBe(true)
    expect(r.data?.balance).toBe(120)
  })

  it('getPointsHistory 解析流水数组', async () => {
    const r = await getPointsHistory(1, 20)
    expect(r.success).toBe(true)
    expect(r.data?.[0].delta).toBe(50)
  })

  it('getSkillCatalog 返回三个技能', async () => {
    const r = await getSkillCatalog()
    expect(r.success).toBe(true)
    expect(r.data).toHaveLength(3)
  })

  it('purchaseSkill 发送 skillId 并解析结果', async () => {
    server.use(
      http.post(`${base}/skills/purchase`, async ({ request }) => {
        const body = (await request.json()) as { skillId: string }
        expect(body.skillId).toBe('behavior_scan')
        return HttpResponse.json({ success: true })
      }),
    )
    const r = await purchaseSkill('behavior_scan')
    expect(r.success).toBe(true)
  })

  it('purchaseSkill 余额不足时返回 success=false', async () => {
    server.use(
      http.post(`${base}/skills/purchase`, () =>
        HttpResponse.json({ success: false, message: '积分不足', code: 'INSUFFICIENT_BALANCE' }),
      ),
    )
    const r = await purchaseSkill('public_inquiry')
    expect(r.success).toBe(false)
    expect(r.code).toBe('INSUFFICIENT_BALANCE')
  })

  it('behaviorScan 透传词云', async () => {
    server.use(
      http.get(`${base}/skills/behavior-scan`, () =>
        HttpResponse.json({ success: true, data: [{ word: 'alpha', count: 4 }] }),
      ),
    )
    const r = await behaviorScan('target1')
    expect(r.success).toBe(true)
    expect(r.data?.[0].word).toBe('alpha')
  })

  it('publicInquiry 返回生成的帖子 id', async () => {
    server.use(
      http.post(`${base}/skills/public-inquiry`, () =>
        HttpResponse.json({ success: true, data: { postId: 'p999' } }),
      ),
    )
    const r = await publicInquiry('target1', '你是人类吗？')
    expect(r.success).toBe(true)
    expect(r.data?.postId).toBe('p999')
  })
})
