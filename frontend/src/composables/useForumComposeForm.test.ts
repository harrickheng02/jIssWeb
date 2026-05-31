import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ path: '/' }),
}))

vi.mock('@/stores/draftUi', () => ({
  useDraftUiStore: () => ({
    refreshBadgeFromServer: vi.fn(),
    clearBadge: vi.fn(),
  }),
}))

vi.mock('@/api/clients', () => ({
  createForumPost: vi.fn(),
  updateForumPost: vi.fn(),
  updateDraft: vi.fn(),
  createDraft: vi.fn(),
  publishDraft: vi.fn(),
  getForumTagSuggest: vi.fn().mockResolvedValue({ success: true, data: [] }),
}))

import * as clients from '@/api/clients'
import { useForumComposeForm } from './useForumComposeForm'

describe('useForumComposeForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('calls updateForumPost when mode is edit', async () => {
    const mockUpdate = vi.mocked(clients.updateForumPost)
    mockUpdate.mockResolvedValue({
      success: true,
      data: {
        id: 'p1',
        title: 'Updated',
        excerpt: '',
        authorId: 'u1',
        publishedAtUtc: new Date().toISOString(),
        board: 'general',
        tags: [],
        likes: 0,
        comments: 0,
        views: 0,
      },
    })

    const form = useForumComposeForm({ getDefaultBoardId: () => 'general' })
    form.openComposeDialogForEdit({ id: 'p1', title: 'Original', body: 'Body', tags: ['a'], boardId: 'general' })
    form.composeTitle.value = 'Updated'

    await form.submitCompose()

    expect(mockUpdate).toHaveBeenCalledOnce()
    expect(mockUpdate).toHaveBeenCalledWith('p1', expect.objectContaining({ title: 'Updated', body: 'Body' }))
    expect(vi.mocked(clients.createForumPost)).not.toHaveBeenCalled()
  })

  it('calls updateDraft and publishDraft when mode is draft-edit and submitCompose', async () => {
    const mockUpdateDraft = vi.mocked(clients.updateDraft)
    const mockPublishDraft = vi.mocked(clients.publishDraft)
    mockUpdateDraft.mockResolvedValue({
      success: true,
      data: {
        id: 'd1',
        title: 'Draft',
        body: 'Draft body',
        excerpt: '',
        authorId: 'u1',
        publishedAtUtc: new Date().toISOString(),
        board: 'general',
        tags: [],
        likes: 0,
        comments: 0,
        views: 0,
        state: 'draft',
      },
    })
    mockPublishDraft.mockResolvedValue({ success: true, data: { id: 'd1', state: 'published' } })

    const form = useForumComposeForm({ getDefaultBoardId: () => 'general' })
    form.openComposeDialogForDraftEdit({ id: 'd1', title: 'Draft', body: 'Draft body' })

    await form.submitCompose()

    expect(mockUpdateDraft).toHaveBeenCalledOnce()
    expect(mockUpdateDraft).toHaveBeenCalledWith('d1', expect.objectContaining({ title: 'Draft', body: 'Draft body' }))
    expect(mockPublishDraft).toHaveBeenCalledOnce()
    expect(mockPublishDraft).toHaveBeenCalledWith('d1')
  })

  it('calls createForumPost when mode is create', async () => {
    const mockCreate = vi.mocked(clients.createForumPost)
    mockCreate.mockResolvedValue({ success: true, data: { id: 'new1' } })

    const form = useForumComposeForm({ getDefaultBoardId: () => 'general' })
    form.openComposeDialog()
    form.composeTitle.value = 'New Post'
    form.composeBody.value = 'Body text'

    await form.submitCompose()

    expect(mockCreate).toHaveBeenCalledOnce()
    expect(mockCreate).toHaveBeenCalledWith(expect.objectContaining({ title: 'New Post', body: 'Body text' }))
  })

  it('saveDraft calls createDraft when in create mode', async () => {
    const mockCreateDraft = vi.mocked(clients.createDraft)
    mockCreateDraft.mockResolvedValue({ success: true, data: { id: 'new-draft', state: 'draft' } })

    const form = useForumComposeForm({ getDefaultBoardId: () => 'general' })
    form.openComposeDialog()
    form.composeTitle.value = 'Draft title'

    await form.saveDraft()

    expect(mockCreateDraft).toHaveBeenCalledOnce()
    expect(form.mode.value).toBe('draft-edit')
    expect(form.editTargetId.value).toBe('new-draft')
  })

  it('saveDraft without title does not call createDraft', async () => {
    const mockCreateDraft = vi.mocked(clients.createDraft)
    const form = useForumComposeForm({ getDefaultBoardId: () => 'general' })
    form.openComposeDialog()
    form.composeBody.value = 'Body only'

    await form.saveDraft()

    expect(mockCreateDraft).not.toHaveBeenCalled()
  })
})
