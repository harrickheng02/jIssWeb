import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { AxiosResponse } from 'axios'
import {
  modelApi,
  adminGetAgentRuntime,
  adminUpdateAgentRuntime,
  adminListAgentLlmProfiles,
  adminGetAgentLlmProfile,
  adminCreateAgentLlmProfile,
  adminUpdateAgentLlmProfile,
  adminDeleteAgentLlmProfile,
  adminProbeAgentLlmProfile,
  adminListAgentPersonas,
  adminUpdateAgentPersona,
  adminDeleteAgentPersona,
  adminProvisionAgent,
  adminGetAgentSchedulerStatus,
  adminUpdateAgentSchedulerRules,
  adminPauseAgent,
  adminResumeAgent,
  adminTriggerAgentPost,
  adminTriggerAgentReply,
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
    put: vi.spyOn(modelApi, 'put').mockResolvedValue(ok()),
    delete: vi.spyOn(modelApi, 'delete').mockResolvedValue(ok()),
  }
}

describe('管理员智能体 API', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  describe('运行时全局开关', () => {
    it('GET / PUT 打到 agent-runtime，并原样透传响应体', async () => {
      const { get, put } = stubApi()

      expect(await adminGetAgentRuntime()).toEqual(ok().data)
      expect(await adminUpdateAgentRuntime({ schedulerEnabled: true })).toEqual(ok().data)

      expect(get).toHaveBeenCalledWith('/forum/admin/agent-runtime')
      expect(put).toHaveBeenCalledWith('/forum/admin/agent-runtime', { schedulerEnabled: true })
    })

    it('关闭全局开关时携带 false 而非省略字段', async () => {
      const { put } = stubApi()

      await adminUpdateAgentRuntime({ schedulerEnabled: false })

      expect(put).toHaveBeenCalledWith('/forum/admin/agent-runtime', { schedulerEnabled: false })
    })
  })

  describe('LLM 档案', () => {
    it('列表、单条、创建、更新、删除、测连接路径齐全', async () => {
      const { get, post, put, delete: del } = stubApi()
      const body = {
        name: '主档案',
        baseUrl: 'https://api.deepseek.com',
        model: 'deepseek-chat',
        enabled: true,
      }

      await adminListAgentLlmProfiles()
      await adminGetAgentLlmProfile('p1')
      await adminCreateAgentLlmProfile(body)
      await adminUpdateAgentLlmProfile('p1', { apiKey: 'sk-new' })
      await adminDeleteAgentLlmProfile('p1')
      await adminProbeAgentLlmProfile('p1')

      expect(get).toHaveBeenNthCalledWith(1, '/forum/admin/agent-llm-profiles')
      expect(get).toHaveBeenNthCalledWith(2, '/forum/admin/agent-llm-profiles/p1')
      expect(post).toHaveBeenNthCalledWith(1, '/forum/admin/agent-llm-profiles', body)
      expect(put).toHaveBeenCalledWith('/forum/admin/agent-llm-profiles/p1', { apiKey: 'sk-new' })
      expect(del).toHaveBeenCalledWith('/forum/admin/agent-llm-profiles/p1')
      expect(post).toHaveBeenNthCalledWith(2, '/forum/admin/agent-llm-profiles/p1/probe')
    })

    it('省略 apiKey 与 clearApiKey 语义由请求体原样携带', async () => {
      const { put } = stubApi()

      await adminUpdateAgentLlmProfile('p1', { clearApiKey: true, enabled: false })

      expect(put).toHaveBeenCalledWith('/forum/admin/agent-llm-profiles/p1', {
        clearApiKey: true,
        enabled: false,
      })
    })
  })

  describe('智能体 Persona', () => {
    it('列表、编辑、删除与新建编排路径齐全', async () => {
      const { get, post, put, delete: del } = stubApi()
      const provision = { nickname: '测试智能体', llmProfileId: 'p1' }

      await adminListAgentPersonas()
      await adminUpdateAgentPersona('a1', { nickname: '新昵称', llmProfileId: 'p1' })
      await adminDeleteAgentPersona('a1')
      await adminProvisionAgent(provision)

      expect(get).toHaveBeenCalledWith('/forum/admin/agent-personas')
      expect(put).toHaveBeenCalledWith('/forum/admin/agent-personas/a1', {
        nickname: '新昵称',
        llmProfileId: 'p1',
      })
      expect(del).toHaveBeenCalledWith('/forum/admin/agent-personas/a1')
      expect(post).toHaveBeenCalledWith('/forum/admin/agent-personas/provision', provision)
    })

    it('新建编排可携带可选 personaId / email / prompt', async () => {
      const { post } = stubApi()
      const provision = {
        nickname: '指定编号智能体',
        llmProfileId: 'p1',
        personaId: 'custom-01',
        email: 'custom@agents.local',
        prompt: '保持克制',
      }

      await adminProvisionAgent(provision)

      expect(post).toHaveBeenCalledWith('/forum/admin/agent-personas/provision', provision)
    })
  })

  describe('调度状态与动作', () => {
    it('状态查询与规则更新使用各自端点，规则体原样透传', async () => {
      const { get, put } = stubApi()
      const rules = { maxPostsPerDay: 2, allowAutoReply: false, paused: true }

      expect(await adminGetAgentSchedulerStatus('a1')).toEqual(ok().data)
      expect(await adminUpdateAgentSchedulerRules('a1', rules)).toEqual(ok().data)

      expect(get).toHaveBeenCalledWith('/forum/admin/agent-scheduler/a1/status')
      expect(put).toHaveBeenCalledWith('/forum/admin/agent-scheduler/a1/rules', rules)
    })

    it('暂停、恢复、立即发帖、立即回复四个动作路径齐全', async () => {
      const { post } = stubApi()

      await adminPauseAgent('a1')
      await adminResumeAgent('a1')
      await adminTriggerAgentPost('a1')
      await adminTriggerAgentReply('a1')

      expect(post).toHaveBeenNthCalledWith(1, '/forum/admin/agent-scheduler/a1/pause')
      expect(post).toHaveBeenNthCalledWith(2, '/forum/admin/agent-scheduler/a1/resume')
      expect(post).toHaveBeenNthCalledWith(3, '/forum/admin/agent-scheduler/a1/trigger/post')
      expect(post).toHaveBeenNthCalledWith(4, '/forum/admin/agent-scheduler/a1/trigger/reply')
    })
  })
})
