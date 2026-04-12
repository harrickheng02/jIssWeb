import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { createForumPost } from '@/api/clients'

const maxComposeTags = 10
const maxComposeTagLen = 32

function composeTagsListEqual(a: string[], b: string[]) {
  if (a.length !== b.length) return false
  return a.every((t, i) => t === b[i])
}

export function useForumComposeForm(opts: {
  getDefaultBoardId: () => string
  fetchPosts: () => Promise<void>
}) {
  const router = useRouter()
  const composeOpen = ref(false)
  const composeTitle = ref('')
  const composeBody = ref('')
  const composeBoardId = ref('general')
  const composeTags = ref<string[]>([])
  const composeSubmitting = ref(false)

  function onComposeTagsChange(val: string[]) {
    const next: string[] = []
    const seen = new Set<string>()
    let droppedLong = false
    let capped = false
    for (const raw of val) {
      const t = raw.trim()
      if (!t) continue
      if (t.length > maxComposeTagLen) {
        droppedLong = true
        continue
      }
      const k = t.toLowerCase()
      if (seen.has(k)) continue
      if (next.length >= maxComposeTags) {
        capped = true
        break
      }
      seen.add(k)
      next.push(t)
    }
    if (droppedLong) ElMessage.warning(`单个标签不超过 ${maxComposeTagLen} 字`)
    if (capped) ElMessage.warning(`最多 ${maxComposeTags} 个标签`)
    if (composeTagsListEqual(val, next)) return
    composeTags.value = next
  }

  function openComposeDialog() {
    composeTitle.value = ''
    composeBody.value = ''
    composeBoardId.value = opts.getDefaultBoardId()
    composeTags.value = []
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
      const tags = composeTags.value.length ? [...composeTags.value] : undefined
      const res = await createForumPost({
        title,
        body,
        boardId: composeBoardId.value,
        tags,
      })
      if (!res.success || !res.data?.id) {
        ElMessage.error(res.message ?? '发帖失败')
        return
      }
      composeOpen.value = false
      ElMessage.success('已发布')
      await opts.fetchPosts()
      void router.push({ name: 'post-detail', params: { id: res.data.id } })
    } catch (e) {
      ElMessage.error(e instanceof Error ? e.message : '发帖失败')
    } finally {
      composeSubmitting.value = false
    }
  }

  return {
    composeOpen,
    composeTitle,
    composeBody,
    composeBoardId,
    composeTags,
    composeSubmitting,
    onComposeTagsChange,
    openComposeDialog,
    submitCompose,
  }
}
