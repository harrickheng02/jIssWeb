<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getProfile, updateProfile } from '@/api/clients'

const loading = ref(false)
const saving = ref(false)
const form = reactive({
  nickname: '',
  birthDate: '',
  gender: '',
})

async function loadProfile() {
  loading.value = true
  try {
    const res = await getProfile()
    if (!res.success || !res.data) throw new Error(res.message ?? '加载失败')
    form.nickname = res.data.nickname ?? ''
    form.gender = res.data.gender ?? ''
    form.birthDate = res.data.birthDate ? res.data.birthDate.slice(0, 10) : ''
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message ?? e?.message ?? '加载失败')
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    const res = await updateProfile({
      nickname: form.nickname || undefined,
      gender: form.gender || undefined,
      birthDate: form.birthDate || undefined,
    })
    if (!res.success) throw new Error(res.message ?? '保存失败')
    ElMessage.success('保存成功')
    window.dispatchEvent(new CustomEvent('jiss-profile-updated'))
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message ?? e?.message ?? '保存失败')
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  void loadProfile()
})
</script>

<template>
  <div class="page">
    <el-card class="card" v-loading="loading">
      <template #header>个人资料</template>
      <el-form label-width="80px">
        <el-form-item label="昵称">
          <el-input v-model="form.nickname" />
        </el-form-item>
        <el-form-item label="生日">
          <el-input v-model="form.birthDate" type="date" class="profile-form__full" />
        </el-form-item>
        <el-form-item label="性别">
          <el-select v-model="form.gender" clearable placeholder="请选择">
            <el-option label="男" value="male" />
            <el-option label="女" value="female" />
            <el-option label="其他" value="other" />
          </el-select>
        </el-form-item>
      </el-form>
      <div class="actions">
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
        <router-link to="/">返回首页</router-link>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  padding: 2rem;
  display: flex;
  justify-content: center;
}
.card {
  width: 560px;
}
.actions {
  display: flex;
  gap: 1rem;
  align-items: center;
}
.profile-form__full {
  width: 100%;
}
</style>
