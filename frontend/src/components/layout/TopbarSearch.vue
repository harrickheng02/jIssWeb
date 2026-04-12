<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Search } from '@element-plus/icons-vue'
import { firstQueryString } from '@/utils/routeQuery'

const route = useRoute()
const router = useRouter()
const keyword = ref('')

function syncFromRoute() {
  keyword.value = firstQueryString(route.query.q)
}

onMounted(() => syncFromRoute())
watch(() => route.query.q, syncFromRoute)

let debounceTimer: ReturnType<typeof setTimeout> | null = null

function pushSearch() {
  const t = keyword.value.trim()
  if (route.name === 'home') {
    void router.replace({ path: '/', query: t ? { q: t } : {} })
  } else {
    void router.push({ name: 'home', query: t ? { q: t } : {} })
  }
}

function scheduleSearch() {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    debounceTimer = null
    pushSearch()
  }, 300)
}

function submitImmediate() {
  if (debounceTimer) {
    clearTimeout(debounceTimer)
    debounceTimer = null
  }
  pushSearch()
}
</script>

<template>
  <div class="search-box">
    <el-input
      v-model="keyword"
      class="search-input"
      placeholder="搜索帖子"
      clearable
      @input="scheduleSearch"
      @keyup.enter="submitImmediate"
    >
      <template #suffix>
        <el-icon class="search-suffix" @click.stop="submitImmediate"><Search /></el-icon>
      </template>
    </el-input>
  </div>
</template>

<style scoped>
.search-box {
  min-width: 200px;
}

.search-input {
  width: 224px;
}

.search-input :deep(.el-input__wrapper) {
  border-radius: var(--radius-pill);
}

.search-suffix {
  cursor: pointer;
  color: var(--text-secondary);
}

.search-suffix:hover {
  color: var(--color-primary);
}

@media (max-width: 900px) {
  .search-box {
    min-width: 0;
  }

  .search-input {
    width: 100%;
  }
}
</style>
