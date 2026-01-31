---
name: implement
description: Implement a feature following project standards
---

# Implementation Workflow

When implementing the requested feature:

1. **Understand** - Read related existing code first
2. **Plan** - Identify files to modify/create
3. **Implement** - Write minimal, focused code
4. **Verify** - Check it builds/compiles

## Project standards:
- Keep changes minimal - don't over-engineer
- Follow existing patterns in the codebase
- C#: Use top-level statements where appropriate
- Vue: Use Composition API with `<script setup>`
- ESP32: Mind memory constraints

## Don't:
- Add unnecessary abstractions
- Refactor unrelated code
- Add features not requested
