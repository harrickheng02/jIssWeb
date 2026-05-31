import {
  listModerationForumReports,
  patchModerationForumReportStatus,
  getForumPost,
  getModForumReply,
  postModReportSanction,
  type ForumPostDetail,
  type ForumReportQueueItem,
  type ForumReportModStatus,
  type ModSanctionDurationPreset,
} from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { forumAuthorLabel } from '@/utils/forumPostDisplay'
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
  targetAuthorSub?: string
  targetAuthorDisplayName?: string
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

  const sanctionDialogOpen = ref(false)
  const sanctionTargetRow = ref<ForumReportQueueItem | null>(null)
  const sanctionType = ref<'warning' | 'mute'>('mute')
  const sanctionDuration = ref<ModSanctionDurationPreset>('24h')
  const sanctionReason = ref('')
  const sanctionBusy = ref(false)

  const durationOptions: { value: ModSanctionDurationPreset; label: string }[] = [
    { value: '24h', label: '24 小时（默认）' },
    { value: '7d', label: '7 天' },
    { value: '30d', label: '30 天' },
  ]

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
      let targetAuthorSub = row.targetAuthorSub?.trim() || postRes.data.authorId
      let targetAuthorDisplayName =
        row.targetType === 'post'
          ? row.targetAuthorDisplayName?.trim() || postRes.data.authorDisplayName
          : row.targetAuthorDisplayName?.trim()
      if (row.targetType === 'reply') {
        const modReply = await getModForumReply(row.targetId)
        if (modReply.success && modReply.data) {
          replySnippet = clipText(modReply.data.body, 420)
          targetAuthorSub = modReply.data.authorId
          targetAuthorDisplayName = modReply.data.authorDisplayName
        } else {
          replySnippet ??= '暂未取得回复正文快照，请点击「查看详情」在新标签中核对。'
        }
      }

      previewByReportId.value = {
        ...previewByReportId.value,
        [id]: {
          loading: false,
          title: postRes.data.title.trim(),
          postSnippet: clipText(postRes.data.body, 520),
          replySnippet,
          postDetail: postRes.data,
          targetAuthorSub,
          targetAuthorDisplayName,
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

  function targetAuthorSubForRow(row: ForumReportQueueItem): string | undefined {
    const fromRow = row.targetAuthorSub?.trim()
    if (fromRow) return fromRow
    const p = previewByReportId.value[row.id]
    if (p && p.loading === false) return p.targetAuthorSub
    return undefined
  }

  function targetAuthorDisplayNameForRow(row: ForumReportQueueItem): string | undefined {
    const fromRow = row.targetAuthorDisplayName?.trim()
    if (fromRow) return fromRow
    const p = previewByReportId.value[row.id]
    if (p && p.loading === false) return p.targetAuthorDisplayName
    return undefined
  }

  function targetTypeLabel(targetType: string) {
    return targetType === 'reply' ? '回复' : '主帖'
  }

  function targetAuthorLabelForRow(row: ForumReportQueueItem): string | undefined {
    const sub = targetAuthorSubForRow(row)
    if (!sub) return undefined
    return forumAuthorLabel(targetAuthorDisplayNameForRow(row), sub)
  }

  function targetSummaryForRow(row: ForumReportQueueItem): string {
    const kind = targetTypeLabel(row.targetType)
    const author = targetAuthorLabelForRow(row)
    return author ? `${kind} · ${author}` : kind
  }

  function openSanctionDialog(row: ForumReportQueueItem, type: 'warning' | 'mute') {
    sanctionTargetRow.value = row
    sanctionType.value = type
    sanctionDuration.value = '24h'
    sanctionReason.value = ''
    sanctionDialogOpen.value = true
  }

  function closeSanctionDialog() {
    sanctionDialogOpen.value = false
    sanctionTargetRow.value = null
  }

  const canSubmitSanction = computed(
    () => sanctionReason.value.trim().length > 0 && Boolean(sanctionTargetRow.value),
  )

  async function submitSanction() {
    const row = sanctionTargetRow.value
    if (!row || !sanctionReason.value.trim()) return
    if (!targetAuthorSubForRow(row)) {
      ElMessage.error('无法解析被举报作者，内容可能已彻底删除')
      return
    }

    sanctionBusy.value = true
    try {
      const res = await postModReportSanction(row.id, {
        type: sanctionType.value,
        reason: sanctionReason.value.trim(),
        durationPreset: sanctionType.value === 'mute' ? sanctionDuration.value : undefined,
      })
      if (!res.success) {
        ElMessage.error(res.message ?? '操作失败')
        return
      }
      ElMessage.success(sanctionType.value === 'warning' ? '已发出警告' : '已禁言')
      closeSanctionDialog()
    } finally {
      sanctionBusy.value = false
    }
  }

  function reportContextForRow(row: ForumReportQueueItem) {
    return { reportId: row.id }
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
    reportContextForRow,
    targetAuthorSubForRow,
    targetAuthorLabelForRow,
    targetSummaryForRow,
    targetTypeLabel,
    sanctionDialogOpen,
    sanctionType,
    sanctionDuration,
    sanctionReason,
    sanctionBusy,
    durationOptions,
    canSubmitSanction,
    openSanctionDialog,
    closeSanctionDialog,
    submitSanction,
  }
}
