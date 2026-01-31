---
name: generate-tests
description: Generate unit tests for code
---

# Test Generation

Generate tests for the specified code:

## Guidelines
1. Test happy path first
2. Test edge cases (null, empty, boundary values)
3. Test error conditions
4. Use descriptive test names: `MethodName_Scenario_ExpectedResult`

## Framework conventions:
- C#: Use xUnit with FluentAssertions
- Vue: Use Vitest with @vue/test-utils
- Follow AAA pattern (Arrange, Act, Assert)

## Output
Create test file with complete, runnable tests.
