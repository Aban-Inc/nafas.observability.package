<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Bar } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  Tooltip,
  Legend,
} from 'chart.js'
import Skeleton from 'primevue/skeleton'

ChartJS.register(CategoryScale, LinearScale, BarElement, Tooltip, Legend)

interface ServiceEntry {
  service: string
  errorRate: number
  totalErrors?: number
  totalLogs?: number
}

const props = defineProps<{
  data: ServiceEntry[] | null
  loading?: boolean
  error?: boolean
}>()

const { t } = useI18n()
const gridColor = 'rgba(255,255,255,0.06)'

const chartData = computed(() => {
  if (!props.data || props.data.length === 0) return null

  return {
    labels: props.data.map(d => d.service),
    datasets: [
      {
        label: 'Error Rate %',
        data: props.data.map(d => d.errorRate),
        backgroundColor: 'rgba(0,191,165,0.70)',
        borderColor: 'transparent',
        borderWidth: 0,
        borderRadius: 4,
      },
    ],
  }
})

const chartOptions = {
  indexAxis: 'y' as const,
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { display: false },
    tooltip: {
      callbacks: {
        label: (ctx: any) => ` ${ctx.parsed.x.toFixed(2)}%`,
      },
    },
  },
  scales: {
    x: {
      grid: { color: gridColor, drawBorder: false },
      ticks: { color: 'rgba(255,255,255,0.35)', font: { size: 10 }, callback: (v: number | string) => `${v}%` },
    },
    y: {
      grid: { color: 'transparent', drawBorder: false },
      ticks: { color: 'rgba(255,255,255,0.55)', font: { size: 11, family: 'IBM Plex Mono' } },
    },
  },
}
</script>

<template>
  <div class="chart-wrap">
    <Skeleton v-if="loading" width="100%" height="100%" />
    <template v-else-if="chartData">
      <Bar :data="chartData" :options="chartOptions" />
    </template>
    <div v-else class="chart-placeholder">
      <span>{{ error ? t('charts.errorRateFailed') : t('charts.noData') }}</span>
    </div>
  </div>
</template>

<style scoped>
.chart-wrap {
  position: relative;
  width: 100%;
  height: 200px;
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
