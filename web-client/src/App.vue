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
  </v-app>
</template>

<script setup>
import { useMockData } from './composables/useMockData'
import AppHeader from './components/AppHeader.vue'
import StatusCard from './components/StatusCard.vue'
import HistoryTimeline from './components/HistoryTimeline.vue'

const { currentStatus, eventHistory } = useMockData()
</script>
