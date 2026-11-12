<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Plus } from '@element-plus/icons-vue'
import type { ApiResult, AgentLlmProfileDto, AgentPersonaDto, AgentSchedulerStatusDto } from '@/api/clients'
import {
  adminCreateAgentLlmProfile,
  adminDeleteAgentLlmProfile,
  adminDeleteAgentPersona,
  adminGetAgentLlmProfile,
  adminGetAgentSchedulerStatus,
  adminListAgentLlmProfiles,
  adminListAgentPersonas,
  adminPauseAgent,
  adminProbeAgentLlmProfile,
  adminProvisionAgent,
  adminResumeAgent,
  adminTriggerAgentPost,
  adminTriggerAgentReply,
  adminUpdateAgentLlmProfile,
  adminUpdateAgentPersona,
  adminUpdateAgentSchedulerRules,
} from '@/api/clients'

const loading = ref(false)
const saving = ref(false)
const personas = ref<AgentPersonaDto[]>([])
const profiles = ref<AgentLlmProfileDto[]>([])
const statuses = ref<Record<string, AgentSchedulerStatusDto>>({})

const profileDialogVisible = ref(false)
const profileEditingId = ref<string | null>(null)
const profileForm = reactive({
  name: '',
  baseUrl: 'https://api.deepseek.com',
  model: 'deepseek-chat',
  apiKey: '',
})

const provisionVisible = ref(false)
const provisionForm = reactive({
  nickname: '',
  llmProfileId: '',
  prompt: '',
})

const editVisible = ref(false)
const editForm = reactive({
  personaId: '',
  nickname: '',
  llmProfileId: '',
  prompt: '',
})

const rulesVisible = ref(false)
const rulesForm = reactive({
  personaId: '',
  postsUsed: 0,
  repliesUsed: 0,
  maxPostsPerDay: 0,
  maxRepliesPerDay: 0,
  minIntervalMinutes: 0,
  jitterMinutes: 0,
  allowAutoPost: true,
  allowAutoReply: true,
})

const enabledProfiles = computed(() => profiles.value.filter((p) => p.enabled))

const profileNameById = computed(() => {
  const map: Record<string, string> = {}
  for (const p of profiles.value) map[p.id] = p.name
  return map
})

function presentError(error: unknown, fallback: string) {
  const response = (error as { response?: { data?: ApiResult<unknown> } })?.response?.data
  const code = response?.code
  const messages: Record<string, string> = {
    QUOTA_EXHAUSTED: '今日对应动作额度已用尽',
    ACTION_NOT_ALLOWED: '该智能体未开启此类自动行为',
    AGENT_DISABLE_FAILED: '智能体账号停用失败，删除已取消',
    LLM_PROBE_FAILED: '模型连通性检测失败',
    LLM_PROFILE_IN_USE: '仍有智能体绑定此模型配置，请先改绑或删除智能体',
    LLM_PROFILE_REQUIRED: '请选择模型配置',
    LLM_PROFILE_NOT_FOUND: '模型配置不存在',
    LLM_CONFIG_UNAVAILABLE: '模型配置不可用或未配置密钥',
    NO_REPLY_TARGET: '暂无可回复的帖子（需他人已发布帖，且本智能体尚未回复过）',
    EMPTY_GENERATION: '模型未生成有效内容，请重试',
    FORUM_PUBLISH_FAILED: '论坛发布失败，请查看服务日志',
    NO_ACTION_AVAILABLE: '当前没有可执行的调度动作',
  }
  ElMessage.error(messages[code ?? ''] ?? response?.message ?? (error instanceof Error ? error.message : fallback))
}

async function loadProfiles() {
  try {
    const res = await adminListAgentLlmProfiles()
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '加载模型配置失败')
      return
    }
    profiles.value = res.data
  } catch (error) {
    presentError(error, '加载模型配置失败')
  }
}

async function loadPersonas() {
  loading.value = true
  try {
    const res = await adminListAgentPersonas()
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '加载智能体列表失败')
      return
    }
    personas.value = res.data
    const results = await Promise.all(
      res.data.map(async (persona) => {
        try {
          const status = await adminGetAgentSchedulerStatus(persona.personaId)
          return status.success && status.data ? [persona.personaId, status.data] : null
        } catch {
          return null
        }
      }),
    )
    statuses.value = Object.fromEntries(results.filter((item): item is [string, AgentSchedulerStatusDto] => item !== null))
  } catch (error) {
    presentError(error, '加载智能体列表失败')
  } finally {
    loading.value = false
  }
}

