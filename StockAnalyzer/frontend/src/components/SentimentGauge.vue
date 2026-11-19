<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts'

// 情绪温度计：0-100 分，分段配色（冰点蓝 → 低迷绿 → 中性黄 → 活跃橙 → 亢奋红）
const props = defineProps<{ score: number; level: string }>()

const el = ref<HTMLElement>()
let chart: echarts.ECharts | null = null

const color = computed(() => {
  const s = props.score
  if (s >= 80) return '#c0392b'
  if (s >= 60) return '#e6a23c'
  if (s >= 40) return '#d4b106'
  if (s >= 20) return '#409eff'
  return '#909399'
})

function render() {
  if (!chart) return
  chart.setOption({
    animation: false,
    series: [{
      type: 'gauge',
      min: 0,
      max: 100,
      startAngle: 210,
      endAngle: -30,
      radius: '96%',
      center: ['50%', '56%'],
      axisLine: {
        lineStyle: {
          width: 14,
          color: [[0.2, '#909399'], [0.4, '#409eff'], [0.6, '#d4b106'], [0.8, '#e6a23c'], [1, '#c0392b']],
        },
      },
      pointer: { width: 5, length: '62%', itemStyle: { color: color.value } },
      axisTick: { show: false },
      splitLine: { length: 10, lineStyle: { color: '#dcdfe6', width: 1 } },
      axisLabel: { distance: 20, fontSize: 10, color: '#909399' },
      title: { show: false },
      detail: {
        valueAnimation: false,
        formatter: '{value}',
        fontSize: 30,
        fontWeight: 'bold',
        offsetCenter: [0, '52%'],
        color: color.value,
      },
      data: [{ value: props.score }],
    }],
  })
}

onMounted(() => {
  chart = echarts.init(el.value!)
  render()
  window.addEventListener('resize', resize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', resize)
  chart?.dispose()
})

function resize() {
  chart?.resize()
}

watch(() => [props.score, props.level], render)
</script>

<template>
  <div ref="el" class="sentiment-gauge" />
</template>

<style scoped>
.sentiment-gauge { width: 100%; height: 200px; }
</style>
