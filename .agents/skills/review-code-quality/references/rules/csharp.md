# C# Rules

Rule IDs are stable references. Do not renumber them when rules are reordered or new rules are inserted.

### `CS001`: Program Main Exists

Every executable C# project must declare an explicit `Main` method in source code. A missing explicit method is a finding whose recommended correction is to create it. Top-level statements do not satisfy this rule.

### `CS002`: Program Main Is Async When Possible

The explicit `Main` method must always be declared `async` when possible.

### `CS003`: Program Main Has Args Array

The explicit `Main` method must always declare a `string[] args` parameter.

### `CS004`: DI Class Has Logger

If a C# class uses dependency injection and has no logger, recommend adding a Microsoft `ILogger<T>`.
