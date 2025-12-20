<template>
  <div class="text-h3 font-weight-bold">{{ formattedTime }}</div>
</template>

<script setup>
import { computed } from 'vue'
import { differenceInMinutes } from 'date-fns'

const props = defineProps({
  lastUpdate: {
    type: Date,
    required: true
  }
})

const formattedTime = computed(() => {
  const minutes = differenceInMinutes(new Date(), props.lastUpdate)
  return `${minutes.toFixed(1)}m`
})
</script>
