import { ElMessageBox } from 'element-plus'

export async function confirmDeleteAuthorForumPost(): Promise<boolean> {
  try {
    await ElMessageBox.confirm('确定要删除这篇帖子吗？删除后其他人将无法查看。', '删除帖子', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消',
    })
    return true
  } catch {
    return false
  }
}

export async function confirmDeleteModerationForumPost(): Promise<boolean> {
  try {
    await ElMessageBox.confirm(
      '删除后该帖及回复将对其他用户不可见。确定删除？',
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
    await ElMessageBox.confirm('删除后该回复将对其他用户不可见。确定删除？', '删除回复', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消',
    })
    return true
  } catch {
    return false
  }
}
