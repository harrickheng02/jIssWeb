import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/clients', () => ({
  getMyHunterBadge: vi.fn(),
}))

import * as clients from '@/api/clients'
import { useHunterBadge } from './useHunterBadge'

const sample = {
  badgeId: 'turing-hunter',
  targetWeekKey: '2026-07-06',
  bountyPoints: 400,
  awardedAtUtc: '2026-07-10T14:00:00Z',
}

describe('useHunterBadge', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('加载本人徽章并暴露全额悬赏积分', async () => {
    vi.mocked(clients.getMyHunterBadge).mockResolvedValue({ success: true, data: sample })
    const { badge, error, loading, load } = useHunterBadge()

    await load()

    expect(badge.value?.bountyPoints).toBe(400)
    expect(badge.value?.badgeId).toBe('turing-hunter')
    expect(error.value).toBeNull()
    expect(loading.value).toBe(false)
  })

  it('无徽章时保持 null 且不进入错误态', async () => {
    vi.mocked(clients.getMyHunterBadge).mockResolvedValue({ success: true, data: null })
    const { badge, error, load } = useHunterBadge()

    await load()

    expect(badge.value).toBeNull()
    expect(error.value).toBeNull()
  })

  it('接口失败时置错误态并允许重试恢复', async () => {
    vi.mocked(clients.getMyHunterBadge)
      .mockResolvedValueOnce({ success: false, message: '未认证' })
      .mockResolvedValueOnce({ success: true, data: sample })
    const { badge, error, load } = useHunterBadge()

    await load()
    expect(error.value).toBe('未认证')
    expect(badge.value).toBeNull()

    await load()
    expect(error.value).toBeNull()
    expect(badge.value?.badgeId).toBe('turing-hunter')
  })

  it('网络异常时 badge 为 null、错误态可读且 loading 归位', async () => {
    vi.mocked(clients.getMyHunterBadge).mockRejectedValue(new Error('network'))
    const { badge, error, loading, load } = useHunterBadge()

    await load()

    expect(badge.value).toBeNull()
    expect(error.value).toBe('徽章加载失败')
    expect(loading.value).toBe(false)
  })
})
