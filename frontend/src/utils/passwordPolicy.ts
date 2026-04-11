export const PASSWORD_STRONG_HINT = '密码至少8位，含大小写字母和数字'

export function isStrongPassword(v: string): boolean {
  return /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$/.test(v)
}
