<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useForumBoards } from '@/composables/useForumBoards'
import { useForumComposeForm } from '@/composables/useForumComposeForm'
import { useForumHomeFeed } from '@/composables/useForumHomeFeed'
import { useForumPopularTags } from '@/composables/useForumPopularTags'
import { useForumSidebar } from '@/composables/useForumSidebar'
import { useCurrentUser } from '@/composables/useCurrentUser'
import ForumPostListCard from '@/components/forum/ForumPostListCard.vue'
import ForumRepliesLockedMark from '@/components/forum/ForumRepliesLockedMark.vue'
import { type ForumAnnouncementItem } from '@/api/clients'

/** Right-column hot list: `forum-homepage-shell` requires page=1 and pageSize=8. */
const hotSidebarPageSize = 8

const router = useRouter()
const { isLoggedIn } = useCurrentUser()
const activeFilter = ref<'latest' | 'hot' | 'featured'>('latest')
const activeSidebar = ref('all')

const filters = [
  { id: 'latest', label: '最新' },
  { id: 'hot', label: '热门' },
  { id: 'featured', label: '精华' },
] as const

const { forumBoards, sidebarItems } = useForumBoards()

function boardIdQueryParam() {
  return activeSidebar.value === 'all' ? undefined : activeSidebar.value
}

const { popularTags, popularTagsLoading, popularTagsError, loadPopularTags } =
  useForumPopularTags(boardIdQueryParam)

function resolveBoardTitle(boardId?: string) {
  if (!boardId) return undefined
  return forumBoards.value.find((b) => b.id === boardId)?.title
}

const {
  postList,
  listLoading,
  listError,
  page,
  totalPages,
  searchQuery,
  tagFilterValue,
  fetchPosts,
  initBundle,
  initWarnings,
  setFeedTag,
  clearFeedTag,
  prevPage,
  nextPage,
  applyPostListPatch,
} = useForumHomeFeed(activeSidebar, activeFilter, {
  onActiveSidebarChange: () => void loadPopularTags(),
  resolveBoardTitle,
})

// 消费 BFF bundle：boards / announcements / popularTags 由首次加载的 forum-init 填充
watch(initBundle, (bundle) => {
  if (!bundle) return
  forumBoards.value = bundle.boards
  announcements.value = bundle.announcements
  popularTags.value = bundle.popularTags
})

// warnings 双层：用户提示 + 开发/运维日志
watch(initWarnings, (warnings) => {
  if (!warnings.length) return
  console.warn('[BFF forum-init] 部分下游服务异常:', warnings)
  ElMessage.warning({
    message: `论坛部分内容加载不完整（${warnings.join('、')}），可刷新重试`,
    duration: 5000,
  })
})

function getDefaultComposeBoardId() {
  if (forumBoards.value.some((b) => b.id === 'general')) return 'general'
  return forumBoards.value[0]?.id ?? 'general'
}

const {
  composeOpen,
  composeTitle,
  composeBody,
  composeBoardId,
  composeTags,
  composeSubmitting,
  composeSavingDraft,
  tagSuggestions,
  tagSuggestionsLoading,
  onTagSearch,
  onComposeTagsChange,
  openComposeDialog,
  submitCompose,
  saveDraft,
} = useForumComposeForm({
  getDefaultBoardId: getDefaultComposeBoardId,
  getBoardTitle: (boardId) => resolveBoardTitle(boardId) ?? boardId,
  fetchPosts,
})

const isAuthed = computed(() => isLoggedIn.value)

const {
  hotSidebarPosts,
  hotSidebarLoading,
  hotSidebarError,
  announcements,
  annLoading,
  annError,
  loadHotSidebar: fetchHotSidebar,
} = useForumSidebar()

async function loadHotSidebar() {
  await fetchHotSidebar(hotSidebarPageSize, boardIdQueryParam())
}

function openAnnouncement(a: ForumAnnouncementItem) {
  const u = a.linkUrl?.trim()
  if (!u) return
  if (/^https?:\/\//i.test(u)) {
    window.open(u, '_blank', 'noopener,noreferrer')
    return
  }
  if (u.startsWith('/')) {
    void router.push(u)
  }
}

function handleCreatePost() {
  if (!isAuthed.value) {
    void router.push('/auth')
    return
  }
  openComposeDialog()
}

function goPost(id: string) {
  void router.push({ name: 'post-detail', params: { id } })
}

function handleOpenPlaceholder(name: string) {
  ElMessage.info(`${name}页面开发中`)
}

watch(activeSidebar, () => {
  void loadHotSidebar()
})

