import {
  listModerationForumReports,
  patchModerationForumReportStatus,
  getForumPost,
  listForumReplies,
  type ForumPostDetail,
  type ForumReportQueueItem,
  type ForumReportModStatus,
} from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { ElMessage } from 'element-plus'
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter, type RouteLocationRaw } from 'vue-router'

type ReportPreviewIdle = {
  loading: false
  title?: string
  postSnippet?: string
  replySnippet?: string
  unavailable?: string
  postDetail?: ForumPostDetail
}

type ReportPreviewEntry = { loading: true } | ReportPreviewIdle

const statusOptions = [
  { value: '', label: '全部状态' },
  { value: 'pending', label: '待处理' },
  { value: 'rejected', label: '已驳回' },
  { value: 'resolved', label: '已处置' },
]

export function useModerationReportsQueue() {
  const router = useRouter()
  const auth = useAuthStore()

  const loading = ref(true)
  const items = ref<ForumReportQueueItem[]>([])
  const totalCount = ref(0)
  const page = ref(1)
  const pageSize = ref(20)
  const statusFilter = ref<string>('pending')
  const busyId = ref<string | null>(null)

  const expandedReportId = ref<string | null>(null)
  const previewByReportId = ref<Record<string, ReportPreviewEntry>>({})

  const canModerate = computed(() => auth.canModerate)

  function formatUtc(iso: string) {
    const d = new Date(iso)
    if (Number.isNaN(d.getTime())) return iso
    return d.toLocaleString('zh-CN')
  }

  function targetRoute(row: ForumReportQueueItem): RouteLocationRaw {
    if (row.targetType === 'reply') {
      return {
        name: 'post-detail',
        params: { id: row.postId },
        query: { reply: row.targetId },
      }
    }
    return { name: 'post-detail', params: { id: row.postId } }
  }

  function detailLinkTitle(row: ForumReportQueueItem) {
    return row.targetType === 'reply'
      ? '新标签打开并定位到被举报回复'
      : '新标签打开帖子全文'
  }

  function targetDetailHref(row: ForumReportQueueItem) {
    return router.resolve(targetRoute(row)).href
  }

  function clipText(s: string, max = 360) {
    const t = s.replace(/\s+/g, ' ').trim()
    return t.length <= max ? t : `${t.slice(0, max)}…`
  }

  async function loadContextPreview(row: ForumReportQueueItem) {
    const id = row.id
    previewByReportId.value = { ...previewByReportId.value, [id]: { loading: true } }
    try {
      const postRes = await getForumPost(row.postId)
      if (!postRes.success || !postRes.data) {
        previewByReportId.value = {
          ...previewByReportId.value,
          [id]: { loading: false, unavailable: postRes.message ?? '主题不可读或已从列表移除。' },
        }
        return
      }

      let replySnippet: string | undefined
      if (row.targetType === 'reply') {
        const rr = await listForumReplies(row.postId)
        if (rr.success && rr.data) {
          const hit = rr.data.find((r) => r.id === row.targetId)
          if (hit) replySnippet = clipText(hit.body, 420)
        }
        replySnippet ??= '暂未取得回复正文快照，请点击「查看详情」在新标签中核对。'
      }

      previewByReportId.value = {
        ...previewByReportId.value,
        [id]: {
          loading: false,
          title: postRes.data.title.trim(),
          postSnippet: clipText(postRes.data.body, 520),
          replySnippet,
          postDetail: postRes.data,
        },
      }
    } catch {
      previewByReportId.value = {
        ...previewByReportId.value,
        [id]: { loading: false, unavailable: '加载上下文失败，请稍后重试。' },
      }
    }
  }

  async function load() {
    loading.value = true
    try {
      const res = await listModerationForumReports(
        page.value,
        pageSize.value,
        statusFilter.value || undefined,
      )
      if (!res.success || !res.data) {
        ElMessage.error(res.message ?? '加载失败')
        items.value = []
        totalCount.value = 0
        return
      }
      items.value = res.data.items
      totalCount.value = res.data.totalCount
    } finally {
      loading.value = false
    }
  }

  function onGovernanceDeletedFromQueue() {
    void load()
  }

  function toggleReportRow(row: ForumReportQueueItem) {
    if (expandedReportId.value === row.id) {
      expandedReportId.value = null
      return
    }
    expandedReportId.value = row.id
    const cur = previewByReportId.value[row.id]
    const hasData =
      cur &&
      cur.loading === false &&
      Boolean(cur.title || cur.postSnippet || cur.replySnippet || cur.unavailable)
    if (!hasData) void loadContextPreview(row)
  }

  function governancePostSnapshotList(row: ForumReportQueueItem): ForumPostDetail[] {
    const p = previewByReportId.value[row.id]
    if (!p || p.loading !== false || p.unavailable || !p.postDetail) return []
    return [p.postDetail]
  }

  function onFilterChange() {
    page.value = 1
    void load()
  }

  function statusSuccessLabel(s: ForumReportModStatus): string {
    if (s === 'pending') return '已设为待处理'
    if (s === 'rejected') return '已设为已驳回'
    return '已设为已处置'
  }

  async function applyStatus(row: ForumReportQueueItem, next: ForumReportModStatus) {
    if (row.status === next) return
    busyId.value = row.id
    try {
      const res = await patchModerationForumReportStatus(row.id, next)
      if (!res.success) {
        ElMessage.error(res.message ?? '操作失败')
        return
      }
      ElMessage.success(statusSuccessLabel(next))
      await load()
    } finally {
      busyId.value = null
    }
  }

  function statusRowLabel(row: ForumReportQueueItem) {
    if (row.status === 'pending') return '待处理'
    if (row.status === 'rejected') return '已驳回'
    if (row.status === 'resolved') return '已处置'
    return row.status
  }

  function reportPreviewIdle(rowId: string): ReportPreviewIdle | undefined {
    const p = previewByReportId.value[rowId]
    if (!p || p.loading !== false) return undefined
    return p
  }

  function reportPreviewIdleList(rowId: string): ReportPreviewIdle[] {
    const p = reportPreviewIdle(rowId)
    return p ? [p] : []
  }

  watch([page, pageSize], () => {
    void load()
  })

  onMounted(() => {
    void load()
  })

  return {
    router,
    canModerate,
    loading,
    items,
    totalCount,
    page,
    pageSize,
    statusFilter,
    busyId,
    expandedReportId,
    previewByReportId,
    statusOptions,
    formatUtc,
    targetDetailHref,
    detailLinkTitle,
    toggleReportRow,
    governancePostSnapshotList,
    onGovernanceDeletedFromQueue,
    onFilterChange,
    applyStatus,
    statusRowLabel,
    reportPreviewIdleList,
  }
}
