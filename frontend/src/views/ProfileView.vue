<script setup lang="ts">
import { onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { useProfile } from '@/composables/useProfile'

const { loading, saving, form, loadProfile, saveProfile } = useProfile()

async function load() {
  const res = await loadProfile()
  if (!res.success) {
    ElMessage.error(res.message ?? '加载失败')
  }
}

async function save() {
  const res = await saveProfile({
    nickname: form.nickname || undefined,
    gender: form.gender || undefined,
    birthDate: form.birthDate || undefined,
  })
  if (!res.success) {
    ElMessage.error(res.message ?? '保存失败')
    return
  }
  ElMessage.success('保存成功')
  window.dispatchEvent(new CustomEvent('jiss-profile-updated'))
}

onMounted(() => {
  void load()
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
