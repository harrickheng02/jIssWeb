import { accountingApi } from './clients'
import type { ApiResult } from './clients'

export interface PointsBalanceResponse {
  balance: number
}

export interface PointsHistoryItem {
  type: string
  delta: number
  balanceAfter: number
  occurredAtUtc: string
}

export interface SkillCatalogItem {
  id: string
  name: string
  price: number
  description?: string
}

export interface WordCloudItem {
  word: string
  count: number
}

export interface PublicInquiryResponse {
  postId: string
}

export async function getPointsBalance(): Promise<ApiResult<PointsBalanceResponse>> {
  const { data } = await accountingApi.get<ApiResult<PointsBalanceResponse>>('/points/balance')
  return data
}

export async function getPointsHistory(
  page = 1,
  size = 20,
): Promise<ApiResult<PointsHistoryItem[]>> {
  const { data } = await accountingApi.get<ApiResult<PointsHistoryItem[]>>('/points/history', {
    params: { page, size },
  })
  return data
}

export async function getSkillCatalog(): Promise<ApiResult<SkillCatalogItem[]>> {
  const { data } = await accountingApi.get<ApiResult<SkillCatalogItem[]>>('/skills/catalog')
  return data
}

export async function purchaseSkill(skillId: string): Promise<ApiResult<unknown>> {
  const { data } = await accountingApi.post<ApiResult<unknown>>('/skills/purchase', { skillId })
  return data
}

export async function behaviorScan(targetUserId: string): Promise<ApiResult<WordCloudItem[]>> {
  const { data } = await accountingApi.get<ApiResult<WordCloudItem[]>>('/skills/behavior-scan', {
    params: { targetUserId },
  })
  return data
}

export async function publicInquiry(
  targetUserId: string,
  question: string,
): Promise<ApiResult<PublicInquiryResponse>> {
  const { data } = await accountingApi.post<ApiResult<PublicInquiryResponse>>('/skills/public-inquiry', {
    targetUserId,
    question,
  })
  return data
}
