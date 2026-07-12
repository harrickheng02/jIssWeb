import axios from 'axios'
import { computed, ref, watch, type Ref } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router'
import {
  getForumInit,
  listForumPosts,
  type ForumInitBundle,
  type ForumPostListItem,
  type ForumPostListPatch,
} from '@/api/clients'
import { applyForumPostListPatch } from '@/utils/applyForumPostListPatch'
import { mergeLocalPostsIntoFeed } from '@/utils/forumLocalContent'
import { firstQueryString } from '@/utils/routeQuery'
import { useAuthStore } from '@/stores/auth'

export type ForumHomeFeedOptions = {
  onActiveSidebarChange?: () => void
  resolveBoardTitle?: (boardId?: string) => string | undefined
}

export function useForumHomeFeed(
  activeSidebar: Ref<string>,
  feedSort: Ref<'latest' | 'hot' | 'featured'>,
  options?: ForumHomeFeedOptions,
) {
  const router = useRouter()
  const route = useRoute()
  const auth = useAuthStore()
  const postList = ref<ForumPostListItem[]>([])
  const listLoading = ref(false)
  const listError = ref<string | null>(null)
  const page = ref(1)
  const pageSize = ref(20)
  const totalCount = ref(0)
  const initBundle = ref<ForumInitBundle | null>(null)
  const initWarnings = ref<string[]>([])
  let _firstLoad = true

  const searchQuery = computed(() => firstQueryString(route.query.q))
  const tagFilterValue = computed(() => {
    const t = firstQueryString(route.query.tag)
    return t || undefined
  })

  const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))

  function listBoardIdParam() {
    return activeSidebar.value === 'all' ? undefined : activeSidebar.value
  }

  async function fetchPosts() {
    listLoading.value = true
    listError.value = null
    try {
      const qResolved = firstQueryString(route.query.q)
      const q = qResolved || undefined
      const sortParam: 'hot' | undefined = feedSort.value === 'hot' ? 'hot' : undefined
      const featuredParam: boolean | undefined = feedSort.value === 'featured' ? true : undefined

      let items: ForumPostListItem[]
      let total: number

      if (_firstLoad) {
        _firstLoad = false
        // 首次加载：使用 BFF bundle，同时获取 boards/announcements/tags
        const res = await getForumInit({
          page: page.value,
          pageSize: pageSize.value,
          boardId: listBoardIdParam(),
          q,
          tag: tagFilterValue.value,
          sort: sortParam,
          featured: featuredParam,
        })
        if (!res.success || !res.data) {
          listError.value = res.message ?? '加载失败'
          postList.value = []
          return
        }
        initBundle.value = res.data
        initWarnings.value = res.data.warnings ?? []
        items = res.data.posts.items
        total = res.data.posts.totalCount
      } else {
        // 后续过滤/翻页：仅取帖子列表
        const res = await listForumPosts(
          page.value, pageSize.value, listBoardIdParam(), q,
          tagFilterValue.value, sortParam, featuredParam,
        )
        if (!res.success || !res.data) {
          listError.value = res.message ?? '加载失败'
          postList.value = []
          return
        }
        items = res.data.items
        total = res.data.totalCount
      }

      if (auth.sub && !qResolved) {
        items = mergeLocalPostsIntoFeed(auth.sub, items, {
          boardTitle: options?.resolveBoardTitle?.(listBoardIdParam()),
          tag: tagFilterValue.value,
        })
      }
      postList.value = items
      totalCount.value = total
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

  function setFeedTag(tag: string) {
    const t = tag.trim()
    if (!t) return
    page.value = 1
    void router.push({ name: 'home', query: { ...route.query, tag: t } as LocationQueryRaw })
  }

  function clearFeedTag() {
    page.value = 1
    const next = { ...route.query } as LocationQueryRaw
    delete next.tag
    void router.push({ name: 'home', query: next })
  }

  function prevPage() {
    if (page.value <= 1) return
    page.value -= 1
  }

  function nextPage() {
    if (page.value >= totalPages.value) return
    page.value += 1
  }

  function applyPostListPatch(patch: ForumPostListPatch) {
    applyForumPostListPatch(postList.value, patch)
  }

  watch(activeSidebar, () => {
    page.value = 1
    options?.onActiveSidebarChange?.()
  })

  watch(searchQuery, () => {
    page.value = 1
  })

  watch(tagFilterValue, () => {
    page.value = 1
  })

  watch(
    [page, activeSidebar, searchQuery, tagFilterValue, feedSort],
    () => {
      void fetchPosts()
    },
    { immediate: true },
  )

  return {
    postList,
    listLoading,
    listError,
    page,
    pageSize,
    totalCount,
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
  }
}
