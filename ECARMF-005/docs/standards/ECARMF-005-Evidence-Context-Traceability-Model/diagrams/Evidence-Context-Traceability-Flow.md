# Evidence, Context & Traceability Flow

```mermaid
flowchart TD
    E[Evidence] --> C[Context Package]
    C --> A[Analysis]
    A --> R[Recommendation]
    R --> D[Decision]
    D --> EX[Explanation]
    D --> AU[Audit Record]
    E --> T[Traceability Graph]
    C --> T
    R --> T
    D --> T
    EX --> T
```
