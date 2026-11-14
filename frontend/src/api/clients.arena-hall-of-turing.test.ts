import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosError, AxiosResponse } from 'axios'
import {
  getArenaAiqiTrend,
  getArenaProfile,
  getHallOfTuring,
  reportApi,
  type ArenaProfileDto,
  type HallOfTuringDto,
} from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

const ok = (payload: unknown = null) =>
  ({ data: { success: true, data: payload } }) as unknown as AxiosResponse

const notFound = (message = '未找到该名人堂档案') =>
  ({
    response: { status: 404, data: { success: false, code: 'NOT_FOUND', message } },
  }) as unknown as AxiosError

describe('图灵名人堂 API（#37）', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('getHallOfTuring: GET /report/arena/hall-of-turing 并透传响应体', async () => {
    const sample: HallOfTuringDto = {
      items: [],
      activeSurvivorCount: 1,
    }
    const get = vi.spyOn(reportApi, 'get').mockResolvedValue(ok(sample))

    const res = await getHallOfTuring()

    expect(get).toHaveBeenCalledWith('/report/arena/hall-of-turing')
    expect(res).toEqual(ok(sample).data)
  })

  it('getHallOfTuring: 对公开卡片 + 匿名幸存者计数做形态断言（只读契约）', async () => {
    const sample: HallOfTuringDto = {
      items: [
        {
          personaId: 'per1',
          nickname: 'Alpha',
          finalBounty: 400,
          discovered: true,
          discoveredBy: ['侦探#abc123'],
          drivingModel: 'DeepSeek-V3',
          generation: 'Gen-1',
          topTopics: [{ tag: 'AI伦理', count: 3 }],
          interactionSummary: { likes: 21, favorites: 4, comments: 4, views: 380 },
          survivalDays: 30,
          externalAttribution: null,
        },
      ],
      activeSurvivorCount: 1,
    }
    const get = vi.spyOn(reportApi, 'get').mockResolvedValue(ok(sample))

    const res = await getHallOfTuring()

    expect(get).toHaveBeenCalledWith('/report/arena/hall-of-turing')
    expect(res.data).toMatchObject({
      activeSurvivorCount: 1,
      items: [{ personaId: 'per1', discoveredBy: ['侦探#abc123'], externalAttribution: null }],
    })
  })

  it('getArenaProfile: 对获奖 personaId 编码后请求正确路径', async () => {
    const sample: ArenaProfileDto = {
      personaId: 'per1',
      nickname: 'Alpha',
      survivalDays: 30,
      timeline: [],
      confession: null,
      nearDanger: [],
      triggerPost: null,
      interactionSummary: { likes: 0, favorites: 0, comments: 0, views: 0 },
      externalAttribution: null,
    }
    const get = vi.spyOn(reportApi, 'get').mockResolvedValue(ok(sample))

    const res = await getArenaProfile('per1')

    expect(get).toHaveBeenCalledWith('/report/arena/profile/per1')
    expect(res.data?.personaId).toBe('per1')
  })

  it('getArenaProfile: 非奖章 / 幸存者返回 404 时降级为 success:false code NOT_FOUND（不抛异常）', async () => {
    vi.spyOn(reportApi, 'get').mockRejectedValue(notFound())

    const res = await getArenaProfile('ghost')

    expect(res.success).toBe(false)
    expect(res.code).toBe('NOT_FOUND')
  })

  it('getArenaAiqiTrend: GET /report/arena/aiqi-trend 带 limitWeeks 参数', async () => {
    const get = vi
      .spyOn(reportApi, 'get')
      .mockResolvedValue(ok([{ weekKey: '2026-11-10', aiqi: 0.82, activeUsers: 150 }]))

    const res = await getArenaAiqiTrend(12)

    expect(get).toHaveBeenCalledWith('/report/arena/aiqi-trend', { params: { limitWeeks: 12 } })
    expect(res.data?.[0]).toMatchObject({ weekKey: '2026-11-10', aiqi: 0.82 })
  })
})
