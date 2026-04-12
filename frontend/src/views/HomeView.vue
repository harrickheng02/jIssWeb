<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useForumBoards } from '@/composables/useForumBoards'
import { useForumComposeForm } from '@/composables/useForumComposeForm'
import { useForumHomeFeed } from '@/composables/useForumHomeFeed'
import { useForumPopularTags } from '@/composables/useForumPopularTags'
import { useAuthStore } from '@/stores/auth'
import { forumAuthorLabel, formatPublishedUtc } from '@/utils/forumPostDisplay'

const router = useRouter()
const auth = useAuthStore()
const activeFilter = ref<'latest' | 'hot' | 'featured'>('latest')
const activeSidebar = ref('all')

const filters = [
  { id: 'latest', label: '最新' },
  { id: 'hot', label: '热门' },
  { id: 'featured', label: '精华' },
] as const

const { forumBoards, sidebarItems, loadForumBoards } = useForumBoards()

function boardIdQueryParam() {
  return activeSidebar.value === 'all' ? undefined : activeSidebar.value
}

const { popularTags, popularTagsLoading, popularTagsError, loadPopularTags } =
  useForumPopularTags(boardIdQueryParam)

const {
  postList,
  listLoading,
  listError,
  page,
  totalPages,
  searchQuery,
  tagFilterValue,
  fetchPosts,
  setFeedTag,
  clearFeedTag,
  prevPage,
  nextPage,
} = useForumHomeFeed(activeSidebar, { onActiveSidebarChange: () => void loadPopularTags() })

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
  onComposeTagsChange,
  openComposeDialog,
  submitCompose,
} = useForumComposeForm({
  getDefaultBoardId: getDefaultComposeBoardId,
  fetchPosts,
})

const isAuthed = computed(() => Boolean(auth.token))

const hotPosts = [
  '新人报道区要不要单独放在顶部？',
  '论坛帖子摘要长度控制在多少最合适',
  '匿名发帖对社区氛围到底是利还是弊',
  '热榜按点赞还是互动总量排序',
  '移动端单列后右侧信息怎么安置',
]

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

onMounted(() => {
  void loadForumBoards().finally(() => {
    void loadPopularTags()
  })
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
              <el-link :underline="false" @click="handleOpenPlaceholder('用户主页')">{{
                forumAuthorLabel(post.authorDisplayName, post.authorId)
              }}</el-link>
              <div class="tag-list">
                <el-tag
                  v-for="tag in post.tags"
                  :key="tag"
                  size="small"
                  class="clickable-tag"
                  :type="tagFilterValue === tag ? 'primary' : 'info'"
                  @click.stop="setFeedTag(tag)"
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
            <el-form-item label="标签">
              <el-select
                v-model="composeTags"
                class="compose-board-select compose-tags-select"
                multiple
                filterable
                allow-create
                default-first-option
                :reserve-keyword="false"
                placeholder="可选，最多 10 个，单个不超过 32 字，输入后回车添加"
                @change="onComposeTagsChange"
              >
                <el-option v-for="t in popularTags" :key="t" :label="t" :value="t" />
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
