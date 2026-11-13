// 图灵场指认埋点：严格白名单，禁止携带被指认者身份。
//
// 设计约束（spec D-侧信道封堵 / 零感知）：前端埋点只允许上报与「指认者行为」相关的
// 聚合字段，绝对不允许出现 targetUserId / targetUserIdHash 等可反推被指认者的字段。
// 本模块集中守门：任何不在白名单内的键（尤其是 targetUserIdHash）都会被 assert 拒绝。

export const ARENA_ANALYTICS_ALLOWED_KEYS = ['ts', 'accuserIdHash', 'weekKey'] as const
export type ArenaAnalyticsAllowedKey = (typeof ARENA_ANALYTICS_ALLOWED_KEYS)[number]

// 不可逆哈希盐：仅用于把指认者标识降维为不可还原的标签，避免上报明文用户 id。
const HASH_SALT = 'turing-arena-v0.2:sink'

function djb2Hash(input: string): string {
  let hash = 5381
  for (let i = 0; i < input.length; i++) {
    hash = (hash * 33) ^ input.charCodeAt(i)
  }
  // 转无符号 32 位十六进制，固定 8 位。
  return (hash >>> 0).toString(16).padStart(8, '0')
}

export function hashAccuserId(accuserId: string): string {
  return djb2Hash(`${accuserId}::${HASH_SALT}`)
}

export interface ArenaAccusationEventInput {
  accuserId: string
  weekKey: string
}

export interface ArenaAccusationEvent {
  ts: number
  accuserIdHash: string
  weekKey: string
}

export function buildArenaAccusationEvent(input: ArenaAccusationEventInput): ArenaAccusationEvent {
  return {
    ts: Date.now(),
    accuserIdHash: hashAccuserId(input.accuserId),
    weekKey: input.weekKey,
  }
}

// 守门：任何不在白名单内的键（尤其是 targetUserIdHash）都抛错，阻断侧信道。
export function assertAllowedAnalyticsKeys(payload: Record<string, unknown>): void {
  for (const key of Object.keys(payload)) {
    if (!(ARENA_ANALYTICS_ALLOWED_KEYS as readonly string[]).includes(key)) {
      throw new Error(`Arena analytics payload contains disallowed key: ${key}`)
    }
  }
}

// 防御性裁剪：返回仅含白名单键的新对象；白名单外的键（如 targetUserIdHash）被丢弃。
export function sanitizeArenaAnalytics(payload: Record<string, unknown>): Partial<ArenaAccusationEvent> {
  const out: Record<string, unknown> = {}
  for (const key of ARENA_ANALYTICS_ALLOWED_KEYS) {
    if (key in payload) out[key] = payload[key]
  }
  return out as Partial<ArenaAccusationEvent>
}

// 以「周一」为周期起点的周键（YYYY-MM-DD），用于聚合计数，不含任何身份信息。
export function getArenaWeekKey(date: Date = new Date()): string {
  const d = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  const day = d.getDay() // 0=周日..6=周六
  const diff = (day + 6) % 7 // 距本周一的天数
  d.setDate(d.getDate() - diff)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day2 = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day2}`
}
