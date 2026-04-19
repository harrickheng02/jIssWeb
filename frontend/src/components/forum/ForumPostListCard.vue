<script setup lang="ts">
import { useRouter } from 'vue-router'
import { ChatDotRound, Star, StarFilled, View } from '@element-plus/icons-vue'
import type { Component } from 'vue'
import type { ForumPostListItem, ForumPostListPatch } from '@/api/clients'
import IconThumbUp from '@/components/icons/IconThumbUp.vue'
import IconThumbUpOutline from '@/components/icons/IconThumbUpOutline.vue'
import { useForumPostEngagement } from '@/composables/useForumPostEngagement'
import { useAuthStore } from '@/stores/auth'
import { forumAuthorLabel, formatPublishedUtc } from '@/utils/forumPostDisplay'

const props = withDefaults(
  defineProps<{
    post: ForumPostListItem
    /** When set, tag chips reflect active filter (home feed). */
    tagFilterValue?: string | null
    /** Home: tags filter feed; personal lists: read-only tags. */
    tagClickable?: boolean
  }>(),
  {
    tagFilterValue: null,
    tagClickable: false,
  },
)

const emit = defineEmits<{
  titleClick: [id: string]
  tagClick: [tag: string]
  commentStatClick: [id: string]
  authorClick: [id: string]
  patchPost: [patch: ForumPostListPatch]
}>()

const auth = useAuthStore()
const router = useRouter()

const { busyLike, busyFavorite, toggleLike, toggleFavorite } = useForumPostEngagement(
  () => props.post,
  (patch) => emit('patchPost', patch),
  {
    isAuthed: () => Boolean(auth.token),
    onUnauthenticated: () => void router.push({ name: 'auth' }),
  },
)

function onTitle() {
  emit('titleClick', props.post.id)
}

function onTag(tag: string) {
  if (!props.tagClickable) return
  emit('tagClick', tag)
}

function onCommentStat() {
  emit('commentStatClick', props.post.id)
}

function onAuthor() {
  emit('authorClick', props.post.authorId)
}

const liked = () => Boolean(props.post.likedByMe)
const favorited = () => Boolean(props.post.favoritedByMe)
const favoriteCount = () => props.post.favoriteCount ?? 0

const favoriteIcon = (): Component => (favorited() ? StarFilled : Star)
</script>

<template>
  <el-card class="post-card" shadow="hover">
    <div class="post-topline">
      <el-tag size="small" effect="plain">{{ post.board }}</el-tag>
      <span class="post-time">{{ formatPublishedUtc(post.publishedAtUtc) }}</span>
    </div>

    <div
      class="post-title"
      role="link"
      tabindex="0"
      @click="onTitle"
      @keydown.enter.prevent="onTitle"
    >
      {{ post.title }}
    </div>

    <p class="post-excerpt">{{ post.excerpt }}</p>

    <div class="post-meta">
      <el-link class="post-author-link" :underline="false" @click="onAuthor">
        {{ forumAuthorLabel(post.authorDisplayName, post.authorId) }}
      </el-link>
      <div class="tag-list">
        <el-tag
          v-for="tag in post.tags"
          :key="tag"
          size="small"
          :class="tagClickable ? 'clickable-tag' : 'tag-readonly'"
          :type="tagFilterValue === tag ? 'primary' : 'info'"
          @click.stop="onTag(tag)"
        >
          {{ tag }}
        </el-tag>
      </div>
    </div>

    <div class="post-stats">
      <button
        type="button"
        class="stat-chip"
        :class="{ 'stat-chip--liked': liked(), 'stat-chip--busy': busyLike }"
        title="点赞"
        :disabled="busyLike"
        @click="toggleLike"
      >
        <IconThumbUpOutline v-if="!liked()" class="stat-svg" aria-hidden="true" />
        <IconThumbUp v-else class="stat-svg" aria-hidden="true" />
        <span class="stat-num">{{ post.likes }}</span>
      </button>
      <button
        type="button"
        class="stat-chip stat-chip--fav"
        :class="{ 'stat-chip--favorited': favorited(), 'stat-chip--busy': busyFavorite }"
        title="收藏"
        :disabled="busyFavorite"
        @click="toggleFavorite"
      >
        <el-icon class="stat-ep" aria-hidden="true"><component :is="favoriteIcon()" /></el-icon>
        <span class="stat-num">{{ favoriteCount() }}</span>
      </button>
      <button type="button" class="stat-chip" title="评论" @click="onCommentStat">
        <el-icon class="stat-ep" aria-hidden="true"><ChatDotRound /></el-icon>
        <span class="stat-num">{{ post.comments }}</span>
      </button>
      <span class="stat-views" title="浏览">
        <el-icon class="stat-ep stat-ep--muted" aria-hidden="true"><View /></el-icon>
        <span class="stat-num stat-num--muted">{{ post.views }}</span>
      </span>
    </div>
  </el-card>
</template>

<style scoped>
.post-card {
  border-radius: var(--radius-lg);
  min-width: 0;
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

.post-time {
  color: var(--text-secondary);
  font-size: var(--font-xs);
  line-height: var(--line-height);
}

.post-title {
  margin-top: var(--space-12);
  font-size: var(--font-lg);
  font-weight: 700;
  width: 100%;
  max-width: 100%;
  line-height: 1.4;
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
  color: var(--color-primary);
  cursor: pointer;
  outline: none;
}

.post-title:focus-visible {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--color-primary) 35%, transparent);
  border-radius: var(--radius-sm);
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
  word-break: break-word;
}

.post-meta {
  justify-content: space-between;
  gap: var(--space-12);
  flex-wrap: wrap;
}

.post-author-link {
  font-size: var(--font-sm);
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-sm);
}

.clickable-tag {
  cursor: pointer;
}

.tag-readonly {
  cursor: default;
}

.post-stats {
  margin-top: var(--space-md);
  gap: var(--space-12);
  flex-wrap: wrap;
  align-items: center;
}

.stat-chip {
  display: inline-flex;
  align-items: center;
  gap: var(--space-xs);
  border: 0;
  background: transparent;
  color: var(--text-secondary);
  border-radius: var(--radius-pill);
  padding: var(--space-xs) var(--space-12);
  font-size: var(--font-xs);
  line-height: var(--line-height);
  cursor: pointer;
}

.stat-chip:hover:not(:disabled) {
  color: var(--color-primary);
}

.stat-chip:disabled,
.stat-chip--busy {
  opacity: 0.65;
  cursor: default;
}

.stat-chip--liked,
.stat-chip--fav.stat-chip--favorited {
  color: var(--color-primary);
}

.stat-svg {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
  color: currentColor;
}

.stat-ep {
  font-size: 18px;
  width: 18px;
  height: 18px;
  flex-shrink: 0;
}

.stat-ep--muted {
  color: var(--text-secondary);
}

.stat-num {
  font-variant-numeric: tabular-nums;
  min-width: 1ch;
}

.stat-num--muted {
  color: var(--text-secondary);
}

.stat-views {
  display: inline-flex;
  align-items: center;
  gap: var(--space-xs);
  padding: var(--space-xs) 0;
  color: var(--text-secondary);
}
</style>
