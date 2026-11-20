<script setup lang="ts">
import { ref } from 'vue'

const props = withDefaults(defineProps<{ title?: string; width?: number }>(), {
  title: '',
  width: 300,
})

const collapsed = ref(false)
</script>

<template>
  <div
    class="collapsible-side qa-side"
    :style="{ width: collapsed ? '40px' : `${props.width}px`, flexBasis: collapsed ? '40px' : `${props.width}px` }"
  >
    <div class="side-toggle-row" :class="{ center: collapsed }">
      <button
        v-if="!collapsed"
        class="side-toggle"
        title="收缩面板"
        @click="collapsed = true"
      >«</button>
      <button
        v-else
        class="side-toggle"
        title="展开面板"
        @click="collapsed = false"
      >»</button>
    </div>
    <div v-show="!collapsed" class="qa-side-body">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.side-toggle-row {
  display: flex;
  justify-content: flex-end;
  padding: 6px;
  flex-shrink: 0;
}
.side-toggle-row.center {
  justify-content: center;
}
.side-toggle {
  width: 24px;
  height: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  color: #909399;
  background: #f5f7fa;
  border: 1px solid #ebeef5;
  border-radius: 4px;
  cursor: pointer;
}
.side-toggle:hover {
  color: #409eff;
  border-color: #c6e2ff;
}
</style>