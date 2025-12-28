<template>
  <v-card class="status-card" elevation="4">
    <v-card-text class="text-center pa-8">
      <v-icon size="120" color="blue-lighten-2">mdi-laptop</v-icon>

      <div class="text-h6 mt-4 mb-2">Status</div>

      <StatusTimer :last-update="lastUpdate" />

      <div class="d-flex justify-space-around mt-6">
        <div class="text-body-1">{{ currentStatus }}</div>
        <!-- <div class="text-body-1">Standby</div>
        <div class="text-body-1">Power-off</div> -->
      </div>
    </v-card-text>
  </v-card>
</template>

<script setup>
import StatusTimer from './StatusTimer.vue'

defineProps({
  currentStatus: {
    type: String,
    required: true
  },
  lastUpdate: {
    type: Date,
    required: true
  }
})
</script>
