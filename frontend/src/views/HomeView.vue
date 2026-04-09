<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'

type SidebarItem = {
  id: string
  label: string
}

type PostItem = {
  id: number
  title: string
  excerpt: string
  author: string
  publishedAt: string
  board: string
  tags: string[]
  likes: number
  comments: number
  views: number
}

const router = useRouter()
const auth = useAuthStore()
const activeFilter = ref<'latest' | 'hot' | 'featured'>('latest')
const activeSidebar = ref('all')

const filters = [
  { id: 'latest', label: '最新' },
  { id: 'hot', label: '热门' },
  { id: 'featured', label: '精华' },
] as const

const sidebarItems: SidebarItem[] = [
  { id: 'all', label: '全部' },
  { id: 'hot', label: '热门' },
  { id: 'latest', label: '最新' },
  { id: 'tech', label: '技术' },
  { id: 'game', label: '游戏' },
  { id: 'rant', label: '吐槽' },
  { id: 'qa', label: '问答' },
]

const postList: PostItem[] = [
  {
    id: 101,
    title: 'Vue 3 + Element Plus 做论坛首页，第一版骨架怎么拆最稳？',
    excerpt: '目前先做首页骨架，目标是内容优先、发帖入口清晰、分类结构稳定，后续再接真实帖子与标签接口。',
    author: 'northwind',
    publishedAt: '10 分钟前',
    board: '技术',
    tags: ['Vue3', 'ElementPlus', '前端'],
    likes: 24,
    comments: 8,
    views: 312,
  },
  {
    id: 102,
    title: '新站冷启动时，论坛首页到底先做分类还是先做标签？',
    excerpt: '如果内容量还不大，首页要先保证可读性和发帖转化，标签和板块的权重要怎么分比较合适？',
    author: 'raincode',
    publishedAt: '32 分钟前',
    board: '问答',
    tags: ['社区产品', '标签', '分类'],
    likes: 17,
    comments: 13,
    views: 428,
  },
  {
    id: 103,
    title: '最近玩的几个独立游戏，聊聊真正让人上头的点',
    excerpt: '不是画面，也不是体量，更多是节奏、反馈和社区讨论氛围。顺手开个帖，欢迎补充。',
    author: 'summerfox',
    publishedAt: '1 小时前',
    board: '游戏',
    tags: ['独立游戏', '推荐'],
    likes: 39,
    comments: 21,
    views: 680,
  },
  {
    id: 104,
    title: '今天就想吐槽一下：很多社区首页把信息密度做没了',
    excerpt: '明明论坛首页最重要的是快速扫内容，结果搞成大卡片和大留白，第一页都看不到几条帖子。',
    author: 'plaintext',
    publishedAt: '2 小时前',
    board: '吐槽',
    tags: ['产品', 'UI'],
    likes: 52,
    comments: 34,
    views: 941,
  },
]

const hotPosts = [
  '新人报道区要不要单独放在顶部？',
  '论坛帖子摘要长度控制在多少最合适',
  '匿名发帖对社区氛围到底是利还是弊',
  '热榜按点赞还是互动总量排序',
  '移动端单列后右侧信息怎么安置',
]

const hotTags = ['论坛', '首页骨架', 'Vue3', '板块', '标签', '产品', '社区运营', '问答']

const isAuthed = computed(() => Boolean(auth.token))

function handleCreatePost() {
  if (!isAuthed.value) {
    void router.push('/auth')
    return
  }
  ElMessage.info('发帖功能开发中')
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

        <div class="post-list">
          <el-card v-for="post in postList" :key="post.id" class="post-card" shadow="hover">
            <div class="post-topline">
              <el-tag size="small" effect="plain">{{ post.board }}</el-tag>
              <span class="post-time">{{ post.publishedAt }}</span>
            </div>

            <el-link class="post-title" :underline="false" @click="handleOpenPlaceholder('帖子详情')">
              {{ post.title }}
            </el-link>

            <p class="post-excerpt">{{ post.excerpt }}</p>

            <div class="post-meta">
              <el-link :underline="false" @click="handleOpenPlaceholder('用户主页')">{{ post.author }}</el-link>
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
              <button class="stat-btn" type="button" @click="handleProtectedAction('评论')">评 {{ post.comments }}</button>
              <span class="stat-text">看 {{ post.views }}</span>
            </div>
          </el-card>
        </div>

        <div class="pager">
          <el-button disabled>上一页</el-button>
          <el-button type="primary" plain>第 1 页</el-button>
          <el-button @click="handleOpenPlaceholder('分页')">下一页</el-button>
        </div>
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
          <div class="notice-text">论坛首页第一版已上线，当前为静态骨架，帖子、板块、标签接口将在后续接入。</div>
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
