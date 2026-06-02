import axios, { type AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '@/stores/auth'
import { redirectToLogin } from '@/utils/authRedirect'

export interface ApiResult<T> {
  success: boolean
  data?: T
  message?: string
  code?: string
}

export type ApiErrorResult = {
  success: false
  message?: string
  code?: string
  data?: unknown
}

type RetryAuthConfig = InternalAxiosRequestConfig & { _retryAuth?: boolean }

const refreshInFlightByPrefix = new Map<string, Promise<void>>()

function normalizeBasePrefix(prefix: string): string {
  const t = prefix.trim()
  if (!t) return ''
  return t.endsWith('/') ? t.slice(0, -1) : t
}

function normalizePath(url: string | undefined): string {
  if (!url) return ''
  return url.startsWith('/') ? url : `/${url}`
}

function isAuthPath(path: string): boolean {
  return normalizePath(path).startsWith('/auth/')
}

function runSingleFlightRefresh(refreshTokenValue: string, apiPrefix: string): Promise<void> {
  const key = normalizeBasePrefix(apiPrefix)
  if (!key) {
    throw new Error('createClient baseURL prefix is required for auth refresh')
  }
  let inflight = refreshInFlightByPrefix.get(key)
  if (inflight) return inflight
  inflight = (async () => {
    try {
      const { data } = await axios.post<ApiResult<AuthTokenPair>>(
        `${key}/auth/refresh`,
        { refreshToken: refreshTokenValue },
        { timeout: 15000 },
      )
      const auth = useAuthStore()
      if (data.success && data.data) {
        auth.applyAuthSession(data.data.accessToken, data.data.refreshToken)
      } else {
        throw new Error(data.message ?? 'refresh failed')
      }
    } finally {
      refreshInFlightByPrefix.delete(key)
    }
  })()
  refreshInFlightByPrefix.set(key, inflight)
  return inflight
}

function createClient(prefix: string): AxiosInstance {
  const instance = axios.create({
    baseURL: prefix,
    timeout: 15000,
  })
  instance.interceptors.request.use((config) => {
    const auth = useAuthStore()
    if (auth.token) {
      config.headers.Authorization = `Bearer ${auth.token}`
    }
    return config
  })
  instance.interceptors.response.use(
    (r) => r,
    async (error: AxiosError) => {
      const original = error.config as RetryAuthConfig | undefined
      const status = error.response?.status
      if (status !== 401 || !original || original._retryAuth) {
        return Promise.reject(error)
      }
      const path = normalizePath(original.url)
      if (isAuthPath(path)) {
        return Promise.reject(error)
      }
      const auth = useAuthStore()
      const bearer = original.headers?.Authorization
      const hadBearer =
        typeof bearer === 'string' && bearer.startsWith('Bearer ') && bearer.length > 'Bearer '.length
      if (!hadBearer || !auth.refreshToken) {
        auth.clearAuth()
        await redirectToLogin()
        return Promise.reject(error)
      }
      try {
        await runSingleFlightRefresh(auth.refreshToken, prefix)
      } catch {
        auth.clearAuth()
        await redirectToLogin()
        return Promise.reject(error)
      }
      original._retryAuth = true
      const next = useAuthStore().token
      if (!next) {
        auth.clearAuth()
        await redirectToLogin()
        return Promise.reject(error)
      }
      original.headers = original.headers ?? {}
      original.headers.Authorization = `Bearer ${next}`
      return instance.request(original)
    },
  )
  return instance
}

export const userApi = createClient('/api')
export const customerApi = createClient('/api')
export const modelApi = createClient('/api')
export const accountingApi = createClient('/api')
export const reportApi = createClient('/api')
export const bffApi = createClient('/api')

export interface AuthTokenPair {
  accessToken: string
  accessTokenExpiresAtUtc: string
  refreshToken: string
  refreshTokenExpiresAtUtc: string
}

export interface RegisterResult {
  value: string
}

export async function register(
  email: string,
  password: string,
  profile: { nickname?: string; gender?: string; birthDate?: string },
) {
  const body: Record<string, string> = {
    email,
    password,
  }
  if (profile.nickname?.trim()) body.nickname = profile.nickname.trim()
  if (profile.gender) body.gender = profile.gender
  if (profile.birthDate) body.birthDate = profile.birthDate
  const { data } = await userApi.post<ApiResult<string>>('/auth/register', body)
  return data
}

export async function login(email: string, password: string) {
  const { data } = await userApi.post<ApiResult<AuthTokenPair>>('/auth/login', { email, password })
  return data
}

export async function refresh(refreshToken: string) {
  const { data } = await userApi.post<ApiResult<AuthTokenPair>>('/auth/refresh', { refreshToken })
  return data
}

export async function revoke(refreshToken: string) {
  const { data } = await userApi.post<ApiResult<string>>('/auth/revoke', { refreshToken })
  return data
}

export async function resendVerification(email: string) {
  const { data } = await userApi.post<ApiResult<string>>('/auth/resend-verification', { email })
  return data
}

export async function exchangeVerifySession(verifySession: string) {
  const { data } = await userApi.post<ApiResult<AuthTokenPair>>('/auth/exchange-verify-session', { verifySession })
  return data
}

export async function forgotPassword(email: string) {
  const { data } = await userApi.post<ApiResult<string>>('/auth/forgot-password', { email })
  return data
}

export async function completeResetPassword(resetSession: string, password: string) {
  const { data } = await userApi.post<ApiResult<AuthTokenPair>>('/auth/complete-reset-password', {
    resetSession,
    password,
  })
  return data
}

export interface CustomerRecord {
  id: string
  ownerUserId: string
  name: string
  remark?: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ProfileRecord {
  id: string
  ownerUserId: string
  nickname?: string
  birthDate?: string
  gender?: string
  createdAtUtc: string
  updatedAtUtc: string
}

export async function getProfile() {
  const { data } = await customerApi.get<ApiResult<ProfileRecord>>('/profile')
  return data
}

export async function updateProfile(payload: { nickname?: string; birthDate?: string; gender?: string }) {
  const { data } = await customerApi.put<ApiResult<ProfileRecord>>('/profile', payload)
  return data
}

export async function getBootstrap() {
  const { data } = await bffApi.get<ApiResult<{ services: Array<{ name: string; available: boolean; message?: string }> }>>(
    '/bff/bootstrap',
  )
  return data
}

export interface ForumPostListItem {
  id: string
  title: string
  excerpt: string
  authorId: string
  authorDisplayName?: string
  publishedAtUtc: string
  board: string
  tags: string[]
  isSticky?: boolean
  repliesLocked?: boolean
  isFeatured?: boolean
  likes: number
  comments: number
  views: number
  /** Present when API returns engagement snapshot for current user. */
  likedByMe?: boolean
  favoritedByMe?: boolean
  favoriteCount?: number
  updatedAtUtc?: string | null
  state?: string
  deletedAtUtc?: string | null
  deletedBySub?: string | null
}

/** Partial update after like/favorite mutation (merge into list row). */
export type ForumPostListPatch = Pick<ForumPostListItem, 'id'> &
  Partial<Pick<ForumPostListItem, 'likes' | 'likedByMe' | 'favoritedByMe' | 'favoriteCount'>>

export interface ForumPostEngagementSnapshot {
  postId: string
  likeCount: number
  favoriteCount: number
  likedByMe: boolean
  favoritedByMe: boolean
}

export interface PagedForumPosts {
  items: ForumPostListItem[]
  totalCount: number
  page: number
  pageSize: number
}

export interface ForumPostDetail extends ForumPostListItem {
  body: string
}

export interface ModerationSetStickyResult {
  postId: string
  isSticky: boolean
}

export interface ModerationSetRepliesLockedResult {
  postId: string
  repliesLocked: boolean
}

export interface ModerationSetFeaturedResult {
  postId: string
  isFeatured: boolean
}

export interface ModerationAuditItem {
  id: string
  targetType: string
  targetId: string
  /** 面向展示的操作说明（如「置顶帖子」） */
  actionLabel: string
  /** 操作者展示名（昵称，来自客档 profiles） */
  operatorDisplayName: string
  occurredAtUtc: string
}

export interface PagedModerationAudit {
  items: ModerationAuditItem[]
  totalCount: number
  page: number
  pageSize: number
}

export type ModerationAuditActionFilter =
  | 'post.setSticky'
  | 'post.unsetSticky'
  | 'post.lockReplies'
  | 'post.unlockReplies'
  | 'post.setFeatured'
  | 'post.unsetFeatured'
  | 'post.modDelete'
  | 'reply.modDelete'
  | 'user.warn'
  | 'user.mute'
  | 'user.unmute'
  | 'report.acknowledge'
  | 'report.resolve'
  | 'report.reject'

export interface ListModerationAuditOptions {
  page?: number
  pageSize?: number
  action?: ModerationAuditActionFilter | ModerationAuditActionFilter[]
  fromUtc?: string
  toUtc?: string
}

function mapModerationError(error: unknown): ApiErrorResult {
  const e = error as AxiosError<ApiResult<unknown>> | undefined
  const status = e?.response?.status
  const data = e?.response?.data
  if (data && typeof data === 'object' && data.success === false) {
    return { success: false, message: data.message, code: data.code, data: data.data }
  }
  if (status === 403) return { success: false, message: '无权操作', code: 'FORBIDDEN' }
  if (status === 404) return { success: false, message: '内容不存在或已删除', code: 'NOT_FOUND' }
  return { success: false, message: e instanceof Error ? e.message : '网络异常，请稍后重试', code: 'REQUEST_FAILED' }
}

function mapForumMutationError(error: unknown): ApiErrorResult {
  const e = error as AxiosError<ApiResult<unknown>> | undefined
  const data = e?.response?.data
  if (data && typeof data === 'object' && data.success === false) {
    return { success: false, message: data.message, code: data.code, data: data.data }
  }
  return mapModerationError(error)
}

export async function setForumPostSticky(postId: string, isSticky: boolean) {
  try {
    const { data } = await modelApi.post<ApiResult<ModerationSetStickyResult>>(`/mod/posts/${postId}/sticky`, {
      isSticky,
    })
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export async function setForumPostFeatured(postId: string, isFeatured: boolean) {
  try {
    const { data } = await modelApi.post<ApiResult<ModerationSetFeaturedResult>>(`/mod/posts/${postId}/featured`, {
      isFeatured,
    })
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export async function setForumPostRepliesLocked(postId: string, repliesLocked: boolean) {
  try {
    const { data } = await modelApi.post<ApiResult<ModerationSetRepliesLockedResult>>(
      `/mod/posts/${postId}/replies-locked`,
      { repliesLocked },
    )
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export async function deleteModerationForumPost(
  postId: string,
  context?: { reportId: string; reason?: string },
) {
  try {
    const { data } = await modelApi.delete<ApiResult<string>>(`/mod/posts/${postId}`, {
      data: context,
    })
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export async function deleteModerationForumReply(
  replyId: string,
  context?: { reportId: string; reason?: string },
) {
  try {
    const { data } = await modelApi.delete<ApiResult<string>>(`/mod/replies/${replyId}`, {
      data: context,
    })
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export type ModSanctionDurationPreset = '24h' | '7d' | '30d'

export async function postModUserSanction(
  sub: string,
  payload: {
    type: 'warning' | 'mute'
    reason: string
    reportId: string
    durationPreset?: ModSanctionDurationPreset
  },
) {
  try {
    const { data } = await modelApi.post<
      ApiResult<{
        sanctionId: string
        type: string
        durationPreset?: string
        expiresAtUtc?: string
      }>
    >(`/mod/users/${encodeURIComponent(sub)}/sanctions`, payload)
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export async function postModReportSanction(
  reportId: string,
  payload: {
    type: 'warning' | 'mute'
    reason: string
    durationPreset?: ModSanctionDurationPreset
  },
) {
  try {
    const { data } = await modelApi.post<
      ApiResult<{
        sanctionId: string
        type: string
        durationPreset?: string
        expiresAtUtc?: string
      }>
    >(`/mod/reports/${encodeURIComponent(reportId)}/sanctions`, payload)
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export async function getModForumReply(replyId: string) {
  try {
    const { data } = await modelApi.get<
      ApiResult<{
        id: string
        postId: string
        authorId: string
        authorDisplayName: string
        body: string
        state: string
        createdAtUtc: string
      }>
    >(`/mod/replies/${encodeURIComponent(replyId)}`)
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export interface ModerationAuditFeedItem extends ModerationAuditItem {
  boardId: string
  boardLabel: string
  postId: string
  reportId: string
}

export interface PagedModerationAuditFeed {
  items: ModerationAuditFeedItem[]
  totalCount: number
  page: number
  pageSize: number
}

export interface ListModerationAuditFeedOptions {
  page?: number
  pageSize?: number
  action?: ModerationAuditActionFilter | ModerationAuditActionFilter[]
  fromUtc?: string
  toUtc?: string
  boardId?: string
}

function buildModerationAuditFeedFilterParams(
  opts: ListModerationAuditFeedOptions,
): Record<string, string | number | string[] | undefined> {
  const params: Record<string, string | number | string[] | undefined> = {}
  if (opts.fromUtc) params.fromUtc = opts.fromUtc
  if (opts.toUtc) params.toUtc = opts.toUtc
  if (opts.boardId) params.boardId = opts.boardId
  if (opts.action) {
    const actions = Array.isArray(opts.action) ? opts.action : [opts.action]
    params.action = actions.length === 1 ? actions[0]! : actions
  }
  return params
}

function buildModerationAuditFeedParams(opts: ListModerationAuditFeedOptions): Record<string, string | number | string[] | undefined> {
  return {
    page: opts.page ?? 1,
    pageSize: opts.pageSize ?? 20,
    ...buildModerationAuditFeedFilterParams(opts),
  }
}

export async function listModerationAuditFeed(options: ListModerationAuditFeedOptions = {}) {
  try {
    const { data } = await modelApi.get<ApiResult<PagedModerationAuditFeed>>('/mod/audit/feed', {
      params: buildModerationAuditFeedParams(options),
      paramsSerializer: { indexes: null },
    })
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export async function exportModerationAuditCsv(options: ListModerationAuditFeedOptions = {}) {
  try {
    const response = await modelApi.get<Blob>('/mod/audit/export', {
      params: buildModerationAuditFeedFilterParams(options),
      paramsSerializer: { indexes: null },
      responseType: 'blob',
    })
    return { success: true as const, blob: response.data, filename: parseContentDispositionFilename(response.headers['content-disposition']) }
  } catch (e) {
    const axiosErr = e as { response?: { data?: Blob; status?: number } }
    if (axiosErr.response?.data instanceof Blob && axiosErr.response.status === 400) {
      const text = await axiosErr.response.data.text()
      try {
        const parsed = JSON.parse(text) as ApiResult<unknown>
        if (parsed.success === false) {
          return { success: false as const, message: parsed.message, code: parsed.code }
        }
      } catch {
        /* fall through */
      }
    }
    return mapModerationError(e)
  }
}

function parseContentDispositionFilename(header: string | undefined): string {
  if (!header) return `moderation-audit-${new Date().toISOString().replace(/[:.]/g, '-')}.csv`
  const match = /filename="?([^";\n]+)"?/i.exec(header)
  return match?.[1]?.trim() || `moderation-audit-${Date.now()}.csv`
}

export async function listModerationAuditByPost(
  postId: string,
  pageOrOptions: number | ListModerationAuditOptions = 1,
  pageSize = 20,
) {
  const opts: ListModerationAuditOptions =
    typeof pageOrOptions === 'number' ? { page: pageOrOptions, pageSize } : pageOrOptions
  const page = opts.page ?? 1
  const size = opts.pageSize ?? 20
  const params: Record<string, string | number | string[] | undefined> = {
    targetType: 'post',
    targetId: postId,
    page,
    pageSize: size,
  }
  if (opts.fromUtc) params.fromUtc = opts.fromUtc
  if (opts.toUtc) params.toUtc = opts.toUtc
  if (opts.action) {
    const actions = Array.isArray(opts.action) ? opts.action : [opts.action]
    params.action = actions.length === 1 ? actions[0]! : actions
  }
  try {
    const { data } = await modelApi.get<ApiResult<PagedModerationAudit>>('/mod/audit', {
      params,
      // ASP.NET [FromQuery] string[] expects action=a&action=b, not action[]=a
      paramsSerializer: { indexes: null },
    })
    return data
  } catch (e) {
    return mapModerationError(e)
  }
}

export interface ForumReply {
  id: string
  postId: string
  authorId: string
  authorDisplayName?: string
  body: string
  createdAtUtc: string
  updatedAtUtc?: string | null
}

export interface PagedForumReplies {
  items: ForumReply[]
  totalCount: number
  page: number
  pageSize: number
}

export interface ForumBoardItem {
  id: string
  title: string
}

export async function getForumBoards() {
  const { data } = await modelApi.get<ApiResult<ForumBoardItem[]>>('/forum/boards')
  return data
}

export interface ForumAnnouncementItem {
  id: string
  title: string
  summary?: string
  linkUrl?: string
  publishedAtUtc: string
  pinned?: boolean
}

export async function getForumAnnouncements(limit = 5) {
  const { data } = await modelApi.get<ApiResult<ForumAnnouncementItem[]>>('/forum/announcements', {
    params: { limit },
  })
  return data
}

export async function listForumPosts(
  page = 1,
  pageSize = 20,
  boardId?: string,
  q?: string,
  tag?: string,
  sort?: 'latest' | 'hot',
  featured?: boolean,
) {
  const params: Record<string, string | number | boolean> = { page, pageSize }
  if (boardId) params.boardId = boardId
  if (q !== undefined && q !== '') params.q = q
  if (tag !== undefined && tag !== '') params.tag = tag
  if (sort === 'hot') params.sort = 'hot'
  if (featured === true) params.featured = true
  const { data } = await modelApi.get<ApiResult<PagedForumPosts>>('/forum/posts', {
    params,
    headers: { 'Cache-Control': 'no-cache', Pragma: 'no-cache' },
  })
  return data
}

export async function listMyForumPosts(page = 1, pageSize = 20) {
  const { data } = await modelApi.get<ApiResult<PagedForumPosts>>('/forum/me/posts', { params: { page, pageSize } })
  return data
}

export async function listMyForumReplies(page = 1, pageSize = 20) {
  const { data } = await modelApi.get<ApiResult<PagedForumReplies>>('/forum/me/replies', { params: { page, pageSize } })
  return data
}

export async function listMyForumFavorites(page = 1, pageSize = 20) {
  const { data } = await modelApi.get<ApiResult<PagedForumPosts>>('/forum/me/favorites', { params: { page, pageSize } })
  return data
}

export async function getForumPopularTags(boardId?: string, limit?: number) {
  const params: Record<string, string | number> = {}
  if (boardId) params.boardId = boardId
  if (limit != null) params.limit = limit
  const { data } = await modelApi.get<ApiResult<string[]>>('/forum/tags/popular', { params })
  return data
}

export async function getForumPost(id: string) {
  const { data } = await modelApi.get<ApiResult<ForumPostDetail>>(`/forum/posts/${id}`)
  return data
}

export async function likeForumPost(postId: string) {
  const { data } = await modelApi.post<ApiResult<ForumPostEngagementSnapshot>>(`/forum/posts/${postId}/like`)
  return data
}

export async function unlikeForumPost(postId: string) {
  const { data } = await modelApi.delete<ApiResult<ForumPostEngagementSnapshot>>(`/forum/posts/${postId}/like`)
  return data
}

export async function favoriteForumPost(postId: string) {
  const { data } = await modelApi.post<ApiResult<ForumPostEngagementSnapshot>>(`/forum/posts/${postId}/favorite`)
  return data
}

export async function unfavoriteForumPost(postId: string) {
  const { data } = await modelApi.delete<ApiResult<ForumPostEngagementSnapshot>>(`/forum/posts/${postId}/favorite`)
  return data
}

export async function createForumPost(payload: {
  title: string
  body: string
  boardId?: string
  board?: string
  tags?: string[]
}) {
  const { data } = await modelApi.post<ApiResult<{ id: string }>>('/forum/posts', payload)
  return data
}

export async function listForumReplies(postId: string) {
  const { data } = await modelApi.get<ApiResult<ForumReply[]>>(`/forum/posts/${postId}/replies`)
  return data
}

export async function createForumReply(postId: string, body: string) {
  try {
    const { data } = await modelApi.post<ApiResult<ForumReply>>(`/forum/posts/${postId}/replies`, { body })
    return data
  } catch (error: unknown) {
    const e = error as AxiosError<ApiResult<unknown>> | undefined
    const status = e?.response?.status
    const parsed = e?.response?.data
    if (parsed && typeof parsed === 'object' && parsed.success === false)
      return {
        success: false,
        message: parsed.message,
        code: parsed.code,
        data: parsed.data,
      } as ApiResult<ForumReply>
    if (status === 401)
      return { success: false, message: '请先登录', code: 'UNAUTHORIZED' } as ApiResult<ForumReply>
    if (status === 403 && parsed?.code === 'REPLIES_LOCKED')
      return { success: false, message: parsed.message ?? '本帖已禁止回复', code: 'REPLIES_LOCKED' } as ApiResult<ForumReply>
    if (status === 403 && parsed?.code === 'FORUM_MUTED')
      return {
        success: false,
        message: parsed.message,
        code: 'FORUM_MUTED',
        data: parsed.data,
      } as ApiResult<ForumReply>
    if (status === 403)
      return { success: false, message: '暂无回复权限', code: 'FORBIDDEN' } as ApiResult<ForumReply>
    if (status === 404)
      return { success: false, message: '帖子不存在或已删除', code: 'NOT_FOUND' } as ApiResult<ForumReply>
    return {
      success: false,
      message: e instanceof Error ? e.message : '网络异常',
      code: 'REQUEST_FAILED',
    } as ApiResult<ForumReply>
  }
}

export interface ForumReportCreated {
  id: string
}

export async function submitForumReport(payload: { targetType: 'post' | 'reply'; targetId: string; reason?: string }) {
  try {
    const body: Record<string, string> = {
      targetType: payload.targetType,
      targetId: payload.targetId,
    }
    const r = payload.reason?.trim()
    if (r) body.reason = r
    const { data } = await modelApi.post<ApiResult<ForumReportCreated>>('/forum/reports', body)
    return data
  } catch (e) {
    return mapForumReportSubmitError(e)
  }
}

function mapForumReportSubmitError(error: unknown): ApiResult<never> {
  const ex = error as AxiosError<ApiResult<unknown>> | undefined
  const status = ex?.response?.status
  const parsed = ex?.response?.data
  if (parsed && typeof parsed === 'object' && parsed.success === false)
    return { success: false, message: parsed.message, code: parsed.code }
  if (status === 401) return { success: false, message: '请先登录', code: 'UNAUTHORIZED' }
  if (status === 404) return { success: false, message: '内容不存在或已删除', code: 'NOT_FOUND' }
  if (status === 409)
    return { success: false, message: parsed?.message ?? '已有待处理的举报', code: 'DUPLICATE_PENDING_REPORT' }
  return { success: false, message: ex instanceof Error ? ex.message : '网络异常', code: 'REQUEST_FAILED' }
}

export type ForumReportModStatus = 'pending' | 'rejected' | 'resolved'

export interface ForumReportQueueItem {
  id: string
  reporterSub: string
  reporterDisplayName: string
  targetType: string
  targetId: string
  postId: string
  boardId: string
  boardTitle: string
  reason?: string
  status: string
  createdAtUtc: string
  updatedAtUtc: string
  handledBySub?: string | null
  handledAtUtc?: string | null
  acknowledgedAtUtc?: string | null
  acknowledgedBySub?: string | null
  targetAuthorSub?: string | null
  targetAuthorDisplayName?: string | null
}

export interface PagedForumReports {
  items: ForumReportQueueItem[]
  totalCount: number
  page: number
  pageSize: number
}

function mapModerationReportsError(error: unknown): ApiResult<never> {
  const e = error as AxiosError<ApiResult<unknown>> | undefined
  const status = e?.response?.status
  const data = e?.response?.data
  if (data && typeof data === 'object' && data.success === false) {
    return { success: false, message: data.message, code: data.code }
  }
  if (status === 403) return { success: false, message: '无权查看或处理举报', code: 'FORBIDDEN' }
  if (status === 401) return { success: false, message: '请先登录', code: 'UNAUTHORIZED' }
  return { success: false, message: e instanceof Error ? e.message : '网络异常', code: 'REQUEST_FAILED' }
}

export async function listModerationForumReports(page = 1, pageSize = 20, status?: string) {
  try {
    const params: Record<string, string | number> = { page, pageSize }
    const s = status?.trim()
    if (s) params.status = s
    const { data } = await modelApi.get<ApiResult<PagedForumReports>>('/mod/reports', { params })
    return data
  } catch (e) {
    return mapModerationReportsError(e)
  }
}

export async function patchModerationForumReportStatus(reportId: string, status: ForumReportModStatus) {
  try {
    const { data } = await modelApi.patch<ApiResult<ForumReportQueueItem>>(`/mod/reports/${reportId}`, { status })
    return data
  } catch (e) {
    return mapModerationReportsError(e)
  }
}

export async function postModReportAcknowledge(reportId: string) {
  try {
    const { data } = await modelApi.post<ApiResult<ForumReportQueueItem>>(
      `/mod/reports/${encodeURIComponent(reportId)}/acknowledge`,
    )
    return data
  } catch (e) {
    return mapModerationReportsError(e)
  }
}

export async function downloadModReportEvidence(reportId: string): Promise<ApiResult<Blob>> {
  try {
    const response = await modelApi.get<Blob>(
      `/mod/reports/${encodeURIComponent(reportId)}/evidence`,
      { responseType: 'blob', validateStatus: () => true },
    )
    if (response.status >= 400) {
      const text = await response.data.text()
      try {
        const parsed = JSON.parse(text) as ApiResult<unknown>
        return {
          success: false,
          message: parsed.message ?? '导出失败',
          code: parsed.code ?? 'REQUEST_FAILED',
        }
      } catch {
        return { success: false, message: '导出失败', code: 'REQUEST_FAILED' }
      }
    }
    return { success: true, data: response.data }
  } catch (e) {
    return mapModerationReportsError(e)
  }
}

export interface ForumNotificationItem {
  id: string
  type: string
  postId: string
  replyId: string | null
  actorId: string
  actorDisplayName?: string
  postTitle: string
  read: boolean
  createdAtUtc: string
}

export interface PagedForumNotifications {
  items: ForumNotificationItem[]
  totalCount: number
  page: number
  pageSize: number
}

export async function listForumNotifications(page = 1, pageSize = 20, unreadOnly = false) {
  const { data } = await modelApi.get<ApiResult<PagedForumNotifications>>('/forum/notifications', {
    params: { page, pageSize, unreadOnly },
  })
  return data
}

export async function getForumUnreadNotificationCount() {
  const { data } = await modelApi.get<ApiResult<{ count: number }>>('/forum/notifications/unread-count')
  return data
}

export async function markForumNotificationRead(id: string) {
  const { data } = await modelApi.post<ApiResult<unknown>>(`/forum/notifications/${id}/read`)
  return data
}

export async function markAllForumNotificationsRead() {
  const { data } = await modelApi.post<ApiResult<unknown>>('/forum/notifications/read-all')
  return data
}

// ── Tag Registry ─────────────────────────────────────────────────────────────

export async function getForumTagSuggest(q?: string, limit = 10) {
  const params: Record<string, string | number> = { limit }
  if (q?.trim()) params.q = q.trim()
  const { data } = await modelApi.get<ApiResult<string[]>>('/forum/tags/suggest', { params })
  return data
}

export interface ForumTagDto {
  id: string
  name: string
  slug: string
  description?: string
  status: 'active' | 'disabled'
  useCount: number
  createdAtUtc: string
  updatedAtUtc?: string
}

export interface PagedForumTags {
  items: ForumTagDto[]
  totalCount: number
  page: number
  pageSize: number
}

export async function adminListTags(params?: {
  page?: number
  pageSize?: number
  status?: string
  q?: string
}) {
  const { data } = await modelApi.get<ApiResult<PagedForumTags>>('/forum/admin/tags', { params })
  return data
}

export async function adminCreateTag(body: { name: string; description?: string }) {
  const { data } = await modelApi.post<ApiResult<ForumTagDto>>('/forum/admin/tags', body)
  return data
}

export async function adminPatchTag(id: string, body: { name?: string; description?: string }) {
  const { data } = await modelApi.patch<ApiResult<ForumTagDto>>(`/forum/admin/tags/${id}`, body)
  return data
}

export async function adminDisableTag(id: string) {
  const { data } = await modelApi.post<ApiResult<ForumTagDto>>(`/forum/admin/tags/${id}/disable`)
  return data
}

export async function adminEnableTag(id: string) {
  const { data } = await modelApi.post<ApiResult<ForumTagDto>>(`/forum/admin/tags/${id}/enable`)
  return data
}

export async function adminDeleteTag(id: string) {
  const { data } = await modelApi.delete<ApiResult<unknown>>(`/forum/admin/tags/${id}`)
  return data
}

export async function adminSeedTagsFromPosts() {
  const { data } = await modelApi.post<ApiResult<unknown>>('/forum/admin/tags/seed-from-posts')
  return data
}

// ── Post / Reply self-edit ────────────────────────────────────────────────────

export interface UpdateForumPostRequest {
  title?: string
  body?: string
  tags?: string[]
}

export interface UpdateForumReplyRequest {
  body: string
}

export async function updateForumPost(postId: string, body: UpdateForumPostRequest) {
  const { data } = await modelApi.put<ApiResult<ForumPostListItem>>(`/forum/posts/${postId}`, body)
  return data
}

export async function updateForumReply(postId: string, replyId: string, body: UpdateForumReplyRequest) {
  const { data } = await modelApi.put<ApiResult<ForumReply>>(`/forum/posts/${postId}/replies/${replyId}`, body)
  return data
}

export async function deleteForumPost(postId: string) {
  try {
    const { data } = await modelApi.delete<ApiResult<string>>(`/forum/posts/${postId}`)
    return data
  } catch (e) {
    return mapForumMutationError(e)
  }
}

export async function deleteForumReply(postId: string, replyId: string) {
  try {
    const { data } = await modelApi.delete<ApiResult<string>>(`/forum/posts/${postId}/replies/${replyId}`)
    return data
  } catch (e) {
    return mapForumMutationError(e)
  }
}

export async function permanentDeleteForumPost(postId: string) {
  const { data } = await modelApi.delete<ApiResult<string>>(`/forum/posts/${postId}/permanent`)
  return data
}

// ── Draft lifecycle ───────────────────────────────────────────────────────────

export interface DraftResult {
  id: string
  state: string
}

export async function createDraft(body: { title?: string; body?: string; boardId?: string; tags?: string[] }) {
  const { data } = await modelApi.post<ApiResult<DraftResult>>('/forum/posts/drafts', body)
  return data
}

export async function updateDraft(draftId: string, body: { title?: string; body?: string; boardId?: string; tags?: string[] }) {
  const { data } = await modelApi.put<ApiResult<ForumPostDetail>>(`/forum/posts/drafts/${draftId}`, body)
  return data
}

export async function deleteDraft(draftId: string) {
  const { data } = await modelApi.delete<ApiResult<unknown>>(`/forum/posts/drafts/${draftId}`)
  return data
}

export async function publishDraft(draftId: string) {
  const { data } = await modelApi.post<ApiResult<DraftResult>>(`/forum/posts/drafts/${draftId}/publish`)
  return data
}

export async function getMyDrafts(page = 1, pageSize = 20) {
  const { data } = await modelApi.get<ApiResult<PagedForumPosts>>('/forum/me/drafts', { params: { page, pageSize } })
  return data
}
