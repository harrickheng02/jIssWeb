<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getMyDrafts, deleteDraft, publishDraft, getForumPost, type ForumPostListItem } from '@/api/clients'
import { useForumBoards } from '@/composables/useForumBoards'
import { useForumComposeForm } from '@/composables/useForumComposeForm'

const { forumBoards, loadForumBoards } = useForumBoards()

const loading = ref(false)
const error = ref<string | null>(null)
const items = ref<ForumPostListItem[]>([])
const totalCount = ref(0)
const page = ref(1)
const pageSize = 10
const deletingId = ref<string | null>(null)
const publishingId = ref<string | null>(null)

const {
  composeOpen,
  composeTitle,
  composeBody,
  composeBoardId,
  composeTags,
  composeSubmitting,
  tagSuggestions,
  tagSuggestionsLoading,
  onTagSearch,
  onComposeTagsChange,
  openComposeDialogForDraftEdit,
  submitCompose,
} = useForumComposeForm({
  getDefaultBoardId: () => forumBoards.value[0]?.id ?? 'general',
  onDraftSaved() {
    void fetchDrafts()
  },
})

const totalPages = ref(1)

async function fetchDrafts() {
  loading.value = true
  error.value = null
  try {
    const res = await getMyDrafts(page.value, pageSize)
    if (!res.success || !res.data) {
      error.value = res.message ?? '加载失败'
      return
    }
    items.value = res.data.items
    totalCount.value = res.data.totalCount
    totalPages.value = Math.max(1, Math.ceil(res.data.totalCount / pageSize))
  } catch (e) {
    error.value = e instanceof Error ? e.message : '网络异常'
  } finally {
    loading.value = false
  }
}

