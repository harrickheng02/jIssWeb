import {
  deleteModerationForumPost,
  deleteForumPost,
  deleteForumReply,
  deleteModerationForumReply,
  getForumPost,
  listModerationAuditByPost,
  setForumPostFeatured,
  setForumPostRepliesLocked,
  setForumPostSticky,
  type ForumPostDetail,
  type ListModerationAuditOptions,
} from '@/api/clients'

/**
 * 帖子治理操作 composable（版主/作者置顶、加精、锁帖、删帖、删回复、审计记录）。
 * ForumPostGovernancePanel 通过此接口访问治理 API，不直接调用 api/clients。
 */
export function usePostGovernance() {
  async function fetchPost(postId: string) {
    return getForumPost(postId)
  }

  async function toggleSticky(postId: string, isSticky: boolean) {
    return setForumPostSticky(postId, isSticky)
  }

  async function toggleFeatured(postId: string, isFeatured: boolean) {
    return setForumPostFeatured(postId, isFeatured)
  }

  async function toggleRepliesLocked(postId: string, repliesLocked: boolean) {
    return setForumPostRepliesLocked(postId, repliesLocked)
  }

  async function deletePost(postId: string, isAuthor: boolean, reportPayload?: { reportId: string }) {
    if (isAuthor) {
      return deleteForumPost(postId)
    }
    return deleteModerationForumPost(postId, reportPayload)
  }

  async function deleteReply(
    postId: string,
    replyId: string,
    reportPayload?: { reportId: string; reason?: string },
  ) {
    if (reportPayload) {
      return deleteModerationForumReply(replyId, reportPayload)
    }
    return deleteForumReply(postId, replyId)
  }

  async function fetchAuditByPost(postId: string, options: ListModerationAuditOptions) {
    return listModerationAuditByPost(postId, options)
  }

  return {
    fetchPost,
    toggleSticky,
    toggleFeatured,
    toggleRepliesLocked,
    deletePost,
    deleteReply,
    fetchAuditByPost,
  }
}
