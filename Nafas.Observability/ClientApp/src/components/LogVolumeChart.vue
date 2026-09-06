<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Filler,
  Tooltip,
  Legend,
} from 'chart.js'
import Skeleton from 'primevue/skeleton'

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Filler, Tooltip, Legend)

interface VolumeEntry {
  timestamp: string
  count: number
}

const props = defineProps<{
  data: VolumeEntry[] | null
  loading?: boolean
  error?: boolean
}>()

const { t } = useI18n()
const gridColor = 'rgba(255,255,255,0.06)'

const chartData = computed(() => {
  if (!props.data || props.data.length === 0) return null

  const labels = props.data.map(d => {
    const t = new Date(d.timestamp)
    return isNaN(t.getTime()) ? d.timestamp : t.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  })

  return {
    labels,
    datasets: [
      {
        label: 'Log Volume',
        data: props.data.map(d => d.count),
        borderColor: '#00BFA5',
        backgroundColor: 'rgba(0,191,165,0.16)',
        fill: true,
        tension: 0.4,
        pointRadius: 0,
        borderWidth: 2,
      },
    ],
  }
})

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { display: false },
    tooltip: { mode: 'index' as const, intersect: false },
  },
  scales: {
    x: {
      grid: { color: gridColor, drawBorder: false },
      ticks: { color: 'rgba(255,255,255,0.35)', font: { size: 10 }, maxTicksLimit: 6 },
    },
    y: {
      grid: { color: gridColor, drawBorder: false },
      ticks: { color: 'rgba(255,255,255,0.35)', font: { size: 10 } },
    },
  },
}
</script>

<template>
  <div class="chart-wrap">
    <Skeleton v-if="loading" width="100%" height="100%" />
    <template v-else-if="chartData">
      <Line :data="chartData" :options="chartOptions" />
    </template>
    <div v-else class="chart-placeholder">
      <span>{{ error ? t('charts.logVolumeFailed') : t('charts.noData') }}</span>
    </div>
  </div>
</template>

<style scoped>
.chart-wrap {
  position: relative;
  width: 100%;
  height: 236px;
}
.chart-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: rgba(255, 255, 255, 0.2);
  font-size: 12px;
  font-family: 'IBM Plex Mono', monospace;
  border: 1px dashed rgba(255, 255, 255, 0.08);
  border-radius: 8px;
}
</style>
