import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getMyDrafts } from '@/api/clients'

export const useDraftUiStore = defineStore('draftUi', () => {
  const badgeCount = ref(0)

  async function refreshBadgeFromServer() {
    try {
      const res = await getMyDrafts(1, 1)
      badgeCount.value = res.success && res.data ? res.data.totalCount : 0
    } catch {
      badgeCount.value = 0
    }
  }

  function clearBadge() {
    badgeCount.value = 0
  }

  return { badgeCount, refreshBadgeFromServer, clearBadge }
})
