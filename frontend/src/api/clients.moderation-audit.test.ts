import axios from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { listModerationAuditByPost, modelApi } from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

const emptyPagedAudit = {
  success: true,
  data: { items: [], totalCount: 0, page: 1, pageSize: 10 },
}

describe('listModerationAuditByPost', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('单值 action 使用标量并配置 paramsSerializer，避免 action[] 格式', async () => {
    const getSpy = vi.spyOn(modelApi, 'get').mockResolvedValue({ data: emptyPagedAudit })

    await listModerationAuditByPost('post-1', { action: 'post.setSticky', page: 2, pageSize: 10 })

    expect(getSpy).toHaveBeenCalledOnce()
    const config = getSpy.mock.calls[0]![1]!
    expect(config.params).toMatchObject({
      targetType: 'post',
      targetId: 'post-1',
      page: 2,
      pageSize: 10,
      action: 'post.setSticky',
    })
    expect(config.paramsSerializer).toEqual({ indexes: null })

    const query = axios.getUri({
      url: '/mod/audit',
      params: config.params as Record<string, unknown>,
      paramsSerializer: config.paramsSerializer as { indexes: null },
    })
    expect(query).toContain('action=post.setSticky')
    expect(query).not.toMatch(/action\[\]/)
  })

  it('未选操作类型时不传 action 参数', async () => {
    const getSpy = vi.spyOn(modelApi, 'get').mockResolvedValue({ data: emptyPagedAudit })

    await listModerationAuditByPost('post-1', { page: 1, pageSize: 20 })
    const params = getSpy.mock.calls[0]![1]!.params as Record<string, unknown>
    expect(params.action).toBeUndefined()
  })
})
