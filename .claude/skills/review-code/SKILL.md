---
name: review-code
description: Review code changes for quality, bugs, and standards
---

# Code Review Process

Review the specified code for:

## Checklist
1. **Correctness** - Does it work as intended?
2. **Edge cases** - Null checks, empty arrays, boundary conditions
3. **Security** - Injection, XSS, secrets exposure
4. **Performance** - Unnecessary loops, memory leaks
5. **Readability** - Clear naming, appropriate comments
6. **Project conventions** - Matches existing patterns

## For this project specifically:
- C#: Check proper async/await usage in Windows Service
- MQTT: Verify QoS levels and retain flags
- ESP32: Check memory constraints (avoid String, prefer char[])

Output format: List issues with file:line references and suggested fixes.
