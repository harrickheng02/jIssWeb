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

export const userApi = createClient('/api-user')
export const customerApi = createClient('/api-customer')
export const modelApi = createClient('/api-model')
export const accountingApi = createClient('/api-accounting')
export const reportApi = createClient('/api-report')

export interface AuthTokenPair {
  accessToken: string
  accessTokenExpiresAtUtc: string
  refreshToken: string
  refreshTokenExpiresAtUtc: string
}

export async function register(email: string, password: string) {
  const { data } = await userApi.post<ApiResult<AuthTokenPair>>('/api/auth/register', { email, password })
  return data
}

export async function login(email: string, password: string) {
  const { data } = await userApi.post<ApiResult<AuthTokenPair>>('/api/auth/login', { email, password })
  return data
}

export async function refresh(refreshToken: string) {
  const { data } = await userApi.post<ApiResult<AuthTokenPair>>('/api/auth/refresh', { refreshToken })
  return data
}

export async function revoke(refreshToken: string) {
  const { data } = await userApi.post<ApiResult<string>>('/api/auth/revoke', { refreshToken })
  return data
}
