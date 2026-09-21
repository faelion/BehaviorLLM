# Current-code matrix versus historical baseline

All rows have 16/16 valid decisions and zero errors. Expected-match counts and p50 latency are shown separately. This run used changed fixtures and uncontrolled machine conditions; changes are observations, not causal estimates.

| Model | Profile | Schema | Expected: baseline → current | p50 ms: baseline → current |
|---|---|---|---|---|
| Qwen3.5-2B | Reactive | True | 10/16 → 10/16 | 188.8 → 179.9 |
| Qwen3.5-2B | Reactive | False | 10/16 → 10/16 | 183.5 → 161.6 |
| Qwen3.5-2B | Deliberative | True | 11/16 → 11/16 | 279.9 → 281.4 |
| Qwen3.5-2B | Deliberative | False | 11/16 → 10/16 | 279.7 → 289.8 |
| Granite-4.1-3B | Reactive | True | 13/16 → 13/16 | 278.5 → 1217.5 |
| Granite-4.1-3B | Reactive | False | 13/16 → 13/16 | 272.6 → 1205.5 |
| Granite-4.1-3B | Deliberative | True | 12/16 → 14/16 | 431.9 → 2092.6 |
| Granite-4.1-3B | Deliberative | False | 12/16 → 11/16 | 440.4 → 2082.5 |
| Qwen3.5-4B | Reactive | True | 13/16 → 12/16 | 382.1 → 1505.4 |
| Qwen3.5-4B | Reactive | False | 13/16 → 12/16 | 375.3 → 1497.2 |
| Qwen3.5-4B | Deliberative | True | 15/16 → 14/16 | 574.3 → 2644.2 |
| Qwen3.5-4B | Deliberative | False | 16/16 → 16/16 | 584.0 → 2718.7 |

Fixtures, complete matrix replies and backend/model metadata are in this directory. The historical baseline is two directories above. Schema-free expected matches include a 16/16 row; do not describe 62–94% as the range of the entire historical matrix.

Regressions require explicit acknowledgement: the Qwen 4B schema-enabled rows each lost one expected match, and larger-model latencies increased substantially. Unity validation ran during this work; repeat with controlled GPU/machine conditions before drawing performance conclusions.
