import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  createForumPost,
  createDraft,
  updateForumPost,
  updateDraft,
  publishDraft,
  getForumTagSuggest,
  type ForumPostListItem,
} from '@/api/clients'
import { formatForumMutedMessage } from '@/utils/forumMutedMessage'
import { useDraftUiStore } from '@/stores/draftUi'

const maxComposeTags = 10

type ComposeMode = 'create' | 'edit' | 'draft-edit'

export function useForumComposeForm(opts: {
  getDefaultBoardId: () => string
  fetchPosts?: () => Promise<void>
  onEditSuccess?: (updated: ForumPostListItem) => void
  onDraftSaved?: (draftId: string) => void
}) {
  const router = useRouter()
  const route = useRoute()
  const draftUi = useDraftUiStore()
  const composeOpen = ref(false)
  const composeTitle = ref('')
  const composeBody = ref('')
  const composeBoardId = ref('general')
  const composeTags = ref<string[]>([])
  const composeSubmitting = ref(false)
  const composeSavingDraft = ref(false)

  const mode = ref<ComposeMode>('create')
  const editTargetId = ref<string | null>(null)

  const tagSuggestions = ref<string[]>([])
  const tagSuggestionsLoading = ref(false)

  async function onTagSearch(q: string) {
    tagSuggestionsLoading.value = true
    try {
      const res = await getForumTagSuggest(q || undefined, 50)
      if (res.success && res.data) {
        tagSuggestions.value = res.data
      }
    } catch {
      // ignore
    } finally {
      tagSuggestionsLoading.value = false
    }
  }

  function onComposeTagsChange(val: string[]) {
    let capped = false
    const next = val.slice(0, maxComposeTags)
    if (val.length > maxComposeTags) {
      capped = true
    }
    if (capped) ElMessage.warning(`最多 ${maxComposeTags} 个标签`)
    composeTags.value = next
  }

  function openComposeDialog() {
    mode.value = 'create'
    editTargetId.value = null
    composeTitle.value = ''
    composeBody.value = ''
    composeBoardId.value = opts.getDefaultBoardId()
    composeTags.value = []
    tagSuggestions.value = []
    composeOpen.value = true
    void onTagSearch('')
  }

  function openComposeDialogForEdit(post: {
    id: string
    title: string
    body: string
    tags?: string[]
    boardId?: string
  }) {
    mode.value = 'edit'
    editTargetId.value = post.id
    composeTitle.value = post.title
    composeBody.value = post.body
    composeBoardId.value = post.boardId ?? opts.getDefaultBoardId()
    composeTags.value = post.tags ? [...post.tags] : []
    tagSuggestions.value = []
    composeOpen.value = true
    void onTagSearch('')
  }

  function openComposeDialogForDraftEdit(draft: {
    id: string
    title?: string
    body?: string
    tags?: string[]
    boardId?: string
  }) {
    mode.value = 'draft-edit'
    editTargetId.value = draft.id
    composeTitle.value = draft.title ?? ''
    composeBody.value = draft.body ?? ''
    composeBoardId.value = draft.boardId ?? opts.getDefaultBoardId()
    composeTags.value = draft.tags ? [...draft.tags] : []
    tagSuggestions.value = []
    composeOpen.value = true
    void onTagSearch('')
  }

  async function submitCompose() {
    const title = composeTitle.value.trim()
    const body = composeBody.value.trim()

    if (mode.value === 'edit') {
      const targetId = editTargetId.value
      if (!targetId) return
      composeSubmitting.value = true
      try {
        const tags = composeTags.value.length ? [...composeTags.value] : []
        const res = await updateForumPost(targetId, { title, body, tags })
        if (!res.success || !res.data) {
          ElMessage.error(res.message ?? '编辑失败')
          return
        }
        composeOpen.value = false
        ElMessage.success('帖子已更新')
        opts.onEditSuccess?.(res.data)
      } catch (e) {
        ElMessage.error(e instanceof Error ? e.message : '编辑失败')
      } finally {
        composeSubmitting.value = false
      }
      return
    }

    if (mode.value === 'draft-edit') {
      const targetId = editTargetId.value
      if (!targetId) return
      if (!title) {
        ElMessage.warning('请填写标题')
        return
      }
      if (!body) {
        ElMessage.warning('请填写正文')
        return
      }
      composeSubmitting.value = true
      try {
        const tags = composeTags.value.length ? [...composeTags.value] : undefined
        const updateRes = await updateDraft(targetId, { title, body, boardId: composeBoardId.value, tags })
        if (!updateRes.success) {
          ElMessage.error(updateRes.message ?? '保存草稿失败')
          return
        }
        const pubRes = await publishDraft(targetId)
        if (!pubRes.success) {
          ElMessage.error(pubRes.message ?? '发布失败')
          return
        }
        composeOpen.value = false
        ElMessage.success('草稿已发布')
        opts.onDraftSaved?.(targetId)
        void router.push({ name: 'post-detail', params: { id: targetId } })
      } catch (e) {
        ElMessage.error(e instanceof Error ? e.message : '发布失败')
      } finally {
        composeSubmitting.value = false
      }
      return
    }

    // create mode
    if (!title || !body) {
      ElMessage.warning('请填写标题与正文')
      return
    }
    composeSubmitting.value = true
    try {
      const tags = composeTags.value.length ? [...composeTags.value] : undefined
      const res = await createForumPost({
        title,
        body,
        boardId: composeBoardId.value,
        tags,
      })
      if (!res.success || !res.data?.id) {
        ElMessage.error(res.code === 'FORUM_MUTED' ? formatForumMutedMessage(res) : (res.message ?? '发帖失败'))
        return
      }
      composeOpen.value = false
      ElMessage.success('已发布')
      await opts.fetchPosts?.()
      void router.push({ name: 'post-detail', params: { id: res.data.id } })
    } catch (e) {
      ElMessage.error(e instanceof Error ? e.message : '发帖失败')
    } finally {
      composeSubmitting.value = false
    }
  }

  async function saveDraft() {
    const title = composeTitle.value.trim()
    const body = composeBody.value.trim()
    if (!title) {
      ElMessage.warning('请填写标题后再保存草稿')
      return
    }
    const tags = composeTags.value.length ? [...composeTags.value] : undefined
    composeSavingDraft.value = true
    try {
      if (mode.value === 'draft-edit' && editTargetId.value) {
        const res = await updateDraft(editTargetId.value, { title, body, boardId: composeBoardId.value, tags })
        if (!res.success) {
          ElMessage.error(res.message ?? '保存草稿失败')
          return
        }
        ElMessage.success('草稿已更新')
        opts.onDraftSaved?.(editTargetId.value)
        if (!route.path.startsWith('/me/drafts')) void draftUi.refreshBadgeFromServer()
      } else {
        const res = await createDraft({ title, body, boardId: composeBoardId.value, tags })
        if (!res.success || !res.data?.id) {
          ElMessage.error(res.message ?? '保存草稿失败')
          return
        }
        mode.value = 'draft-edit'
        editTargetId.value = res.data.id
        ElMessage.success('草稿已保存')
        opts.onDraftSaved?.(res.data.id)
        if (!route.path.startsWith('/me/drafts')) void draftUi.refreshBadgeFromServer()
      }
    } catch (e) {
      ElMessage.error(e instanceof Error ? e.message : '保存草稿失败')
    } finally {
      composeSavingDraft.value = false
    }
  }

  return {
    composeOpen,
    composeTitle,
    composeBody,
    composeBoardId,
    composeTags,
    composeSubmitting,
    composeSavingDraft,
    mode,
    editTargetId,
    tagSuggestions,
    tagSuggestionsLoading,
    onTagSearch,
    onComposeTagsChange,
    openComposeDialog,
    openComposeDialogForEdit,
    openComposeDialogForDraftEdit,
    submitCompose,
    saveDraft,
  }
}
