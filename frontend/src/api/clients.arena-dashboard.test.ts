import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosResponse } from 'axios'
import { getArenaDashboard, reportApi, type ArenaDashboardDto } from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

const ok = (payload: unknown = null) =>
  ({ data: { success: true, data: payload } }) as unknown as AxiosResponse

describe('运营仪表盘 API（#36）', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('GET /report/arena-dashboard 并原样透传响应体', async () => {
    const get = vi.spyOn(reportApi, 'get').mockResolvedValue(ok({ generatedAtUtc: '2026-11-14T00:00:00Z' }))

    const res = await getArenaDashboard()

    expect(get).toHaveBeenCalledWith('/report/arena-dashboard')
    expect(res).toEqual(ok({ generatedAtUtc: '2026-11-14T00:00:00Z' }).data)
  })

  it('对 5 面板聚合结构做形态断言（只读契约）', async () => {
    const sample: ArenaDashboardDto = {
      generatedAtUtc: '2026-11-14T00:00:00Z',
      systemHealth: { schedulerStatus: 'healthy', schedulerLastTickAtUtc: '2026-11-14T00:00:00Z', llmProfiles: [], agentAliveCount: 1, agentTotalCount: 2, pendingReveal: [] },
      economy: { weeklyNetFlow: 0, netFlowRatioPct: 0, netFlowLevel: 'healthy', pointsInjectionDensity: 0, lastWeekDensity: 0, aiqi: 0, aiqiConfident: false },
      gameplay: { weeklyAccusations: 0, nextRevealAtUtc: '2026-11-20T14:00:00Z', legendaryPrey: [], topDetectives: [] },
      queue: { pendingReports: [], agentAlerts: [] },
      community: { weeklyActiveHumans: 0, weeklyPostsTotal: 0, weeklyPostsAi: 0, weeklyPostsHuman: 0 },
    }
    const get = vi.spyOn(reportApi, 'get').mockResolvedValue(ok(sample))

    const res = await getArenaDashboard()

    expect(get).toHaveBeenCalledWith('/report/arena-dashboard')
    expect(res.data).toMatchObject({
      systemHealth: { schedulerStatus: 'healthy' },
      economy: { netFlowLevel: 'healthy' },
      gameplay: { weeklyAccusations: 0 },
      queue: { pendingReports: [], agentAlerts: [] },
      community: { weeklyPostsTotal: 0 },
    })
  })
})