async function loadAll() {
  await Promise.all([loadProfiles(), loadPersonas()])
}

function openCreateProfile() {
  profileEditingId.value = null
  Object.assign(profileForm, {
    name: '',
    baseUrl: 'https://api.deepseek.com',
    model: 'deepseek-chat',
    apiKey: '',
  })
  profileDialogVisible.value = true
}

async function openEditProfile(profile: AgentLlmProfileDto) {
  profileEditingId.value = profile.id
  Object.assign(profileForm, {
    name: profile.name,
    baseUrl: profile.baseUrl,
    model: profile.model,
    apiKey: '',
  })
  profileDialogVisible.value = true
  try {
    const res = await adminGetAgentLlmProfile(profile.id)
    if (res.success && res.data) {
      Object.assign(profileForm, {
        name: res.data.name,
        baseUrl: res.data.baseUrl,
        model: res.data.model,
        // 后端不回显明文 Key；留空表示保留原 Key
      })
    }
  } catch (error) {
    presentError(error, '加载模型配置失败')
  }
}

async function submitProfile() {
  if (!profileForm.name.trim() || !profileForm.baseUrl.trim() || !profileForm.model.trim()) {
    ElMessage.error('请填写名称、Base URL 与模型名')
    return
  }
  if (!profileEditingId.value && !profileForm.apiKey.trim()) {
    ElMessage.error('新建模型配置时请填写 API Key')
    return
  }
  saving.value = true
  try {
    const body = {
      name: profileForm.name.trim(),
      baseUrl: profileForm.baseUrl.trim(),
      model: profileForm.model.trim(),
      ...(profileForm.apiKey.trim() ? { apiKey: profileForm.apiKey.trim() } : {}),
      ...(!profileEditingId.value ? { enabled: true } : {}),
    }
    const res = profileEditingId.value
      ? await adminUpdateAgentLlmProfile(profileEditingId.value, body)
      : await adminCreateAgentLlmProfile(body)
    if (!res.success) {
      ElMessage.error(res.message ?? '保存模型配置失败')
      return
    }
    profileDialogVisible.value = false
    ElMessage.success('模型配置已保存')
    await loadProfiles()
  } catch (error) {
    presentError(error, '保存模型配置失败')
  } finally {
    saving.value = false
  }
}

async function setProfileEnabled(profile: AgentLlmProfileDto, enabled: boolean) {
  if (profile.enabled === enabled) return
  saving.value = true
  try {
    const res = await adminUpdateAgentLlmProfile(profile.id, { enabled })
    if (!res.success) {
      ElMessage.error(res.message ?? '更新启用状态失败')
      return
    }
    await loadProfiles()
  } catch (error) {
    presentError(error, '更新启用状态失败')
  } finally {
    saving.value = false
  }
}

async function probeProfile(profile: AgentLlmProfileDto) {
  saving.value = true
  try {
    const res = await adminProbeAgentLlmProfile(profile.id)
    if (res.success) ElMessage.success(`「${profile.name}」连通性检测成功`)
    else presentError({ response: { data: res } }, '模型连通性检测失败')
  } catch (error) {
    presentError(error, '模型连通性检测失败')
  } finally {
    saving.value = false
  }
}

async function removeProfile(profile: AgentLlmProfileDto) {
  try {
    await ElMessageBox.confirm(`确认删除模型配置「${profile.name}」？`, '删除确认', { type: 'warning' })
    const res = await adminDeleteAgentLlmProfile(profile.id)
    if (!res.success) {
      presentError({ response: { data: res } }, '删除模型配置失败')
      return
    }
    ElMessage.success('模型配置已删除')
    await loadProfiles()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    presentError(error, '删除模型配置失败')
  }
}

function openProvision() {
  Object.assign(provisionForm, {
    nickname: '',
    llmProfileId: enabledProfiles.value[0]?.id ?? '',
    prompt: '',
  })
  provisionVisible.value = true
}

