import axios, { type AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '@/stores/auth'
import { redirectToLogin } from '@/utils/authRedirect'

export interface ApiResult<T> {
  success: boolean
  data?: T
  message?: string
  code?: string
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
  likes: number
  comments: number
  views: number
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

export interface ForumReply {
  id: string
  postId: string
  authorId: string
  authorDisplayName?: string
  body: string
  createdAtUtc: string
}

export interface ForumBoardItem {
  id: string
  title: string
}

export async function getForumBoards() {
  const { data } = await modelApi.get<ApiResult<ForumBoardItem[]>>('/forum/boards')
  return data
}

export async function listForumPosts(page = 1, pageSize = 20, boardId?: string, q?: string) {
  const params: Record<string, string | number> = { page, pageSize }
  if (boardId) params.boardId = boardId
  if (q !== undefined && q !== '') params.q = q
  const { data } = await modelApi.get<ApiResult<PagedForumPosts>>('/forum/posts', {
    params,
    headers: { 'Cache-Control': 'no-cache', Pragma: 'no-cache' },
  })
  return data
}

export async function getForumPost(id: string) {
  const { data } = await modelApi.get<ApiResult<ForumPostDetail>>(`/forum/posts/${id}`)
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
  const { data } = await modelApi.post<ApiResult<ForumReply>>(`/forum/posts/${postId}/replies`, { body })
  return data
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
