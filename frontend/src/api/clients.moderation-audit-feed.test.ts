import axios from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { exportModerationAuditCsv, listModerationAuditFeed, modelApi } from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

const emptyFeed = {
  success: true,
  data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
}

describe('listModerationAuditFeed', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('序列化 boardId 与 action，使用 paramsSerializer', async () => {
    const getSpy = vi.spyOn(modelApi, 'get').mockResolvedValue({ data: emptyFeed })

    await listModerationAuditFeed({
      page: 2,
      pageSize: 10,
      boardId: 'general',
      action: 'report.resolve',
      fromUtc: '2026-01-01T00:00:00.000Z',
      toUtc: '2026-06-01T00:00:00.000Z',
    })

    const config = getSpy.mock.calls[0]![1]!
    expect(config.params).toMatchObject({
      page: 2,
      pageSize: 10,
      boardId: 'general',
      action: 'report.resolve',
      fromUtc: '2026-01-01T00:00:00.000Z',
      toUtc: '2026-06-01T00:00:00.000Z',
    })
    expect(config.paramsSerializer).toEqual({ indexes: null })

    const query = axios.getUri({
      url: '/mod/audit/feed',
      params: config.params as Record<string, unknown>,
      paramsSerializer: config.paramsSerializer as { indexes: null },
    })
    expect(query).toContain('boardId=general')
    expect(query).toContain('action=report.resolve')
    expect(query).not.toMatch(/action\[\]/)
  })
})

describe('exportModerationAuditCsv', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('导出请求不传 page/pageSize', async () => {
    const getSpy = vi.spyOn(modelApi, 'get').mockResolvedValue({
      data: new Blob(['occurredAtUtc\n']),
      headers: { 'content-disposition': 'attachment; filename="audit.csv"' },
    })

    await exportModerationAuditCsv({
      page: 2,
      pageSize: 10,
      boardId: 'general',
      action: 'post.setSticky',
    })

    const config = getSpy.mock.calls[0]![1]!
    expect(config.params).toEqual({
      boardId: 'general',
      action: 'post.setSticky',
    })
    expect(config.responseType).toBe('blob')
  })
})
