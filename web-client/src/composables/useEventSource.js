import { ref, onMounted, onUnmounted } from 'vue'

export function useEventSource(url = 'http://localhost:5000/events') {
  const currentStatus = ref({
    state: 'Unknown',
    lastUpdate: new Date()
  })

  const eventHistory = ref([])
  const isConnected = ref(false)

  let eventSource = null

  const connect = () => {
    eventSource = new EventSource(url)

    eventSource.onopen = () => {
      console.log('SSE connection opened')
      isConnected.value = true
    }

    eventSource.onerror = (error) => {
      console.error('SSE connection error:', error)
      isConnected.value = false
    }

    eventSource.addEventListener('power-event', (e) => {
      const data = JSON.parse(e.data)
      console.log('Power event received:', data)

      // Update current status
      currentStatus.value = {
        state: data.state,
        lastUpdate: new Date(data.timeGenerated)
      }

      // Add to history
      const description = data.state === 'Awake'
        ? 'Laptop turned on'
        : data.state === 'Standby'
          ? 'Laptop went to standby'
          : `Laptop ${data.state.toLowerCase()}`
      eventHistory.value.unshift({
        state: data.state,
        description,
        timeGenerated: new Date(data.timeGenerated)
      })
    })
  }

  onMounted(() => {
    connect()
  })

  onUnmounted(() => {
    if (eventSource) {
      eventSource.close()
      console.log('SSE connection closed')
    }
  })

  return {
    currentStatus,
    eventHistory,
    isConnected
  }
}
