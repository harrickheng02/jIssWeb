import { reactive, ref } from 'vue'
import { getProfile, updateProfile } from '@/api/clients'

export interface ProfileForm {
  nickname: string
  birthDate: string
  gender: string
}

/**
 * 用户个人资料加载与保存的 composable。
 * ProfileView 通过此接口访问资料 API，不直接调用 api/clients。
 */
export function useProfile() {
  const loading = ref(false)
  const saving = ref(false)
  const form = reactive<ProfileForm>({
    nickname: '',
    birthDate: '',
    gender: '',
  })

  async function loadProfile() {
    loading.value = true
    try {
      const res = await getProfile()
      if (!res.success || !res.data) {
        return { success: false as const, message: res.message ?? '加载失败' }
      }
      form.nickname = res.data.nickname ?? ''
      form.gender = res.data.gender ?? ''
      form.birthDate = res.data.birthDate ? res.data.birthDate.slice(0, 10) : ''
      return { success: true as const }
    } finally {
      loading.value = false
    }
  }

  async function saveProfile(payload: { nickname?: string; birthDate?: string; gender?: string }) {
    saving.value = true
    try {
      const res = await updateProfile(payload)
      if (!res.success) {
        return { success: false as const, message: res.message ?? '保存失败' }
      }
      return { success: true as const }
    } finally {
      saving.value = false
    }
  }

  return {
    loading,
    saving,
    form,
    loadProfile,
    saveProfile,
  }
}
