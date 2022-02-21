# ParaParty Util Kotlinize

## Feature

1. Kotlin [scoped function](https://kotlinlang.org/docs/reference/scope-functions.html).

## Examples

### Let

**The context object** is available as an argument (`it`). **The return value** is the lambda result.

```C#
var numbers = new []{"one", "two", "three", "four", "five"};
var resultList = numbers.Select(s => s.Length).Where(l => l > 3);
Console.WriteLine(string.Join(",", resultList));
```

With `Let`, you can rewrite it:

```C#
var numbers = new []{"one", "two", "three", "four", "five"};
numbers.Select(s => s.Length)
    .Where(l => l > 3)
    .Let(it => Console.WriteLine(string.Join(",", it)));
```

### Also

**The context object** is available as an argument (`it`). **The return value** is the object itself.

`Also` is good for performing some actions that take the context object as an argument. Use `Also` for actions that need a
reference to the object rather than its properties and functions, or when you don't want to shadow the `this` reference
from an outer scope. When you see `Also` in the code, you can read it as _“ and also do the following with the object.”_

```C#
var numbers = new List<string> {"one", "two", "three"};
numbers
    .Also(it => Console.WriteLine($"The list elements before adding new one: {string.Join(',', it)}"))
    .Add("four");
```
