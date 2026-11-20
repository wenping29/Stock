<script setup lang="ts">
import { onBeforeUnmount, onMounted, provide, ref } from 'vue'
import { FS_RESIZE_KEY } from '../../composables/useEChart'

defineProps<{ title: string }>()

const root = ref<HTMLElement>()
const isFullscreen = ref(false)
const fsResize = ref(0)

provide(FS_RESIZE_KEY, fsResize)

function onFullscreenChange() {
  isFullscreen.value = !!document.fullscreenElement
  fsResize.value++
}

onMounted(() => document.addEventListener('fullscreenchange', onFullscreenChange))
onBeforeUnmount(() => document.removeEventListener('fullscreenchange', onFullscreenChange))

async function toggle() {
  try {
    if (document.fullscreenElement) {
      await document.exitFullscreen()
    } else {
      await root.value?.requestFullscreen()
    }
  } catch {
    /* 浏览器可能拒绝全屏请求 */
  }
}
</script>

<template>
  <div ref="root" class="fs-wrap" :class="{ full: isFullscreen }">
    <button
      class="fs-btn"
      :title="isFullscreen ? '退出全屏 (Esc)' : `${title} 全屏`"
      @click="toggle"
    >{{ isFullscreen ? '✕' : '⛶' }}</button>
    <slot :is-fullscreen="isFullscreen" />
  </div>
</template>

<style scoped>
.fs-wrap {
  position: relative;
}
.fs-wrap.full {
  background: #fff;
  padding: 44px 16px 16px;
  height: 100vh;
  overflow: auto;
  display: flex;
  align-items: center;
}
.fs-wrap.full > :not(.fs-btn) {
  flex: 1;
  width: 100%;
}
.fs-btn {
  position: absolute;
  top: 2px;
  right: 2px;
  z-index: 10;
  width: 26px;
  height: 26px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  color: #606266;
  background: #f5f7fa;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  cursor: pointer;
}
.fs-btn:hover {
  color: #409eff;
  border-color: #c6e2ff;
}
</style>