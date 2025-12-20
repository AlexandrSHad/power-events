<template>
  <v-timeline side="end" align="start">
    <v-timeline-item
      v-for="(event, index) in events"
      :key="index"
      dot-color="rgba(255, 255, 255, 0.6)"
      size="small"
    >
      <template v-slot:opposite>
        <div v-if="index % 2 === 0" class="text-body-2 text-white">
          {{ formatTime(event.timeGenerated) }}
        </div>
        <v-card v-else class="event-card" max-width="200">
          <v-card-text class="pa-3">
            <div class="d-flex align-center">
              <v-icon size="small" class="mr-2">mdi-laptop</v-icon>
              <div>
                <div class="text-body-2">{{ event.description }}</div>
                <div class="text-caption text-grey-darken-1">{{ formatTime(event.timeGenerated) }}</div>
              </div>
            </div>
          </v-card-text>
        </v-card>
      </template>

      <v-card v-if="index % 2 === 0" class="event-card" max-width="200">
        <v-card-text class="pa-3">
          <div class="d-flex align-center">
            <v-icon size="small" class="mr-2">mdi-laptop</v-icon>
            <div>
              <div class="text-body-2">{{ event.description }}</div>
              <div class="text-caption text-grey-darken-1">{{ formatTime(event.timeGenerated) }}</div>
            </div>
          </div>
        </v-card-text>
      </v-card>
      <div v-else class="text-body-2 text-white">
        {{ formatTime(event.timeGenerated) }}
      </div>
    </v-timeline-item>
  </v-timeline>
</template>

<script setup>
import { format } from 'date-fns'

defineProps({
  events: {
    type: Array,
    required: true
  }
})

const formatTime = (date) => {
  return format(new Date(date), 'h:mm a')
}
</script>
