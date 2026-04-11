<script setup lang="ts">
import { ref } from 'vue'
import { Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'

const keyword = ref('')

function handleSearch() {
  ElMessage.info(keyword.value ? `搜索功能预留：${keyword.value}` : '请输入搜索内容')
}
</script>

<template>
  <div class="search-box">
    <el-input
      v-model="keyword"
      class="search-input"
      placeholder="搜索帖子 / 标签 / 用户"
      clearable
      @keyup.enter="handleSearch"
    >
      <template #suffix>
        <el-icon class="search-suffix" @click.stop="handleSearch"><Search /></el-icon>
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
