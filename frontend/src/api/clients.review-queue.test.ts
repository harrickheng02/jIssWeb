import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { AxiosResponse } from 'axios'
import {
  modelApi,
  listModerationReviewQueue,
  approveModerationReviewItem,
  rejectModerationReviewItem,
} from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

const ok = (payload: unknown = null) =>
  ({ data: { success: true, data: payload } }) as unknown as AxiosResponse

function stubApi() {
  return {
    get: vi.spyOn(modelApi, 'get').mockResolvedValue(ok()),
    post: vi.spyOn(modelApi, 'post').mockResolvedValue(ok()),
  }
}

describe('内容复审队列 API', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('列表按状态过滤并透传分页参数', async () => {
    const { get } = stubApi()

    await listModerationReviewQueue(2, 10, 'flagged')

    expect(get).toHaveBeenCalledWith('/mod/review-queue', {
      params: { page: 2, pageSize: 10, status: 'flagged' },
    })
  })

  it('不传状态时不带 status 参数', async () => {
    const { get } = stubApi()

    await listModerationReviewQueue()

    expect(get).toHaveBeenCalledWith('/mod/review-queue', { params: { page: 1, pageSize: 20 } })
  })

  it('通过动作打到 approve 端点', async () => {
    const { post } = stubApi()

    await approveModerationReviewItem('q-1')

    expect(post).toHaveBeenCalledWith('/mod/review-queue/q-1/approve')
  })

  it('驳回动作打到 reject 端点并转义 id', async () => {
    const { post } = stubApi()

    await rejectModerationReviewItem('q/2')

    expect(post).toHaveBeenCalledWith('/mod/review-queue/q%2F2/reject')
  })
})