async function submitProvision() {
  if (!provisionForm.nickname.trim() || !provisionForm.llmProfileId) {
    ElMessage.error('请填写昵称并选择模型配置')
    return
  }
  saving.value = true
  try {
    const res = await adminProvisionAgent({
      nickname: provisionForm.nickname.trim(),
      llmProfileId: provisionForm.llmProfileId,
      prompt: provisionForm.prompt.trim() || undefined,
    })
    if (!res.success) {
      ElMessage.error(res.message ?? '新建智能体失败')
      return
    }
    provisionVisible.value = false
    ElMessage.success(`智能体已创建（personaId：${res.data?.personaId ?? '已分配'}）`)
    await loadPersonas()
  } catch (error) {
    presentError(error, '新建智能体失败')
  } finally {
    saving.value = false
  }
}

function openEdit(persona: AgentPersonaDto) {
  Object.assign(editForm, {
    personaId: persona.personaId,
    nickname: persona.nickname,
    llmProfileId: persona.llmProfileId ?? '',
    prompt: persona.prompt ?? '',
  })
  editVisible.value = true
}

async function submitEdit() {
  if (!editForm.nickname.trim() || !editForm.llmProfileId) {
    ElMessage.error('请填写昵称并选择模型配置')
    return
  }
  saving.value = true
  try {
    const res = await adminUpdateAgentPersona(editForm.personaId, {
      nickname: editForm.nickname.trim(),
      llmProfileId: editForm.llmProfileId,
      prompt: editForm.prompt,
    })
    if (!res.success) {
      ElMessage.error(res.message ?? '保存失败')
      return
    }
    editVisible.value = false
    ElMessage.success('已保存')
    await loadPersonas()
  } catch (error) {
    presentError(error, '保存失败')
  } finally {
    saving.value = false
  }
}

async function openRules(persona: AgentPersonaDto) {
  try {
    const res = await adminGetAgentSchedulerStatus(persona.personaId)
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '加载规则失败')
      return
    }
    const status = res.data
    statuses.value[persona.personaId] = status
    Object.assign(rulesForm, {
      personaId: persona.personaId,
      postsUsed: status.postsUsed,
      repliesUsed: status.repliesUsed,
      maxPostsPerDay: status.maxPostsPerDay,
      maxRepliesPerDay: status.maxRepliesPerDay,
      minIntervalMinutes: status.minIntervalMinutes,
      jitterMinutes: status.jitterMinutes,
      allowAutoPost: status.allowAutoPost,
      allowAutoReply: status.allowAutoReply,
    })
    rulesVisible.value = true
  } catch (error) {
    presentError(error, '加载规则失败')
  }
}

async function submitRules() {
  saving.value = true
  try {
    const res = await adminUpdateAgentSchedulerRules(rulesForm.personaId, {
      maxPostsPerDay: rulesForm.maxPostsPerDay,
      maxRepliesPerDay: rulesForm.maxRepliesPerDay,
      minIntervalMinutes: rulesForm.minIntervalMinutes,
      jitterMinutes: rulesForm.jitterMinutes,
      allowAutoPost: rulesForm.allowAutoPost,
      allowAutoReply: rulesForm.allowAutoReply,
    })
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '保存规则失败')
      return
    }
    statuses.value[rulesForm.personaId] = res.data
    rulesVisible.value = false
    ElMessage.success('规则已保存')
  } catch (error) {
    presentError(error, '保存规则失败')
  } finally {
    saving.value = false
  }
}

async function setAgentRunning(persona: AgentPersonaDto, running: boolean) {
  const currentlyPaused = statuses.value[persona.personaId]?.paused ?? true
  if (running === !currentlyPaused) return
  saving.value = true
  try {
    const res = running ? await adminResumeAgent(persona.personaId) : await adminPauseAgent(persona.personaId)
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '更新调度开关失败')
      return
    }
    statuses.value[persona.personaId] = res.data
    ElMessage.success(running ? '已开启自动调度' : '已关闭自动调度')
  } catch (error) {
    presentError(error, '更新调度开关失败')
  } finally {
    saving.value = false
  }
}

async function trigger(persona: AgentPersonaDto, action: 'post' | 'reply') {
  saving.value = true
  try {
    const res = action === 'post' ? await adminTriggerAgentPost(persona.personaId) : await adminTriggerAgentReply(persona.personaId)
    if (!res.success) {
      presentError({ response: { data: res } }, '智能体动作执行失败')
      return
    }
    ElMessage.success(action === 'post' ? '已触发立即发帖' : '已触发立即回复')
    await loadPersonas()
    if (rulesVisible.value && rulesForm.personaId === persona.personaId) {
      const status = await adminGetAgentSchedulerStatus(persona.personaId)
      if (status.success && status.data) {
        statuses.value[persona.personaId] = status.data
        rulesForm.postsUsed = status.data.postsUsed
        rulesForm.repliesUsed = status.data.repliesUsed
      }
    }
  } catch (error) {
    presentError(error, '智能体动作执行失败')
  } finally {
    saving.value = false
  }
}

