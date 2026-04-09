<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { customerApi, type ApiResult, type CustomerRecord } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const list = ref<CustomerRecord[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const form = reactive({ name: '', remark: '' })

async function loadList() {
  if (!auth.token) return
  loading.value = true
  try {
    const { data: res } = await customerApi.get<ApiResult<CustomerRecord[]>>('/customers')
    if (!res.success) throw new Error(res.message ?? '加载失败')
    list.value = res.data ?? []
  } catch {
    ElMessage.error('加载失败')
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  if (auth.token) void loadList()
})

function openCreate() {
  editingId.value = null
  form.name = ''
  form.remark = ''
  dialogVisible.value = true
}

function openEdit(row: CustomerRecord) {
  editingId.value = row.id
  form.name = row.name
  form.remark = row.remark ?? ''
  dialogVisible.value = true
}

async function save() {
  try {
    if (editingId.value) {
      const { data: res } = await customerApi.put<ApiResult<CustomerRecord>>(
        `/customers/${editingId.value}`,
        { name: form.name, remark: form.remark || undefined },
      )
      if (!res.success) throw new Error(res.message ?? '保存失败')
    } else {
      const { data: res } = await customerApi.post<ApiResult<CustomerRecord>>('/customers', {
        name: form.name,
        remark: form.remark || undefined,
      })
      if (!res.success) throw new Error(res.message ?? '保存失败')
    }
    dialogVisible.value = false
    ElMessage.success('已保存')
    await loadList()
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '保存失败'
    ElMessage.error(msg)
  }
}

async function remove(row: CustomerRecord) {
  try {
    await ElMessageBox.confirm('确认删除？', '提示', { type: 'warning' })
    const { data: res } = await customerApi.delete<ApiResult<string>>(`/customers/${row.id}`)
    if (!res.success) throw new Error(res.message ?? '删除失败')
    ElMessage.success('已删除')
    await loadList()
  } catch (e: unknown) {
    if (e === 'cancel') return
    const msg = e instanceof Error ? e.message : '删除失败'
    ElMessage.error(msg)
  }
}
</script>

<template>
  <div class="page">
    <div v-if="!auth.token" class="hint">
      <el-alert title="请先登录后再访问客档" type="warning" :closable="false" show-icon />
      <router-link class="link" to="/auth">去登录</router-link>
    </div>
    <template v-else>
      <div class="toolbar">
        <el-button type="primary" @click="openCreate">新建客档</el-button>
        <router-link class="link" to="/profile">个人资料</router-link>
        <router-link class="link" to="/">返回首页</router-link>
      </div>
      <el-table v-loading="loading" :data="list" stripe>
        <el-table-column prop="name" label="名称" />
        <el-table-column prop="remark" label="备注" />
        <el-table-column label="操作" width="160">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </template>

    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑客档' : '新建客档'" width="480px">
      <el-form label-width="64px">
        <el-form-item label="名称">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.remark" type="textarea" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  padding: 2rem;
  max-width: 960px;
  margin: 0 auto;
}
.hint {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  align-items: flex-start;
}
.toolbar {
  margin-bottom: 1rem;
  display: flex;
  gap: 1rem;
  align-items: center;
}
.link {
  margin-left: 0.5rem;
}
</style>
