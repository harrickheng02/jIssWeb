import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

import { getForumInit } from './clients'

const mockBundle = {
  boards: [{ id: 'b1', name: '综合', description: '', postCount: 0, replyCount: 0 }],
  announcements: [
    {
      id: 'a1',
      title: '公告',
      content: '内容',
      authorId: 'u1',
      authorNickname: 'admin',
      isPinned: true,
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
    },
  ],
  posts: { items: [], total: 0, page: 1, pageSize: 20, totalPages: 0 },
  popularTags: ['vue', 'dotnet'],
}

const server = setupServer(
  http.get('http://localhost:3000/api/bff/forum-init', () =>
    HttpResponse.json({ success: true, data: mockBundle }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('getForumInit', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('正常响应时解析聚合数据', async () => {
    const res = await getForumInit({ page: 1, pageSize: 20 })
    expect(res.success).toBe(true)
    expect(res.data?.boards).toHaveLength(1)
    expect(res.data?.boards[0].id).toBe('b1')
    expect(res.data?.popularTags).toEqual(['vue', 'dotnet'])
    expect(res.data?.warnings).toBeUndefined()
  })

  it('含 warnings 字段时正确透传', async () => {
    server.use(
      http.get('http://localhost:3000/api/bff/forum-init', () =>
        HttpResponse.json({
          success: true,
          data: { ...mockBundle, warnings: ['Model API timeout'] },
        }),
      ),
    )
    const res = await getForumInit()
    expect(res.success).toBe(true)
    expect(res.data?.warnings).toEqual(['Model API timeout'])
  })

  it('无参数时请求仍成功', async () => {
    const res = await getForumInit()
    expect(res.success).toBe(true)
    expect(Array.isArray(res.data?.boards)).toBe(true)
  })
})
