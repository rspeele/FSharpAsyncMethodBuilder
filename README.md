# Use F#'s `Async` type in C#

This is a proof-of-concept implementing a method builder compatible with C#
7.0's "arbitrary async returns" feature, for F#'s `Async<_>` type (aka
`FSharpAsync<T>` when used from C#).

That is, it lets you write F# async expressions directly in C# using async/await
syntax, instead of writing `Task<T>`s and using `FSharpAsync.AwaitTask` to
convert them.

# Example

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.FSharp.Control;
using FAsync;

public static class ExampleClass
{
    // FAsync<T> is a simple wrapper around FSharpAsync<T>.
    // It only exists to point to the compiler to the async method builder to use.
    // You can get out the wrapped FSharpAsync<T> easily.
    public static FAsync<int> DelayedFactorial(int x)
    {
        // You can wait Task<T>s
        await Task.Delay(1);
        // ... and F# Async<T>s
        await FSharpAsync.Sleep(1);
        // ... and other FAsync<T>s
        if (x > 1)
            return x * await DelayedFactorial(x - 1);
        else
            return x;
    }

    public static void ExampleUsage()
    {
         // FAsync<T> has an implicit conversion to FSharpAsync<T>.
         FSharpAsync<int> fac = DelayedFactorial(1);
         // Like in F#, none of the code in the body of an FAsync method runs until it's started.
         var result = FSharpAsync.RunSynchronously(fac, null, null);
         Console.WriteLine(result);
    }
}
```

See [Tests/Program.cs](Tests/Program.cs) for more examples.

To use the code yourself, you can copy
[FAsync.fs](FSharpAsyncMethodBuilder/FAsync.fs) into an F# project, add a
reference to NuGet package System.Threading.Tasks.Extensions, then reference
that F# project from your C# projects.

# Motivation

This would make sense to use if you have a lot of F# code currently using `async
{ ... }` builders, and are sprinkling in some C# code that interops with it.
It saves some of the overhead of constantly converting between the two task types.

I suspect in the real world more systems are the other way around, and would
benefit more from my other project,
[TaskBuilder.fs](https://github.com/rspeele/TaskBuilder.fs), which lets you
write `Task`s from F# code.

For me, this was just an exercise in learning how to use the new arbitrary async
return feature in C#, which isn't well documented yet. It may serve as a useful
reference for others trying to convert F# computation expressions to C# async
method builders.

Unfortunately, there aren't that many more applications for this technique,
because the async/await feature in C# is not as general-purpose as the
computation expression feature in F#. Any async method builder must support
awaiting _any_ type that implements that awaitable pattern.

This means that you can't reasonably write a builder for a monad that has
nothing to do with asynchronous programming, say, `Option<T>` or `Result<TOk,
TError>`, since users would be allowed to await TPL tasks, `ValueTask`, etc.
within your builder. What's more, your `MyMonad<T>` type would appear awaitable
to every other builder, which probably won't be able to handle it the way you
intend.

It might be handy for some other async-ish things like
[Hopac](https://github.com/Hopac/Hopac), though I haven't looked into that.

# Caveat

There is one subtle but important difference between these async methods and the
F# equivalents. Each `Async<_>` logically represents a "recipe" for a task --
something like a `unit -> Task<_>`. When you have an `Async<_>` in F#, you can
run it as many times as you want with `Async.StartAsTask`, including in parallel
with itself. It'll work fine as long as you don't have them sharing mutable
state created outside the `async` block.

With the C# async/await implementation, calling the method creates a single
instance of a state machine which encapsulates the local variables of the
method. If you call the method once and start the resulting `FSharpAsync`
multiple times concurrently, each running `Task` will use the same state
machine, and they'll trample over each others' local variables with disastrous
results. So don't do that!
