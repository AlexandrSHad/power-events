<template>
  <v-app>
    <AppHeader />
    <v-main>
      <v-container class="py-8">
        <v-row justify="center">
          <v-col cols="12" md="8" lg="6">
            <StatusCard
              :current-status="currentStatus.state"
              :last-update="currentStatus.lastUpdate"
            />
          </v-col>
        </v-row>

        <v-row justify="center" class="mt-8">
          <v-col cols="12" md="8" lg="6">
            <div class="text-h5 text-white text-center mb-6">History</div>
            <HistoryTimeline :events="eventHistory" />
          </v-col>
        </v-row>
      </v-container>
    </v-main>

    <!-- Connection status indicator -->
    <v-snackbar
      v-model="showStatus"
      :color="isConnected ? 'success' : 'error'"
      location="bottom"
      :timeout="-1"
    >
      <div class="d-flex align-center">
        <v-icon :icon="isConnected ? 'mdi-wifi' : 'mdi-wifi-off'" class="mr-2"></v-icon>
        <span>{{ isConnected ? '🚀 Houston, we have connection!' : '👻 Server ghosted us...' }}</span>
      </div>
    </v-snackbar>
  </v-app>
</template>

<script setup>
import { ref } from 'vue'
import { useEventSource } from './composables/useEventSource'
import AppHeader from './components/AppHeader.vue'
import StatusCard from './components/StatusCard.vue'
import HistoryTimeline from './components/HistoryTimeline.vue'

const { currentStatus, eventHistory, isConnected } = useEventSource()
const showStatus = ref(true)
</script>
