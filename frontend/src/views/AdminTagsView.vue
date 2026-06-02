<script setup lang="ts">
import { ref, reactive, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import axios from 'axios'
import {
  adminListTags,
  adminCreateTag,
  adminPatchTag,
  adminDisableTag,
  adminEnableTag,
  adminDeleteTag,
  type ForumTagDto,
  type PagedForumTags,
} from '@/api/clients'

function httpErrMsg(e: unknown): string {
  if (axios.isAxiosError(e)) {
    const status = e.response?.status
    const msg = (e.response?.data as { message?: string })?.message
    return msg ? `${status}: ${msg}` : `HTTP ${status ?? '连接失败'}`
  }
  return e instanceof Error ? e.message : '未知错误'
}

// ── 列表状态 ─────────────────────────────────────────────────────────────────
const loading = ref(false)
const tags = ref<ForumTagDto[]>([])
const totalCount = ref(0)
const page = ref(1)
const pageSize = ref(20)
const filterStatus = ref('')
const searchQ = ref('')
let searchDebounceTimer: ReturnType<typeof setTimeout> | null = null

async function loadTags() {
  loading.value = true
  try {
    const params: Record<string, string | number> = { page: page.value, pageSize: pageSize.value }
    if (filterStatus.value) params.status = filterStatus.value
    if (searchQ.value.trim()) params.q = searchQ.value.trim()
    const res = await adminListTags(params as Parameters<typeof adminListTags>[0])
    if (res.success && res.data) {
      tags.value = (res.data as PagedForumTags).items
      totalCount.value = (res.data as PagedForumTags).totalCount
    } else {
      ElMessage.error(res.message ?? '加载失败')
    }
  } catch (e) {
    ElMessage.error(httpErrMsg(e))
  } finally {
    loading.value = false
  }
}

watch([filterStatus], () => {
  page.value = 1
  void loadTags()
})

watch(searchQ, () => {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer)
  searchDebounceTimer = setTimeout(() => {
    page.value = 1
    void loadTags()
  }, 300)
})

function onPageChange(p: number) {
  page.value = p
  void loadTags()
}

onMounted(() => void loadTags())

// ── 状态徽标 ─────────────────────────────────────────────────────────────────
function statusType(status: string) {
  return status === 'active' ? 'success' : 'warning'
}
function statusLabel(status: string) {
  return status === 'active' ? '启用' : '禁用'
}

// ── 创建对话框 ─────────────────────────────────────────────────────────────────
const createDialogVisible = ref(false)
const createFormRef = ref<FormInstance>()
const createForm = reactive({ name: '', description: '' })
const createError = ref('')
const createSubmitting = ref(false)

const createRules: FormRules = {
  name: [
    { required: true, message: '请输入标签名称', trigger: 'blur' },
    { max: 32, message: '标签名称不超过 32 字', trigger: 'blur' },
  ],
}

function openCreateDialog() {
  createForm.name = ''
  createForm.description = ''
  createError.value = ''
  createDialogVisible.value = true
}

async function submitCreate() {
  if (!createFormRef.value) return
  const valid = await createFormRef.value.validate().catch(() => false)
  if (!valid) return

  createSubmitting.value = true
  createError.value = ''
  try {
    const res = await adminCreateTag({
      name: createForm.name.trim(),
      description: createForm.description.trim() || undefined,
    })
    if (res.success) {
      ElMessage.success('标签创建成功')
      createDialogVisible.value = false
      void loadTags()
    } else if (res.code === 'TAG_SLUG_CONFLICT') {
      createError.value = '该标签名称（或规范化后的标识）已存在'
    } else {
      createError.value = res.message ?? '创建失败'
    }
  } catch (e) {
    createError.value = httpErrMsg(e)
  } finally {
    createSubmitting.value = false
  }
}

// ── 编辑对话框 ─────────────────────────────────────────────────────────────────
const editDialogVisible = ref(false)
const editFormRef = ref<FormInstance>()
const editForm = reactive({ id: '', name: '', description: '' })
const editError = ref('')
const editSubmitting = ref(false)

const editRules: FormRules = {
  name: [
    { required: true, message: '请输入标签名称', trigger: 'blur' },
    { max: 32, message: '标签名称不超过 32 字', trigger: 'blur' },
  ],
}

function openEditDialog(tag: ForumTagDto) {
  editForm.id = tag.id
  editForm.name = tag.name
  editForm.description = tag.description ?? ''
  editError.value = ''
  editDialogVisible.value = true
}

async function submitEdit() {
  if (!editFormRef.value) return
  const valid = await editFormRef.value.validate().catch(() => false)
  if (!valid) return

  editSubmitting.value = true
  editError.value = ''
  try {
    const res = await adminPatchTag(editForm.id, {
      name: editForm.name.trim(),
      description: editForm.description.trim() || undefined,
    })
    if (res.success) {
      ElMessage.success('标签已更新')
      editDialogVisible.value = false
      void loadTags()
    } else if (res.code === 'TAG_SLUG_CONFLICT') {
      editError.value = '该标签名称（或规范化标识）已被其他标签占用'
    } else {
      editError.value = res.message ?? '更新失败'
    }
  } catch (e) {
    editError.value = httpErrMsg(e)
  } finally {
    editSubmitting.value = false
  }
}

