import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { AxiosResponse } from 'axios'
import { userApi, getRegistrationStatus } from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

const ok = (payload: unknown) =>
  ({ data: { success: true, data: payload } }) as unknown as AxiosResponse

describe('注册护栏 API（V0.3）', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('GET /auth/registration-status 透传关闭态', async () => {
    const get = vi.spyOn(userApi, 'get').mockResolvedValue(ok({ enabled: false, remainingToday: 0 }))

    const res = await getRegistrationStatus()

    expect(get).toHaveBeenCalledWith('/auth/registration-status')
    expect(res.data).toEqual({ enabled: false, remainingToday: 0 })
  })

  it('GET /auth/registration-status 透传剩余名额', async () => {
    vi.spyOn(userApi, 'get').mockResolvedValue(ok({ enabled: true, remainingToday: 7 }))

    const res = await getRegistrationStatus()

    expect(res.data?.enabled).toBe(true)
    expect(res.data?.remainingToday).toBe(7)
  })

  it('未设置日上限时 remainingToday 为 -1，原样保留', async () => {
    vi.spyOn(userApi, 'get').mockResolvedValue(ok({ enabled: true, remainingToday: -1 }))

    const res = await getRegistrationStatus()

    expect(res.data?.remainingToday).toBe(-1)
  })
})