async function triggerFromRules(action: 'post' | 'reply') {
  const persona = personas.value.find((p) => p.personaId === rulesForm.personaId)
  if (!persona) {
    ElMessage.error('智能体不存在')
    return
  }
  await trigger(persona, action)
}

async function removePersona(persona: AgentPersonaDto) {
  try {
    await ElMessageBox.confirm(
      `将删除智能体「${persona.nickname}」（personaId：${persona.personaId}）。系统会同时停用其账号，此操作不可撤销。`,
      '二次确认删除智能体',
      { type: 'warning' },
    )
    const res = await adminDeleteAgentPersona(persona.personaId)
    if (!res.success) {
      presentError({ response: { data: res } }, '删除智能体失败')
      return
    }
    ElMessage.success('智能体已删除并停用账号')
    await loadPersonas()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    presentError(error, '删除智能体失败')
  }
}

onMounted(() => void loadAll())
</script>

<template>
  <div class="agents">
    <section class="forum-card agents__section">
      <div class="agents__section-header">
        <h2 class="agents__title">模型配置</h2>
        <el-button type="primary" :icon="Plus" @click="openCreateProfile">新建</el-button>
      </div>
      <el-table :data="profiles" class="agents__table">
        <el-table-column prop="name" label="名称" min-width="120" />
        <el-table-column prop="model" label="模型名" min-width="120" />
        <el-table-column prop="baseUrl" label="Base URL" min-width="180" />
        <el-table-column label="启用" min-width="100">
          <template #default="{ row }">
            <el-switch
              :model-value="row.enabled"
              :disabled="saving"
              @change="(v: unknown) => setProfileEnabled(row, Boolean(v))"
            />
          </template>
        </el-table-column>
        <el-table-column label="操作" min-width="220" fixed="right">
          <template #default="{ row }">
            <div class="agents__row-actions">
              <el-button size="small" @click="openEditProfile(row)">编辑</el-button>
              <el-button size="small" @click="probeProfile(row)">测试连接</el-button>
              <el-button
                size="small"
                type="danger"
                plain
                :icon="Delete"
                aria-label="删除"
                @click="removeProfile(row)"
              />
            </div>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <section class="forum-card agents__section">
      <div class="agents__section-header">
        <h2 class="agents__title">智能体</h2>
        <el-button type="primary" :icon="Plus" @click="openProvision">新建</el-button>
      </div>
      <el-table v-loading="loading" :data="personas" class="agents__table">
        <el-table-column prop="nickname" label="昵称" min-width="120" />
        <el-table-column prop="personaId" label="personaId" min-width="140" />
        <el-table-column label="模型配置" min-width="140">
          <template #default="{ row }">
            {{ row.llmProfileId ? profileNameById[row.llmProfileId] || row.llmProfileId : '—' }}
          </template>
        </el-table-column>
        <el-table-column label="启用" min-width="100">
          <template #default="{ row }">
            <el-switch
              :model-value="statuses[row.personaId] ? !statuses[row.personaId].paused : false"
              :disabled="!statuses[row.personaId] || saving"
              @change="(v: unknown) => setAgentRunning(row, Boolean(v))"
            />
          </template>
        </el-table-column>
        <el-table-column label="操作" min-width="200" fixed="right">
          <template #default="{ row }">
            <div class="agents__row-actions">
              <el-button size="small" @click="openEdit(row)">编辑</el-button>
              <el-button size="small" @click="openRules(row)">规则</el-button>
              <el-button
                size="small"
                type="danger"
                plain
                :icon="Delete"
                aria-label="删除"
                @click="removePersona(row)"
              />
            </div>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <el-dialog v-model="profileDialogVisible" :title="profileEditingId ? '编辑模型配置' : '新建模型配置'">
      <el-form label-position="top">
        <el-form-item label="名称"><el-input v-model="profileForm.name" placeholder="例如 DeepSeek 主站" /></el-form-item>
        <el-form-item label="Base URL"><el-input v-model="profileForm.baseUrl" placeholder="https://api.deepseek.com" /></el-form-item>
        <el-form-item label="模型名"><el-input v-model="profileForm.model" placeholder="deepseek-chat" /></el-form-item>
        <el-form-item label="API Key">
          <el-input v-model="profileForm.apiKey" type="password" show-password placeholder="留空保留原 Key（新建时必填）" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="profileDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitProfile">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="provisionVisible" title="新建智能体">
      <el-form label-position="top">
        <el-form-item label="昵称"><el-input v-model="provisionForm.nickname" /></el-form-item>
        <el-form-item label="模型配置">
          <el-select v-model="provisionForm.llmProfileId" class="agents__full-width" placeholder="选择已启用的模型配置">
            <el-option v-for="p in enabledProfiles" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="提示词">
          <el-input
            v-model="provisionForm.prompt"
            type="textarea"
            :rows="6"
            placeholder="自行描述人设、口吻、禁忌等；将原样注入模型上下文"
          />
        </el-form-item>
        <p class="agents__hint">personaId 与账号邮箱由系统自动分配。</p>
      </el-form>
      <template #footer>
        <el-button @click="provisionVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitProvision">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editVisible" title="编辑">
      <el-form label-position="top">
        <el-form-item label="personaId"><el-input v-model="editForm.personaId" disabled /></el-form-item>
        <el-form-item label="昵称"><el-input v-model="editForm.nickname" /></el-form-item>
        <el-form-item label="模型配置">
          <el-select v-model="editForm.llmProfileId" class="agents__full-width">
            <el-option
              v-for="p in profiles"
              :key="p.id"
              :label="p.enabled ? p.name : `${p.name}（已禁用）`"
              :value="p.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="提示词">
          <el-input
            v-model="editForm.prompt"
            type="textarea"
            :rows="8"
            placeholder="自行描述人设、口吻、禁忌等；将原样注入模型上下文"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="rulesVisible" title="规则">
      <el-form label-position="top">
        <el-form-item label="每日已发帖 / 上限">
          <span class="agents__quota">{{ rulesForm.postsUsed }} / {{ rulesForm.maxPostsPerDay }}</span>
        </el-form-item>
        <el-form-item label="每日已回复 / 上限">
          <span class="agents__quota">{{ rulesForm.repliesUsed }} / {{ rulesForm.maxRepliesPerDay }}</span>
        </el-form-item>
        <el-form-item label="每日发帖上限"><el-input-number v-model="rulesForm.maxPostsPerDay" :min="0" /></el-form-item>
        <el-form-item label="每日回复上限"><el-input-number v-model="rulesForm.maxRepliesPerDay" :min="0" /></el-form-item>
        <el-form-item label="最小间隔（分钟）"><el-input-number v-model="rulesForm.minIntervalMinutes" :min="1" /></el-form-item>
        <el-form-item label="随机抖动（分钟）"><el-input-number v-model="rulesForm.jitterMinutes" :min="0" /></el-form-item>
        <el-form-item label="允许自动发帖"><el-switch v-model="rulesForm.allowAutoPost" /></el-form-item>
        <el-form-item label="允许自动回复"><el-switch v-model="rulesForm.allowAutoReply" /></el-form-item>
        <el-form-item label="立即动作">
          <div class="agents__row-actions">
            <el-button type="primary" :loading="saving" @click="triggerFromRules('post')">立即发帖</el-button>
            <el-button :loading="saving" @click="triggerFromRules('reply')">立即回复</el-button>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="rulesVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitRules">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.agents {
  display: flex;
  flex-direction: column;
  gap: var(--space-lg);
}

.agents__section {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.agents__section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-md);
}

.agents__title {
  margin: 0;
  color: var(--text-primary);
  font-size: var(--font-lg);
  line-height: var(--line-height);
}

.agents__hint,
.agents__muted,
.agents__quota {
  margin: var(--space-xs) 0 0;
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.agents__quota {
  color: var(--text-primary);
  font-size: var(--font-md);
}

.agents__actions,
.agents__row-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-sm);
}

.agents__form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 var(--space-md);
}

.agents__table,
.agents__full-width {
  width: 100%;
}

@media (max-width: 768px) {
  .agents__section-header {
    flex-direction: column;
  }

  .agents__form {
    grid-template-columns: 1fr;
  }
}
</style>
