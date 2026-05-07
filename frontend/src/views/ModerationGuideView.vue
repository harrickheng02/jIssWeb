<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

const isAuthed = computed(() => Boolean(auth.token))
const canModerate = computed(() => auth.canModerate)
const roleLabel = computed(() => {
  if (auth.forumRole === 'admin') return '管理员'
  if (auth.forumRole === 'moderator') return '版主'
  return '普通用户'
})

function goReportQueue() {
  void router.push({ name: 'moderation-reports' })
}
</script>

<template>
  <div class="moderation-guide">
    <div class="moderation-guide__inner">
      <el-card shadow="never" class="guide-card">
        <template #header>治理入口</template>
        <div class="guide-meta">
          <div class="guide-row">
            <span class="k">登录状态</span>
            <span class="v">{{ isAuthed ? '已登录' : '未登录' }}</span>
          </div>
          <div class="guide-row">
            <span class="k">论坛角色</span>
            <span class="v">{{ roleLabel }}</span>
          </div>
        </div>

        <el-divider />

        <div class="guide-section">
          <div class="guide-title">当前可用治理能力</div>
          <ul class="guide-list">
            <li>帖子详情页底部「治理」：置顶、锁帖禁回、删帖、删回复；举报队列展开<strong>帖子类</strong>举报后提供同一套能力（版主/管理员）</li>
            <li>帖子详情页：「操作记录」审计查询（版主/管理员）</li>
            <li>
              举报队列：维护<strong>举报工单</strong>三类状态；点击<strong>整条条目</strong>展开后查看摘要与治理。举报<strong>帖子</strong>时治理能力与帖子详情一致；举报<strong>回复</strong>时队列内仅「删除该回复」，其它请在帖子详情「治理」操作。「<strong>查看详情</strong>」在新标签打开。结案超保留期从库移除时审计可查（版主/管理员）
            </li>
          </ul>
        </div>

        <div class="guide-section">
          <div class="guide-title">验证路径</div>
          <div class="guide-desc">
            从首页进入任一帖子详情，在页面底部“治理”区块完成置顶操作，再打开“操作记录”查看审计条目。
          </div>
        </div>

        <div class="guide-actions">
          <el-button v-if="canModerate" type="primary" plain @click="goReportQueue">举报队列</el-button>
          <el-button type="primary" @click="router.push('/')">去首页</el-button>
        </div>
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.moderation-guide {
  min-height: 100%;
  background: var(--bg-main);
  color: var(--text-primary);
}

.moderation-guide__inner {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
}

.guide-card {
  border-radius: var(--radius-lg);
}

.guide-meta {
  display: flex;
  flex-direction: column;
  gap: var(--space-sm);
}

.guide-row {
  display: flex;
  justify-content: space-between;
  gap: var(--space-md);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.k {
  color: var(--text-secondary);
}

.v {
  color: var(--text-primary);
  font-weight: 600;
}

.guide-section {
  display: flex;
  flex-direction: column;
  gap: var(--space-sm);
}

.guide-title {
  font-size: var(--font-md);
  font-weight: 700;
  line-height: 1.4;
}

.guide-desc {
  font-size: var(--font-sm);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.guide-list {
  margin: 0;
  padding-left: 18px;
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.guide-actions {
  margin-top: var(--space-md);
  display: flex;
  justify-content: flex-end;
  gap: var(--space-md);
  flex-wrap: wrap;
}
</style>