onMounted(() => {
  void loadHotSidebar()
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
          <div class="feed-head-actions">
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
            <el-button v-if="tagFilterValue" type="info" plain @click="clearFeedTag">
              清除标签：{{ tagFilterValue }}
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

        <div v-else-if="!postList.length && tagFilterValue" class="list-empty">该标签下暂无帖子</div>

        <div v-else-if="!postList.length" class="list-empty">暂无帖子</div>

        <div v-else class="post-list">
          <ForumPostListCard
            v-for="post in postList"
            :key="post.id"
            :post="post"
            :tag-filter-value="tagFilterValue"
            tag-clickable
            @title-click="goPost"
            @tag-click="setFeedTag"
            @patch-post="applyPostListPatch"
            @comment-stat-click="goPost"
            @author-click="() => handleOpenPlaceholder('用户主页')"
          />
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
            <el-form-item label="标签">
              <el-select
                v-model="composeTags"
                class="compose-board-select compose-tags-select"
                multiple
                remote
                filterable
                allow-create
                default-first-option
                :remote-method="onTagSearch"
                :loading="tagSuggestionsLoading"
                :reserve-keyword="false"
                placeholder="可选，最多 10 个，输入新标签或从建议中选择"
                @change="onComposeTagsChange"
              >
                <el-option v-for="t in tagSuggestions" :key="t" :label="t" :value="t" />
              </el-select>
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="composeOpen = false">取消</el-button>
            <el-button :loading="composeSavingDraft" @click="saveDraft">保存草稿</el-button>
            <el-button type="primary" :loading="composeSubmitting" @click="submitCompose">发布</el-button>
          </template>
        </el-dialog>
      </section>

      <aside class="right-panel">
        <el-card shadow="never">
          <template #header>热门内容</template>
          <el-skeleton v-if="hotSidebarLoading" :rows="4" animated />
          <div v-else-if="hotSidebarError" class="list-error">{{ hotSidebarError }}</div>
          <div v-else-if="!hotSidebarPosts.length" class="list-empty">暂无热门帖子</div>
          <div v-else class="hot-list">
            <div v-for="post in hotSidebarPosts" :key="post.id" class="hot-row">
              <ForumRepliesLockedMark v-if="post.repliesLocked" :size="14" class="hot-row__lock" />
              <el-link
                underline="never"
                class="hot-item hot-row__link"
                @click="goPost(post.id)"
              >
                {{ post.title }}
              </el-link>
            </div>
          </div>
        </el-card>

        <el-card shadow="never">
          <template #header>热门标签</template>
          <el-skeleton v-if="popularTagsLoading" :rows="2" animated />
          <div v-else-if="popularTagsError" class="list-error">{{ popularTagsError }}</div>
          <div v-else-if="!popularTags.length" class="list-empty">暂无标签数据</div>
          <div v-else class="right-tags">
            <el-tag
              v-for="tag in popularTags"
              :key="tag"
              class="clickable-tag"
              :type="tagFilterValue === tag ? 'primary' : 'info'"
              @click="setFeedTag(tag)"
            >
              {{ tag }}
            </el-tag>
          </div>
        </el-card>

        <el-card shadow="never">
          <template #header>公告</template>
          <el-skeleton v-if="annLoading" :rows="2" animated />
          <div v-else-if="annError" class="list-error">{{ annError }}</div>
          <div v-else-if="!announcements.length" class="list-empty">暂无公告</div>
          <div v-else class="notice-list">
            <div v-for="a in announcements" :key="a.id" class="notice-item">
              <el-link
                v-if="a.linkUrl?.trim()"
                type="primary"
                underline="never"
                @click.stop="openAnnouncement(a)"
              >
                {{ a.title }}
              </el-link>
              <span v-else class="notice-title-static">{{ a.title }}</span>
              <div v-if="a.summary" class="notice-summary">{{ a.summary }}</div>
            </div>
          </div>
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

.compose-tags-select :deep(.el-select__tags) {
  flex-wrap: wrap;
  max-width: 100%;
}

.compose-tags-select :deep(.el-tag) {
  margin-block: var(--space-xs);
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

.feed-head-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: var(--space-12);
}

.filter-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-12);
}

.composer-card {
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

.notice-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-12);
}

.notice-item {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  align-items: flex-start;
}

.notice-title-static {
  color: var(--text-primary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.notice-summary {
  color: var(--text-secondary);
  font-size: var(--font-xs);
  line-height: var(--line-height);
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

.hot-row {
  display: flex;
  align-items: flex-start;
  gap: var(--space-xs);
  width: 100%;
  min-width: 0;
}

.hot-row__lock {
  flex-shrink: 0;
  margin-top: 2px;
}

.hot-row__link {
  flex: 1;
  min-width: 0;
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
