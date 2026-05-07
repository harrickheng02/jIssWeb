import { ElMessageBox } from 'element-plus'

export async function confirmDeleteModerationForumPost(): Promise<boolean> {
  try {
    await ElMessageBox.confirm(
      '删除后帖子与全部回复不可恢复，并已阅读当前主题与回复。确定删除？',
      '删除帖子',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
    return true
  } catch {
    return false
  }
}

export async function confirmDeleteModerationForumReply(): Promise<boolean> {
  try {
    await ElMessageBox.confirm('删除该条回复后不可恢复。确定删除？', '删除回复', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消',
    })
    return true
  } catch {
    return false
  }
}
