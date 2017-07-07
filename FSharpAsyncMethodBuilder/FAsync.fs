// FAsync.fs - F# async method builder for C# 7.0 and up
//
// Written in 2017 by Robert Peele (humbobst@gmail.com)
//
// To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights
// to this software to the public domain worldwide. This software is distributed without any warranty.
//
// You should have received a copy of the CC0 Public Domain Dedication along with this software.
// If not, see <http://creativecommons.org/publicdomain/zero/1.0/>.

namespace rec FAsync
open System
open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks

type FAsyncAwaiter internal (wrap : unit Async) =
    member __.AsyncWork = wrap
    member __.IsCompleted = false
    member __.GetResult() = ()
    /// This fallback is only called when we are awaited by another async method builder, e.g. within
    /// a `public static async Task<T> Something()` method. With our own method builder, we detect these
    /// awaiters specially and plug their wrapped Asyncs into a computation expression.
    member __.OnCompleted(callback : Action) =
        // Wraps the async as a Task.
        Async.StartAsTask(wrap).GetAwaiter().OnCompleted(callback)
    interface INotifyCompletion with
        member this.OnCompleted(callback) = this.OnCompleted(callback)

type FAsyncAwaiter<'a> internal (wrap : unit Async) =
    inherit FAsyncAwaiter(wrap)
    let mutable result = Unchecked.defaultof<'a>
    let mutable exn = null : exn
    member internal __.SetResult(it : 'a) = result <- it
    member internal __.SetException(it) = exn <- it 
    member __.GetResult() =
        if isNull exn then result
        else raise exn

/// Wrapper around F# `async` type, to support arbitrary async returns.
[<Struct>]
[<AsyncMethodBuilder(typedefof<FAsyncMethodBuilder<_>>)>]
type FAsync<'a>(wrap : 'a Async) =
    member __.Async = wrap
    member __.GetAwaiter() =
        let mutable awaiter = Unchecked.defaultof<FAsyncAwaiter<'a>>
        let wrap = wrap // capture locally
        let wrapper =
            async {
                try
                    let! it = wrap
                    // have to put the result on the awaiter
                    awaiter.SetResult(it)
                with
                | exn ->
                    // must defer throwing the exception till calling GetResult() on the awaiter
                    awaiter.SetException(exn)
            }
        awaiter <- FAsyncAwaiter<'a>(wrapper)
        awaiter
    static member op_Implicit(fasync : 'a FAsync) = fasync.Async

[<Extension>]
type FAsyncExtensions =
    /// Make F# `async` type meet the awaitable pattern.
    [<Extension>]
    static member GetAwaiter(async : Async<'a>) =
        FAsync(async).GetAwaiter()

/// After each time we successfully `MoveNext()` the C# compiler-generated state machine,
/// we will be in one of these three states.
type private FAsyncMethodState<'a> =
    | Returning of 'a
    | AwaitingAsync of FAsyncAwaiter * IAsyncStateMachine
    | AwaitingAwaitable of INotifyCompletion * IAsyncStateMachine

type FAsyncMethodBuilder<'a>() =
    let mutable state = Unchecked.defaultof<FAsyncMethodState<'a>>
    let mutable fasync = Unchecked.defaultof<FAsync<'a>>
    let rec moveState (stm : IAsyncStateMachine) =
        async {
            stm.MoveNext()
            match state with
            | Returning x -> return x
            | AwaitingAsync (fs, stm) ->
                do! fs.AsyncWork
                return! moveState stm
            | AwaitingAwaitable (awaiter, stm) ->
                use waitHandle = new ManualResetEvent(false)
                awaiter.OnCompleted(fun () -> ignore(waitHandle.Set()))
                let! _ = Async.AwaitWaitHandle(waitHandle)
                return! moveState stm
        }
    static member Create() = FAsyncMethodBuilder<'a>()
    member __.Start(stm : #IAsyncStateMachine byref) =
        fasync <- FAsync(moveState stm)
    member __.Task =
        fasync
    member __.SetResult(it : 'a) =
        state <- Returning it
    member __.SetException(exn : exn) =
        ignore(raise exn)
    member __.SetStateMachine(_ : IAsyncStateMachine) =
        ()
    member __.AwaitOnCompleted(awaiter : #INotifyCompletion byref, stm : #IAsyncStateMachine byref) =
        state <-
            match awaiter :> INotifyCompletion with
            | :? FAsyncAwaiter as fasyncAwaiter -> AwaitingAsync(fasyncAwaiter, stm)
            | awaiter -> AwaitingAwaitable(awaiter, stm)
    member this.AwaitUnsafeOnCompleted(awaiter : #ICriticalNotifyCompletion byref, stm : #IAsyncStateMachine byref) =
        this.AwaitOnCompleted(&awaiter, &stm)

