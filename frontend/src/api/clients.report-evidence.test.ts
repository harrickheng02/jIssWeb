import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { downloadModReportEvidence, modelApi } from './clients'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

describe('downloadModReportEvidence', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('成功时返回 blob', async () => {
    const blob = new Blob(['zip'], { type: 'application/zip' })
    vi.spyOn(modelApi, 'get').mockResolvedValue({ status: 200, data: blob })

    const res = await downloadModReportEvidence('report-1')

    expect(res.success).toBe(true)
    expect(res.data).toBe(blob)
  })

  it('REPORT_NOT_CLOSED 解析 JSON 错误体', async () => {
    const errBlob = new Blob(
      [JSON.stringify({ success: false, message: '仅已结案', code: 'REPORT_NOT_CLOSED' })],
      { type: 'application/json' },
    )
    vi.spyOn(modelApi, 'get').mockResolvedValue({ status: 400, data: errBlob })

    const res = await downloadModReportEvidence('report-pending')

    expect(res.success).toBe(false)
    expect(res.code).toBe('REPORT_NOT_CLOSED')
  })
})
