<script setup lang="ts">
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import type { ForumPostListPatch } from '@/api/clients'
import ForumPostListCard from '@/components/forum/ForumPostListCard.vue'
import { useMeForumFavorites } from '@/composables/useMeForumFavorites'

const router = useRouter()
const { loading, error, items, page, totalPages, applyPostListPatch, refresh } =
  useMeForumFavorites()

function onFavoritePatch(p: ForumPostListPatch) {
  if (p.favoritedByMe === false) void refresh()
  else applyPostListPatch(p)
}

function goPost(id: string) {
  void router.push({ name: 'post-detail', params: { id } })
}

function onAuthor() {
  ElMessage.info('用户主页开发中')
}

function prevPage() {
  if (page.value > 1) page.value -= 1
}

function nextPage() {
  if (page.value < totalPages.value) page.value += 1
}
</script>

<template>
  <div class="me-section">
    <h1 class="me-title">我的收藏</h1>
    <el-skeleton v-if="loading" :rows="6" animated />
    <div v-else-if="error" class="list-error">{{ error }}</div>
    <div v-else-if="!items.length" class="list-empty">暂无收藏</div>
    <div v-else class="post-list">
      <ForumPostListCard
        v-for="post in items"
        :key="post.id"
        :post="post"
        @title-click="goPost"
        @patch-post="onFavoritePatch"
        @comment-stat-click="goPost"
        @author-click="onAuthor"
      />
    </div>
    <div v-if="!loading && !error && items.length" class="pager">
      <el-button :disabled="page <= 1" @click="prevPage">上一页</el-button>
      <el-button type="primary" plain>第 {{ page }} / {{ totalPages }} 页</el-button>
      <el-button :disabled="page >= totalPages" @click="nextPage">下一页</el-button>
    </div>
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

.pager {
  display: flex;
  justify-content: center;
  gap: var(--space-12);
  flex-wrap: wrap;
}
</style>
