<template>
  <div class="text-h3 font-weight-bold">{{ formattedTime }}</div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { differenceInMinutes, differenceInSeconds } from 'date-fns'

const props = defineProps({
  lastUpdate: {
    type: Date,
    required: true
  }
})

const now = ref(new Date())
let interval

onMounted(() => {
  interval = setInterval(() => {
    now.value = new Date()
  }, 60*1000)
})

onUnmounted(() => {
  clearInterval(interval)
})

const formattedTime = computed(() => {
  const minutes = differenceInMinutes(now.value, props.lastUpdate)
  return `${minutes}m`
})
</script>