function formatRelativeTime(iso: string) {
  const d = new Date(iso)
  const diff = Date.now() - d.getTime()
  const m = Math.floor(diff / 60000)
  if (m < 1) return '刚刚'
  if (m < 60) return `${m} 分钟前`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h} 小时前`
  const days = Math.floor(h / 24)
  if (days < 7) return `${days} 天前`
  return d.toLocaleDateString('zh-CN')
}

const loadingEditId = ref<string | null>(null)

async function editDraft(draft: ForumPostListItem) {
  loadingEditId.value = draft.id
  try {
    const res = await getForumPost(draft.id)
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '加载草稿失败')
      return
    }
    const full = res.data
    const boardId = forumBoards.value.find((b) => b.title === full.board)?.id
    openComposeDialogForDraftEdit({
      id: full.id,
      title: full.title,
      body: full.body,
      tags: full.tags,
      boardId,
    })
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '加载草稿失败')
  } finally {
    loadingEditId.value = null
  }
}

async function confirmDelete(draft: ForumPostListItem) {
  try {
    await ElMessageBox.confirm('确定要删除这篇草稿吗？此操作不可撤销。', '删除草稿', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  deletingId.value = draft.id
  try {
    const res = await deleteDraft(draft.id)
    if (!res.success) {
      ElMessage.error((res as { message?: string }).message ?? '删除失败')
      return
    }
    ElMessage.success('草稿已删除')
    void fetchDrafts()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '删除失败')
  } finally {
    deletingId.value = null
  }
}

async function confirmPublish(draft: ForumPostListItem) {
  if (!draft.title?.trim()) {
    ElMessage.warning('请先填写标题再发布')
    editDraft(draft)
    return
  }
  try {
    await ElMessageBox.confirm('将草稿直接发布为帖子？', '发布草稿', {
      confirmButtonText: '发布',
      cancelButtonText: '取消',
      type: 'info',
    })
  } catch {
    return
  }
  publishingId.value = draft.id
  try {
    const res = await publishDraft(draft.id)
    if (!res.success) {
      ElMessage.error((res as { message?: string }).message ?? '发布失败')
      return
    }
    ElMessage.success('草稿已发布')
    void fetchDrafts()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '发布失败')
  } finally {
    publishingId.value = null
  }
}

function prevPage() {
  if (page.value > 1) {
    page.value -= 1
    void fetchDrafts()
  }
}

function nextPage() {
  if (page.value < totalPages.value) {
    page.value += 1
    void fetchDrafts()
  }
}

onMounted(() => {
  void loadForumBoards()
  void fetchDrafts()
})
</script>

<template>
  <div class="me-section">
    <h1 class="me-title">我的草稿</h1>

    <el-skeleton v-if="loading" :rows="4" animated />
    <div v-else-if="error" class="list-error">{{ error }}</div>
    <div v-else-if="!items.length" class="list-empty">暂无草稿</div>
    <div v-else class="draft-list">
      <el-card
        v-for="draft in items"
        :key="draft.id"
        shadow="never"
        class="draft-card"
      >
        <div class="draft-header">
          <span class="draft-title">{{ draft.title?.trim() || '（无标题）' }}</span>
          <el-tag size="small" type="info" effect="plain">草稿</el-tag>
        </div>
        <div v-if="draft.tags?.length" class="draft-tags">
          <el-tag v-for="tag in draft.tags" :key="tag" size="small">{{ tag }}</el-tag>
        </div>
        <div class="draft-meta">
          <span class="draft-time">创建于 {{ formatRelativeTime(draft.publishedAtUtc) }}</span>
          <span v-if="draft.updatedAtUtc" class="draft-time">已编辑 {{ formatRelativeTime(draft.updatedAtUtc) }}</span>
        </div>
        <div class="draft-actions">
          <el-button type="primary" size="small" :loading="loadingEditId === draft.id" @click="editDraft(draft)">继续编辑</el-button>
          <el-button
            type="success"
            size="small"
            plain
            :loading="publishingId === draft.id"
            @click="confirmPublish(draft)"
          >
            发布
          </el-button>
          <el-button
            type="danger"
            size="small"
            plain
            :loading="deletingId === draft.id"
            @click="confirmDelete(draft)"
          >
            删除
          </el-button>
        </div>
      </el-card>
    </div>

    <div v-if="!loading && !error && items.length" class="pager">
      <el-button :disabled="page <= 1" @click="prevPage">上一页</el-button>
      <el-button type="primary" plain>第 {{ page }} / {{ totalPages }} 页（共 {{ totalCount }} 篇）</el-button>
      <el-button :disabled="page >= totalPages" @click="nextPage">下一页</el-button>
    </div>

    <!-- 草稿编辑对话框 -->
    <el-dialog v-model="composeOpen" title="编辑草稿" width="520px" destroy-on-close>
      <el-form label-position="top">
        <el-form-item label="标题">
          <el-input v-model="composeTitle" maxlength="200" show-word-limit placeholder="草稿标题（可选）" />
        </el-form-item>
        <el-form-item label="正文">
          <el-input v-model="composeBody" type="textarea" :rows="8" maxlength="20000" show-word-limit placeholder="草稿正文（可选）" />
        </el-form-item>
        <el-form-item label="板块">
          <el-select v-model="composeBoardId" class="compose-full-select">
            <el-option v-for="b in forumBoards" :key="b.id" :label="b.title" :value="b.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="标签">
          <el-select
            v-model="composeTags"
            class="compose-full-select"
            multiple
            remote
            filterable
            allow-create
            default-first-option
            :remote-method="onTagSearch"
            :loading="tagSuggestionsLoading"
            :reserve-keyword="false"
            placeholder="可选，最多 10 个"
            @change="onComposeTagsChange"
          >
            <el-option v-for="t in tagSuggestions" :key="t" :label="t" :value="t" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="composeOpen = false">取消</el-button>
        <el-button type="primary" :loading="composeSubmitting" @click="submitCompose">保存草稿</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.me-section {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.me-title {
  margin: 0;
  font-size: var(--font-xl);
  font-weight: 700;
  line-height: 1.4;
}

.list-error,
.list-empty {
  color: var(--text-secondary);
  padding: var(--space-md);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.draft-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.draft-card {
  border-radius: var(--radius-lg);
}

.draft-header {
  display: flex;
  align-items: center;
  gap: var(--space-sm);
  margin-bottom: var(--space-sm);
}

.draft-title {
  font-size: var(--font-md);
  font-weight: 600;
  line-height: 1.4;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.draft-tags {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-sm);
  margin-bottom: var(--space-sm);
}

.draft-meta {
  display: flex;
  gap: var(--space-md);
  margin-bottom: var(--space-md);
}

.draft-time {
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.draft-actions {
  display: flex;
  gap: var(--space-sm);
  flex-wrap: wrap;
}

.pager {
  display: flex;
  justify-content: center;
  gap: var(--space-12);
  flex-wrap: wrap;
}

.compose-full-select {
  width: 100%;
}
</style>
