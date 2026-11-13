import { modelApi } from './clients'
import type { ApiResult } from './clients'

// 图灵场静默指认：后端对所有分支（成功/重复/自指认/参数错/限流/未登录/异常）一律返回
// 字节级一致的 200，不泄露目标身份或判定结果。前端忽略响应体，既不展示成败也不抛错。
export async function accuseUser(targetUserId: string): Promise<void> {
  try {
    await modelApi.post<ApiResult<unknown>>('/arena/accusations', { targetUserId })
  } catch {
    // 即便网络异常也静默，维持「零感知」。
  }
}
