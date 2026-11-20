import { inject, onBeforeUnmount, onMounted, watch, type InjectionKey, type Ref } from 'vue'
import * as echarts from 'echarts'

/** FullscreenChart 进入/退出全屏时递增，订阅方据此 resize */
export const FS_RESIZE_KEY: InjectionKey<Ref<number>> = Symbol('fs-resize')

/**
 * ECharts 通用挂载：初始化、数据变更重绘、窗口/全屏 resize、卸载销毁
 */
export function useEChart(
  el: Ref<HTMLElement | undefined>,
  getOption: () => echarts.EChartsOption | null,
  sources: () => unknown,
) {
  let chart: echarts.ECharts | null = null
  const fsResize = inject(FS_RESIZE_KEY, null)

  function render() {
    if (!chart) return
    const option = getOption()
    if (option) chart.setOption(option, true)
  }

  function resize() {
    chart?.resize()
  }

  onMounted(() => {
    if (!el.value) return
    chart = echarts.init(el.value)
    render()
    window.addEventListener('resize', resize)
  })

  onBeforeUnmount(() => {
    window.removeEventListener('resize', resize)
    chart?.dispose()
    chart = null
  })

  watch(sources, render, { deep: true })

  if (fsResize) {
    watch(fsResize, () => setTimeout(resize, 60))
  }

  return { render, resize }
}

export const UP_COLOR = '#ef232a'
export const DOWN_COLOR = '#14b143'
export const AXIS_COLOR = '#c0c4cc'
export const GRID_COLOR = '#f0f0f0'
export const CHART_PALETTE = ['#409eff', '#e6a23c', '#67c23a', '#f56c6c', '#909399', '#7e57c2', '#00897b']

export const baseTooltip = {
  trigger: 'axis' as const,
  axisPointer: { type: 'cross' as const },
  backgroundColor: 'rgba(255,255,255,0.96)',
  borderColor: '#ddd',
  textStyle: { color: '#303133', fontSize: 12 },
}

export const baseXAxis = {
  type: 'category' as const,
  boundaryGap: true,
  axisLine: { lineStyle: { color: AXIS_COLOR } },
}

export const baseYAxis = {
  type: 'value' as const,
  scale: true,
  splitLine: { lineStyle: { color: GRID_COLOR } },
}