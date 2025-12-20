<template>
  <v-app-bar color="transparent" elevation="0">
    <template v-slot:prepend>
      <v-avatar size="48" class="ml-4">
        <svg viewBox="0 0 128 128">
          <path fill="#42b883" d="M78.8,10L64,35.4L49.2,10H0l64,110l64-110C128,10,78.8,10,78.8,10z"/>
          <path fill="#35495e" d="M78.8,10L64,35.4L49.2,10H25.6L64,76l38.4-66H78.8z"/>
        </svg>
      </v-avatar>
    </template>

    <v-toolbar-title class="text-white">
      <div class="text-h4 font-weight-medium">Monitoring</div>
      <!-- <div class="text-subtitle-1">Laptop status</div> -->
    </v-toolbar-title>

    <template v-slot:append>
      <v-btn icon="mdi-dots-vertical" color="white" class="mr-4"></v-btn>
    </template>
  </v-app-bar>
</template>

<script setup>
</script>
