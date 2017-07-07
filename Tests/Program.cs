using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.FSharp.Control;
using FAsync;

namespace Tests
{
    public class SpecialException : Exception
    {
        public SpecialException(string msg) : base(msg) { }
    }
    class Program
    {
        public static void TestTask<T>(Task<T> task, T expected)
        {
            if (EqualityComparer<T>.Default.Equals(task.Result, expected)) return;
            throw new Exception($"Expected {expected} but got {task.Result}");
        }
        public static void TestAsync<T>(FSharpAsync<T> async, T expected)
            => TestTask(FSharpAsync.StartAsTask(async, null, null), expected);

        public static async Task<int> DelayAdditionAsTask(int x, int y)
        {
            await FSharpAsync.Sleep(1);
            var sum = x + y;
            await FSharpAsync.Sleep(1);
            return sum;
        }

        public static async FAsync<int> DelayAdditionAsAsync(int x, int y)
        {
            await FSharpAsync.Sleep(1);
            var sum = x + y;
            await FSharpAsync.Sleep(1);
            return sum;
        }

        public static async FAsync<int> NestedCalls()
        {
            var sum1 = await DelayAdditionAsAsync(1, 2);
            var sum2 = await DelayAdditionAsTask(3, 4);
            return sum1 + sum2;
        }

        public static async FAsync<string> DirectThrowCatch()
        {
            await FSharpAsync.Sleep(1);
            string str = "unset";
            try
            {
                if (int.Parse("1") > 0)
                    throw new SpecialException("direct");
                str = "impossible";
            }
            catch(SpecialException ex) when (ex.Message == "direct")
            {
                str = "caught";
            }
            return str;
        }

        public static async FAsync<string> Thrower()
        {
            await FSharpAsync.Sleep(1);
            await Task.Delay(1);
            if (int.Parse("1") > 0)
                throw new SpecialException("bad");
            await Task.Delay(2);
            return "unreachable";
        }
        
        public static async FAsync<string> Catcher()
        {
            await FSharpAsync.Sleep(1);
            await Task.Delay(1);
            string result;
            try
            {
                result = await Thrower();
            }
            catch (SpecialException ex) when (ex.Message == "bad")
            {
                result = "good";
            }
            catch (Exception)
            {
                Console.WriteLine("some other exn");
                result = "misc";
            }
            return result;
        }

        public static async FAsync<int> LongLoop()
        {
            for (var i = 0; i < 10000; i++)
            {
                await Task.Yield();
                await FSharpAsync.Sleep(0);
            }
            return 0;
        }

        // The below 2 methods illustrate a difference between Async and Task.
        // When you call an async Task<T> method, the code up until the first "await" runs immediately.
        // When you call an aysnc FAsync<T> method, none of its code runs until it is explicitly started.

        private static int _x;
        public static async FAsync<int> TestDelayed()
        {
            _x = 1;
            await FSharpAsync.Sleep(0);
            return 0;
        }

        private static int _y;
        public static async Task<int> TestNotDelayed()
        {
            _y = 1;
            await Task.Delay(0);
            return 0;
        }

        static void Main(string[] args)
        {
            TestTask(DelayAdditionAsTask(1, 2), 3);
            TestAsync(DelayAdditionAsAsync(1, 2), 3);
            TestAsync(NestedCalls(), 10);
            TestAsync(DirectThrowCatch(), "caught");
            TestAsync(Catcher(), "good");
            TestAsync(LongLoop(), 0);

            FSharpAsync<int> delayed = TestDelayed();
            if (_x != 0) throw new Exception("Async was not delayed!");

            FSharpAsync.RunSynchronously(delayed, null, null);
            if (_x != 1) throw new Exception("Async didn't run");

            Task<int> notDelayed = TestNotDelayed();
            if (_y == 0) throw new Exception("Task was delayed!");

            Console.WriteLine("Passed all tests!");
        }
    }
}
