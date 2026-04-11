<script setup lang="ts">
withDefaults(
  defineProps<{
    type?: 'primary' | 'default' | 'success' | 'warning' | 'danger' | 'info'
    plain?: boolean
    disabled?: boolean
    loading?: boolean
  }>(),
  { type: 'default', plain: false, disabled: false, loading: false },
)
</script>

<template>
  <el-button v-bind="$attrs" :type="type" :plain="plain" :disabled="disabled" :loading="loading">
    <slot />
  </el-button>
</template>
