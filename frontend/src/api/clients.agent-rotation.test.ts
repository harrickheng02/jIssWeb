import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { AxiosResponse } from 'axios'
import {
  modelApi,
  adminGetAgentRotationStatus,
  adminListAgentRotationRuns,
  adminTriggerAgentRotation,
  type AgentRotationRunDto,
  type AgentRotationStatusDto,
} from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

const ok = (payload: unknown = null) =>
  ({ data: { success: true, data: payload } }) as unknown as AxiosResponse

describe('账号轮换器 API（V0.3）', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('GET /forum/admin/agent-rotation/status 原样透传水位', async () => {
    const status: AgentRotationStatusDto = {
      enabled: true,
      activeCount: 4,
      minActiveAgents: 6,
      maxActiveAgents: 12,
      maxPerRun: 2,
      gap: 2,
    }
    const get = vi.spyOn(modelApi, 'get').mockResolvedValue(ok(status))

    const res = await adminGetAgentRotationStatus()

    expect(get).toHaveBeenCalledWith('/forum/admin/agent-rotation/status')
    expect(res).toEqual(ok(status).data)
  })

  it('GET /runs 带上 limit 查询参数', async () => {
    const get = vi.spyOn(modelApi, 'get').mockResolvedValue(ok([]))

    await adminListAgentRotationRuns(5)

    expect(get).toHaveBeenCalledWith('/forum/admin/agent-rotation/runs', { params: { limit: 5 } })
  })

  it('默认 limit 为 20', async () => {
    const get = vi.spyOn(modelApi, 'get').mockResolvedValue(ok([]))

    await adminListAgentRotationRuns()

    expect(get).toHaveBeenCalledWith('/forum/admin/agent-rotation/runs', { params: { limit: 20 } })
  })

  it('POST /run 不带请求体，返回本次运行记录', async () => {
    const run: AgentRotationRunDto = {
      id: 'run-1',
      runKey: 'manual:20261117120000',
      trigger: 'manual',
      gapBefore: 2,
      createdCount: 2,
      failedCount: 0,
      activeCountAfter: 6,
      startedAtUtc: '2026-11-17T12:00:00Z',
      completedAtUtc: '2026-11-17T12:00:03Z',
      triggeredBySub: 'moderator-1',
      failures: [],
    }
    const post = vi.spyOn(modelApi, 'post').mockResolvedValue(ok(run))

    const res = await adminTriggerAgentRotation()

    expect(post).toHaveBeenCalledWith('/forum/admin/agent-rotation/run')
    expect(res).toEqual(ok(run).data)
  })

  it('运行记录结构含缺口/补建/失败/操作人字段', () => {
    const run: AgentRotationRunDto = {
      id: 'run-2',
      runKey: 'reveal:2026-W46',
      trigger: 'reveal',
      gapBefore: 3,
      createdCount: 2,
      failedCount: 1,
      activeCountAfter: 5,
      startedAtUtc: '2026-11-13T14:00:00Z',
      failures: ['AGENT_PROVISION_FAILED: 智能体账号创建失败'],
    }

    expect(run.trigger).toBe('reveal')
    expect(run.gapBefore).toBe(3)
    expect(run.createdCount + run.failedCount).toBe(3)
    expect(run.triggeredBySub).toBeUndefined()
    expect(run.failures).toHaveLength(1)
  })
})
