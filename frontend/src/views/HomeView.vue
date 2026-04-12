<script setup lang="ts">
import axios from 'axios'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { createForumPost, getForumBoards, listForumPosts, type ForumBoardItem, type ForumPostListItem } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { firstQueryString } from '@/utils/routeQuery'

type SidebarItem = {
  id: string
  label: string
}

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const activeFilter = ref<'latest' | 'hot' | 'featured'>('latest')
const activeSidebar = ref('all')
const forumBoards = ref<ForumBoardItem[]>([])

const filters = [
  { id: 'latest', label: '最新' },
  { id: 'hot', label: '热门' },
  { id: 'featured', label: '精华' },
] as const

const sidebarItems = computed<SidebarItem[]>(() => [
  { id: 'all', label: '全部' },
  ...forumBoards.value.map((b) => ({ id: b.id, label: b.title })),
])

const postList = ref<ForumPostListItem[]>([])
const listLoading = ref(false)
const listError = ref<string | null>(null)
const page = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)
const composeOpen = ref(false)
const composeTitle = ref('')
const composeBody = ref('')
const composeBoardId = ref('general')
const composeSubmitting = ref(false)

const hotPosts = [
  '新人报道区要不要单独放在顶部？',
  '论坛帖子摘要长度控制在多少最合适',
  '匿名发帖对社区氛围到底是利还是弊',
  '热榜按点赞还是互动总量排序',
  '移动端单列后右侧信息怎么安置',
]

const hotTags = ['论坛', '首页骨架', 'Vue3', '板块', '标签', '产品', '社区运营', '问答']

const isAuthed = computed(() => Boolean(auth.token))

const searchQuery = computed(() => firstQueryString(route.query.q))

const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))

function listBoardIdParam() {
  return activeSidebar.value === 'all' ? undefined : activeSidebar.value
}

function defaultComposeBoardId() {
  if (forumBoards.value.some((b) => b.id === 'general')) return 'general'
  return forumBoards.value[0]?.id ?? 'general'
}

function formatPublishedUtc(iso: string) {
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

function shortAuthor(id: string) {
  return id.length <= 14 ? id : `${id.slice(0, 10)}…`
}

async function fetchPosts() {
  listLoading.value = true
  listError.value = null
  try {
    const qRaw = route.query.q
    const qResolved = firstQueryString(qRaw)
    const q = qResolved || undefined
    const res = await listForumPosts(page.value, pageSize.value, listBoardIdParam(), q)
    if (!res.success || !res.data) {
      listError.value = res.message ?? '加载失败'
      postList.value = []
      return
    }
    postList.value = res.data.items
    totalCount.value = res.data.totalCount
  } catch (e) {
    if (axios.isAxiosError(e) && e.response?.status === 429) {
      listError.value = '请求过于频繁，请稍后再试'
    } else {
      listError.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
    }
    postList.value = []
    totalCount.value = 0
  } finally {
    listLoading.value = false
  }
}

function handleCreatePost() {
  if (!isAuthed.value) {
    void router.push('/auth')
    return
  }
  composeTitle.value = ''
  composeBody.value = ''
  composeBoardId.value = defaultComposeBoardId()
  composeOpen.value = true
}

async function submitCompose() {
  const title = composeTitle.value.trim()
  const body = composeBody.value.trim()
  if (!title || !body) {
    ElMessage.warning('请填写标题与正文')
    return
  }
  composeSubmitting.value = true
  try {
    const res = await createForumPost({
      title,
      body,
      boardId: composeBoardId.value,
    })
    if (!res.success || !res.data?.id) {
      ElMessage.error(res.message ?? '发帖失败')
      return
    }
    composeOpen.value = false
    ElMessage.success('已发布')
    await fetchPosts()
    void router.push({ name: 'post-detail', params: { id: res.data.id } })
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '发帖失败')
  } finally {
    composeSubmitting.value = false
  }
}

function goPost(id: string) {
  void router.push({ name: 'post-detail', params: { id } })
}

function handleProtectedAction(name: string) {
  if (!isAuthed.value) {
    void router.push('/auth')
    return
  }
  ElMessage.info(`${name}功能开发中`)
}

function handleOpenPlaceholder(name: string) {
  ElMessage.info(`${name}页面开发中`)
}

function prevPage() {
  if (page.value <= 1) return
  page.value -= 1
}

function nextPage() {
  if (page.value >= totalPages.value) return
  page.value += 1
}

watch(activeSidebar, () => {
  page.value = 1
})

watch(searchQuery, () => {
  page.value = 1
})

