---
name: security-review
description: Deep security analysis of code changes
---

# Security Review

Analyze code for vulnerabilities:

1. **Injection** - SQL, command, XSS
2. **Secrets** - Hardcoded credentials, API keys
3. **Authentication** - Bypass, weak validation
4. **Data exposure** - Logging sensitive data
5. **Dependencies** - Known vulnerabilities

Search patterns:
- `password`, `secret`, `key`, `token` in code
- Direct string concatenation in queries
- User input without validation

Use the Explore agent to search the codebase thoroughly.

Report findings with severity and remediation.
