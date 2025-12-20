import { ref } from 'vue'

export function useMockData() {
  const currentStatus = ref({
    state: 'Standby', // 'Awake', 'Standby', 'Power-off'
    lastUpdate: new Date(Date.now() - 14 * 60 * 1000) // 14 minutes ago
  })

  const eventHistory = ref([
    {
      state: 'Awake',
      description: 'Laptop turned on',
      timeGenerated: new Date('2025-12-20T12:40:00')
    },
    {
      state: 'Power-off',
      description: 'Laptop turned off',
      timeGenerated: new Date('2025-12-20T12:30:00')
    },
    {
      state: 'Power-off',
      description: 'Laptop turned off',
      timeGenerated: new Date('2025-12-20T12:15:00')
    },
    {
      state: 'Power-off',
      description: 'Laptop turned off',
      timeGenerated: new Date('2025-12-20T12:30:00')
    },
    {
      state: 'Power-off',
      description: 'Laptop turned off',
      timeGenerated: new Date('2025-12-20T12:40:00')
    }
  ])

  return {
    currentStatus,
    eventHistory
  }
}