// ── 禁用 / 启用 ─────────────────────────────────────────────────────────────────
async function handleDisable(tag: ForumTagDto) {
  try {
    await ElMessageBox.confirm(`确定禁用标签「${tag.name}」？禁用后该标签不会出现在建议列表中。`, '禁用标签', {
      confirmButtonText: '确定禁用',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    const res = await adminDisableTag(tag.id)
    if (res.success) {
      ElMessage.success('已禁用')
      void loadTags()
    } else {
      ElMessage.error(res.message ?? '操作失败')
    }
  } catch (e) {
    ElMessage.error(httpErrMsg(e))
  }
}

async function handleEnable(tag: ForumTagDto) {
  try {
    const res = await adminEnableTag(tag.id)
    if (res.success) {
      ElMessage.success('已启用')
      void loadTags()
    } else {
      ElMessage.error(res.message ?? '操作失败')
    }
  } catch (e) {
    ElMessage.error(httpErrMsg(e))
  }
}

// ── 删除 ─────────────────────────────────────────────────────────────────────
async function handleDelete(tag: ForumTagDto) {
  try {
    await ElMessageBox.confirm(
      `确定永久删除标签「${tag.name}」？此操作不可撤销。`,
      '删除标签',
      { confirmButtonText: '确定删除', cancelButtonText: '取消', type: 'error' },
    )
  } catch {
    return
  }
  try {
    const res = await adminDeleteTag(tag.id)
    if (res.success) {
      ElMessage.success('已删除')
      void loadTags()
    } else {
      ElMessage.error(res.message ?? '删除失败')
    }
  } catch (e) {
    ElMessage.error(httpErrMsg(e))
  }
}
</script>

<template>
  <div class="admin-tags">
    <div class="admin-tags__header">
      <el-button type="primary" @click="openCreateDialog">新建标签</el-button>
    </div>

    <!-- 筛选栏 -->
    <div class="admin-tags__filters">
      <el-select
        v-model="filterStatus"
        placeholder="全部状态"
        clearable
        class="admin-tags__filter-status"
      >
        <el-option label="全部" value="" />
        <el-option label="启用" value="active" />
        <el-option label="禁用" value="disabled" />
      </el-select>
      <el-input
        v-model="searchQ"
        placeholder="搜索标签名称…"
        clearable
        class="admin-tags__filter-search"
      />
    </div>

    <!-- 数据表格 -->
    <el-table v-loading="loading" :data="tags" class="admin-tags__table">
      <el-table-column label="名称" prop="name" min-width="140" />
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="statusType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="使用数" prop="useCount" width="80" align="center" header-align="center" />
      <el-table-column label="操作" width="220" fixed="right" align="center" header-align="center">
        <template #default="{ row }">
          <el-button size="small" @click="openEditDialog(row)">
            编辑
          </el-button>
          <el-button
            v-if="row.status === 'active'"
            size="small"
            type="warning"
            @click="handleDisable(row)"
          >
            禁用
          </el-button>
          <el-button
            v-if="row.status === 'disabled'"
            size="small"
            type="success"
            @click="handleEnable(row)"
          >
            启用
          </el-button>
          <el-button
            size="small"
            type="danger"
            @click="handleDelete(row)"
          >
            删除
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 分页 -->
    <div class="admin-tags__pagination">
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :total="totalCount"
        layout="total, prev, pager, next"
        @current-change="onPageChange"
      />
    </div>

    <!-- 新建标签对话框 -->
    <el-dialog v-model="createDialogVisible" title="新建标签" width="480px">
      <el-form ref="createFormRef" :model="createForm" :rules="createRules" label-width="80px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="createForm.name" maxlength="32" show-word-limit placeholder="标签名称（最多 32 字）" />
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="createForm.description" type="textarea" :rows="2" placeholder="可选说明" />
        </el-form-item>
        <div v-if="createError" class="admin-tags__form-error">{{ createError }}</div>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="createSubmitting" @click="submitCreate">创建</el-button>
      </template>
    </el-dialog>

    <!-- 编辑标签对话框 -->
    <el-dialog v-model="editDialogVisible" title="编辑标签" width="480px">
      <el-form ref="editFormRef" :model="editForm" :rules="editRules" label-width="80px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="editForm.name" maxlength="32" show-word-limit />
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="editForm.description" type="textarea" :rows="2" />
        </el-form-item>
        <div v-if="editError" class="admin-tags__form-error">{{ editError }}</div>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="editSubmitting" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.admin-tags {
  max-width: 1100px;
}

.admin-tags__header {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  margin-bottom: var(--space-16);
}

.admin-tags__filters {
  display: flex;
  gap: var(--space-12);
  margin-bottom: var(--space-16);
}

.admin-tags__filter-status {
  width: 140px;
}

.admin-tags__filter-search {
  width: 240px;
}

.admin-tags__table {
  width: 100%;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
}

.admin-tags__pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: var(--space-16);
}

.admin-tags__form-error {
  color: var(--el-color-danger);
  font-size: var(--font-sm);
  margin-top: var(--space-4);
}
</style>