watch(
  [page, activeSidebar, searchQuery],
  () => {
    void fetchPosts()
  },
  { immediate: true },
)

async function loadForumBoards() {
  try {
    const r = await getForumBoards()
    if (r.success && r.data?.length) {
      forumBoards.value = r.data
      return
    }
    ElMessage.warning(
      r.success ? '板块配置为空，侧栏仅显示全部' : (r.message ?? '板块配置加载失败，侧栏仅显示全部'),
    )
  } catch {
    ElMessage.warning('板块配置加载失败（网络异常），侧栏仅显示全部')
  }
}

onMounted(() => {
  void loadForumBoards()
})
</script>

<template>
  <div class="forum-page">
    <main class="content-wrap">
      <aside class="left-panel">
        <el-card shadow="never">
          <template #header>分类</template>
          <div class="sidebar-list">
            <el-button
              v-for="item in sidebarItems"
              :key="item.id"
              :type="activeSidebar === item.id ? 'primary' : 'default'"
              plain
              class="sidebar-btn"
              @click="activeSidebar = item.id"
            >
              {{ item.label }}
            </el-button>
          </div>
        </el-card>
      </aside>

      <section class="center-panel">
        <div class="feed-head">
          <div class="feed-title">
            <h1>社区首页</h1>
            <p>先看内容，再决定互动。</p>
          </div>
          <div class="filter-list">
            <el-button
              v-for="item in filters"
              :key="item.id"
              :type="activeFilter === item.id ? 'primary' : 'default'"
              plain
              @click="activeFilter = item.id"
            >
              {{ item.label }}
            </el-button>
          </div>
        </div>

        <el-card class="composer-card" shadow="never">
          <div class="composer-row">
            <div class="composer-text">发个帖子吧，分享问题、观点或发现。</div>
            <el-button type="primary" @click="handleCreatePost">立即发帖</el-button>
          </div>
        </el-card>

        <el-skeleton v-if="listLoading" :rows="6" animated />

        <div v-else-if="listError" class="list-error">{{ listError }}</div>

        <div v-else-if="!postList.length && searchQuery" class="list-empty">未找到匹配的帖子</div>

        <div v-else-if="!postList.length" class="list-empty">暂无帖子</div>

        <div v-else class="post-list">
          <el-card v-for="post in postList" :key="post.id" class="post-card" shadow="hover">
            <div class="post-topline">
              <el-tag size="small" effect="plain">{{ post.board }}</el-tag>
              <span class="post-time">{{ formatPublishedUtc(post.publishedAtUtc) }}</span>
            </div>

            <el-link class="post-title" :underline="false" @click="goPost(post.id)">
              {{ post.title }}
            </el-link>

            <p class="post-excerpt">{{ post.excerpt }}</p>

            <div class="post-meta">
              <el-link :underline="false" @click="handleOpenPlaceholder('用户主页')">{{ shortAuthor(post.authorId) }}</el-link>
              <div class="tag-list">
                <el-tag
                  v-for="tag in post.tags"
                  :key="tag"
                  size="small"
                  class="clickable-tag"
                  @click="handleOpenPlaceholder(`标签 ${tag}`)"
                >
                  {{ tag }}
                </el-tag>
              </div>
            </div>

            <div class="post-stats">
              <button class="stat-btn" type="button" @click="handleProtectedAction('点赞')">赞 {{ post.likes }}</button>
              <button class="stat-btn" type="button" @click="goPost(post.id)">评 {{ post.comments }}</button>
              <span class="stat-text">看 {{ post.views }}</span>
            </div>
          </el-card>
        </div>

        <div v-if="!listLoading && !listError && postList.length" class="pager">
          <el-button :disabled="page <= 1" @click="prevPage">上一页</el-button>
          <el-button type="primary" plain>第 {{ page }} / {{ totalPages }} 页</el-button>
          <el-button :disabled="page >= totalPages" @click="nextPage">下一页</el-button>
        </div>

        <el-dialog v-model="composeOpen" title="发帖" width="520px" destroy-on-close>
          <el-form label-position="top">
            <el-form-item label="标题">
              <el-input v-model="composeTitle" maxlength="200" show-word-limit />
            </el-form-item>
            <el-form-item label="正文">
              <el-input v-model="composeBody" type="textarea" :rows="8" maxlength="20000" show-word-limit />
            </el-form-item>
            <el-form-item label="板块">
              <el-select v-model="composeBoardId" class="compose-board-select">
                <el-option v-for="b in forumBoards" :key="b.id" :label="b.title" :value="b.id" />
              </el-select>
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="composeOpen = false">取消</el-button>
            <el-button type="primary" :loading="composeSubmitting" @click="submitCompose">发布</el-button>
          </template>
        </el-dialog>
      </section>

      <aside class="right-panel">
        <el-card shadow="never">
          <template #header>热门内容</template>
          <div class="hot-list">
            <el-link
              v-for="item in hotPosts"
              :key="item"
              :underline="false"
              class="hot-item"
              @click="handleOpenPlaceholder('热门帖子')"
            >
              {{ item }}
            </el-link>
          </div>
        </el-card>

        <el-card shadow="never">
          <template #header>热门标签</template>
          <div class="right-tags">
            <el-tag
              v-for="tag in hotTags"
              :key="tag"
              class="clickable-tag"
              @click="handleOpenPlaceholder(`标签 ${tag}`)"
            >
              {{ tag }}
            </el-tag>
          </div>
        </el-card>

        <el-card shadow="never">
          <template #header>公告</template>
          <div class="notice-text">帖子列表与详情已接入论坛 API；互动与搜索等功能将陆续上线。</div>
        </el-card>
      </aside>
    </main>
  </div>
