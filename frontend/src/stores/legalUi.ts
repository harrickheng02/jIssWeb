import { defineStore } from 'pinia'
import { ref } from 'vue'

export const useLegalUiStore = defineStore('legalUi', () => {
  const agreementOpen = ref(false)
  const privacyOpen = ref(false)

  function openAgreement() {
    agreementOpen.value = true
  }

  function openPrivacy() {
    privacyOpen.value = true
  }

  return { agreementOpen, privacyOpen, openAgreement, openPrivacy }
})
