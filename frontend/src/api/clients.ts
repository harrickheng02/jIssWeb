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

export async function fetchDevToken() {
  const { data } = await userApi.post<ApiResult<string>>('/api/auth/token')
  return data
}
