---
name: architect
description: Design system architecture and plan component integration
---

# Architecture Planning

When designing or extending the system:

## Analysis Phase
1. **Understand requirements** - What problem are we solving?
2. **Think critical about requirements** - Are there any conflicts or not clearly defined requests?
3. **Map existing components** - How does current system work?
4. **Identify integration points** - Where does new feature connect?
5. **Verify your decision** - Double check against the modern approaches and best practices.

## Design Considerations
- **Data flow** - How does information move between components?
- **Protocol choice** - MQTT topics, SSE events, API endpoints
- **State management** - Where is truth stored?
- **Failure modes** - What happens when a component fails?

## For this project:
- Windows Service is the source of truth for power state
- MQTT is the communication backbone
- Backend bridges MQTT → Web (SSE)
- ESP32 devices are passive consumers

## Output Format
1. Component diagram (text-based)
2. Data flow description
3. New MQTT topics (if any)
4. Files to modify/create
5. Implementation order recommendation

## Questions to Answer
- Does this change affect multiple components?
- How does new feature integrate with existing components?
- Are there any infrastructural changes needed?
