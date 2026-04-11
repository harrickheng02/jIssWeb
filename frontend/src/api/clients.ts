import axios, { type AxiosInstance } from 'axios'
import { useAuthStore } from '@/stores/auth'

export interface ApiResult<T> {
  success: boolean
  data?: T
  message?: string
  code?: string
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

export async function register(email: string, password: string) {
  const { data } = await userApi.post<ApiResult<string>>('/auth/register', { email, password })
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

export async function listForumPosts(page = 1, pageSize = 20, boardId?: string) {
  const params: Record<string, string | number> = { page, pageSize }
  if (boardId) params.boardId = boardId
  const { data } = await modelApi.get<ApiResult<PagedForumPosts>>('/forum/posts', { params })
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