</template>

<style scoped>
.forum-page {
  min-height: 100%;
  background: var(--bg-main);
  color: var(--text-primary);
}

.content-wrap {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  display: grid;
  grid-template-columns: 240px minmax(0, 1fr) 300px;
  gap: var(--space-lg);
  align-items: start;
}

.left-panel,
.right-panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.sidebar-list,
.hot-list,
.right-tags {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-12);
}

.compose-board-select {
  width: 100%;
}

.sidebar-btn {
  width: 100%;
  justify-content: flex-start;
}

.center-panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.feed-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: var(--space-md);
}

.feed-title h1 {
  margin: 0;
  font-size: var(--font-xxl);
  font-weight: 700;
  line-height: 1.4;
}

.feed-title p {
  margin: var(--space-sm) 0 0;
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.filter-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-12);
}

.composer-card,
.post-card {
  border-radius: var(--radius-lg);
}

.composer-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--space-12);
}

.composer-text {
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.list-error {
  color: var(--text-secondary);
  padding: var(--space-md);
}

.list-empty {
  color: var(--text-secondary);
  padding: var(--space-md);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.post-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.post-topline,
.post-meta,
.post-stats {
  display: flex;
  align-items: center;
}

.post-topline {
  justify-content: space-between;
  gap: var(--space-12);
}

.post-time,
.notice-text {
  color: var(--text-secondary);
  font-size: var(--font-xs);
  line-height: var(--line-height);
}

.post-title {
  margin-top: var(--space-12);
  font-size: var(--font-lg);
  font-weight: 700;
  width: fit-content;
  line-height: 1.4;
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
}

.post-excerpt {
  margin: var(--space-12) 0 var(--space-md);
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
}

.post-meta {
  justify-content: space-between;
  gap: var(--space-12);
  flex-wrap: wrap;
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-sm);
}

.clickable-tag {
  cursor: pointer;
}

.post-stats {
  margin-top: var(--space-md);
  gap: var(--space-12);
  flex-wrap: wrap;
}

.stat-btn {
  border: 0;
  background: var(--el-color-primary-light-9);
  color: var(--color-primary);
  border-radius: var(--radius-pill);
  padding: var(--space-sm) var(--space-12);
  cursor: pointer;
}

.stat-text {
  color: var(--text-secondary);
}

.pager {
  display: flex;
  justify-content: center;
  gap: var(--space-12);
}

.hot-list {
  flex-direction: column;
  align-items: flex-start;
}

.hot-item {
  line-height: var(--line-height);
}

@media (max-width: 1200px) {
  .content-wrap {
    grid-template-columns: 240px minmax(0, 1fr);
  }

  .right-panel {
    grid-column: 1 / -1;
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: var(--space-md);
  }
}

@media (max-width: 900px) {
  .composer-row,
  .feed-head {
    flex-direction: column;
    align-items: stretch;
  }

  .filter-list {
    flex-wrap: wrap;
  }

  .content-wrap {
    grid-template-columns: 1fr;
  }

  .left-panel {
    order: 2;
  }

  .right-panel {
    order: 3;
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .content-wrap {
    padding-left: 12px;
    padding-right: 12px;
  }

  .content-wrap {
    padding-top: 16px;
  }

  .left-panel,
  .right-panel {
    display: none;
  }

  .post-title {
    font-size: 18px;
  }

  .composer-row,
  .post-meta {
    align-items: flex-start;
  }

  .pager {
    justify-content: stretch;
    flex-wrap: wrap;
  }
}
</style>
